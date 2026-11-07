using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using YtmHybridClient.Helpers;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;

namespace YtmHybridClient
{
    public partial class SettingsWindow : Window
    {
        private sealed record AudioDeviceOption(string Id, string Name);

        public AppSettings CurrentSettings { get; private set; }

        private readonly string _initialLanguage;
        private readonly bool _isInitialized;
        private bool _isSaved = false;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            CurrentSettings = settings ?? new AppSettings();
            _initialLanguage = CurrentSettings.Language ?? "tr";
            ChkMinimizeToTray.IsChecked = CurrentSettings.MinimizeToTray;

            var devices = GetAudioOutputDevices();
            CmbAudioDevice.ItemsSource = devices;
            CmbAudioDevice.SelectedValue = CurrentSettings.AudioDevice ?? string.Empty;
            if (CmbAudioDevice.SelectedIndex == -1 && devices.Count > 0)
            {
                CmbAudioDevice.SelectedIndex = 0;
            }

            SetComboBoxSelection(CmbLanguage, CurrentSettings.Language);
            _isInitialized = true;
        }

        /// <summary>
        /// Dil değiştirildiğinde anlık (live preview) olarak arayüze yansıtır.
        /// </summary>
        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (CmbLanguage.SelectedItem is ComboBoxItem langItem)
            {
                string? selectedLang = langItem.Tag?.ToString() ?? langItem.Content?.ToString();
                if (!string.IsNullOrEmpty(selectedLang))
                {
                    CurrentSettings.Language = selectedLang;
                    LanguageManager.ChangeLanguage(selectedLang);
                }
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!await SaveSettingsAsync()) return;
            DialogResult = true;
            Close();
        }

        private async void BtnSaveRestart_Click(object sender, RoutedEventArgs e)
        {
            if (!await SaveSettingsAsync()) return;
            MainWindow.Current?.AllowApplicationExit();

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }

            Application.Current.Shutdown();
        }

        private async Task<bool> SaveSettingsAsync()
        {
            string selectedDeviceName = SelectedDeviceName;
            CurrentSettings.AudioDevice = selectedDeviceName;
            CurrentSettings.MinimizeToTray = ChkMinimizeToTray.IsChecked ?? false;

            if (CmbLanguage.SelectedItem is ComboBoxItem langItem)
                CurrentSettings.Language = langItem.Tag?.ToString() ?? langItem.Content?.ToString() ?? "tr";

            if (!CurrentSettings.Save())
            {
                string message = string.Equals(CurrentSettings.Language, "en", StringComparison.OrdinalIgnoreCase)
                    ? "Settings could not be saved. See the debug log for details."
                    : "Ayarlar kaydedilemedi. Ayrıntılar için hata günlüğünü kontrol edin.";
                MessageBox.Show(this, message, "YTM Hybrid Client", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            _isSaved = true;
            Debug.WriteLine($"[Ayarlar Kaydedildi] Seçilen Cihaz: {selectedDeviceName}");

            if (MainWindow.Current is MainWindow mainWin)
                await mainWin.ApplyAudioDeviceToWebView(selectedDeviceName);

            return true;
        }

        private string SelectedDeviceName => CmbAudioDevice.SelectedItem is AudioDeviceOption device
            ? device.Name
            : string.Empty;

        private static List<AudioDeviceOption> GetAudioOutputDevices()
        {
            var devices = new List<AudioDeviceOption> { new(string.Empty, "Sistem varsayılanı") };

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    devices.Add(new AudioDeviceOption(device.ID, device.FriendlyName));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Ayarlar] Ses aygıtları listelenemedi: {ex.Message}");
            }

            return devices;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Kaydetmeden kapatıldığında dil önizlemesini geri alır.
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isSaved)
            {
                CurrentSettings.Language = _initialLanguage;
                LanguageManager.ChangeLanguage(_initialLanguage);
            }
        }

        // ComboBox açıldığında mevcut ayardaki değerin seçili gelmesini sağlayan yardımcı metod
        private static void SetComboBoxSelection(ComboBox comboBox, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;

            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    string? tag = cbi.Tag?.ToString();
                    string? content = cbi.Content?.ToString();
                    if (string.Equals(tag, value, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(content, value, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBox.SelectedItem = cbi;
                        break;
                    }
                }
            }
        }
    }
}