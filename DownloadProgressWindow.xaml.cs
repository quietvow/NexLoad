using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;

namespace DOWN_MNG
{
    public partial class DownloadProgressWindow : Window
    {
        private readonly DownloadItem _item;
        private readonly DownloadEngine _engine;
        private double _peakSpeedBytes = 0;
        private readonly List<double> _speedSamples = new List<double>();

        public DownloadProgressWindow(DownloadItem item, DownloadEngine engine)
        {
            InitializeComponent();
            _item = item;
            _engine = engine;

            UrlTextBlock.Text = _item.Url;
            Title = $"{_item.ProgressPercentage:F0}% " + (_item.Filename.Length > 25 ? _item.Filename.Substring(0, 25) + "..." : _item.Filename);

            // Initialize checkboxes from saved AppSettings
            ShowCompleteDialogCheckBox.IsChecked = App.Settings.ShowDownloadCompleteDialog;
            AutoCloseCheckBox.IsChecked = App.Settings.CloseProgressWindowOnCompletion;
            OpenFileCheckBox.IsChecked = App.Settings.OpenFileOnCompletion;
            OpenFolderCheckBox.IsChecked = App.Settings.OpenFolderOnCompletion;
            PlaySoundCheckBox.IsChecked = App.Settings.PlaySoundOnCompletion;
            ShutdownCheckBox.IsChecked = App.Settings.ShutdownOnCompletion;

            // Wire settings changes
            ShowCompleteDialogCheckBox.Click += (s, e) => { App.Settings.ShowDownloadCompleteDialog = ShowCompleteDialogCheckBox.IsChecked == true; App.Settings.Save(); };
            AutoCloseCheckBox.Click += (s, e) => { App.Settings.CloseProgressWindowOnCompletion = AutoCloseCheckBox.IsChecked == true; App.Settings.Save(); };
            OpenFileCheckBox.Click += (s, e) => { App.Settings.OpenFileOnCompletion = OpenFileCheckBox.IsChecked == true; App.Settings.Save(); };
            OpenFolderCheckBox.Click += (s, e) => { App.Settings.OpenFolderOnCompletion = OpenFolderCheckBox.IsChecked == true; App.Settings.Save(); };
            PlaySoundCheckBox.Click += (s, e) => { App.Settings.PlaySoundOnCompletion = PlaySoundCheckBox.IsChecked == true; App.Settings.Save(); };
            ShutdownCheckBox.Click += (s, e) => { App.Settings.ShutdownOnCompletion = ShutdownCheckBox.IsChecked == true; App.Settings.Save(); };

            // Listen to progress updates
            _engine.ProgressChanged += OnProgressChanged;
            _engine.DownloadCompleted += OnDownloadCompleted;
            _engine.DownloadError += OnDownloadError;

            UpdateUI();
        }

        private void OnProgressChanged(DownloadItem item)
        {
            if (item.Id == _item.Id)
            {
                UpdateUI();
            }
        }

