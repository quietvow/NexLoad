using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DOWN_MNG
{
    public partial class DownloadProgressWindow : Window
    {
        private readonly DownloadItem _item;
        private readonly DownloadEngine _engine;

        public DownloadProgressWindow(DownloadItem item, DownloadEngine engine)
        {
            InitializeComponent();
            _item = item;
            _engine = engine;

            UrlTextBlock.Text = _item.Url;
            Title = $"{_item.ProgressPercentage:F0}% " + (item.Filename.Length > 20 ? item.Filename.Substring(0, 20) + "..." : item.Filename);

            // Listen to progress updates
            _engine.ProgressChanged += OnProgressChanged;
            _engine.DownloadCompleted += OnDownloadCompleted;
            _engine.DownloadError += OnDownloadError;

            UpdateUI();
        }

        private void OnProgressChanged(DownloadItem item)
        {
            UpdateUI();
        }

        private void OnDownloadCompleted(DownloadItem item)
        {
            try
            {
                if (Application.Current == null) return;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateUI();
                    StatusTextBlock.Text = "Completed";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    PauseResumeButton.IsEnabled = false;
                    CancelButton.Content = "Close";
                    MessageBox.Show($"Download Complete: {_item.Filename}", "SDM Success", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch { }
        }

        private void OnDownloadError(DownloadItem item, string errorMsg)
        {
            try
            {
                if (Application.Current == null) return;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateUI();
                    StatusTextBlock.Text = "Failed";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    MessageBox.Show($"Download Failed: {errorMsg}", "SDM Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            catch { }
        }

        private void UpdateUI()
        {
            try
            {
                if (Application.Current == null) return;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Title = $"{_item.ProgressPercentage:F0}% " + (_item.Filename.Length > 20 ? _item.Filename.Substring(0, 20) + "..." : _item.Filename);
                    StatusTextBlock.Text = _item.Status == "Downloading" ? "Receiving data..." : _item.Status;
                    
                    // Toggle status colors
                    if (_item.Status == "Downloading")
                    {
                        StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 34, 255));
                        PauseResumeButton.Content = "Pause";
                    }
                    else if (_item.Status == "Paused")
                    {
                        StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
                        PauseResumeButton.Content = "Resume";
                    }

                    // Size and downloaded formatting matching the image with commas!
                    SizeTextBlock.Text = _item.TotalSizeBytes > 0 ? $"{_item.TotalSizeBytes:N0} Bytes" : "Unknown";
                    DownloadedTextBlock.Text = $"{_item.BytesDownloaded:N0} Bytes ( {_item.ProgressPercentage:F2} % )";
                    
                    // Format Speed to MB/sec or KB/sec precisely
                    SpeedTextBlock.Text = _item.Status == "Downloading" ? FormatSpeed(_item.SpeedBytesPerSec) : "-";
                    
                    TimeLeftTextBlock.Text = _item.Status == "Downloading" ? _item.DisplayTimeLeft : "-";
                    ResumeCapabilityTextBlock.Text = "Yes"; // Supports parallel chunks

                    GlobalProgressBar.Value = _item.ProgressPercentage;
                    GlobalProgressText.Text = $"{_item.ProgressPercentage:F1}%";

                    // Segment progress bar
                    ChunksItemsControl.ItemsSource = null;
                    ChunksItemsControl.ItemsSource = _item.Chunks;

                    // Build Table rows matching image
                    if (_item.Chunks != null)
                    {
                        var rowList = _item.Chunks.Select(chunk => new ConnectionRowItem
                        {
                            DisplayId = (chunk.Id + 1).ToString(),
                            DisplayDownloaded = $"{chunk.BytesDownloaded:N0} Bytes",
                            DisplayInfo = chunk.IsCompleted ? "Completed" : chunk.IsActive ? "Receiving data..." : "Send GET..."
                        }).ToList();

                        ConnectionsListView.ItemsSource = rowList;
                    }
                });
            }
            catch { }
        }

        private string FormatSpeed(double speedBytes)
        {
            double mb = speedBytes / (1024 * 1024);
            if (mb >= 0.1) return $"{mb:N3} MB/sec";
            double kb = speedBytes / 1024;
            return $"{kb:N1} KB/sec";
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (System.IO.File.Exists(_item.SavedPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_item.SavedPath}\"");
                }
                else
                {
                    string dir = System.IO.Path.GetDirectoryName(_item.SavedPath) ?? "";
                    if (System.IO.Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                    }
                }
            }
            catch { }
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_item.Status == "Downloading")
            {
                _engine.Pause();
            }
            else if (_item.Status == "Paused" || _item.Status == "Queued")
            {
                _ = _engine.StartAsync();
            }
            UpdateUI();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_item.Status == "Downloading")
            {
                _engine.Pause();
            }
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe to avoid memory leaks
            _engine.ProgressChanged -= OnProgressChanged;
            _engine.DownloadCompleted -= OnDownloadCompleted;
            _engine.DownloadError -= OnDownloadError;
            base.OnClosed(e);
        }
    }

    public class ConnectionRowItem
    {
        public string DisplayId { get; set; } = "";
        public string DisplayDownloaded { get; set; } = "";
        public string DisplayInfo { get; set; } = "";
    }
}
