using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using YtmHybridClient.Helpers;

namespace YtmHybridClient
{
    public partial class SettingsWindow : Window
    {
        public AppSettings CurrentSettings { get; private set; }

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
            // Metin ve CheckBox alanlarını kaydet
            CurrentSettings.LocalFlacFolder = TxtFlacFolder.Text;
            CurrentSettings.JamendoClientId = TxtJamendoClientId.Text;
            CurrentSettings.UseWasapiExclusive = ChkWasapiExclusive.IsChecked ?? true;
            CurrentSettings.EnableSmartFallback = ChkSmartFallback.IsChecked ?? true;
            CurrentSettings.StrictMatching = ChkStrictMatching.IsChecked ?? true;
            CurrentSettings.AudioDeviceId = CmbAudioDevice.SelectedValue as string ?? "";

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

            DialogResult = true;
            Close();
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