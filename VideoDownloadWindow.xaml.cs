using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DOWN_MNG
{
    public partial class VideoDownloadWindow : Window
    {
        public DownloadItem? ResultItem { get; private set; }
        public bool ShouldStartImmediately { get; private set; } = true;

        private VideoInfo? _currentInfo;
        private string _targetFolder;

        public VideoDownloadWindow(string prefillUrl = "", string platformPreset = "")
        {
            InitializeComponent();

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _targetFolder = Path.Combine(userProfile, "Downloads", "SDM-Downloads");
            if (!Directory.Exists(_targetFolder))
            {
                Directory.CreateDirectory(_targetFolder);
            }

            if (!string.IsNullOrWhiteSpace(prefillUrl))
            {
                UrlTextBox.Text = prefillUrl;
                Loaded += (s, e) => _ = FetchVideoDetailsAsync(prefillUrl);
            }
            else
            {
                Loaded += (s, e) =>
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string clipText = Clipboard.GetText().Trim();
                            if (clipText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                clipText.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                string plat = VideoDownloaderEngine.DetectPlatform(clipText);
                                if (plat != "Direct" || !string.IsNullOrEmpty(platformPreset))
                                {
                                    UrlTextBox.Text = clipText;
                                    _ = FetchVideoDetailsAsync(clipText);
                                }
                            }
                        }
                    }
                    catch { }
                };
            }
        }

        private async void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Please enter or paste a valid video URL.", "URL Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await FetchVideoDetailsAsync(url);
        }

        private async Task FetchVideoDetailsAsync(string url)
        {
            FetchButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            QueueButton.IsEnabled = false;
            StatusTextBlock.Text = "Analyzing link and fetching media metadata...";
            StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));

            try
            {
                var info = await Task.Run(() => VideoDownloaderEngine.FetchVideoInfoAsync(url, (status) =>
                {
                    Dispatcher.Invoke(() => StatusTextBlock.Text = status);
                }));

                _currentInfo = info;

                TitleTextBox.Text = info.Title;
                AuthorTextBlock.Text = string.IsNullOrEmpty(info.Uploader) ? "Channel" : info.Uploader;
                DurationTextBlock.Text = string.IsNullOrEmpty(info.Duration) ? "--:--" : info.Duration;

                PlatformBadgeText.Text = info.Platform;
                ApplyPlatformBadgeColor(info.Platform);

                if (!string.IsNullOrWhiteSpace(info.ThumbnailUrl))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(info.ThumbnailUrl, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ThumbnailImage.Source = bitmap;
                        ThumbnailFallback.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        ThumbnailFallback.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    ThumbnailFallback.Visibility = Visibility.Visible;
                }

                FormatComboBox.Items.Clear();
                foreach (var f in info.Formats)
                {
                    FormatComboBox.Items.Add(f);
                }

                if (FormatComboBox.Items.Count > 0)
                {
                    FormatComboBox.SelectedIndex = 0;
                }

                UpdateSavePath();

                StatusTextBlock.Text = $"Ready to download {info.Platform} video.";
                StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                DownloadButton.IsEnabled = true;
                QueueButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            }
            finally
            {
                FetchButton.IsEnabled = true;
            }
        }

        private void ApplyPlatformBadgeColor(string platform)
        {
            if (platform == "YouTube")
            {
                PlatformBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            }
            else if (platform == "Facebook")
            {
                PlatformBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
            }
            else if (platform == "TikTok")
            {
                PlatformBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7E22CE"));
            }
            else
            {
                PlatformBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0066CC"));
            }
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSavePath();
        }

        private void UpdateSavePath()
        {
            if (_currentInfo == null) return;

            var selectedFormat = FormatComboBox.SelectedItem as VideoFormatItem;
            string ext = selectedFormat?.Extension ?? "mp4";
            string cleanTitle = VideoDownloaderEngine.SanitizeFilename(TitleTextBox.Text.Trim());
            if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = "video_download";

            string filename = $"{cleanTitle}.{ext}";
            SavePathTextBox.Text = Path.Combine(_targetFolder, filename);
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    UrlTextBox.Text = text;
                    _ = FetchVideoDetailsAsync(text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BatchGrabButton_Click(object sender, RoutedEventArgs e)
        {
            var batchWin = new BatchLinkGrabberWindow(UrlTextBox.Text.Trim()) { Owner = this };
            if (batchWin.ShowDialog() == true && batchWin.ResultItems.Count > 0)
            {
                if (Owner is MainWindow mainWin)
                {
                    foreach (var item in batchWin.ResultItems)
                    {
                        mainWin.AddDownloadedItemFromBatch(item, batchWin.ShouldStartImmediately);
                    }
                }
                Close();
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFormat = FormatComboBox.SelectedItem as VideoFormatItem;
            string ext = selectedFormat?.Extension ?? "mp4";
            string cleanTitle = VideoDownloaderEngine.SanitizeFilename(TitleTextBox.Text.Trim());

            var sfd = new SaveFileDialog
            {
                FileName = $"{cleanTitle}.{ext}",
                Filter = $"{ext.ToUpperInvariant()} files (*.{ext})|*.{ext}|All files (*.*)|*.*",
                InitialDirectory = _targetFolder
            };

            if (sfd.ShowDialog(this) == true)
            {
                SavePathTextBox.Text = sfd.FileName;
                _targetFolder = Path.GetDirectoryName(sfd.FileName) ?? _targetFolder;
            }
        }

        private void QuickFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                switch (tag)
                {
                    case "Videos":
                        _targetFolder = Path.Combine(userProfile, "Videos");
                        break;
                    case "Music":
                        _targetFolder = Path.Combine(userProfile, "Music");
                        break;
                    case "Desktop":
                        _targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        break;
                    default:
                        _targetFolder = Path.Combine(userProfile, "Downloads", "SDM-Downloads");
                        break;
                }

                if (!Directory.Exists(_targetFolder))
                {
                    Directory.CreateDirectory(_targetFolder);
                }

                UpdateSavePath();
            }
        }

        private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            string plat = VideoDownloaderEngine.DetectPlatform(url);
            PlatformBadgeText.Text = plat;
            ApplyPlatformBadgeColor(plat);
        }

        private void UrlTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                string url = UrlTextBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _ = FetchVideoDetailsAsync(url);
                }
            }
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            CommitResult(StartImmediatelyCheckBox.IsChecked ?? true);
        }

        private void QueueButton_Click(object sender, RoutedEventArgs e)
        {
            CommitResult(false);
        }

        private void CommitResult(bool startNow)
        {
            string url = UrlTextBox.Text.Trim();
            string path = SavePathTextBox.Text.Trim();
            string filename = Path.GetFileName(path);
            var selectedFormat = FormatComboBox.SelectedItem as VideoFormatItem;

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Please ensure URL and save path are valid.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string platform = _currentInfo?.Platform ?? VideoDownloaderEngine.DetectPlatform(url);
            string category = (selectedFormat != null && selectedFormat.IsAudioOnly) ? "Music" : "Video";

            ResultItem = new DownloadItem
            {
                Url = url,
                SavedPath = path,
                Filename = filename,
                TotalSizeBytes = -1,
                Category = category,
                Status = "Queued",
                AddedDate = DateTime.Now,
                Protocol = "Video",
                Platform = platform,
                ThumbnailUrl = _currentInfo?.ThumbnailUrl ?? "",
                Duration = _currentInfo?.Duration ?? "",
                VideoQuality = selectedFormat?.Label ?? "Best",
                FormatCode = selectedFormat?.FormatArg ?? "b[ext=mp4]/best[ext=mp4]/best"
            };

            ShouldStartImmediately = startNow;
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
