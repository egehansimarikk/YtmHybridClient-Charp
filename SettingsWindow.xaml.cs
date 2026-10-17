using System.Windows;

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
            CurrentSettings.LocalFlacFolder = TxtFlacFolder.Text;
            CurrentSettings.JamendoClientId = TxtJamendoClientId.Text;
            CurrentSettings.UseWasapiExclusive = ChkWasapiExclusive.IsChecked ?? true;
            CurrentSettings.EnableSmartFallback = ChkSmartFallback.IsChecked ?? true;

            CurrentSettings.Save();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}