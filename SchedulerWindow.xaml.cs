using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace DOWN_MNG
{
    public partial class SchedulerWindow : Window
    {
        public SchedulerSettings Settings { get; private set; } = new SchedulerSettings();
        private readonly string _settingsFilePath;

        public SchedulerWindow()
        {
            InitializeComponent();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dbFolder = Path.Combine(appData, "NexLoad");
            _settingsFilePath = Path.Combine(dbFolder, "scheduler.json");

            LoadSettings();
            BindSettingsToUI();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<SchedulerSettings>(json);
                    if (settings != null)
                    {
                        Settings = settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading scheduler settings: {ex.Message}");
            }
        }

        private void BindSettingsToUI()
        {
            EnabledCheckBox.IsChecked = Settings.Enabled;
            StartTimeTextBox.Text = Settings.StartTime;
            StopTimeTextBox.Text = Settings.StopTime;
            StopOnTimeCheckBox.IsChecked = Settings.StopOnTime;

            // Set ComboBox selection based on CompletionAction tag
            foreach (ComboBoxItem item in CompletionActionComboBox.Items)
            {
                if (item.Tag?.ToString() == Settings.CompletionAction)
                {
                    CompletionActionComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SaveSettingsFromUI()
        {
            Settings.Enabled = EnabledCheckBox.IsChecked == true;
            Settings.StartTime = StartTimeTextBox.Text.Trim();
            Settings.StopTime = StopTimeTextBox.Text.Trim();
            Settings.StopOnTime = StopOnTimeCheckBox.IsChecked == true;

            if (CompletionActionComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                Settings.CompletionAction = selectedItem.Tag?.ToString() ?? "None";
            }
        }

        private bool ValidateTimeFormat(string time)
        {
            if (string.IsNullOrEmpty(time)) return false;
            var parts = time.Split(':');
            if (parts.Length != 2) return false;
            
            if (int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int minutes))
            {
                return hours >= 0 && hours < 24 && minutes >= 0 && minutes < 60;
            }
            return false;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string startTime = StartTimeTextBox.Text.Trim();
            string stopTime = StopTimeTextBox.Text.Trim();

            if (!ValidateTimeFormat(startTime))
            {
                MessageBox.Show("Please enter a valid start time in HH:mm format (e.g. 02:00 or 14:30).", "Invalid Time Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateTimeFormat(stopTime))
            {
                MessageBox.Show("Please enter a valid stop time in HH:mm format (e.g. 05:00 or 17:00).", "Invalid Time Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettingsFromUI();

            try
            {
                string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
