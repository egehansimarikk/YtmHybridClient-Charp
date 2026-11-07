using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YtmHybridClient
{
    public enum MediaHotkey
    {
        PlayPause,
        NextTrack,
        PreviousTrack
    }

    public sealed class GlobalHotkeyManager : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const uint VkMediaNextTrack = 0xB5;
        private const uint VkMediaPreviousTrack = 0xB6;
        private const uint VkMediaPlayPause = 0xCD;
        private const uint ModNoRepeat = 0x4000;
        private readonly Action<MediaHotkey> _onHotkey;
        private Window? _window;
        private HwndSource? _source;
        private bool _disposed;

        public GlobalHotkeyManager(Action<MediaHotkey> onHotkey)
        {
            _onHotkey = onHotkey ?? throw new ArgumentNullException(nameof(onHotkey));
        }

        public void Attach(Window window)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GlobalHotkeyManager));
            if (ReferenceEquals(_window, window)) return;

            Detach();
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _window.SourceInitialized += Window_SourceInitialized;
            _window.Closed += Window_Closed;

            if (_window.IsInitialized && new WindowInteropHelper(_window).Handle != IntPtr.Zero)
                AttachHook();
        }

        public void Detach()
        {
            if (_window != null)
            {
                _window.SourceInitialized -= Window_SourceInitialized;
                _window.Closed -= Window_Closed;
            }

            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                UnregisterHotKey(_source.Handle, 1);
                UnregisterHotKey(_source.Handle, 2);
                UnregisterHotKey(_source.Handle, 3);
                _source = null;
            }

            _window = null;
        }

        private void Window_SourceInitialized(object? sender, EventArgs e) => AttachHook();

        private void Window_Closed(object? sender, EventArgs e) => Detach();

        private void AttachHook()
        {
            if (_window == null || _source != null) return;

            var handle = new WindowInteropHelper(_window).Handle;
            _source = HwndSource.FromHwnd(handle);
            if (_source == null) return;

            _source.AddHook(WndProc);
            Register(1, VkMediaPlayPause);
            Register(2, VkMediaNextTrack);
            Register(3, VkMediaPreviousTrack);
        }

        private void Register(int id, uint virtualKey)
        {
            if (!RegisterHotKey(_source!.Handle, id, ModNoRepeat, virtualKey))
                Debug.WriteLine($"[GlobalHotkey] VK 0x{virtualKey:X2} kaydedilemedi: {Marshal.GetLastWin32Error()}");
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WmHotkey) return IntPtr.Zero;

            switch (wParam.ToInt32())
            {
                case 1:
                    _onHotkey(MediaHotkey.PlayPause);
                    handled = true;
                    break;
                case 2:
                    _onHotkey(MediaHotkey.NextTrack);
                    handled = true;
                    break;
                case 3:
                    _onHotkey(MediaHotkey.PreviousTrack);
                    handled = true;
                    break;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Detach();
            _disposed = true;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
