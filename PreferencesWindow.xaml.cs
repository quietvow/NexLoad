using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DOWN_MNG
{
    public partial class PreferencesWindow : Window
    {
        private readonly AppSettings _settings;
        public bool LanguageChanged { get; private set; }
        public bool ThemeChanged { get; private set; }

        public PreferencesWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            // Load settings into UI
            DownloadFolderTextBox.Text = _settings.DefaultDownloadFolder;
            ClipboardMonitoringCheckBox.IsChecked = _settings.ClipboardMonitoringEnabled;
            AutoStartCheckBox.IsChecked = _settings.StartDownloadsAutomatically;
            PlaySoundCheckBox.IsChecked = _settings.PlaySoundOnCompletion;
            MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
            CloseToTrayCheckBox.IsChecked = _settings.CloseToTray;

            // Start with windows registry check
            StartWithWindowsCheckBox.IsChecked = CheckRunOnStartup();

            // Connections
            switch (_settings.DefaultConnections)
            {
                case 1: ConnectionsComboBox.SelectedIndex = 0; break;
                case 2: ConnectionsComboBox.SelectedIndex = 1; break;
                case 4: ConnectionsComboBox.SelectedIndex = 2; break;
                case 8: ConnectionsComboBox.SelectedIndex = 3; break;
                case 16: ConnectionsComboBox.SelectedIndex = 4; break;
                case 32: ConnectionsComboBox.SelectedIndex = 5; break;
                default: ConnectionsComboBox.SelectedIndex = 3; break;
            }

            // Speed limit
            EnableSpeedLimitCheckBox.IsChecked = _settings.SpeedLimitEnabled;
            SpeedLimitValTextBox.Text = _settings.SpeedLimitKBps.ToString();

            // Theme
            ThemeComboBox.SelectedIndex = _settings.Theme == "Dark" ? 1 : 0;

            // Language
            LanguageComboBox.SelectedIndex = _settings.Language == "si" ? 1 : 0;
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Default Download Directory",
                InitialDirectory = Directory.Exists(_settings.DefaultDownloadFolder) ? _settings.DefaultDownloadFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog(this) == true)
            {
                DownloadFolderTextBox.Text = dialog.FolderName;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string newFolder = DownloadFolderTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(newFolder))
            {
                _settings.DefaultDownloadFolder = newFolder;
            }

            _settings.ClipboardMonitoringEnabled = ClipboardMonitoringCheckBox.IsChecked == true;
            _settings.StartDownloadsAutomatically = AutoStartCheckBox.IsChecked == true;
            _settings.PlaySoundOnCompletion = PlaySoundCheckBox.IsChecked == true;
            _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
            _settings.CloseToTray = CloseToTrayCheckBox.IsChecked == true;

            // Connections
            int[] connMap = { 1, 2, 4, 8, 16, 32 };
            int selConn = ConnectionsComboBox.SelectedIndex;
            _settings.DefaultConnections = (selConn >= 0 && selConn < connMap.Length) ? connMap[selConn] : 8;

            // Speed limit
            _settings.SpeedLimitEnabled = EnableSpeedLimitCheckBox.IsChecked == true;
            if (long.TryParse(SpeedLimitValTextBox.Text.Trim(), out long kb) && kb > 0)
            {
                _settings.SpeedLimitKBps = kb;
            }

            // Startup Registry
            SetRunOnStartup(StartWithWindowsCheckBox.IsChecked == true);

            // Theme
            string newTheme = ThemeComboBox.SelectedIndex == 1 ? "Dark" : "Light";
            if (newTheme != _settings.Theme)
            {
                _settings.Theme = newTheme;
                ThemeChanged = true;
            }

            // Language
            string newLang = LanguageComboBox.SelectedIndex == 1 ? "si" : "en";
            if (newLang != _settings.Language)
            {
                _settings.Language = newLang;
                LanguageChanged = true;
            }

            _settings.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool CheckRunOnStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("NexLoad") != null || key?.GetValue("DOWN-MNG") != null;
            }
            catch
            {
                return false;
            }
        }

        private void SetRunOnStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("NexLoad", $"\"{exePath}\" --background");
                    }
                }
                else
                {
                    key.DeleteValue("NexLoad", false);
                    key.DeleteValue("DOWN-MNG", false);
                }
            }
            catch { }
        }

        private void SetupExtension_Click(object sender, RoutedEventArgs e)
        {
            var win = new BrowserExtensionSetupWindow { Owner = this };
            win.ShowDialog();
        }
    }
}