        private void OnDownloadCompleted(DownloadItem item)
        {
            if (item.Id != _item.Id) return;

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

                    // 1. Play notification sound if enabled
                    if (PlaySoundCheckBox.IsChecked == true)
                    {
                        try { SystemSounds.Asterisk.Play(); } catch { }
                    }

                    // 2. Open file automatically if option selected
                    if (OpenFileCheckBox.IsChecked == true && File.Exists(_item.SavedPath))
                    {
                        try { Process.Start(new ProcessStartInfo(_item.SavedPath) { UseShellExecute = true }); } catch { }
                    }

                    // 3. Open containing folder if option selected
                    if (OpenFolderCheckBox.IsChecked == true)
                    {
                        try
                        {
                            if (File.Exists(_item.SavedPath))
                            {
                                Process.Start("explorer.exe", $"/select,\"{_item.SavedPath}\"");
                            }
                        }
                        catch { }
                    }

                    // 4. Shutdown computer if option selected
                    if (ShutdownCheckBox.IsChecked == true)
                    {
                        try
                        {
                            Process.Start("shutdown.exe", "/s /t 60 /c \"NexLoad: Download completed. Shutting down in 60 seconds.\"");
                        }
                        catch { }
                    }

                    bool shouldAutoClose = AutoCloseCheckBox.IsChecked == true;
                    bool shouldShowCompleteDialog = ShowCompleteDialogCheckBox.IsChecked == true;

                    // 5. Open Download Complete Window
                    if (shouldShowCompleteDialog)
                    {
                        var completeWin = new DownloadCompleteWindow(_item);
                        completeWin.Show();
                    }

                    // 6. Automatically close progress window if requested
                    if (shouldAutoClose)
                    {
                        Close();
                    }
                });
            }
            catch { }
        }

        private void OnDownloadError(DownloadItem item, string errorMsg)
        {
            if (item.Id != _item.Id) return;

            try
            {
                if (Application.Current == null) return;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateUI();
                    StatusTextBlock.Text = "Failed";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    MessageBox.Show($"Download Failed:\n{errorMsg}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    Title = $"{_item.ProgressPercentage:F0}% " + (_item.Filename.Length > 25 ? _item.Filename.Substring(0, 25) + "..." : _item.Filename);
                    StatusTextBlock.Text = _item.Status == "Downloading" ? "Receiving data..." : _item.Status;
                    
                    if (_item.Status == "Downloading")
                    {
                        StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                        PauseResumeButton.Content = "Pause";
                        PauseResumeButton.IsEnabled = true;
                    }
                    else if (_item.Status == "Paused")
                    {
                        StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
                        PauseResumeButton.Content = "Resume";
                        PauseResumeButton.IsEnabled = true;
                    }
                    else if (_item.Status == "Completed")
                    {
                        StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                        PauseResumeButton.IsEnabled = false;
                        CancelButton.Content = "Close";
                    }

                    // Calculate speed metrics
                    if (_item.Status == "Downloading" && _item.SpeedBytesPerSec > 0)
                    {
                        if (_item.SpeedBytesPerSec > _peakSpeedBytes)
                        {
                            _peakSpeedBytes = _item.SpeedBytesPerSec;
                        }
                        _speedSamples.Add(_item.SpeedBytesPerSec);
                        if (_speedSamples.Count > 60) _speedSamples.RemoveAt(0);
                    }

                    double avgSpeed = _speedSamples.Count > 0 ? _speedSamples.Average() : 0;
                    PeakSpeedTextBlock.Text = FormatSpeed(_peakSpeedBytes);
                    AvgSpeedTextBlock.Text = FormatSpeed(avgSpeed);

                    int activeChunks = _item.Chunks != null ? _item.Chunks.Count(c => c.IsActive && !c.IsCompleted) : 0;
                    int totalChunks = _item.Chunks != null && _item.Chunks.Count > 0 ? _item.Chunks.Count : 8;
                    ActiveStreamsTextBlock.Text = _item.Status == "Completed" 
                        ? $"All {totalChunks} Streams Finished" 
                        : $"{activeChunks} of {totalChunks} Multi-Threaded Streams Active";

                    // Size and downloaded formatting with byte accuracy
                    SizeTextBlock.Text = _item.TotalSizeBytes > 0 ? $"{_item.TotalSizeBytes:N0} Bytes ({_item.DisplaySize})" : "Unknown";
                    DownloadedTextBlock.Text = $"{_item.BytesDownloaded:N0} Bytes ( {_item.ProgressPercentage:F2} % )";
                    
                    SpeedTextBlock.Text = _item.Status == "Downloading" ? FormatSpeed(_item.SpeedBytesPerSec) : "-";
                    TimeLeftTextBlock.Text = _item.Status == "Downloading" ? _item.DisplayTimeLeft : "-";
                    ResumeCapabilityTextBlock.Text = _item.TotalSizeBytes > 0 ? "Yes" : "Unknown";

                    GlobalProgressBar.Value = _item.ProgressPercentage;
                    GlobalProgressText.Text = $"{_item.ProgressPercentage:F1}%";

                    // Segment visualizer bar
                    ChunksItemsControl.ItemsSource = null;
                    ChunksItemsControl.ItemsSource = _item.Chunks;

                    // Build connection table rows
                    if (_item.Chunks != null && _item.Chunks.Count > 0)
                    {
                        var rowList = _item.Chunks.Select(chunk => new ConnectionRowItem
                        {
                            DisplayId = (chunk.Id + 1).ToString(),
                            DisplayDownloaded = $"{chunk.BytesDownloaded:N0} Bytes",
                            DisplayInfo = chunk.IsCompleted ? "Completed" : chunk.IsActive ? $"Receiving data ({chunk.ProgressPercentage:F0}%)" : "Connecting..."
                        }).ToList();

                        ConnectionsListView.ItemsSource = rowList;
                    }
                });
            }
            catch { }
        }

        private string FormatSpeed(double speedBytes)
        {
            if (speedBytes <= 0) return "0 KB/s";
            double mb = speedBytes / (1024 * 1024);
            if (mb >= 0.1) return $"{mb:N2} MB/s";
            double kb = speedBytes / 1024;
            return $"{kb:N1} KB/s";
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
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_item.Status == "Completed" || _item.Status == "Failed")
            {
                Close();
            }
            else
            {
                var result = MessageBox.Show($"Are you sure you want to cancel downloading '{_item.Filename}'?", "Cancel Download", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _engine.Pause();
                    Close();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
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
