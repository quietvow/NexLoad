using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DOWN_MNG
{
    public partial class DownloadCompleteWindow : Window
    {
        private readonly DownloadItem _item;
        private HashResult? _hashResult;
        private bool _isHashing = false;

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

        private async void HashExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (_hashResult != null || _isHashing) return;
            if (!File.Exists(_item.SavedPath))
            {
                Sha256TextBox.Text = "File not found.";
                Md5TextBox.Text = "File not found.";
                return;
            }

            _isHashing = true;
            Sha256TextBox.Text = "Calculating SHA-256...";
            Md5TextBox.Text = "Calculating MD5...";

            try
            {
                _hashResult = await FileHashVerifier.ComputeHashesAsync(_item.SavedPath);
                if (_hashResult.Success)
                {
                    Sha256TextBox.Text = _hashResult.SHA256;
                    Md5TextBox.Text = _hashResult.MD5;
                    UpdateHashComparison();
                }
                else
                {
                    Sha256TextBox.Text = $"Error: {_hashResult.Error}";
                    Md5TextBox.Text = $"Error: {_hashResult.Error}";
                }
            }
            catch (Exception ex)
            {
                Sha256TextBox.Text = ex.Message;
                Md5TextBox.Text = ex.Message;
            }
            finally
            {
                _isHashing = false;
            }
        }

        private void CopySha256_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Sha256TextBox.Text))
            {
                try { Clipboard.SetText(Sha256TextBox.Text); } catch { }
            }
        }

        private void CopyMd5_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(Md5TextBox.Text))
            {
                try { Clipboard.SetText(Md5TextBox.Text); } catch { }
            }
        }

        private void CompareHash_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateHashComparison();
        }

        private void UpdateHashComparison()
        {
            if (_hashResult == null || !_hashResult.Success) return;

            string expected = CompareHashTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(expected))
            {
                HashMatchBadge.Text = "Paste hash to verify";
                HashMatchBadge.Foreground = (Brush)FindResource("TextMuted");
                return;
            }

            bool matches = FileHashVerifier.VerifyHash(_hashResult, expected);
            if (matches)
            {
                HashMatchBadge.Text = "✓ Integrity Match!";
                HashMatchBadge.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // emerald
            }
            else
            {
                HashMatchBadge.Text = "✗ Hash Mismatch";
                HashMatchBadge.Foreground = new SolidColorBrush(Color.FromRgb(244, 63, 94)); // rose
            }
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
