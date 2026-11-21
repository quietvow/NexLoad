using System;
using System.Windows;
using System.Windows.Controls;

namespace DOWN_MNG
{
    public partial class SpeedLimiterWindow : Window
    {
        private readonly AppSettings _settings;

        public SpeedLimiterWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            EnableLimiterCheckBox.IsChecked = _settings.SpeedLimitEnabled;

            if (_settings.SpeedLimitKBps >= 1024 && _settings.SpeedLimitKBps % 1024 == 0)
            {
                SpeedTextBox.Text = (_settings.SpeedLimitKBps / 1024).ToString();
                UnitComboBox.SelectedIndex = 1; // MB/s
            }
            else
            {
                SpeedTextBox.Text = _settings.SpeedLimitKBps.ToString();
                UnitComboBox.SelectedIndex = 0; // KB/s
            }

            UpdateInputs();
        }

        private void UpdateInputs()
        {
            bool isEnabled = EnableLimiterCheckBox.IsChecked == true;
            SpeedTextBox.IsEnabled = isEnabled;
            UnitComboBox.IsEnabled = isEnabled;
        }

        private void EnableLimiterCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateInputs();
        }

        private void EnableLimiterCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateInputs();
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && long.TryParse(tag, out long kb))
            {
                EnableLimiterCheckBox.IsChecked = true;
                if (kb >= 1024 && kb % 1024 == 0)
                {
                    SpeedTextBox.Text = (kb / 1024).ToString();
                    UnitComboBox.SelectedIndex = 1;
                }
                else
                {
                    SpeedTextBox.Text = kb.ToString();
                    UnitComboBox.SelectedIndex = 0;
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = EnableLimiterCheckBox.IsChecked == true;
            if (enabled)
            {
                if (!double.TryParse(SpeedTextBox.Text.Trim(), out double val) || val <= 0)
                {
                    MessageBox.Show("Please enter a valid numeric speed limit.", "Invalid Speed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                long kb = UnitComboBox.SelectedIndex == 1 ? (long)(val * 1024) : (long)val;
                _settings.SpeedLimitEnabled = true;
                _settings.SpeedLimitKBps = Math.Max(10, kb);
            }
            else
            {
                _settings.SpeedLimitEnabled = false;
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
    }
}
