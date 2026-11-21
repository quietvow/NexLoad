using System;
using System.Windows;

namespace DOWN_MNG
{
    public static class LocalizationManager
    {
        public static string CurrentLanguage { get; private set; } = "en";

        public static void SwitchLanguage(string cultureCode)
        {
            try
            {
                string dictPath = cultureCode == "si" 
                    ? "Resources/Strings.si.xaml" 
                    : "Resources/Strings.en.xaml";

                var resourceDict = Application.LoadComponent(new Uri(dictPath, UriKind.Relative)) as ResourceDictionary;

                if (resourceDict != null && Application.Current != null)
                {
                    var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
                    for (int i = mergedDictionaries.Count - 1; i >= 0; i--)
                    {
                        var dict = mergedDictionaries[i];
                        if (dict.Source != null && (dict.Source.OriginalString.Contains("Strings.en.xaml") || dict.Source.OriginalString.Contains("Strings.si.xaml")))
                        {
                            mergedDictionaries.RemoveAt(i);
                        }
                    }

                    mergedDictionaries.Add(resourceDict);
                    CurrentLanguage = cultureCode;
                    if (App.Settings != null)
                    {
                        App.Settings.Language = cultureCode;
                    }
                }
            }
            catch { }
        }
    }
}
