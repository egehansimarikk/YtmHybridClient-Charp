using System;
using System.IO;
using System.Text.Json;

namespace YtmHybridClient
{
    public class AppSettings
    {
        public string AudioDevice { get; set; } = "";
        public string Language { get; set; } = "tr"; // "tr" veya "en"
        public bool MinimizeToTray { get; set; } = false;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YtmHybridClient", "appsettings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Yükleme hatası: {ex.Message}");
            }
            return new AppSettings();
        }

        public bool Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Kaydetme hatası: {ex.Message}");
                return false;
            }
        }
    }
}