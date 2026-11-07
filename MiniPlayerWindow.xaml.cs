using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YtmHybridClient
{
    public partial class MiniPlayerWindow : Window
    {
        private readonly Action _togglePlayback;
        private readonly Action _returnToMain;
        private bool _isDragging;
        private bool _isUpdatingProgress;

        public MiniPlayerWindow(Action togglePlayback, Action returnToMain)
        {
            InitializeComponent();
            _togglePlayback = togglePlayback;
            _returnToMain = returnToMain;
        }

        public void UpdateTrack(string title, string artist, string? artworkUrl)
        {
            TxtTitle.Text = string.IsNullOrWhiteSpace(title) ? "Şu anda çalmıyor" : title;
            TxtArtist.Text = artist;
            AlbumArt.Source = null;

            if (Uri.TryCreate(artworkUrl, UriKind.Absolute, out var artworkUri) &&
                (artworkUri.Scheme == Uri.UriSchemeHttp || artworkUri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnDemand;
                    image.UriSource = artworkUri;
                    image.EndInit();
                    AlbumArt.Source = image;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MiniPlayer] Albüm kapağı yüklenemedi: {ex.Message}");
                }
            }
        }

        public void UpdatePlaybackState(bool isPlaying)
        {
            BtnPlayPause.Content = isPlaying ? "❚❚" : "▶";
        }

        public void UpdateProgress(double currentSeconds, double totalSeconds)
        {
            totalSeconds = double.IsFinite(totalSeconds)
                ? Math.Clamp(totalSeconds, 0, TimeSpan.MaxValue.TotalSeconds)
                : 0;
            currentSeconds = double.IsFinite(currentSeconds) ? Math.Clamp(currentSeconds, 0, totalSeconds) : 0;

            _isUpdatingProgress = true;
            try
            {
                SldProgress.Maximum = totalSeconds;
                TxtTotalTime.Text = FormatTime(totalSeconds);

                if (!_isDragging)
                {
                    SldProgress.Value = currentSeconds;
                    TxtCurrentTime.Text = FormatTime(currentSeconds);
                }
            }
            finally
            {
                _isUpdatingProgress = false;
            }
        }

        private static string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }

        private void MiniPlayer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != this)
            {
                if (source is Button || source is ButtonBase || source is Slider || source is Thumb)
                    return;

                source = source is Visual
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                e.Handled = true;
                DragMove();
            }
        }

        private void SldProgress_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
        }

        private async void SldProgress_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            double seconds = SldProgress.Value;
            TxtCurrentTime.Text = FormatTime(seconds);

            if (MainWindow.Current is { } mainWindow)
                await mainWindow.SeekTrack(seconds);
        }

        private void SldProgress_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingProgress) return;

            TxtCurrentTime.Text = FormatTime(e.NewValue);
            if (!_isDragging && SldProgress.IsKeyboardFocusWithin && MainWindow.Current is { } mainWindow)
                _ = mainWindow.SeekTrack(e.NewValue);
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            await MainWindow.Current!.ExecuteYtmCommand("prev");
        }

        private void TogglePlayback_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _togglePlayback();
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            await MainWindow.Current!.ExecuteYtmCommand("next");
        }

        private void ReturnToMain_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _returnToMain();
        }
    }
}
