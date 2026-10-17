using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YtmHybridClient.Providers
{
    /// <summary>
    /// SoundCloud kaynağı. Resmi olmayan web API'sini (api-v2) kullanır; client_id herkese açık
    /// soundcloud.com sayfasının JS dosyalarından otomatik bulunur.
    /// Notlar:
    ///  - Bu kullanım SoundCloud'un resmi API şartları kapsamında değildir ve herhangi bir zamanda bozulabilir.
    ///  - Sadece tam olarak çalınabilir (playable) parçalar kullanılır; önizleme/kısıtlı parçalar atlanır.
    ///  - Sadece "progressive" (doğrudan MP3) akışlar desteklenir. Yalnızca HLS sunan parçalar bulunamadı sayılır.
    ///    (API v2'de "httpmp3" diye bir protokol yoktur; değerler "progressive" ve "hls" biçimindedir.)
    /// </summary>
    public class SoundCloudProvider : ILosslessProvider
    {
        public string Name => "SoundCloud";

        private const string HomeUrl = "https://soundcloud.com";
        private const string SearchUrl = "https://api-v2.soundcloud.com/search/tracks";

        private static readonly HttpClient _http;
        private static readonly SemaphoreSlim _clientIdLock = new SemaphoreSlim(1, 1);
        private static string? _clientId;   // tüm örnekler arasında paylaşılır

        private static readonly Regex VariantRegex = new Regex(
            @"\b(remix|cover|slowed|sped\s*up|speed\s*up|nightcore|reverb|bass\s*boosted|8d|karaoke|instrumental|mashup)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly bool _strict;

        static SoundCloudProvider()
        {
            // Asıl süre kontrolü CancellationToken ile yapılır (LosslessResolver / toggle), bu sadece üst sınır.
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
        }

        public SoundCloudProvider(bool strictMatching = true)
        {
            _strict = strictMatching;

            // client_id'yi arka planda önceden al: ilk aramada 3-4 saniyelik süre bununla yenmesin
            if (_clientId == null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                        await GetClientIdAsync(false, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SoundCloud] client_id ön yükleme hatası: {ex.Message}");
                    }
                });
            }
        }

        public async Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album, CancellationToken cancellationToken = default)
        {
            string cleanTitle = CleanQuery(title);
            string cleanArtist = CleanQuery(artist);
            if (string.IsNullOrEmpty(cleanTitle)) return null;

            // Önce "başlık sanatçı", bulunamazsa sadece başlık
            string[] queries = { $"{cleanTitle} {cleanArtist}".Trim(), cleanTitle };

            string? previous = null;
            foreach (var q in queries)
            {
                if (q == previous) continue;
                previous = q;

                cancellationToken.ThrowIfCancellationRequested();

                string? url = await SearchAndResolveAsync(q, cleanTitle, cleanArtist, cancellationToken);
                if (!string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] Bulundu: {url}");
                    return url;
                }
            }

            System.Diagnostics.Debug.WriteLine("[SoundCloud] Eşleşen, çalınabilir parça yok.");
            return null;
        }

        // ---------------- Arama ve akış adresi çözümleme ----------------

        private async Task<string?> SearchAndResolveAsync(string query, string wantedTitle, string wantedArtist, CancellationToken ct)
        {
            string? json = await GetWithClientIdAsync(
                id => $"{SearchUrl}?q={Uri.EscapeDataString(query)}&client_id={id}&limit=10", ct);
            if (json == null) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("collection", out var collection) ||
                collection.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var track in collection.EnumerateArray())
            {
                string trackTitle = GetString(track, "title");
                if (string.IsNullOrEmpty(trackTitle)) continue;

                // Sadece tam olarak çalınabilir parçalar (önizleme / kısıtlı olanları atla)
                if (track.TryGetProperty("streamable", out var st) && st.ValueKind == JsonValueKind.False) continue;
                if (track.TryGetProperty("access", out var acc) && acc.ValueKind == JsonValueKind.String &&
                    !string.Equals(acc.GetString(), "playable", StringComparison.OrdinalIgnoreCase)) continue;
                string policy = GetString(track, "policy");
                if (policy.Equals("SNIP", StringComparison.OrdinalIgnoreCase) ||
                    policy.Equals("BLOCK", StringComparison.OrdinalIgnoreCase)) continue;

                string uploader = track.TryGetProperty("user", out var u) ? GetString(u, "username") : "";
                string publisherArtist = track.TryGetProperty("publisher_metadata", out var pm) &&
                                         pm.ValueKind == JsonValueKind.Object ? GetString(pm, "artist") : "";

                if (!IsMatch(wantedTitle, wantedArtist, trackTitle, uploader, publisherArtist, _strict))
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] Elendi: {uploader} - {trackTitle}");
                    continue;
                }

                string? transcodingUrl = FindProgressiveTranscodingUrl(track);
                if (transcodingUrl == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] Progressive akış yok (sadece HLS): {trackTitle}");
                    continue;
                }

                string authorization = GetString(track, "track_authorization");
                string? streamUrl = await ResolveStreamUrlAsync(transcodingUrl, authorization, ct);
                if (!string.IsNullOrEmpty(streamUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] Eşleşti: {uploader} - {trackTitle}");
                    return streamUrl;
                }
            }

            return null;
        }

        // media.transcodings içinden doğrudan MP3 (progressive) akışı seç
        private static string? FindProgressiveTranscodingUrl(JsonElement track)
        {
            if (!track.TryGetProperty("media", out var media) ||
                !media.TryGetProperty("transcodings", out var transcodings) ||
                transcodings.ValueKind != JsonValueKind.Array)
                return null;

            string? fallback = null;

            foreach (var t in transcodings.EnumerateArray())
            {
                if (t.TryGetProperty("snipped", out var sn) && sn.ValueKind == JsonValueKind.True) continue;

                string url = GetString(t, "url");
                if (string.IsNullOrEmpty(url)) continue;

                if (!t.TryGetProperty("format", out var format)) continue;
                string protocol = GetString(format, "protocol");
                string mime = GetString(format, "mime_type");

                if (!protocol.Equals("progressive", StringComparison.OrdinalIgnoreCase)) continue;

                if (mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
                    return url;          // en uygunu: progressive MP3

                fallback ??= url;        // progressive ama başka biçim
            }

            return fallback;
        }

        // Transcoding adresine client_id (ve track_authorization) ekleyip nihai akış adresini al
        private static async Task<string?> ResolveStreamUrlAsync(string transcodingUrl, string authorization, CancellationToken ct)
        {
            string separator = transcodingUrl.Contains('?') ? "&" : "?";

            string? json = await GetWithClientIdAsync(id =>
            {
                var sb = new StringBuilder(transcodingUrl).Append(separator).Append("client_id=").Append(id);
                if (!string.IsNullOrEmpty(authorization))
                    sb.Append("&track_authorization=").Append(Uri.EscapeDataString(authorization));
                return sb.ToString();
            }, ct);

            if (json == null) return null;

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString()
                : null;
        }

        // ---------------- client_id yönetimi ----------------

        // İstek atar; 401/403 gelirse client_id'yi yenileyip bir kez daha dener
        private static async Task<string?> GetWithClientIdAsync(Func<string, string> buildUrl, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string? id = await GetClientIdAsync(forceRefresh: attempt > 0, ct);
                if (id == null) return null;

                using var response = await _http.GetAsync(buildUrl(id), ct);

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] HTTP {(int)response.StatusCode}, client_id yenilenecek.");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] HTTP {(int)response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadAsStringAsync(ct);
            }

            return null;
        }

        private static async Task<string?> GetClientIdAsync(bool forceRefresh, CancellationToken ct)
        {
            if (!forceRefresh && _clientId != null) return _clientId;

            await _clientIdLock.WaitAsync(ct);
            try
            {
                if (!forceRefresh && _clientId != null) return _clientId;

                _clientId = await ScrapeClientIdAsync(ct);
                System.Diagnostics.Debug.WriteLine(_clientId != null
                    ? "[SoundCloud] client_id bulundu."
                    : "[SoundCloud] client_id bulunamadı.");
                return _clientId;
            }
            finally
            {
                _clientIdLock.Release();
            }
        }

        // soundcloud.com ana sayfasındaki JS dosyalarını tarayıp client_id'yi bulur
        private static async Task<string?> ScrapeClientIdAsync(CancellationToken ct)
        {
            string home = await _http.GetStringAsync(HomeUrl, ct);

            var scriptUrls = Regex.Matches(home, @"https://a-v2\.sndcdn\.com/assets/[^""'\s>]+\.js")
                                  .Select(m => m.Value)
                                  .Distinct()
                                  .Reverse()          // client_id genelde son dosyalarda olur
                                  .Take(8)
                                  .ToList();

            foreach (var scriptUrl in scriptUrls)
            {
                ct.ThrowIfCancellationRequested();

                string js;
                try
                {
                    js = await _http.GetStringAsync(scriptUrl, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SoundCloud] Script indirilemedi: {ex.Message}");
                    continue;
                }

                var match = Regex.Match(js, @"client_id\s*[:=]\s*""([0-9a-zA-Z]{32})""");
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }

        // ---------------- Eşleştirme ----------------

        private static bool IsMatch(string wantedTitle, string wantedArtist, string trackTitle,
                                    string uploader, string publisherArtist, bool strict)
        {
            string wt = Normalize(wantedTitle);
            string tt = Normalize(trackTitle);
            if (wt.Length < 2 || tt.Length < 2) return false;

            // SoundCloud başlıkları sık sık "Sanatçı - Şarkı" biçimindedir: içerme kontrolü gerekli.
            // Kısa başlıklarda içerme yanıltıcı olur, tam eşleşme iste.
            int minLen = strict ? 4 : 3;
            bool titleOk = (wt.Length < minLen || tt.Length < minLen)
                ? wt == tt
                : (tt.Contains(wt) || wt.Contains(tt));
            if (!titleOk) return false;

            // Gevşek mod: sadece başlık
            if (!strict) return true;

            // Katı mod: remix/cover/slowed gibi farklı sürümleri, istenmediyse ele
            if (HasUnwantedVariant(wantedTitle, trackTitle)) return false;

            if (string.IsNullOrWhiteSpace(wantedArtist)) return true;

            string up = Normalize(uploader);
            string haystack = up + Normalize(publisherArtist) + tt;

            foreach (var part in wantedArtist.Split(new[] { ',', '&', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string a = Normalize(part);
                if (a.Length >= 2 && haystack.Contains(a))
                    return true;
            }

            // Yükleyen adı istenen sanatçı metninde geçiyorsa (ör. "RAIKAHO Official" / "RAIKAHO")
            return up.Length >= 2 && Normalize(wantedArtist).Contains(up);
        }

        private static bool HasUnwantedVariant(string wantedTitle, string trackTitle)
        {
            foreach (Match m in VariantRegex.Matches(trackTitle))
            {
                if (!wantedTitle.Contains(m.Value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static string CleanQuery(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string cleaned = Regex.Replace(raw, @"\([^\)]*\)|\[[^\]]*\]", " ");
            cleaned = Regex.Replace(cleaned, @"(?i)\b(feat|ft)\b\.?.*$", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            return cleaned.Trim();
        }

        private static string GetString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(property, out var p) &&
                   p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? ""
                : "";
        }
    }
}