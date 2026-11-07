using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using YtmHybridClient.Helpers;

namespace YtmHybridClient
{
    public partial class MainWindow : Window
    {
        public static MainWindow? Current { get; private set; }

        private readonly GlobalHotkeyManager _hotkeyManager;
        private readonly SystemTrayManager _trayManager;
        private readonly DispatcherTimer _miniPlayerProgressTimer;
        private MiniPlayerWindow? _miniPlayerWindow;
        private bool _isExplicitExit;
        private bool _isProgressUpdateInFlight;
        private bool _isYtmPlaying;
        private string _currentArtworkUrl = "";

        private string _currentTitle = "";
        private string _currentArtist = "";

        private AppSettings _appSettings = AppSettings.Load();

        public MainWindow()
        {
            InitializeComponent();
            Current = this;

            _miniPlayerProgressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _miniPlayerProgressTimer.Tick += MiniPlayerProgressTimer_Tick;

            _hotkeyManager = new GlobalHotkeyManager(HandleMediaHotkey);
            _hotkeyManager.Attach(this);
            _trayManager = new SystemTrayManager(
                TogglePlaybackFromTray,
                NextTrackFromTray,
                ShowSettingsFromTray,
                ShowMiniPlayer,
                ExitApplication,
                ShowMainWindow);
            Closing += MainWindow_Closing;

            try
            {
                _appSettings = AppSettings.Load();
                LanguageManager.ChangeLanguage(_appSettings.Language);
            }

            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Init Hata]: {ex.Message}");
            }

