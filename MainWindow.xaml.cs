using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace YtmHybridClient
{
    public partial class MainWindow : Window
    {
        private readonly AudioEngine _audioEngine = new AudioEngine();
        private readonly LosslessResolver _resolver = new LosslessResolver();
        private bool _isExternalAudioActive = false;

        // Uygulama Ayarları Nesnesi
        private AppSettings _appSettings = AppSettings.Load();

        public MainWindow()
        {
            InitializeComponent();
            InitializeWebViewAsync();
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string userDataFolder = Path.Combine(appData, "YtmHybridClient", "WebView2Profile");

                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);

                await YtmWebView.EnsureCoreWebView2Async(env);

                // User-Agent ve DevTools Ayarları
                YtmWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
                YtmWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // =========================================================================
                // 1. AĞ SEVİYESİNDE REKLAM ENGELLEME (Network Filter)
                // =========================================================================
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*googleads*", CoreWebView2WebResourceContext.All);
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*doubleclick.net*", CoreWebView2WebResourceContext.All);
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*youtube.com/pagead/*", CoreWebView2WebResourceContext.All);
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*googlesyndication.com*", CoreWebView2WebResourceContext.All);

                YtmWebView.CoreWebView2.WebResourceRequested += (sender, args) =>
                {
                    args.Response = YtmWebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                };

                YtmWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // =========================================================================
                // 2. YOUTUBE API PAYLOAD INTERCEPTOR (Gelişmiş API Reklam Temizleyici)
                // =========================================================================
                string interceptorScript = @"
                    (function () {
                        window.isExternalAudioPlaying = false;

                        // --- A. YOUTUBE API VERİLERİNDEN REKLAMLARI TEMİZLEME (FETCH HOOK) ---
                        const originalFetch = window.fetch;
                        window.fetch = async function(...args) {
                            const response = await originalFetch.apply(this, args);
                            const url = args[0] ? (typeof args[0] === 'string' ? args[0] : args[0].url) : '';

                            if (url && (url.includes('/youtubei/v1/player') || url.includes('/youtubei/v1/next'))) {
                                try {
                                    const clone = response.clone();
                                    const data = await clone.json();

                                    // JSON Verisi İçindeki Tüm Reklam Objelerini Temizle
                                    if (data.adPlacements) delete data.adPlacements;
                                    if (data.playerAds) delete data.playerAds;
                                    if (data.adSlots) delete data.adSlots;

                                    return new Response(JSON.stringify(data), {
                                        status: response.status,
                                        statusText: response.statusText,
                                        headers: response.headers
                                    });
                                } catch (e) {
                                    return response;
                                }
                            }
                            return response;
                        };

                        // ytInitialPlayerResponse Objesini Dinleyip Reklamları Silme
                        let _ytInitialPlayerResponse = window.ytInitialPlayerResponse;
                        Object.defineProperty(window, 'ytInitialPlayerResponse', {
                            get: function() { return _ytInitialPlayerResponse; },
                            set: function(val) {
                                if (val) {
                                    if (val.adPlacements) delete val.adPlacements;
                                    if (val.playerAds) delete val.playerAds;
                                    if (val.adSlots) delete val.adSlots;
                                }
                                _ytInitialPlayerResponse = val;
                            },
                            configurable: true
                        });

                        // --- B. CSS İLE BANNER VE POP-UP ENGELLEME ---
                        const style = document.createElement('style');
                        style.innerHTML = `
                            .ytp-ad-overlay-container, .ytp-ad-message-container,
                            .video-ads, ytm-promoted-sparkles-web-renderer,
                            ytmusic-mealbar-promo-renderer, .tp-yt-paper-dialog,
                            ytmusic-popup-container, .ytp-ad-player-overlay,
                            ytmusic-pivots, #player-ads { display: none !important; }
                        `;
                        document.head.appendChild(style);

                        // --- C. MUTE KİLİT MEKANİZMASI ---
                        function lockMediaElements() {
                            const mediaElements = document.querySelectorAll('video, audio');
                            mediaElements.forEach(media => {
                                if (window.isExternalAudioPlaying) {
                                    media.muted = true;
                                    media.volume = 0;
                                } else {
                                    media.muted = false;
                                }
                            });
                        }

                        document.addEventListener('volumechange', lockMediaElements, true);
                        document.addEventListener('play', lockMediaElements, true);
                        document.addEventListener('playing', lockMediaElements, true);
                        setInterval(lockMediaElements, 100);

                        window.setYtmMute = function(shouldMute) {
                            window.isExternalAudioPlaying = shouldMute;
                            lockMediaElements();
                        };

                        // --- D. PLAY / PAUSE & SEEK EVENT DİNLEYİCİLERİ ---
                        document.addEventListener('pause', function () {
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({ type: 'PLAYER_STATE', state: 'PAUSED' });
                            }
                        }, true);

                        document.addEventListener('play', function () {
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({ type: 'PLAYER_STATE', state: 'PLAYING' });
                            }
                        }, true);

                        document.addEventListener('seeking', function (e) {
                            const media = e.target;
                            if (media && window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({ 
                                    type: 'SEEK', 
                                    positionSeconds: media.currentTime 
                                });
                            }
                        }, true);

                        // --- E. ŞARKI DEĞİŞİMİ TAKİBİ ---
                        let lastTrackKey = '';
                        function checkTrackChange() {
                            if (navigator.mediaSession && navigator.mediaSession.metadata) {
                                const meta = navigator.mediaSession.metadata;
                                const currentKey = meta.title + '-' + meta.artist;

                                if (currentKey !== lastTrackKey && meta.title !== '') {
                                    lastTrackKey = currentKey;
                                    window.setYtmMute(true);

                                    const trackData = {
                                        type: 'TRACK_CHANGED',
                                        title: meta.title || '',
                                        artist: meta.artist || '',
                                        album: meta.album || ''
                                    };
                                    if (window.chrome && window.chrome.webview) {
                                        window.chrome.webview.postMessage(trackData);
                                    }
                                }
                            }
                        }

                        const observer = new MutationObserver(checkTrackChange);
                        observer.observe(document.body, { childList: true, subtree: true });
                    })();
                ";

                await YtmWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(interceptorScript);
                YtmWebView.CoreWebView2.Navigate("https://music.youtube.com");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata oluştu: {ex.Message}");
            }
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string jsonMessage = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(jsonMessage);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) return;
                string msgType = typeProp.GetString() ?? "";

                switch (msgType)
                {
                    case "TRACK_CHANGED":
                        string title = root.GetProperty("title").GetString() ?? "";
                        string artist = root.GetProperty("artist").GetString() ?? "";

                        Dispatcher.Invoke(() => {
                            this.Title = $"YTM Lossless Client - {title} ({artist})";
                        });

                        string? losslessUrl = await _resolver.ResolveLosslessStreamAsync(title, artist);

                        if (!string.IsNullOrEmpty(losslessUrl))
                        {
                            _isExternalAudioActive = true;
                            await YtmWebView.CoreWebView2.ExecuteScriptAsync("window.setYtmMute(true);");
                            _audioEngine.PlayStream(losslessUrl);
                        }
                        else
                        {
                            _isExternalAudioActive = false;
                            _audioEngine.Stop();

                            if (_appSettings.EnableSmartFallback)
                            {
                                await YtmWebView.CoreWebView2.ExecuteScriptAsync("window.setYtmMute(false);");
                            }
                        }
                        break;

                    case "PLAYER_STATE":
                        if (_isExternalAudioActive)
                        {
                            string state = root.GetProperty("state").GetString() ?? "";
                            if (state == "PAUSED")
                            {
                                _audioEngine.Pause();
                            }
                            else if (state == "PLAYING")
                            {
                                _audioEngine.Resume();
                            }
                        }
                        break;

                    case "SEEK":
                        if (_isExternalAudioActive && root.TryGetProperty("positionSeconds", out var posProp))
                        {
                            double seconds = posProp.GetDouble();
                            _audioEngine.Seek(TimeSpan.FromSeconds(seconds));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebMessage Error: {ex.Message}");
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_appSettings)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                _appSettings = settingsWindow.CurrentSettings;
                System.Diagnostics.Debug.WriteLine("[Settings] Ayarlar güncellendi ve kaydedildi.");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioEngine.Dispose();
            base.OnClosed(e);
        }
    }
}