using System;
using System.IO;
using System.Text.Json;

namespace DOWN_MNG
{
    public class AppSettings
    {
        public string DefaultDownloadFolder { get; set; } = "";
        public int DefaultConnections { get; set; } = 8;
        public bool SpeedLimitEnabled { get; set; } = false;
        public long SpeedLimitKBps { get; set; } = 1024; // Default 1024 KB/s = 1 MB/s
        public bool ClipboardMonitoringEnabled { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true; // Like IDM / JDownloader: Keep running in background when closed
        public bool RunInBackground { get; set; } = true;
        public bool PlaySoundOnCompletion { get; set; } = true;
        public bool StartDownloadsAutomatically { get; set; } = true;
        public bool ShowDownloadCompleteDialog { get; set; } = true;
        public bool CloseProgressWindowOnCompletion { get; set; } = true;
        public bool OpenFileOnCompletion { get; set; } = false;
        public bool OpenFolderOnCompletion { get; set; } = false;
        public bool ShutdownOnCompletion { get; set; } = false;
        public string Theme { get; set; } = "Light"; // Light, Dark
        public string Language { get; set; } = "en"; // en, si

        private static readonly string SettingsFilePath;

        static AppSettings()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(appData, "NexLoad");
            string legacyDir = Path.Combine(appData, "DOWN-MNG");
            if (!Directory.Exists(appDir))
            {
                Directory.CreateDirectory(appDir);
                if (Directory.Exists(legacyDir) && File.Exists(Path.Combine(legacyDir, "settings.json")))
                {
                    try { File.Copy(Path.Combine(legacyDir, "settings.json"), Path.Combine(appDir, "settings.json"), true); } catch { }
                }
            }
            SettingsFilePath = Path.Combine(appDir, "settings.json");
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        if (string.IsNullOrEmpty(settings.DefaultDownloadFolder))
                        {
                            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                            settings.DefaultDownloadFolder = Path.Combine(userProfile, "Downloads", "SDM-Downloads");
                        }
                        return settings;
                    }
                }
            }
            catch { }

            var defaultSettings = new AppSettings();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            defaultSettings.DefaultDownloadFolder = Path.Combine(profile, "Downloads", "SDM-Downloads");
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }
}
