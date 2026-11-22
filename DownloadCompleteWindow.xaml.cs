using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DOWN_MNG
{
    public partial class DownloadCompleteWindow : Window
    {
        private readonly DownloadItem _item;

        public DownloadCompleteWindow(DownloadItem item)
        {
            InitializeComponent();
            _item = item;

            Title = $"Download Complete — {item.Filename}";
            FileNameTextBlock.Text = item.Filename;
            FileSizeTextBlock.Text = item.TotalSizeBytes > 0 ? $"{item.DisplaySize} ({item.TotalSizeBytes:N0} Bytes)" : item.DisplaySize;
            SavePathTextBlock.Text = item.SavedPath;
            UrlTextBlock.Text = item.Url;

            DontShowAgainCheckBox.IsChecked = !App.Settings.ShowDownloadCompleteDialog;
            DontShowAgainCheckBox.Checked += (s, e) =>
            {
                App.Settings.ShowDownloadCompleteDialog = false;
                App.Settings.Save();
            };
            DontShowAgainCheckBox.Unchecked += (s, e) =>
            {
                App.Settings.ShowDownloadCompleteDialog = true;
                App.Settings.Save();
            };
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_item.SavedPath))
                {
                    Process.Start(new ProcessStartInfo(_item.SavedPath) { UseShellExecute = true });
                    Close();
                }
                else
                {
                    MessageBox.Show("The downloaded file could not be found at:\n" + _item.SavedPath, "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open file:\n{ex.Message}", "Open File Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenWithButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_item.SavedPath))
                {
                    Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{_item.SavedPath}\"") { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("The downloaded file could not be found at:\n" + _item.SavedPath, "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open dialog:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_item.SavedPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{_item.SavedPath}\"");
                }
                else
                {
                    string dir = Path.GetDirectoryName(_item.SavedPath) ?? "";
                    if (Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                    else
                    {
                        MessageBox.Show("The folder could not be found:\n" + dir, "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
