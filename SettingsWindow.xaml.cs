using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using YtmHybridClient.Helpers;

namespace YtmHybridClient
{
    public partial class SettingsWindow : Window
    {
        public AppSettings CurrentSettings { get; private set; }

        // Ses aygıtı seçilir seçilmez çalan akışa uygulanır (önizleme); İptal'de eskisine dönülür
        private string _originalDeviceId = "";
        private bool _initialized;
        private bool _previewed;
        private bool _saved;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            CurrentSettings = settings;

            // Mevcut ayarları forma doldur
            TxtFlacFolder.Text = settings.LocalFlacFolder;
            TxtJamendoClientId.Text = settings.JamendoClientId;
            ChkWasapiExclusive.IsChecked = settings.UseWasapiExclusive;
            ChkSmartFallback.IsChecked = settings.EnableSmartFallback;
            ChkStrictMatching.IsChecked = settings.StrictMatching;

            // Dil ve Kaynak ComboBox seçimlerini mevcut ayarlara göre ayarla
            SetComboBoxSelection(CmbLanguage, settings.Language);
            SetComboBoxSelection(CmbPrimarySource, settings.PrimarySource);
            SetComboBoxSelection(CmbSecondarySource, settings.SecondarySource);

            // Ses çıkış aygıtlarını listele ve kayıtlı olanı seç
            CmbAudioDevice.ItemsSource = AudioEngine.GetOutputDevices();
            CmbAudioDevice.SelectedValue = settings.AudioDeviceId ?? "";
            if (CmbAudioDevice.SelectedIndex < 0)
                CmbAudioDevice.SelectedIndex = 0; // kayıtlı aygıt takılı değilse sistem varsayılanı

            _originalDeviceId = settings.AudioDeviceId ?? "";
            _initialized = true;   // buradan sonraki seçim değişiklikleri kullanıcıdan gelir
        }

        // Aygıt seçilir seçilmez, çalan harici akışı yeni aygıta taşı (Kaydet beklenmez)
        private async void CmbAudioDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;

            string deviceId = CmbAudioDevice.SelectedValue as string ?? "";
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                _previewed = true;
                try
                {
                    await mainWin.ApplyAudioDeviceAsync(deviceId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Settings] Aygıt değiştirilemedi: {ex.Message}");
                }
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "FLAC Müzik Klasörünü Seçin"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtFlacFolder.Text = dialog.FolderName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            DialogResult = true;
            Close();
        }

        // Kaydeder, sonra kurulu uygulamayı kapatıp yeniden açar
        private void BtnSaveRestart_Click(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            RestartApplication();
        }

        // Formdaki değerleri ayarlara yazar, dosyaya kaydeder ve ana pencereye uygular
        private void ApplySettings()
        {
            _saved = true;

            // Metin ve CheckBox alanlarını kaydet
            CurrentSettings.LocalFlacFolder = TxtFlacFolder.Text;
            CurrentSettings.JamendoClientId = TxtJamendoClientId.Text;
            CurrentSettings.UseWasapiExclusive = ChkWasapiExclusive.IsChecked ?? true;
            CurrentSettings.EnableSmartFallback = ChkSmartFallback.IsChecked ?? true;
            CurrentSettings.StrictMatching = ChkStrictMatching.IsChecked ?? true;
            CurrentSettings.AudioDeviceId = CmbAudioDevice.SelectedValue as string ?? "";
            // Not: seçilen aygıt, ComboBox değişirken çalan akışa zaten uygulandı

            // Yeni Dil ve Kaynak Ayarlarını ComboBox'lardan oku
            if (CmbLanguage.SelectedItem is ComboBoxItem langItem)
                CurrentSettings.Language = langItem.Tag?.ToString() ?? langItem.Content.ToString();

            if (CmbPrimarySource.SelectedItem is ComboBoxItem primaryItem)
                CurrentSettings.PrimarySource = primaryItem.Content.ToString();

            if (CmbSecondarySource.SelectedItem is ComboBoxItem secondaryItem)
                CurrentSettings.SecondarySource = secondaryItem.Content.ToString();

            // Ayarları appsettings.json dosyasına kaydet
            CurrentSettings.Save();

            // Dili anında arayüze uygula
            LanguageManager.ChangeLanguage(CurrentSettings.Language);

            // Ana penceredeki ses çözücü zincirini (Resolver Chain) yeniden başlat
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.InitAudioResolver();
            }
        }

        // Çalışan exe'yi (kurulu uygulama ya da proje çıktısı) kapatıp yeniden başlatır
        private void RestartApplication()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    MessageBox.Show("Uygulama yolu bulunamadı. Ayarlar kaydedildi; lütfen uygulamayı elle kapatıp yeniden aç.",
                                    "Yeniden başlat", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Yeni kopya, eskisi kapanıp WebView2 profilini bırakana kadar ~2 sn bekler
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c ping 127.0.0.1 -n 3 >nul & start \"\" \"{exePath}\"",
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uygulama yeniden başlatılamadı: {ex.Message}\nAyarlar kaydedildi; uygulamayı elle yeniden aç.",
                                "Yeniden başlat", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Kaydetmeden kapatılırsa (İptal ya da pencere X) önizlenen aygıttan eskisine dön
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (_previewed && !_saved && Application.Current?.MainWindow is MainWindow mainWin)
                _ = mainWin.ApplyAudioDeviceAsync(_originalDeviceId);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ComboBox açıldığında mevcut ayardaki değerin seçili gelmesini sağlayan yardımcı metod
        private void SetComboBoxSelection(ComboBox comboBox, string value)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                string tagOrContent = item.Tag?.ToString() ?? item.Content.ToString();
                if (tagOrContent == value || item.Content.ToString() == value)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }
    }
}