            InitializeWebViewAsync();
        }

        private void BtnMiniPlayer_Click(object sender, RoutedEventArgs e) => ShowMiniPlayer();

        public async Task<bool> ApplyAudioDeviceToWebView(string deviceFriendlyName)
        {
            if (YtmWebView?.CoreWebView2 == null)
            {
                System.Diagnostics.Debug.WriteLine("[WebView ses aygıtı] WebView2 henüz hazır değil.");
                return false;
            }

            string targetNameJson = JsonSerializer.Serialize(deviceFriendlyName ?? string.Empty);
            string script = $$"""
                (async (targetName) => {
                    try {
                        if (!window.__ytmAudioLabelHandshake) {
                            window.__ytmAudioLabelHandshake = (async () => {
                                try {
                                    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                                    stream.getTracks().forEach(track => track.stop());
                                } catch (_) { }
                            })();
                        }
                        await window.__ytmAudioLabelHandshake;

                        for (let attempt = 0; attempt < 20; attempt++) {
                            const media = document.querySelector('video.html5-main-video') || document.querySelector('video') || document.querySelector('audio');
                            if (media && typeof media.setSinkId === 'function') {
                                if (!targetName || /default|varsayılan/i.test(targetName.trim())) {
                                    await media.setSinkId('');
                                    console.log('WebView2 ses çıkışı varsayılan aygıta ayarlandı.');
                                    return true;
                                }

                                const normalize = value => value.replace(/\([^)]*\)/g, ' ').toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
                                const target = normalize(targetName);
                                const outputs = (await navigator.mediaDevices.enumerateDevices()).filter(device => device.kind === 'audiooutput');
                                const matched = outputs.find(device => {
                                    const label = normalize(device.label || '');
                                    return label.length > 2 && target.length > 2 && (label.includes(target) || target.includes(label));
                                });

                                if (!matched) {
                                    console.warn('Seçilen WebView2 ses aygıtı bulunamadı:', targetName);
                                    return false;
                                }

                                await media.setSinkId(matched.deviceId);
                                console.log('WebView2 ses çıkışı yönlendirildi:', matched.label);
                                return true;
                            }
                            await new Promise(resolve => setTimeout(resolve, 500));
                        }
                    } catch (error) {
                        console.error('WebView2 ses yönlendirme hatası:', error);
                    }
                    return false;
                })({{targetNameJson}});
                """;

            try
            {
                string result = await YtmWebView.CoreWebView2.ExecuteScriptAsync(script);
                bool applied = string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
                System.Diagnostics.Debug.WriteLine(applied
                    ? $"[WebView ses aygıtı] Ses çıkışı '{deviceFriendlyName}' aygıtına yönlendirildi."
                    : $"[WebView ses aygıtı] '{deviceFriendlyName}' aygıtına yönlendirme başarısız oldu.");
                return applied;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView ses aygıtı] Yönlendirme başarısız: {ex.Message}");
                return false;
            }
        }

        // target=_blank / window.open istekleri (paylaş, giriş, harici bağlantılar vb.)
        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Varsayılan davranış ayrı bir WebView2 penceresi açmaktır; onu kendimiz yönetiriz
            e.Handled = true;

            // Kullanıcı tıklaması olmadan açılmaya çalışan pencereleri (reklam/pop-up) engelle
            if (!e.IsUserInitiated)
                return;

            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return;

            try
            {
                if (IsHostOrSubdomain(uri.Host, "youtube.com") || IsHostOrSubdomain(uri.Host, "google.com"))
                {
                    // YouTube / Google sayfaları aynı pencerede açılır (oturum açma, hesap sayfaları vb.)
                    YtmWebView.CoreWebView2.Navigate(e.Uri);
                }
                else
                {
                    // Diğer bağlantılar varsayılan tarayıcıda açılır
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NewWindow Hata]: {ex.Message}");
            }
        }

        private static bool IsHostOrSubdomain(string host, string domain)
        {
            return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string userDataFolder = Path.Combine(appData, "YtmHybridClient", "WebView2Profile");

                var options = new CoreWebView2EnvironmentOptions(
                    "--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);

                await YtmWebView.EnsureCoreWebView2Async(env);

                YtmWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
                YtmWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                YtmWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                YtmWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;   // alert/confirm penceresi
                YtmWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                YtmWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

                YtmWebView.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                YtmWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*googleads*", CoreWebView2WebResourceContext.All);
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*doubleclick.net*", CoreWebView2WebResourceContext.All);
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*youtube.com/pagead/*", CoreWebView2WebResourceContext.All);
                YtmWebView.CoreWebView2.AddWebResourceRequestedFilter("*googlesyndication.com*", CoreWebView2WebResourceContext.All);

                YtmWebView.CoreWebView2.WebResourceRequested += (sender, args) =>
                {
                    args.Response = YtmWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                        new MemoryStream(), 403, "Blocked", "");
                };

                YtmWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                YtmWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                string interceptorScript = @"
                    (function () {
                        function reportJsError(where, e) {
                            try {
                                window.chrome.webview.postMessage({
                                    type: 'JS_ERROR',
                                    message: where + ': ' + (e && e.message ? e.message : String(e))
                                });
                            } catch (_) { }
                        }

                        function whenDomReady(fn) {
                            if (document.readyState === 'loading') {
                                document.addEventListener('DOMContentLoaded', fn, { once: true });
                            } else {
                                fn();
                            }
                        }

                        const originalFetch = window.fetch;
                        window.fetch = async function(...args) {
                            const response = await originalFetch.apply(this, args);
                            const url = args[0] ? (typeof args[0] === 'string' ? args[0] : args[0].url) : '';

                            if (url && (url.includes('/youtubei/v1/player') || url.includes('/youtubei/v1/next'))) {
                                try {
                                    const clone = response.clone();
                                    const data = await clone.json();

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

                        try {
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
                        } catch (e) { reportJsError('defineProperty', e); }

                        try {
                        const style = document.createElement('style');
                        style.textContent = `
                            .ytp-ad-overlay-container, .ytp-ad-message-container,
                            .video-ads, ytm-promoted-sparkles-web-renderer,
                            ytmusic-mealbar-promo-renderer, .ytp-ad-player-overlay,
                            ytmusic-pivots, #player-ads { display: none !important; }
                        `;
                        whenDomReady(function () { (document.head || document.documentElement).appendChild(style); });
                        } catch (e) { reportJsError('style', e); }

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

                        let lastTrackKey = '';
                        function checkTrackChange() {
                            try {
                            if (navigator.mediaSession && navigator.mediaSession.metadata) {
                                const meta = navigator.mediaSession.metadata;
                                const currentKey = meta.title + '-' + meta.artist;

                                if (currentKey !== lastTrackKey && meta.title !== '') {
                                    lastTrackKey = currentKey;

                                    const trackData = {
                                        type: 'TRACK_CHANGED',
                                        title: meta.title || '',
                                        artist: meta.artist || '',
                                        album: meta.album || '',
                                        artworkUrl: meta.artwork && meta.artwork.length ? meta.artwork[0].src : ''
                                    };
                                    if (window.chrome && window.chrome.webview) {
                                        window.chrome.webview.postMessage(trackData);
                                    }
                                }
                            }
                            } catch (e) { reportJsError('checkTrackChange', e); }
                        }

                        const observer = new MutationObserver(checkTrackChange);
                        whenDomReady(function () {
                            observer.observe(document.documentElement, { childList: true, subtree: true });
                            checkTrackChange();
                        });
                        // DOM değişmese bile mediaSession değişimini yakalamak için yedek kontrol
                        setInterval(checkTrackChange, 1000);

                        try { window.chrome.webview.postMessage({ type: 'JS_READY' }); } catch (_) { }
                    })();
                ";

                await YtmWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(interceptorScript);
                YtmWebView.CoreWebView2.Navigate("https://music.youtube.com");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hata oluştu: {ex.Message}");
            }
        }

        private void CoreWebView2_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (e.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera)
            {
                e.State = CoreWebView2PermissionState.Allow;
                e.Handled = true;
            }
        }

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            await ApplyAudioDeviceToWebView(AppSettings.Load().AudioDevice);
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
                        string title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                        string artist = root.TryGetProperty("artist", out var aProp) ? aProp.GetString() ?? "" : "";
                        _currentArtworkUrl = root.TryGetProperty("artworkUrl", out var artworkProp) ? artworkProp.GetString() ?? "" : "";
                        _currentTitle = title;
                        _currentArtist = artist;
                        UpdateMiniPlayerTrack();
                        await ApplyAudioDeviceToWebView(_appSettings.AudioDevice);
                        await UpdateStatusBadgeAsync("▶ YouTube Music", "#FF0055");
                        break;

                    case "JS_READY":
                        System.Diagnostics.Debug.WriteLine("[JS] Script yüklendi.");
                        await UpdateStatusBadgeAsync("✔ Script hazır", "#455A64");
                        break;

                    case "JS_ERROR":
                        string jsErr = root.TryGetProperty("message", out var jeProp) ? jeProp.GetString() ?? "" : "";
                        System.Diagnostics.Debug.WriteLine($"[JS HATA]: {jsErr}");
                        await UpdateStatusBadgeAsync("⚠ JS hatası", "#B71C1C");
                        break;

                    case "PLAYER_STATE":
                        string state = root.TryGetProperty("state", out var sProp) ? sProp.GetString() ?? "" : "";
                        _isYtmPlaying = state == "PLAYING";
                        UpdateMiniPlayerPlaybackState();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebMessage Error: {ex.Message}");
            }
        }

        private async Task UpdateStatusBadgeAsync(string text, string hexColor)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    BadgeSource.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
                    TxtAudioSource.Text = text;
                }).Task;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Rozet güncelleme Hata]: {ex.Message}");
            }
        }

        private async Task<string> SafeExecuteScriptAsync(string script)
        {
            var operation = Dispatcher.InvokeAsync<Task<string>>(() =>
            {
                if (YtmWebView?.CoreWebView2 == null)
                    return Task.FromResult("null");

                return YtmWebView.CoreWebView2.ExecuteScriptAsync(script);
            });

            return await operation.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(3));
        }

        private async void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(AppSettings.Load()) { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                _appSettings = AppSettings.Load();
                LanguageManager.ChangeLanguage(_appSettings.Language);
                System.Diagnostics.Debug.WriteLine("[Settings] Ayarlar yüklendi.");
            }
        }

        private void HandleMediaHotkey(MediaHotkey hotkey)
        {
            RunOnUi(() =>
            {
                switch (hotkey)
                {
                    case MediaHotkey.PlayPause:
                        TogglePlayback();
                        break;
                    case MediaHotkey.NextTrack:
                        SkipTrack(true);
                        break;
                    case MediaHotkey.PreviousTrack:
                        SkipTrack(false);
                        break;
                }
            });
        }

        private void TogglePlaybackFromTray() => RunOnUi(TogglePlayback);

        private void NextTrackFromTray() => RunOnUi(() => SkipTrack(true));

        private void ShowSettingsFromTray()
        {
            RunOnUi(() =>
            {
                ShowMainWindow();
                BtnSettings_Click(this, new RoutedEventArgs());
            });
        }

        private void ShowMiniPlayer()
        {
            RunOnUi(() =>
            {
                if (_miniPlayerWindow != null)
                {
                    _miniPlayerWindow.Topmost = true;
                    _miniPlayerWindow.Show();
                    _miniPlayerWindow.Activate();
                    _miniPlayerProgressTimer.Start();
                    Hide();
                    return;
                }

                _miniPlayerWindow = new MiniPlayerWindow(TogglePlayback, ShowMainWindow);
                _miniPlayerWindow.Topmost = true;
                _miniPlayerWindow.Closed += MiniPlayerWindow_Closed;
                UpdateMiniPlayerTrack();
                UpdateMiniPlayerPlaybackState();
                _hotkeyManager.Attach(_miniPlayerWindow);
                Hide();
                _miniPlayerWindow.Show();
                _miniPlayerProgressTimer.Start();
            });
        }

        private void ShowMainWindow()
        {
            RunOnUi(() =>
            {
                _miniPlayerProgressTimer.Stop();
                if (_miniPlayerWindow != null)
                {
                    var miniPlayer = _miniPlayerWindow;
                    _miniPlayerWindow = null;
                    miniPlayer.Closed -= MiniPlayerWindow_Closed;
                    miniPlayer.Close();
                }

                Show();
                WindowState = WindowState.Normal;
                Activate();
                _hotkeyManager.Attach(this);
            });
        }

        private void MiniPlayerWindow_Closed(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, _miniPlayerWindow)) return;
            _miniPlayerWindow = null;
            _miniPlayerProgressTimer.Stop();
            if (!_isExplicitExit)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                _hotkeyManager.Attach(this);
            }
        }

        public async Task SeekTrack(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) return;

            string position = JsonSerializer.Serialize(seconds);
            try
            {
                await SafeExecuteScriptAsync($"document.querySelector('#movie_player')?.seekTo({position}, true);");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YTM seek] {ex.Message}");
            }
        }

        private async void MiniPlayerProgressTimer_Tick(object? sender, EventArgs e)
        {
            await UpdateMiniPlayerProgressAsync();
        }

        private async Task UpdateMiniPlayerProgressAsync()
        {
            if (_miniPlayerWindow == null || _isProgressUpdateInFlight) return;

            _isProgressUpdateInFlight = true;
            try
            {
                string result = await SafeExecuteScriptAsync(
                    "(() => { let p = document.querySelector('#movie_player'); return p ? JSON.stringify({ current: p.getCurrentTime(), duration: p.getDuration() }) : ''; })();");
                if (_miniPlayerWindow == null || string.IsNullOrWhiteSpace(result)) return;

                using var resultDocument = JsonDocument.Parse(result);
                if (resultDocument.RootElement.ValueKind != JsonValueKind.String) return;

                string? payload = resultDocument.RootElement.GetString();
                if (string.IsNullOrWhiteSpace(payload)) return;

                using var progressDocument = JsonDocument.Parse(payload);
                var progress = progressDocument.RootElement;
                if (!progress.TryGetProperty("current", out var currentElement) ||
                    !progress.TryGetProperty("duration", out var durationElement) ||
                    !currentElement.TryGetDouble(out double currentSeconds) ||
                    !durationElement.TryGetDouble(out double totalSeconds))
                    return;

                _miniPlayerWindow.UpdateProgress(currentSeconds, totalSeconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MiniPlayer progress] {ex.Message}");
            }
            finally
            {
                _isProgressUpdateInFlight = false;
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExplicitExit || !_appSettings.MinimizeToTray) return;
            e.Cancel = true;
            Hide();
        }

        public void AllowApplicationExit()
        {
            _isExplicitExit = true;
        }

        private void ExitApplication()
        {
            RunOnUi(() =>
            {
                _isExplicitExit = true;
                _trayManager.Dispose();
                if (_miniPlayerWindow != null)
                {
                    var miniPlayer = _miniPlayerWindow;
                    _miniPlayerWindow = null;
                    miniPlayer.Closed -= MiniPlayerWindow_Closed;
                    miniPlayer.Close();
                }
                System.Windows.Application.Current.Shutdown();
            });
        }

        private void TogglePlayback()
        {
            _ = SafeExecuteScriptAsync("(() => { const button = document.querySelector('ytmusic-player-bar #play-pause-button, #play-pause-button'); if (button) { button.click(); return true; } return false; })();");
        }

        private void SkipTrack(bool next)
        {
            _ = ExecuteYtmCommand(next ? "next" : "prev");
        }

        public async Task ExecuteYtmCommand(string command)
        {
            string script = command switch
            {
                "prev" => "(() => { const button = document.querySelector('.previous-button') || document.querySelector('tp-yt-paper-icon-button.previous-button'); button?.click(); })();",
                "next" => "(() => { const button = document.querySelector('.next-button') || document.querySelector('tp-yt-paper-icon-button.next-button'); button?.click(); })();",
                _ => "void 0;"
            };

            try
            {
                await SafeExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YTM komutu] {ex.Message}");
            }
        }

        private void UpdateMiniPlayerTrack()
        {
            _miniPlayerWindow?.UpdateTrack(_currentTitle, _currentArtist, _currentArtworkUrl);
        }

        private void UpdateMiniPlayerPlaybackState()
        {
            _miniPlayerWindow?.UpdatePlaybackState(_isYtmPlaying);
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.BeginInvoke(action);
        }

        protected override void OnClosed(EventArgs e)
        {
            _miniPlayerProgressTimer.Stop();
            _miniPlayerProgressTimer.Tick -= MiniPlayerProgressTimer_Tick;
            if (ReferenceEquals(Current, this)) Current = null;
            if (_miniPlayerWindow != null)
            {
                var miniPlayer = _miniPlayerWindow;
                _miniPlayerWindow = null;
                miniPlayer.Closed -= MiniPlayerWindow_Closed;
                miniPlayer.Close();
            }
            _hotkeyManager.Dispose();
            _trayManager.Dispose();
            base.OnClosed(e);
        }
    }
}