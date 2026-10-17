using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YtmHybridClient.Providers
{
    /// <summary>
    /// Audius (audius.co): bağımsız sanatçıların yüklediği, ücretsiz ve herkese açık bir müzik platformu.
    /// API anahtarı gerekmez. Katalog ana akım pop kadar geniş değildir (elektronik, hip-hop, bağımsız ağırlıklı).
    /// Not: Audius kullanım şartları sanatçıya atıf yapılmasını ister; rozet "Audius" gösterir.
    /// </summary>
    public class AudiusProvider : ILosslessProvider
    {
        public string Name => "Audius";

        private const string BaseUrl = "https://discoveryprovider.audius.co";
        private const string AppName = "YtmHybridClient";

        private static readonly HttpClient _http;

        static AudiusProvider()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("YtmHybridClient/1.0");
        }

        private readonly bool _strict;

        public AudiusProvider(bool strictMatching = true)
        {
            _strict = strictMatching;
        }

        public async Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Önce "başlık + sanatçı", bulunamazsa sadece başlık ile ara
            string[] queries =
            {
                $"{title} {artist}".Trim(),
                title.Trim()
            };

            foreach (var q in queries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? id = await FindMatchingTrackIdAsync(q, title, artist, _strict, cancellationToken);
                if (id == null) continue;

                string? url = await GetStreamUrlAsync(id, cancellationToken);
                if (!string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Debug.WriteLine($"[Audius] Bulundu: {url}");
                    return url;
                }
            }

            System.Diagnostics.Debug.WriteLine("[Audius] Eşleşen parça yok.");
            return null;
        }

        private static async Task<string?> FindMatchingTrackIdAsync(string query, string wantedTitle, string wantedArtist, bool strict, CancellationToken ct)
        {
            string url = $"{BaseUrl}/v1/tracks/search?query={Uri.EscapeDataString(query)}&limit=5&app_name={AppName}";
            System.Diagnostics.Debug.WriteLine($"[Audius] İstek: {url}");

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Audius] HTTP {(int)response.StatusCode}");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var track in data.EnumerateArray())
            {
                string trackTitle = track.TryGetProperty("title", out var tp) ? tp.GetString() ?? "" : "";
                string userName = "";
                if (track.TryGetProperty("user", out var user) && user.TryGetProperty("name", out var np))
                    userName = np.GetString() ?? "";

                if (track.TryGetProperty("is_streamable", out var st) &&
                    st.ValueKind == JsonValueKind.False)
                    continue;

                if (!IsMatch(wantedTitle, wantedArtist, trackTitle, userName, strict))
                    continue;

                if (track.TryGetProperty("id", out var idProp))
                    return idProp.GetString();
            }

            return null;
        }

        private static async Task<string?> GetStreamUrlAsync(string trackId, CancellationToken ct)
        {
            string endpoint = $"{BaseUrl}/v1/tracks/{trackId}/stream?app_name={AppName}";

            try
            {
                // no_redirect=true: akış adresini JSON olarak döndürür
                using var response = await _http.GetAsync(endpoint + "&no_redirect=true", ct);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String)
                    {
                        string? streamUrl = d.GetString();
                        if (!string.IsNullOrEmpty(streamUrl)) return streamUrl;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Audius] Akış adresi alınamadı: {ex.Message}");
            }

            // Yedek: yönlendiren (redirect) uç noktanın kendisi
            return endpoint;
        }

        // ---- Yanlış şarkı çalmasın diye eşleştirme kontrolü ----

        private static bool IsMatch(string wantedTitle, string wantedArtist, string trackTitle, string userName, bool strict)
        {
            string wt = Normalize(wantedTitle);
            string tt = Normalize(trackTitle);
            if (wt.Length < 2 || tt.Length < 2) return false;

            // Kısa başlıklarda içerme kontrolü yanıltıcı olur: tam eşleşme iste
            int minLen = strict ? 4 : 3;
            bool titleOk = (wt.Length < minLen || tt.Length < minLen)
                ? wt == tt
                : (tt.Contains(wt) || wt.Contains(tt));
            if (!titleOk) return false;

            // Gevşek mod: sanatçıya bakma
            if (!strict) return true;

            if (string.IsNullOrWhiteSpace(wantedArtist)) return true;

            string user = Normalize(userName);
            string haystack = user + Normalize(trackTitle);
            foreach (var part in wantedArtist.Split(new[] { ',', '&', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string a = Normalize(part);
                if (a.Length < 2) continue;
                if (haystack.Contains(a) || (user.Length >= 2 && a.Contains(user)))
                    return true;
            }
            return false;
        }

        private static string Normalize(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}