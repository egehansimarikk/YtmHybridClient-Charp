using System;
using DiscordRPC;

namespace YtmHybridClient.Helpers
{
    public sealed class DiscordRpcManager : IDisposable
    {
        private const string ClientId = "1572348116726845611";
        private DiscordRpcClient? _client;
        private bool _isEnabled;

        public void SetEnabled(bool enabled)
        {
            if (_isEnabled == enabled) return;
            _isEnabled = enabled;

            if (_isEnabled)
            {
                Initialize();
            }
            else
            {
                Shutdown();
            }
        }

        private void Initialize()
        {
            if (_client != null) return;
            try
            {
                _client = new DiscordRpcClient(ClientId);
                _client.Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Discord RPC Init Hatası]: {ex.Message}");
            }
        }

        public void UpdatePresence(string title, string artist, string artworkUrl, bool isPlaying, double currentSeconds = 0, double totalSeconds = 0)
        {
            if (!_isEnabled || _client == null || !_client.IsInitialized) return;
            if (string.IsNullOrWhiteSpace(title)) return;

            try
            {
                if (!isPlaying)
                {
                    _client.SetPresence(new RichPresence
                    {
                        Type = ActivityType.Listening,
                        Details = Truncate(title, 128),
                        State = Truncate(string.IsNullOrWhiteSpace(artist) ? "YouTube Music (Duraklatıldı)" : $"{artist} • Duraklatıldı", 128),
                        Assets = new Assets
                        {
                            LargeImageKey = !string.IsNullOrWhiteSpace(artworkUrl) ? artworkUrl : "ytm_logo",
                            LargeImageText = "YTM Hybrid Client"
                        }
                    });
                    return;
                }

                Timestamps? timestamps = null;
                if (totalSeconds > 0 && currentSeconds >= 0 && totalSeconds >= currentSeconds)
                {
                    var now = DateTime.UtcNow;
                    var start = now - TimeSpan.FromSeconds(currentSeconds);
                    var end = start + TimeSpan.FromSeconds(totalSeconds);
                    timestamps = new Timestamps(start, end);
                }

                _client.SetPresence(new RichPresence
                {
                    Type = ActivityType.Listening,
                    Details = Truncate(title, 128),
                    State = Truncate(string.IsNullOrWhiteSpace(artist) ? "YouTube Music" : artist, 128),
                    Timestamps = timestamps,
                    Assets = new Assets
                    {
                        LargeImageKey = !string.IsNullOrWhiteSpace(artworkUrl) ? artworkUrl : "ytm_logo",
                        LargeImageText = "YTM Hybrid Client Lossless"
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Discord RPC Güncelleme Hatası]: {ex.Message}");
            }
        }

        public void ClearPresence()
        {
            try
            {
                if (_client != null && _client.IsInitialized)
                    _client.ClearPresence();
            }
            catch { }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
        }

        private void Shutdown()
        {
            try
            {
                _client?.ClearPresence();
                _client?.Dispose();
            }
            catch { }
            finally
            {
                _client = null;
            }
        }

        public void Dispose() => Shutdown();
    }
}