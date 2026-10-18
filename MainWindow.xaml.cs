using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using YtmHybridClient.Helpers;
using YtmHybridClient.Providers;
using YtmHybridClient.Services;

namespace YtmHybridClient
{
    public partial class MainWindow : Window
    {
        private readonly AudioEngine _audioEngine = new AudioEngine();
        private readonly SemaphoreSlim _audioEngineLock = new SemaphoreSlim(1, 1);
        private LosslessResolver? _audioResolver;
        private bool _isExternalAudioActive = false;
        private int _trackChangeVersion;

        // --- Hızlı kaynak değiştirme (Quick Source Toggle) ---
        // Sadece o anki şarkı için geçerlidir; yeni şarkıda otomatik çözümleme yeniden başlar.
        private static readonly string[] SourceCycle = { "Jamendo", "Audius", "SoundCloud", "YouTube Music" };
        private Dictionary<string, ILosslessProvider> _toggleProviders = new();
        private string _currentSource = "";
        private string _currentTitle = "";
        private string _currentArtist = "";
        private string _currentAlbum = "";
        private bool _isSwitchingSource;
        private string _currentStreamUrl = "";   // çalan harici akışın adresi (aygıt değişince yeniden açmak için)

        private AppSettings _appSettings = AppSettings.Load();

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _appSettings = AppSettings.Load();
                LanguageManager.ChangeLanguage(_appSettings.Language);
                InitAudioResolver();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Init Hata]: {ex.Message}");
            }

            InitializeWebViewAsync();
        }

        public void InitAudioResolver()
        {
            _appSettings = AppSettings.Load();
            _audioEngine.UseExclusive = _appSettings.UseWasapiExclusive;
            _audioEngine.DeviceId = _appSettings.AudioDeviceId ?? "";
            var activeProviders = new List<ILosslessProvider>();

            AddProvider(activeProviders, _appSettings.PrimarySource, _appSettings);
            AddProvider(activeProviders, _appSettings.SecondarySource, _appSettings);

            _audioResolver = new LosslessResolver(activeProviders);

            // Elle kaynak değiştirme için: seçili birincil/ikincil sıradan bağımsız, sabit iki kaynak
            _toggleProviders = new Dictionary<string, ILosslessProvider>
            {
                ["Jamendo"] = new JamendoProvider(_appSettings.JamendoClientId, _appSettings.StrictMatching),
                ["Audius"] = new AudiusProvider(_appSettings.StrictMatching),
                ["SoundCloud"] = new SoundCloudProvider(_appSettings.StrictMatching)
            };
        }

        private void AddProvider(List<ILosslessProvider> list, string sourceName, AppSettings settings)
        {
            if (sourceName == "JioSaavn")
                list.Add(new JioSaavnProvider());
            else if (sourceName == "Audius")
                list.Add(new AudiusProvider(settings.StrictMatching));
            else if (sourceName == "SoundCloud")
                list.Add(new SoundCloudProvider(settings.StrictMatching));
            else if (sourceName == "Jamendo")
                list.Add(new JamendoProvider(settings.JamendoClientId, settings.StrictMatching));
            else if (sourceName == "LocalFLAC")
                list.Add(new LocalFlacProvider(settings.LocalFlacFolder));
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

                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);

                await YtmWebView.EnsureCoreWebView2Async(env);

                YtmWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
                YtmWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                YtmWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                YtmWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;   // alert/confirm penceresi
                YtmWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                YtmWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

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

                string interceptorScript = @"
                    (function () {
                        window.isExternalAudioPlaying = false;

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

                        function lockMediaElements() {
                            const mediaElements = document.querySelectorAll('video, audio');
                            mediaElements.forEach(media => {
                                if (window.isExternalAudioPlaying) {
                                    if (media.volume > 0) media.__savedVolume = media.volume;
                                    media.muted = true;
                                    media.volume = 0;
                                } else {
                                    media.muted = false;
                                    if (media.__savedVolume !== undefined) {
                                        media.volume = media.__savedVolume;
                                        delete media.__savedVolume;
                                    }
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

                        let lastTrackKey = '';
                        function checkTrackChange() {
                            try {
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
                        string title = root.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                        string artist = root.TryGetProperty("artist", out var aProp) ? aProp.GetString() ?? "" : "";
                        string album = root.TryGetProperty("album", out var alProp) ? alProp.GetString() ?? "" : "";
                        _currentTitle = title;
                        _currentArtist = artist;
                        _currentAlbum = album;
                        _currentSource = "";
                        int trackChangeVersion = Interlocked.Increment(ref _trackChangeVersion);
                        await HandleTrackChangedAsync(title, artist, album, trackChangeVersion);
                        break;

                    case "JS_READY":
                        System.Diagnostics.Debug.WriteLine("[JS] Script yüklendi.");
                        if (!_isExternalAudioActive)
                            await UpdateStatusBadgeAsync("✔ Script hazır", "#455A64");
                        break;

                    case "JS_ERROR":
                        string jsErr = root.TryGetProperty("message", out var jeProp) ? jeProp.GetString() ?? "" : "";
                        System.Diagnostics.Debug.WriteLine($"[JS HATA]: {jsErr}");
                        await UpdateStatusBadgeAsync("⚠ JS hatası", "#B71C1C");
                        break;

                    case "PLAYER_STATE":
                        if (_isExternalAudioActive)
                        {
                            string state = root.TryGetProperty("state", out var sProp) ? sProp.GetString() ?? "" : "";
                            if (state == "PAUSED") _audioEngine.Pause();
                            else if (state == "PLAYING") _audioEngine.Resume();
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

        private async Task HandleTrackChangedAsync(string title, string artist, string album, int trackChangeVersion)
        {
            bool externalPlaybackStarted = false;
            System.Diagnostics.Debug.WriteLine($"[1] Parça değişti: {artist} - {title}");

            try
            {
                await UpdateStatusBadgeAsync("🔍 Aranıyor...", "#666666");

                if (!await _audioEngineLock.WaitAsync(TimeSpan.FromSeconds(3)))
                {
                    System.Diagnostics.Debug.WriteLine("[!] Ses motoru kilidi 3 sn içinde alınamadı.");
                    return;
                }
                try
                {
                    if (trackChangeVersion != Volatile.Read(ref _trackChangeVersion))
                        return;

                    _isExternalAudioActive = false;
                    _audioEngine.Stop();
                }
                finally
                {
                    _audioEngineLock.Release();
                }

                var resolver = _audioResolver;
                if (resolver == null)
                    return;

                // Toplam çözümleme bütçesi: 7 sn (her kaynağa ayrıca 3 sn verilir)
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                var result = await Task.Run(
                        () => resolver.ResolveAudioAsync(title, artist, album, cts.Token),
                        cts.Token)
                    .WaitAsync(cts.Token);

                System.Diagnostics.Debug.WriteLine($"[2] Çözüldü: {result.ProviderName} -> {result.StreamUrl ?? "(yok)"}");

                if (trackChangeVersion != Volatile.Read(ref _trackChangeVersion) || string.IsNullOrWhiteSpace(result.StreamUrl))
                    return;

                await UpdateStatusBadgeAsync($"⏳ {result.ProviderName} açılıyor...", "#FFA000");

                if (!await _audioEngineLock.WaitAsync(TimeSpan.FromSeconds(3)))
                {
                    System.Diagnostics.Debug.WriteLine("[!] Ses motoru kilidi (çalma) 3 sn içinde alınamadı.");
                    return;
                }
                try
                {
                    if (trackChangeVersion != Volatile.Read(ref _trackChangeVersion))
                        return;

                    System.Diagnostics.Debug.WriteLine("[3] Çalma başlıyor");
                    bool playbackStarted;
                    try
                    {
                        playbackStarted = await _audioEngine
                            .PlayStreamAsync(result.StreamUrl, TimeSpan.FromSeconds(5))
                            .WaitAsync(TimeSpan.FromSeconds(7));
                    }
                    catch (TimeoutException)
                    {
                        System.Diagnostics.Debug.WriteLine("[!] PlayStreamAsync 7 sn içinde dönmedi.");
                        playbackStarted = false;
                    }
                    System.Diagnostics.Debug.WriteLine($"[4] Çalma sonucu: {playbackStarted}");

                    if (trackChangeVersion != Volatile.Read(ref _trackChangeVersion) || !playbackStarted)
                        return;

                    _isExternalAudioActive = true;
                    await SafeExecuteScriptAsync("window.setYtmMute(true);");
                    _currentSource = result.ProviderName;
                    _currentStreamUrl = result.StreamUrl ?? "";
                    await UpdateStatusBadgeAsync(GetProviderBadgeText(result.ProviderName), GetBadgeColor(result.ProviderName));
                    externalPlaybackStarted = true;
                }
                finally
                {
                    _audioEngineLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Resolver Hata]: {ex.Message}");
            }
            finally
            {
                // Bu parça hâlâ güncelse ve harici ses başlamadıysa rozet MUTLAKA yeniden çizilir.
                if (trackChangeVersion == Volatile.Read(ref _trackChangeVersion) && !externalPlaybackStarted)
                {
                    bool lockTaken = false;
                    try
                    {
                        lockTaken = await _audioEngineLock.WaitAsync(TimeSpan.FromSeconds(2));
                        if (lockTaken && trackChangeVersion == Volatile.Read(ref _trackChangeVersion))
                        {
                            _isExternalAudioActive = false;
                            _audioEngine.Stop();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Audio fallback Hata]: {ex.Message}");
                    }
                    finally
                    {
                        if (lockTaken) _audioEngineLock.Release();
                    }

                    if (trackChangeVersion == Volatile.Read(ref _trackChangeVersion))
                    {
                        bool smartFallback = _appSettings.EnableSmartFallback;
                        _currentSource = "YouTube Music";

                        try
                        {
                            // Akıllı yedekleme açık: YouTube Music'in kendi sesi açılır.
                            // Kapalı: YouTube Music sessiz kalır, lossless bulunamayan şarkı çalmaz.
                            await SafeExecuteScriptAsync(smartFallback ? "window.setYtmMute(false);" : "window.setYtmMute(true);");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[YTM ses ayarı Hata]: {ex.Message}");
                        }
                        finally
                        {
                            if (smartFallback)
                                await UpdateStatusBadgeAsync("▶ YouTube Music", "#FF0055");
                            else
                                await UpdateStatusBadgeAsync("🔇 Lossless bulunamadı", "#757575");
                        }
                    }
                }
            }
        }

        private static string GetProviderBadgeText(string providerName)
        {
            return providerName switch
            {
                "Jamendo" => "🎵 Jamendo (FLAC)",
                "JioSaavn" => "🎵 JioSaavn (320kbps)",
                "Audius" => "🎵 Audius (320kbps)",
                "SoundCloud" => "🎵 SoundCloud",
                "Local FLAC" or "LocalFLAC" => "🎵 Local FLAC",
                "YouTube Music" => "▶ YouTube Music",
                _ => $"🎵 {providerName}"
            };
        }

        private static string GetBadgeColor(string providerName)
        {
            return providerName switch
            {
                "Audius" => "#8E24AA",
                "SoundCloud" => "#FF5500",
                "YouTube Music" => "#FF0055",
                _ => "#00C853"
            };
        }

        // ================= Hızlı Kaynak Değiştirme =================

        private async void BtnToggleSource_Click(object sender, RoutedEventArgs e) => await ToggleSourceAsync();

        private async void BadgeSource_MouseUp(object sender, MouseButtonEventArgs e) => await ToggleSourceAsync();

        /// <summary>Jamendo → Audius → SoundCloud → YouTube Music → Jamendo ... sırayla geçiş yapar (sadece mevcut şarkı için).</summary>
        private async Task ToggleSourceAsync()
        {
            if (_isSwitchingSource || string.IsNullOrEmpty(_currentTitle))
                return;

            _isSwitchingSource = true;
            BtnToggleSource.IsEnabled = false;

            int index = Array.IndexOf(SourceCycle, _currentSource);   // bilinmiyorsa -1 -> Jamendo
            string target = SourceCycle[(index + 1) % SourceCycle.Length];
            _currentSource = target;

            // Devam eden otomatik çözümlemeyi iptal eder (sürüm numarası değişir)
            int version = Interlocked.Increment(ref _trackChangeVersion);

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Toggle] Hedef kaynak: {target}");

                // 1) Çalan harici sesi hemen durdur
                if (!await StopEngineAsync(version))
                    return;

                // 2) YouTube Music: kendi sesi açılır
                if (target == "YouTube Music")
                {
                    await SetYtmMuteAsync(false);
                    await UpdateStatusBadgeAsync(GetProviderBadgeText(target), GetBadgeColor(target));
                    return;
                }

                // 3) Harici kaynak: YouTube Music'i hemen sustur, akış adresini bul
                await UpdateStatusBadgeAsync($"🔍 {target} aranıyor...", "#666666");
                await SetYtmMuteAsync(true);
                TimeSpan position = await GetYtmPositionAsync();

                string? url = null;
                if (_toggleProviders.TryGetValue(target, out var provider))
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                        url = await provider.GetFlacStreamUrlAsync(_currentTitle, _currentArtist, _currentAlbum, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Toggle] {target} arama hatası: {ex.Message}");
                    }
                }

                if (version != Volatile.Read(ref _trackChangeVersion)) return;   // parça değişti

                if (string.IsNullOrEmpty(url))
                {
                    await FailSwitchAsync(target, "bulunamadı");
                    return;
                }

                await UpdateStatusBadgeAsync($"⏳ {target} açılıyor...", "#FFA000");

                // 4) Yeni akışı YouTube Music'in konumundan başlat
                if (!await _audioEngineLock.WaitAsync(TimeSpan.FromSeconds(8)))
                {
                    await FailSwitchAsync(target, "ses motoru meşgul");
                    return;
                }

                bool started = false;
                try
                {
                    if (version != Volatile.Read(ref _trackChangeVersion)) return;

                    try
                    {
                        started = await _audioEngine
                            .PlayStreamAsync(url, TimeSpan.FromSeconds(5), position)
                            .WaitAsync(TimeSpan.FromSeconds(7));
                    }
                    catch (TimeoutException)
                    {
                        started = false;
                    }

                    if (version != Volatile.Read(ref _trackChangeVersion)) return;

                    if (started)
                    {
                        _isExternalAudioActive = true;
                        _currentStreamUrl = url;
                    }
                }
                finally
                {
                    _audioEngineLock.Release();
                }

                if (started)
                    await UpdateStatusBadgeAsync(GetProviderBadgeText(target), GetBadgeColor(target));
                else
                    await FailSwitchAsync(target, "açılamadı");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toggle Hata]: {ex.Message}");
            }
            finally
            {
                _isSwitchingSource = false;
                BtnToggleSource.IsEnabled = true;
            }
        }

        private async Task<bool> StopEngineAsync(int version)
        {
            if (!await _audioEngineLock.WaitAsync(TimeSpan.FromSeconds(8)))
            {
                await UpdateStatusBadgeAsync("⚠ Ses motoru meşgul", "#B71C1C");
                return false;
            }

            try
            {
                if (version != Volatile.Read(ref _trackChangeVersion))
                    return false;

                _isExternalAudioActive = false;
                _audioEngine.Stop();
                return true;
            }
            finally
            {
                _audioEngineLock.Release();
            }
        }

        /// <summary>
        /// Çıkış aygıtını anında değiştirir (Ayarlar penceresinden çağrılır). Çalan harici akış kesilmeden yeni
        /// aygıta taşınır; konum ve duraklatma durumu korunur. Harici akış çalmıyorsa sadece sonraki akışta kullanılır.
        /// </summary>
        public async Task ApplyAudioDeviceAsync(string deviceId)
        {
            _audioEngine.DeviceId = deviceId ?? "";
            await ReapplyAudioOutputAsync();
        }

        // Ses aygıtı / WASAPI modu değişince çalan harici akışı yeni çıkışa taşır
        private async Task ReapplyAudioOutputAsync()
        {
            if (!_isExternalAudioActive || string.IsNullOrEmpty(_currentStreamUrl))
                return;

            int version = Volatile.Read(ref _trackChangeVersion);

            if (!await _audioEngineLock.WaitAsync(TimeSpan.FromSeconds(8)))
                return;

            bool failed = false;
            try
            {
                if (version != Volatile.Read(ref _trackChangeVersion) || !_isExternalAudioActive)
                    return;

                bool wasPaused = _audioEngine.IsPaused;

                // 1) Hızlı yol: akış açık kalır, sadece WASAPI çıkışı yeni aygıta taşınır
                bool switched;
                try
                {
                    switched = await _audioEngine
                        .ChangeOutputDeviceAsync(_audioEngine.DeviceId)
                        .WaitAsync(TimeSpan.FromSeconds(6));
                }
                catch (TimeoutException)
                {
                    // Motor takılmış olabilir; ona dokunmadan hata göster
                    System.Diagnostics.Debug.WriteLine("[Aygıt] Aygıt değiştirme 6 sn içinde bitmedi.");
                    failed = true;
                    return;
                }

                if (switched)
                {
                    System.Diagnostics.Debug.WriteLine("[Aygıt] Çalan akış yeni aygıta taşındı.");
                    return;
                }

                if (version != Volatile.Read(ref _trackChangeVersion))
                    return;

                // 2) Yedek yol: akışı yeni aygıtta aynı konumdan baştan aç
                TimeSpan position = _audioEngine.CurrentPosition;
                System.Diagnostics.Debug.WriteLine($"[Aygıt] Yedek yol: akış yeniden açılıyor (konum: {position:mm\\:ss})");

                bool started;
                try
                {
                    started = await _audioEngine
                        .PlayStreamAsync(_currentStreamUrl, TimeSpan.FromSeconds(5), position)
                        .WaitAsync(TimeSpan.FromSeconds(7));
                }
                catch (TimeoutException)
                {
                    started = false;
                }

                if (version != Volatile.Read(ref _trackChangeVersion))
                    return;

                if (started)
                {
                    if (wasPaused) _audioEngine.Pause();
                }
                else
                {
                    failed = true;
                }
            }
            finally
            {
                _audioEngineLock.Release();
            }

            if (failed)
                await FailSwitchAsync(string.IsNullOrEmpty(_currentSource) ? "Ses" : _currentSource, "yeni aygıtta açılamadı");
        }

        // Seçilen kaynak çalmadıysa sessiz kalma: YouTube Music'in sesini geri aç
        private async Task FailSwitchAsync(string target, string reason)
        {
            _isExternalAudioActive = false;
            await SetYtmMuteAsync(false);
            await UpdateStatusBadgeAsync($"⚠ {target}: {reason}", "#FF8F00");
        }

        private async Task SetYtmMuteAsync(bool mute)
        {
            try
            {
                await SafeExecuteScriptAsync(mute ? "window.setYtmMute(true);" : "window.setYtmMute(false);");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YTM ses ayarı Hata]: {ex.Message}");
            }
        }

        // YouTube Music'in o anki konumu (yeni kaynağı aynı yerden başlatmak için)
        private async Task<TimeSpan> GetYtmPositionAsync()
        {
            try
            {
                string result = await SafeExecuteScriptAsync("(document.querySelector('video') || {}).currentTime || 0");
                if (double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                    && seconds > 0 && !double.IsInfinity(seconds))
                    return TimeSpan.FromSeconds(seconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YTM konum Hata]: {ex.Message}");
            }
            return TimeSpan.Zero;
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
            // Ayarlar kaydedilirken InitAudioResolver motoru güncellediği için eski değeri önce al
            bool oldExclusive = _audioEngine.UseExclusive;

            var settingsWindow = new SettingsWindow(AppSettings.Load()) { Owner = this };
            if (settingsWindow.ShowDialog() == true)
            {
                _appSettings = AppSettings.Load();
                LanguageManager.ChangeLanguage(_appSettings.Language);
                InitAudioResolver();
                System.Diagnostics.Debug.WriteLine("[Settings] Ayarlar kaydedildi, dil ve ses çözücü zinciri yenilendi.");

                // Aygıt, Ayarlar penceresinde seçilirken zaten uygulandı. WASAPI modu değiştiyse çalan akışı yeni modda aç.
                if (_audioEngine.UseExclusive != oldExclusive)
                    await ReapplyAudioOutputAsync();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioEngine.Dispose();
            base.OnClosed(e);
        }
    }
}