using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace YtmHybridClient.Helpers
{
    public sealed record UpdateInfo(string TagName, string DownloadUrl, string ReleasePageUrl);

    public static class GitHubUpdateChecker
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/egehansimarikk/YtmHybridClient-Charp/releases/latest";

        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("YtmHybridClient-Updater/1.0");

                string json = await client.GetStringAsync(LatestReleaseApi);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagProp)) return null;
                string rawTag = tagProp.GetString() ?? "";
                string htmlUrl = root.TryGetProperty("html_url", out var htmlProp)
                    ? htmlProp.GetString() ?? "https://github.com/egehansimarikk/YtmHybridClient-Charp/releases"
                    : "https://github.com/egehansimarikk/YtmHybridClient-Charp/releases";

                Version? latestVersion = ParseVersion(rawTag);
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 5, 0);

                if (latestVersion == null || latestVersion <= new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build))
                    return null;

                string setupUrl = "";
                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            setupUrl = asset.TryGetProperty("browser_download_url", out var dProp) ? dProp.GetString() ?? "" : "";
                            break;
                        }
                    }
                }

                return new UpdateInfo(rawTag, setupUrl, htmlUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoUpdater] Kontrol hatası: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> DownloadAndRunInstallerAsync(UpdateInfo update)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(update.DownloadUrl))
                {
                    Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
                    return false;
                }

                string tempSetupPath = Path.Combine(Path.GetTempPath(), $"YTM_Hybrid_Client_{update.TagName}_Setup.exe");
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("YtmHybridClient-Updater/1.0");

                byte[] fileBytes = await client.GetByteArrayAsync(update.DownloadUrl);
                await File.WriteAllBytesAsync(tempSetupPath, fileBytes);

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempSetupPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoUpdater] İndirme hatası: {ex.Message}");
                Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
                return false;
            }
        }

        private static Version? ParseVersion(string tag)
        {
            string cleaned = tag.Trim().TrimStart('v', 'V');
            int dashIndex = cleaned.IndexOf('-');
            if (dashIndex > 0)
                cleaned = cleaned.Substring(0, dashIndex);

            return Version.TryParse(cleaned, out var version) ? version : null;
        }
    }
}