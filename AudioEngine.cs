using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace YtmHybridClient
{
    /// <summary>Ayarlar penceresindeki ses aygıtı listesi için (Id boş = sistem varsayılanı).</summary>
    public record AudioDeviceInfo(string Id, string Name);

    /// <summary>
    /// Örnek düzeyinde kazanç (takviye) uygular. 0 dBFS'yi aşan tepeleri patlatmak yerine yumuşak bir
    /// sınırlayıcıyla (soft clip) 1.0'ın altında tutar. Kazanç değişimi tampon içinde yumuşatılır (tıklama olmaz).
    /// Not: Bu bir "look-ahead limiter" değil, basit bir yumuşak kırpıcıdır; çok yüksek seviyeli kayıtlarda tepelerde
    /// hafif harmonik renklenme olabilir.
    /// </summary>
    internal sealed class GainSampleProvider : ISampleProvider
    {
        private const float Threshold = 0.89f;   // ≈ -1 dBFS: bunun altındaki örnekler dokunulmadan geçer

        private readonly ISampleProvider _source;
        private float _appliedGain;
        private volatile float _targetGain;

        public GainSampleProvider(ISampleProvider source, float gain)
        {
            _source = source;
            _appliedGain = gain;
            _targetGain = gain;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>Doğrusal çarpan (1.0 = değişiklik yok).</summary>
        public float Gain
        {
            get => _targetGain;
            set => _targetGain = value;
        }

        // NAudio'nun bu sürümünde ISampleProvider.Read, Span<float> alır
        public int Read(Span<float> buffer)
        {
            int read = _source.Read(buffer);
            if (read <= 0) return read;

            float start = _appliedGain;
            float end = _targetGain;

            for (int i = 0; i < read; i++)
            {
                // Kazanç değiştiyse tampon boyunca yavaşça yeni değere geç
                float gain = start == end ? end : start + (end - start) * (i + 1) / read;
                buffer[i] = Limit(buffer[i] * gain);
            }

            _appliedGain = end;
            return read;
        }

        private static float Limit(float x)
        {
            if (float.IsNaN(x)) return 0f;

            float abs = MathF.Abs(x);
            if (abs <= Threshold) return x;

            // Eşiğin üstünü tanh ile 1.0'a asimptotik yaklaştır: asla 1.0'ı geçmez (patlama/clipping olmaz)
            float range = 1f - Threshold;
            float limited = Threshold + range * MathF.Tanh((abs - Threshold) / range);
            return MathF.CopySign(MathF.Min(limited, 1f), x);
        }
    }

    public class AudioEngine : IDisposable
    {
        public const float MinGainDb = 0f;
        public const float MaxGainDb = 6f;

        // _wasapiOut, _audioReader ve _gain'e erişimi sıraya sokar (UI iş parçacığı ile arka plan işleri çakışmasın)
        private readonly object _sync = new object();

        private WasapiOut? _wasapiOut;
        private WaveStream? _audioReader;
        private GainSampleProvider? _gain;
        private float _gainDb;
        private TimeSpan _lastPosition;
        private bool _isDisposed;

        public event Action<TimeSpan>? PositionChanged;
        public event Action? PlaybackEnded;

        /// <summary>true: önce WASAPI Exclusive denenir (olmazsa Shared'a düşer). false: doğrudan Shared.</summary>
        public bool UseExclusive { get; set; } = true;

        /// <summary>Çıkış aygıtının kimliği (MMDevice.ID). Boş ise sistem varsayılanı kullanılır.</summary>
        public string DeviceId { get; set; } = "";

        /// <summary>
        /// Ses takviyesi (dB). 0 = takviye yok ve akış doğrudan çıkışa verilir (bit-perfect, Yerel FLAC için).
        /// Sonraki akışlarda kullanılır; çalan akışa da anında uygulanır.
        /// </summary>
        public float GainDb
        {
            get => _gainDb;
            set
            {
                float clamped = float.IsNaN(value) ? 0f : Math.Clamp(value, MinGainDb, MaxGainDb);
                bool needsRebuild;

                lock (_sync)
                {
                    _gainDb = clamped;
                    if (_gain != null)
                        _gain.Gain = DbToLinear(clamped);

                    // Akış takviyesiz (doğrudan) açılmıştı ve şimdi takviye isteniyor: çıkışı zincirle yeniden kur
                    needsRebuild = _gain == null && clamped > 0f && _wasapiOut != null;
                }

                if (needsRebuild)
                    _ = Task.Run(SwitchOutputToCurrentDevice);
            }
        }

        public static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);

        /// <summary>Etkin ses çıkış aygıtlarını listeler. İlk eleman "Sistem varsayılanı"dır.</summary>
        public static List<AudioDeviceInfo> GetOutputDevices()
        {
            var list = new List<AudioDeviceInfo> { new AudioDeviceInfo("", "Sistem varsayılanı") };
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    try
                    {
                        list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioEngine] Aygıt okunamadı: {ex.Message}");
                    }
                    finally
                    {
                        device.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine] Aygıtlar listelenemedi: {ex.Message}");
            }
            return list;
        }

        // Seçili aygıt varsa onu, yoksa (ya da bulunamazsa) sistem varsayılanını kullanır
        private WasapiOut CreateOutput(AudioClientShareMode mode)
        {
            string id = DeviceId;
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var device = enumerator.GetDevice(id);
                    if (device.State == DeviceState.Active)
                    {
                        Debug.WriteLine($"[AudioEngine] Aygıt: {device.FriendlyName}");
                        return new WasapiOut(device, mode, true, 100);
                    }
                    Debug.WriteLine("[AudioEngine] Seçili aygıt etkin değil, varsayılana dönülüyor.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioEngine] Seçili aygıt açılamadı ({ex.Message}), varsayılana dönülüyor.");
                }
            }

            Debug.WriteLine("[AudioEngine] Aygıt: sistem varsayılanı");
            return new WasapiOut(mode, latency: 100);
        }

        // Çıkışa verilecek kaynağı kurar: takviye varsa kazanç + sınırlayıcı zinciri (16 bit), yoksa doğrudan reader
        private IWaveProvider BuildSource(WaveStream reader, out GainSampleProvider? gain)
        {
            float gainDb = _gainDb;
            if (gainDb <= 0f)
            {
                gain = null;
                return reader;
            }

            gain = new GainSampleProvider(reader.ToSampleProvider(), DbToLinear(gainDb));
            Debug.WriteLine($"[AudioEngine] Ses takviyesi: +{gainDb:0.0} dB");
            return new SampleToWaveProvider16(gain);
        }

        public bool IsPlaying
        {
            get { lock (_sync) { return _wasapiOut?.PlaybackState == PlaybackState.Playing; } }
        }

        public bool IsPaused
        {
            get { lock (_sync) { return _wasapiOut?.PlaybackState == PlaybackState.Paused; } }
        }

        /// <summary>Çalan akışın o anki konumu.</summary>
        public TimeSpan CurrentPosition
        {
            get { lock (_sync) { return ReadPosition(); } }
        }

        // Reader okunamıyorsa (ör. kapatılmışsa) son bilinen konumu döndürür
        private TimeSpan ReadPosition()
        {
            try
            {
                if (_audioReader != null)
                    _lastPosition = _audioReader.CurrentTime;
            }
            catch
            {
                // son bilinen konum kullanılır
            }
            return _lastPosition;
        }

        /// <param name="startAt">Akışın başlayacağı konum (kaynak değiştirirken YouTube Music ile senkron kalmak için).</param>
        public async Task<bool> PlayStreamAsync(string streamUrlOrPath, TimeSpan timeout, TimeSpan startAt = default)
        {
            Stop();

            var playbackTask = Task.Run(() => CreateAndStartPlayback(streamUrlOrPath, startAt));
            (WasapiOut Output, WaveStream Reader, GainSampleProvider? Gain) playback;
            try
            {
                playback = await playbackTask.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                _ = DisposePlaybackWhenReadyAsync(playbackTask);
                Debug.WriteLine($"[AudioEngine] Akış {timeout.TotalSeconds:0.#} saniyede açılamadı.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine Hata] Akış başlatılamadı: {ex.Message}");
                return false;
            }

            lock (_sync)
            {
                if (_isDisposed)
                {
                    DisposePlayback(playback);
                    return false;
                }

                _audioReader = playback.Reader;
                _wasapiOut = playback.Output;
                _gain = playback.Gain;
                _lastPosition = startAt;
            }

            Debug.WriteLine($"[AudioEngine] WASAPI akış başladı: {streamUrlOrPath}");
            return IsPlaying;
        }

        private (WasapiOut Output, WaveStream Reader, GainSampleProvider? Gain) CreateAndStartPlayback(string streamUrlOrPath, TimeSpan startAt)
        {
            if (!UseExclusive)
                return CreateAndStartPlayback(streamUrlOrPath, startAt, AudioClientShareMode.Shared);

            try
            {
                return CreateAndStartPlayback(streamUrlOrPath, startAt, AudioClientShareMode.Exclusive);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine] Exclusive mod başarısız: {ex.Message}");
                return CreateAndStartPlayback(streamUrlOrPath, startAt, AudioClientShareMode.Shared);
            }
        }

        private (WasapiOut Output, WaveStream Reader, GainSampleProvider? Gain) CreateAndStartPlayback(string streamUrlOrPath, TimeSpan startAt, AudioClientShareMode mode)
        {
            WaveStream? reader = null;
            WasapiOut? output = null;
            try
            {
                Debug.WriteLine($"[AudioEngine] WASAPI modu: {mode}");
                reader = new MediaFoundationReader(streamUrlOrPath);
                output = CreateOutput(mode);
                output.Init(BuildSource(reader, out var gain));

                if (startAt > TimeSpan.Zero)
                {
                    try { reader.CurrentTime = startAt; }
                    catch (Exception ex) { Debug.WriteLine($"[AudioEngine] Başlangıç konumuna gidilemedi: {ex.Message}"); }
                }

                output.PlaybackStopped += OnPlaybackStopped;
                output.Play();
                return (output, reader, gain);
            }
            catch
            {
                if (output != null)
                {
                    output.PlaybackStopped -= OnPlaybackStopped;
                    output.Dispose();
                }

                reader?.Dispose();
                throw;
            }
        }

        private async Task DisposePlaybackWhenReadyAsync(Task<(WasapiOut Output, WaveStream Reader, GainSampleProvider? Gain)> playbackTask)
        {
            try
            {
                var playback = await playbackTask.ConfigureAwait(false);
                DisposePlayback(playback);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine] Zaman aşımına uğrayan akış temizlenemedi: {ex.Message}");
            }
        }

        private void DisposePlayback((WasapiOut Output, WaveStream Reader, GainSampleProvider? Gain) playback)
        {
            playback.Output.PlaybackStopped -= OnPlaybackStopped;
            playback.Output.Dispose();
            playback.Reader.Dispose();
        }

        // ================= Çıkış aygıtını anında değiştirme =================

        /// <summary>
        /// Çıkış aygıtını çalan akışı kesmeden değiştirir. Akış (reader) açık kalır, yeniden indirilmez; sadece
        /// WASAPI çıkışı dispose edilip yeni aygıtta yeniden başlatılır. Playing/Paused durumu ve konum korunur.
        /// Çalan akış yoksa sadece yeni aygıt kimliği saklanır ve bir sonraki akışta kullanılır.
        /// (Aygıt, indeks yerine kimlikle seçilir: WASAPI'de indeks yoktur ve cihaz takılıp çıkarılınca kayar.)
        /// </summary>
        /// <param name="deviceId">MMDevice.ID. Boş veya null = sistem varsayılanı.</param>
        /// <returns>true: yeni aygıtta devam ediyor (ya da çalan akış yoktu). false: yeni aygıtta açılamadı.</returns>
        public Task<bool> ChangeOutputDeviceAsync(string? deviceId)
        {
            DeviceId = deviceId ?? "";
            return Task.Run(SwitchOutputToCurrentDevice);
        }

        // Çıkışı, mevcut DeviceId / UseExclusive / GainDb ayarlarıyla aynı reader üzerinde yeniden kurar
        private bool SwitchOutputToCurrentDevice()
        {
            lock (_sync)
            {
                var reader = _audioReader;
                var oldOutput = _wasapiOut;

                if (_isDisposed || reader == null || oldOutput == null)
                    return true;   // çalan akış yok

                bool wasPaused = oldOutput.PlaybackState == PlaybackState.Paused;

                // 1) Eski çıkışı bırak. Reader'a DOKUNMA: kaldığı yerde durur, yeni çıkış oradan devam eder.
                oldOutput.PlaybackStopped -= OnPlaybackStopped;   // aygıt değişimi "şarkı bitti" sayılmasın
                _wasapiOut = null;
                try { oldOutput.Stop(); }
                catch (Exception ex) { Debug.WriteLine($"[AudioEngine] Eski çıkış durdurulamadı: {ex.Message}"); }
                try { oldOutput.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[AudioEngine] Eski çıkış kapatılamadı: {ex.Message}"); }

                ReadPosition();   // son bilinen konumu sakla (yedek yol için)

                // 2) Aynı reader ile yeni aygıtta başlat
                (WasapiOut Output, GainSampleProvider? Gain) opened;
                try
                {
                    opened = OpenOutputForReader(reader);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioEngine Hata] Yeni aygıtta başlatılamadı: {ex.Message}");
                    return false;   // reader açık kalır; çağıran yedek yolu dener ya da Stop() temizler
                }

                _wasapiOut = opened.Output;
                _gain = opened.Gain;
                if (wasPaused)
                    opened.Output.Pause();

                Debug.WriteLine($"[AudioEngine] Çıkış yeniden kuruldu (konum: {_lastPosition:mm\\:ss}, duraklatılmış: {wasPaused})");
                return true;
            }
        }

        private (WasapiOut Output, GainSampleProvider? Gain) OpenOutputForReader(WaveStream reader)
        {
            if (UseExclusive)
            {
                try
                {
                    return OpenOutputForReader(reader, AudioClientShareMode.Exclusive);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioEngine] Exclusive mod başarısız: {ex.Message}");
                }
            }

            return OpenOutputForReader(reader, AudioClientShareMode.Shared);
        }

        private (WasapiOut Output, GainSampleProvider? Gain) OpenOutputForReader(WaveStream reader, AudioClientShareMode mode)
        {
            WasapiOut? output = null;
            try
            {
                Debug.WriteLine($"[AudioEngine] WASAPI modu: {mode}");
                output = CreateOutput(mode);
                output.Init(BuildSource(reader, out var gain));
                output.PlaybackStopped += OnPlaybackStopped;
                output.Play();
                return (output, gain);
            }
            catch
            {
                if (output != null)
                {
                    output.PlaybackStopped -= OnPlaybackStopped;
                    output.Dispose();
                }
                throw;
            }
        }

        // ================= Temel kontroller =================

        public void Pause()
        {
            lock (_sync)
            {
                if (_wasapiOut?.PlaybackState == PlaybackState.Playing)
                {
                    _wasapiOut.Pause();
                }
            }
        }

        public void Resume()
        {
            lock (_sync)
            {
                if (_wasapiOut?.PlaybackState == PlaybackState.Paused)
                {
                    _wasapiOut.Play();
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_wasapiOut != null)
                {
                    _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
                    _wasapiOut.Stop();
                    _wasapiOut.Dispose();
                    _wasapiOut = null;
                }

                if (_audioReader != null)
                {
                    _audioReader.Dispose();
                    _audioReader = null;
                }

                _gain = null;
                _lastPosition = TimeSpan.Zero;
            }
        }

        public void Seek(TimeSpan targetTime)
        {
            lock (_sync)
            {
                if (_audioReader != null && targetTime <= _audioReader.TotalTime)
                {
                    _audioReader.CurrentTime = targetTime;
                }
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            PlaybackEnded?.Invoke();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            Stop();
        }
    }
}