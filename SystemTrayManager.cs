using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace YtmHybridClient
{
    public sealed class SystemTrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Action _togglePlayback;
        private readonly Action _nextTrack;
        private readonly Action _showSettings;
        private readonly Action _showMiniPlayer;
        private readonly Action _exitApplication;
        private readonly Action _restoreMainWindow;
        private bool _disposed;

        public SystemTrayManager(
            Action togglePlayback,
            Action nextTrack,
            Action showSettings,
            Action showMiniPlayer,
            Action exitApplication,
            Action restoreMainWindow)
        {
            _togglePlayback = togglePlayback;
            _nextTrack = nextTrack;
            _showSettings = showSettings;
            _showMiniPlayer = showMiniPlayer;
            _exitApplication = exitApplication;
            _restoreMainWindow = restoreMainWindow;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Çal / Duraklat", null, (_, _) => _togglePlayback());
            menu.Items.Add("Sonraki Parça", null, (_, _) => _nextTrack());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ayarlar", null, (_, _) => _showSettings());
            menu.Items.Add("Mini Player Modu", null, (_, _) => _showMiniPlayer());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, _) => _exitApplication());

            string iconPath = Path.Combine(AppContext.BaseDirectory, "YtmHybridClientLogo.ico");
            _notifyIcon = new NotifyIcon
            {
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Text = "YTM Hybrid Client",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        private void NotifyIcon_DoubleClick(object? sender, EventArgs e) => _restoreMainWindow();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
