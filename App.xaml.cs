using System;
using System.Windows;

namespace YtmHybridClient
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(args.Exception.ToString(), "Başlangıç Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}