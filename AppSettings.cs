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
        public int MaxConcurrentDownloads { get; set; } = 3; // 1, 2, 3, 5, or -1 for unlimited
        public bool AutoSortCategories { get; set; } = true; // Auto-route to Compressed, Programs, Video, Music, Documents
        public bool AutoExtractArchives { get; set; } = false; // Auto-extract .zip/.rar/.7z on finish
        public bool ShowFloatingWidget { get; set; } = false; // Desktop mini drop target
        public string CompletionPowerAction { get; set; } = "None"; // None, Shutdown, Sleep, Hibernate, Lock
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
                        settings.CloseToTray = true;
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
