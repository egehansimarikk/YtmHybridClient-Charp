using System;
using System.Windows;

namespace YtmHybridClient.Helpers
{
    public static class LanguageManager
    {
        public static void ChangeLanguage(string langCode)
        {
            try
            {
                string resourcePath = langCode == "en"
                    ? "pack://application:,,,/Resources/Strings.en.xaml"
                    : "pack://application:,,,/Resources/Strings.tr.xaml";

                var dict = new ResourceDictionary
                {
                    Source = new Uri(resourcePath, UriKind.Absolute)
                };

                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LanguageManager Hata]: {ex.Message}");
            }
        }
    }
}