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
        private WasapiOut? _wasapiOut;
        private WaveStream? _audioReader;
        private bool _isDisposed;

        public event Action<TimeSpan>? PositionChanged;
        public event Action? PlaybackEnded;

        /// <summary>true: önce WASAPI Exclusive denenir (olmazsa Shared'a düşer). false: doğrudan Shared.</summary>
        public bool UseExclusive { get; set; } = true;

        /// <summary>Çıkış aygıtının kimliği. Boş ise sistem varsayılanı kullanılır.</summary>
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

        public bool IsPlaying => _wasapiOut?.PlaybackState == PlaybackState.Playing;

        public async Task<bool> PlayStreamAsync(string streamUrlOrPath, TimeSpan timeout)
        {
            Stop();

            var playbackTask = Task.Run(() => CreateAndStartPlayback(streamUrlOrPath));
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

            if (_isDisposed)
            {
                DisposePlayback(playback);
                return false;
            }

            _audioReader = playback.Reader;
            _wasapiOut = playback.Output;
            try
            {
                Debug.WriteLine($"[AudioEngine] WASAPI akış başladı: {streamUrlOrPath}");
                return IsPlaying;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine Hata] Oynatım başlatılamadı: {ex.Message}");
                Stop();
                return false;
            }
        }

        private (WasapiOut Output, WaveStream Reader) CreateAndStartPlayback(string streamUrlOrPath)
        {
            if (!UseExclusive)
                return CreateAndStartPlayback(streamUrlOrPath, AudioClientShareMode.Shared);

            try
            {
                return CreateAndStartPlayback(streamUrlOrPath, AudioClientShareMode.Exclusive);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine] Exclusive mod başarısız: {ex.Message}");
                return CreateAndStartPlayback(streamUrlOrPath, AudioClientShareMode.Shared);
            }
        }

        private (WasapiOut Output, WaveStream Reader) CreateAndStartPlayback(string streamUrlOrPath, AudioClientShareMode mode)
        {
            WaveStream? reader = null;
            WasapiOut? output = null;
            try
            {
                Debug.WriteLine($"[AudioEngine] WASAPI modu: {mode}");
                reader = new MediaFoundationReader(streamUrlOrPath);
                output = CreateOutput(mode);
                output.Init(reader);
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

        public void Pause()
        {
            if (_wasapiOut?.PlaybackState == PlaybackState.Playing)
            {
                _wasapiOut.Pause();
            }
        }

        public void Resume()
        {
            if (_wasapiOut?.PlaybackState == PlaybackState.Paused)
            {
                _wasapiOut.Play();
            }
        }

        public void Stop()
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
        }

        public void Seek(TimeSpan targetTime)
        {
            if (_audioReader != null && targetTime <= _audioReader.TotalTime)
            {
                _audioReader.CurrentTime = targetTime;
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            PlaybackEnded?.Invoke();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Stop();
                _isDisposed = true;
            }
        }
    }
}