using System;
using System.IO;
using System.Text.Json;

namespace YtmHybridClient
{
    public class AppSettings
    {
        // --- Ses & Klasör Ayarları ---
        public string LocalFlacFolder { get; set; } = @"C:\Music\FLAC";
        public string JamendoClientId { get; set; } = "6a4bec36";
        public bool EnableSmartFallback { get; set; } = true;
        public bool UseWasapiExclusive { get; set; } = true;

        // Ses çıkış aygıtının kimliği. Boş = sistem varsayılanı
        public string AudioDeviceId { get; set; } = "";

        // --- Yeni Eklenecek Özellikler (v1.1.0) ---
        public string Language { get; set; } = "tr"; // "tr" veya "en"
        public string PrimarySource { get; set; } = "Jamendo"; // "Jamendo", "Audius", "LocalFLAC", "JioSaavn"
        public string SecondarySource { get; set; } = "Audius";

        // Ayar dosyası sürümü. Eski dosyalarda bu alan yoktur ve 1 olarak okunur (otomatik geçiş için).
        private const int CurrentVersion = 2;
        public int SettingsVersion { get; set; } = 1;

        // true: başlık VE sanatçı eşleşmeli (yanlış şarkı çalmaz). false: sadece başlık yeterli (daha çok sonuç, daha çok yanlış eşleşme riski)
        public bool StrictMatching { get; set; } = true;

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
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings { SettingsVersion = CurrentVersion };

                    if (loaded.SettingsVersion < CurrentVersion)
                    {
                        loaded.MigrateToCurrentVersion();
                        loaded.Save();
                    }

                    return loaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Yükleme hatası: {ex.Message}");
            }
            return new AppSettings { SettingsVersion = CurrentVersion };
        }

        // v1.2.0'a geçiş: çalışmayan JioSaavn kaynağını bir kereliğine değiştirir.
        // Sürüm numarası yükseldiği için kullanıcı sonradan JioSaavn'ı bilerek seçerse bir daha dokunulmaz.
        private void MigrateToCurrentVersion()
        {
            if (PrimarySource == "JioSaavn")
            {
                PrimarySource = "Jamendo";
                System.Diagnostics.Debug.WriteLine("[Settings] Birincil kaynak JioSaavn -> Jamendo olarak güncellendi.");
            }

            if (SecondarySource == "JioSaavn")
            {
                SecondarySource = "Audius";
                System.Diagnostics.Debug.WriteLine("[Settings] İkincil kaynak JioSaavn -> Audius olarak güncellendi.");
            }

            // Aynı kaynak iki kez seçili kalmasın
            if (PrimarySource == SecondarySource)
                SecondarySource = PrimarySource == "Audius" ? "Jamendo" : "Audius";

            SettingsVersion = CurrentVersion;
        }

        public void Save()
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Kaydetme hatası: {ex.Message}");
            }
        }
    }
}