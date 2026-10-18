using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace YtmHybridClient
{
    /// <summary>Ayarlar penceresindeki ses aygıtı listesi için (Id boş = sistem varsayılanı).</summary>
    public record AudioDeviceInfo(string Id, string Name);

    public class AudioEngine : IDisposable
    {
        // _wasapiOut ve _audioReader'a erişimi sıraya sokar (UI iş parçacığı ile aygıt değiştirme iş parçacığı çakışmasın)
        private readonly object _sync = new object();

        private WasapiOut? _wasapiOut;
        private WaveStream? _audioReader;
        private TimeSpan _lastPosition;
        private bool _isDisposed;

        public event Action<TimeSpan>? PositionChanged;
        public event Action? PlaybackEnded;

        /// <summary>true: önce WASAPI Exclusive denenir (olmazsa Shared'a düşer). false: doğrudan Shared.</summary>
        public bool UseExclusive { get; set; } = true;

        /// <summary>Çıkış aygıtının kimliği (MMDevice.ID). Boş ise sistem varsayılanı kullanılır.</summary>
        public string DeviceId { get; set; } = "";

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
            (WasapiOut Output, WaveStream Reader) playback;
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
                _lastPosition = startAt;
            }

            Debug.WriteLine($"[AudioEngine] WASAPI akış başladı: {streamUrlOrPath}");
            return IsPlaying;
        }

        private (WasapiOut Output, WaveStream Reader) CreateAndStartPlayback(string streamUrlOrPath, TimeSpan startAt)
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

        private (WasapiOut Output, WaveStream Reader) CreateAndStartPlayback(string streamUrlOrPath, TimeSpan startAt, AudioClientShareMode mode)
        {
            WaveStream? reader = null;
            WasapiOut? output = null;
            try
            {
                Debug.WriteLine($"[AudioEngine] WASAPI modu: {mode}");
                reader = new MediaFoundationReader(streamUrlOrPath);
                output = CreateOutput(mode);
                output.Init(reader);

                if (startAt > TimeSpan.Zero)
                {
                    try { reader.CurrentTime = startAt; }
                    catch (Exception ex) { Debug.WriteLine($"[AudioEngine] Başlangıç konumuna gidilemedi: {ex.Message}"); }
                }

                output.PlaybackStopped += OnPlaybackStopped;
                output.Play();
                return (output, reader);
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

        private async Task DisposePlaybackWhenReadyAsync(Task<(WasapiOut Output, WaveStream Reader)> playbackTask)
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

        private void DisposePlayback((WasapiOut Output, WaveStream Reader) playback)
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
                WasapiOut newOutput;
                try
                {
                    newOutput = OpenOutputForReader(reader);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioEngine Hata] Yeni aygıtta başlatılamadı: {ex.Message}");
                    return false;   // reader açık kalır; çağıran yedek yolu dener ya da Stop() temizler
                }

                _wasapiOut = newOutput;
                if (wasPaused)
                    newOutput.Pause();

                Debug.WriteLine($"[AudioEngine] Çıkış aygıtı değiştirildi (konum: {_lastPosition:mm\\:ss}, duraklatılmış: {wasPaused})");
                return true;
            }
        }

        private WasapiOut OpenOutputForReader(WaveStream reader)
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

        private WasapiOut OpenOutputForReader(WaveStream reader, AudioClientShareMode mode)
        {
            WasapiOut? output = null;
            try
            {
                Debug.WriteLine($"[AudioEngine] WASAPI modu: {mode}");
                output = CreateOutput(mode);
                output.Init(reader);
                output.PlaybackStopped += OnPlaybackStopped;
                output.Play();
                return output;
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