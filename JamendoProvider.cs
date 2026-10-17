using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YtmHybridClient.Providers
{
    public class JamendoProvider : ILosslessProvider
    {
        public string Name => "Jamendo";

        private readonly string _clientId;
        private readonly bool _strict;

        // Asıl süre kontrolü CancellationToken ile yapılır (LosslessResolver), bu sadece üst sınır.
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public JamendoProvider(string clientId, bool strictMatching = true)
        {
            _clientId = clientId;
            _strict = strictMatching;
        }

        public async Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album = "", CancellationToken cancellationToken = default)
        {
            try
            {
                string cleanTitle = CleanQuery(title);
                string cleanArtist = CleanQuery(artist);

                if (string.IsNullOrEmpty(cleanTitle)) return null;

                // 1. deneme: başlık + sanatçı filtresi
                if (!string.IsNullOrEmpty(cleanArtist))
                {
                    string? url = await SearchAsync(
                        $"&namesearch={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}",
                        cleanTitle, cleanArtist, cancellationToken);
                    if (url != null) return url;
                }

                // 2. deneme: serbest arama ("başlık sanatçı"), sonuçlar yine doğrulanır
                string combined = $"{cleanTitle} {cleanArtist}".Trim();
                string? fallbackUrl = await SearchAsync(
                    $"&namesearch={Uri.EscapeDataString(combined)}",
                    cleanTitle, cleanArtist, cancellationToken);
                if (fallbackUrl != null) return fallbackUrl;

                Debug.WriteLine($"[{Name}] '{cleanArtist} - {cleanTitle}' için eşleşen parça yok.");
            }
            catch (Exception ex)
            {
                // Zaman aşımı / iptal: resolver halleder
                if (ex is OperationCanceledException)
                    throw;

                Debug.WriteLine($"[{Name} Hata] {ex.Message}");
            }

            return null;
        }

        private async Task<string?> SearchAsync(string extraQuery, string wantedTitle, string wantedArtist, CancellationToken ct)
        {
            string requestUrl = "https://api.jamendo.com/v3.0/tracks/?" +
                                $"client_id={Uri.EscapeDataString(_clientId)}" +
                                "&format=json" +
                                "&limit=10" +
                                "&audioformat=flac" +
                                extraQuery;

            Debug.WriteLine($"[{Name}] İstek: {requestUrl}");

            string response = await _httpClient.GetStringAsync(requestUrl, ct);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var track in results.EnumerateArray())
            {
                string trackName = track.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string trackArtist = track.TryGetProperty("artist_name", out var a) ? a.GetString() ?? "" : "";
                string audio = track.TryGetProperty("audio", out var au) ? au.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(audio))
                    continue;

                if (!IsMatch(wantedTitle, wantedArtist, trackName, trackArtist, _strict))
                {
                    Debug.WriteLine($"[{Name}] Elendi (yanlış eşleşme): {trackArtist} - {trackName}");
                    continue;
                }

                Debug.WriteLine($"[{Name}] Eşleşti: {trackArtist} - {trackName} -> {audio}");
                return audio;
            }

            return null;
        }

        // ---- Yanlış şarkı çalmasın diye eşleştirme kontrolü ----

        private static bool IsMatch(string wantedTitle, string wantedArtist, string trackTitle, string trackArtist, bool strict)
        {
            string wt = Normalize(wantedTitle);
            string tt = Normalize(trackTitle);
            if (wt.Length < 2 || tt.Length < 2) return false;

            // Kısa başlıklarda ("Ja", "Tu" gibi) içerme kontrolü yanıltıcı olur: tam eşleşme iste
            int minLen = strict ? 4 : 3;
            bool titleOk = (wt.Length < minLen || tt.Length < minLen)
                ? wt == tt
                : (tt.Contains(wt) || wt.Contains(tt));
            if (!titleOk) return false;

            // Gevşek mod: sanatçıya bakma
            if (!strict) return true;

            // Sanatçı bilinmiyorsa sadece başlığa güven
            if (string.IsNullOrWhiteSpace(wantedArtist)) return true;

            string ra = Normalize(trackArtist);
            if (ra.Length < 2) return false;

            foreach (var part in wantedArtist.Split(new[] { ',', '&', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string wa = Normalize(part);
                if (wa.Length >= 2 && (ra.Contains(wa) || wa.Contains(ra)))
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

        private static string CleanQuery(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return "";

            string cleaned = Regex.Replace(rawText, @"\([^\)]*\)", " ");   // (parantez içi)
            cleaned = Regex.Replace(cleaned, @"\[[^\]]*\]", " ");          // [köşeli parantez içi]
            cleaned = Regex.Replace(cleaned, @"(?i)\b(feat|ft)\b\.?.*$", " "); // "feat. X" ve sonrası
            cleaned = Regex.Replace(cleaned, @"(?i)\b(official\s+(music\s+)?video|lyrics?\s+video|remastered|remaster|hd)\b", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            return cleaned.Trim();
        }
    }
}