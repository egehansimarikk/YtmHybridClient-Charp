using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YtmHybridClient
{
    public class JamendoProvider : ILosslessProvider
    {
        public string Name => "Jamendo Music API";

        private const string ClientId = "6a4bec36";
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album = "")
        {
            try
            {
                string cleanTitle = CleanQuery(title);
                string cleanArtist = CleanQuery(artist);

                string requestUrl = $"https://api.jamendo.com/v3.0/tracks/?" +
                                   $"client_id={ClientId}" +
                                   $"&format=json" +
                                   $"&limit=1" +
                                   $"&namesearch={Uri.EscapeDataString(cleanTitle)}" +
                                   (string.IsNullOrEmpty(cleanArtist) ? "" : $"&artist_name={Uri.EscapeDataString(cleanArtist)}") +
                                   $"&audioformat=flac";

                Debug.WriteLine($"[{Name}] İstek atılıyor: {requestUrl}");

                var response = await _httpClient.GetStringAsync(requestUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var track = results[0];
                    if (track.TryGetProperty("audio", out var audioProp))
                    {
                        string streamUrl = audioProp.GetString() ?? "";
                        Debug.WriteLine($"[{Name}] Akış URL'si Bulundu: {streamUrl}");
                        return streamUrl;
                    }
                }

                Debug.WriteLine($"[{Name}] '{cleanArtist} - {cleanTitle}' için sonuç bulunamadı.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{Name} Hata] {ex.Message}");
            }

            return null;
        }

        private string CleanQuery(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return "";
            string cleaned = Regex.Replace(rawText, @"\([^\)]*\)", "");
            cleaned = Regex.Replace(cleaned, @"\[[^\]]*\]", "");
            cleaned = Regex.Replace(cleaned, @"(?i)(official video|lyric video|remastered|remaster|hd|ft\.|feat\.)", "");
            return cleaned.Trim();
        }
    }
}