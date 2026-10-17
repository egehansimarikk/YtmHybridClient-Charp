using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YtmHybridClient.Providers
{
    public class JioSaavnProvider : ILosslessProvider
    {
        public string Name => "JioSaavn";

        // Resmi olmayan topluluk API'leri. Sırayla denenir; biri kapalıysa diğerine geçilir.
        // Çalıştığını tarayıcıda şu şekilde test edebilirsin:
        //   https://saavn.dev/api/search/songs?query=coldplay&limit=1
        private static readonly string[] SearchEndpoints =
        {
            "https://saavn.dev/api/search/songs",
            "https://saavn.me/search/songs"
        };

        private static readonly HttpClient _httpClient;

        static JioSaavnProvider()
        {
            // Asıl süre kontrolü CancellationToken ile yapılır (LosslessResolver), bu sadece üst sınır.
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/128.0.0.0 Safari/537.36");
        }

        public async Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            string query = Uri.EscapeDataString($"{title} {artist}".Trim());

            foreach (var endpoint in SearchEndpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    string searchUrl = $"{endpoint}?query={query}&limit=1";
                    System.Diagnostics.Debug.WriteLine($"[JioSaavn] İstek: {searchUrl}");

                    using var response = await _httpClient.GetAsync(searchUrl, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"[JioSaavn] {endpoint} -> HTTP {(int)response.StatusCode}");
                        continue;
                    }

                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    string? url = ParseBestStreamUrl(json);

                    if (!string.IsNullOrEmpty(url))
                    {
                        System.Diagnostics.Debug.WriteLine($"[JioSaavn] Bulundu: {url}");
                        return url;
                    }

                    System.Diagnostics.Debug.WriteLine($"[JioSaavn] {endpoint}: uygun sonuç yok.");
                }
                catch (OperationCanceledException)
                {
                    throw; // zaman aşımı / iptal: resolver halleder
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[JioSaavn Hata] {endpoint}: {ex.Message}");
                }
            }

            return null;
        }

        private static string? ParseBestStreamUrl(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var dataProp)) return null;

            JsonElement resultsArray;
            if (dataProp.ValueKind == JsonValueKind.Array)
                resultsArray = dataProp;
            else if (dataProp.ValueKind == JsonValueKind.Object &&
                     dataProp.TryGetProperty("results", out var resProp) &&
                     resProp.ValueKind == JsonValueKind.Array)
                resultsArray = resProp;
            else
                return null;

            if (resultsArray.GetArrayLength() == 0) return null;

            var firstSong = resultsArray[0];
            if (!firstSong.TryGetProperty("downloadUrl", out var downloadUrls) ||
                downloadUrls.ValueKind != JsonValueKind.Array)
                return null;

            string? best = null;
            foreach (var urlObj in downloadUrls.EnumerateArray())
            {
                string quality = urlObj.TryGetProperty("quality", out var qProp) ? qProp.GetString() ?? "" : "";

                // Bazı sürümlerde alan adı "url", bazılarında "link"
                string url = "";
                if (urlObj.TryGetProperty("url", out var uProp)) url = uProp.GetString() ?? "";
                else if (urlObj.TryGetProperty("link", out var lProp)) url = lProp.GetString() ?? "";

                if (string.IsNullOrEmpty(url)) continue;

                if (quality == "320kbps")
                    return url;          // en iyisi

                best = url;              // 320 yoksa listedeki en son (genelde en yüksek) kalite
            }

            return best;
        }
    }
}