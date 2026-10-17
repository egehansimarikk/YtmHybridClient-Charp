using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Diagnostics;

namespace YtmHybridClient
{
    public class AudioEngine : IDisposable
    {
        private WasapiOut? _wasapiOut;
        private WaveStream? _audioReader;
        private bool _isDisposed;

        public event Action<TimeSpan>? PositionChanged;
        public event Action? PlaybackEnded;

        public bool IsPlaying => _wasapiOut?.PlaybackState == PlaybackState.Playing;

        /// <summary>
        /// Yerel bir ses dosyasını veya Lossless HTTP URL akışını WASAPI Exclusive modunda oynatır.
        /// </summary>
        public void PlayStream(string streamUrlOrPath)
        {
            Stop();

            try
            {
                // 1. Akış kaynağını yükleme (MediaFoundationReader hem URL akışlarını hem FLAC/WAV/MP3 dosyalarını destekler)
                _audioReader = new MediaFoundationReader(streamUrlOrPath);

                // 2. WASAPI Exclusive Mod yapılandırması
                // AudioClientShareMode.Exclusive: Windows mikserini baypas eder (Bit-Perfect)
                // latency: Buffer süresi (ms)
                _wasapiOut = new WasapiOut(AudioClientShareMode.Exclusive, latency: 100);
                _wasapiOut.Init(_audioReader);

                // Oynatımı tamamlandığında tetiklenecek olay
                _wasapiOut.PlaybackStopped += OnPlaybackStopped;

                _wasapiOut.Play();
                Debug.WriteLine($"[AudioEngine] WASAPI Exclusive akış başladı: {streamUrlOrPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine Hata] WASAPI başlatılamadı: {ex.Message}");

                // Eğer ses kartı Exclusive modu desteklemiyorsa Shared moda fallback yapılabilir
                FallbackToSharedMode(streamUrlOrPath);
            }
        }

        private void FallbackToSharedMode(string streamUrlOrPath)
        {
            try
            {
                Debug.WriteLine("[AudioEngine] Exclusive mod başarısız oldu, Shared moda geçiliyor...");
                _audioReader = new MediaFoundationReader(streamUrlOrPath);
                _wasapiOut = new WasapiOut(AudioClientShareMode.Shared, latency: 100);
                _wasapiOut.Init(_audioReader);
                _wasapiOut.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioEngine Hata] Shared mod da başarısız oldu: {ex.Message}");
            }
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