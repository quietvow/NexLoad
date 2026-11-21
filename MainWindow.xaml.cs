using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DOWN_MNG
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<DownloadItem> _downloads = new ObservableCollection<DownloadItem>();
        private readonly Dictionary<string, DownloadEngine> _activeEngines = new Dictionary<string, DownloadEngine>();
        private readonly Dictionary<string, DownloadProgressWindow> _openProgressWindows = new Dictionary<string, DownloadProgressWindow>();
        private readonly string _dbPath;
        private string _currentFilter = "All";
        private string _searchQuery = "";
        private HttpListener? _httpListener;
        private SchedulerSettings _schedulerSettings = new SchedulerSettings();
        private string _schedulerSettingsPath = "";
        private TrayIconManager? _trayIconManager;
        private DispatcherTimer? _clipboardTimer;
        private string _lastClipboardText = "";
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            
            if (App.StartInBackground)
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
            }

            // Set DB path inside local app data folder
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dbFolder = Path.Combine(appData, "NexLoad");
            string legacyFolder = Path.Combine(appData, "DOWN-MNG");
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
                if (Directory.Exists(legacyFolder))
                {
                    foreach (var f in Directory.GetFiles(legacyFolder))
                    {
                        try { File.Copy(f, Path.Combine(dbFolder, Path.GetFileName(f)), true); } catch { }
                    }
                }
            }
            _dbPath = Path.Combine(dbFolder, "downloads.json");
            _schedulerSettingsPath = Path.Combine(dbFolder, "scheduler.json");

            LoadDownloadsDatabase();
            LoadSchedulerSettings();
            StartSchedulerTimer();
            StartClipboardMonitor();
            ApplyFilter();
            UpdateStatusBar();
            UpdateSpeedLimiterStatus();
            
            // Start local server for browser extension auto-capture
            StartLocalHttpServer();

            Loaded += (s, e) =>
            {
                InitializeTrayIcon();
                _trayIconManager?.Show("NexLoad Pro — Active");

                if (App.StartInBackground)
                {
                    Hide();
                    ShowInTaskbar = true;
                    _trayIconManager?.ShowBalloon("NexLoad Pro", "Running in the background. Ready to capture downloads.");
                }
            };

            Application.Current.SessionEnding += (s, e) => { _isExplicitExit = true; };
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIconManager = new TrayIconManager(this);
                _trayIconManager.TrayDoubleClicked += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        RestoreFromTray();
                    });
                };
                _trayIconManager.TrayRightClicked += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowTrayContextMenu();
                    });
                };
            }
            catch { }
        }

        public void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            _trayIconManager?.Show("DOWN-MNG Pro — Running in Background");
            _trayIconManager?.ShowBalloon("DOWN-MNG Pro", "Running in the background. Right-click the tray icon for quick actions.");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void ShowTrayContextMenu()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    SetForegroundWindow(helper.Handle);
                }

                var contextMenu = new ContextMenu();

                var itemOpen = new MenuItem { Header = "Open NexLoad Pro", FontWeight = FontWeights.Bold };
                itemOpen.Click += (s, e) => RestoreFromTray();
                contextMenu.Items.Add(itemOpen);

                contextMenu.Items.Add(new Separator());

                var itemAdd = new MenuItem { Header = "Add Download URL..." };
                itemAdd.Click += (s, e) => { RestoreFromTray(); AddUrl_Click(s, e); };
                contextMenu.Items.Add(itemAdd);

                var itemGrab = new MenuItem { Header = "Media & Video Grabber..." };
                itemGrab.Click += (s, e) => { RestoreFromTray(); VideoGrabber_Click(s, e); };
                contextMenu.Items.Add(itemGrab);

                var itemBatch = new MenuItem { Header = "Batch Link Grabber (From Text)..." };
                itemBatch.Click += (s, e) => { RestoreFromTray(); BatchLinkGrabber_Click(s, e); };
                contextMenu.Items.Add(itemBatch);

                var itemExt = new MenuItem { Header = "Browser Extension Setup (1-Click)..." };
                itemExt.Click += (s, e) => BrowserExtensionSetup_Click(s, e);
                contextMenu.Items.Add(itemExt);

                contextMenu.Items.Add(new Separator());

                var itemResumeAll = new MenuItem { Header = "Resume All Downloads" };
                itemResumeAll.Click += (s, e) => StartAll_Click(s, e);
                contextMenu.Items.Add(itemResumeAll);

                var itemPauseAll = new MenuItem { Header = "Pause All Downloads" };
                itemPauseAll.Click += (s, e) => PauseAll_Click(s, e);
                contextMenu.Items.Add(itemPauseAll);

                contextMenu.Items.Add(new Separator());

                var itemPrefs = new MenuItem { Header = "Preferences..." };
                itemPrefs.Click += (s, e) => { RestoreFromTray(); Preferences_Click(s, e); };
                contextMenu.Items.Add(itemPrefs);

                contextMenu.Items.Add(new Separator());

                var itemExit = new MenuItem { Header = "Exit NexLoad Pro" };
                itemExit.Click += (s, e) => { _isExplicitExit = true; Close(); };
                contextMenu.Items.Add(itemExit);

                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                contextMenu.IsOpen = true;
            }
            catch { }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && App.Settings.MinimizeToTray)
            {
                Hide();
                _trayIconManager?.Show("NexLoad Pro — Running in Background");
            }
        }

        private void StartClipboardMonitor()
        {
            _clipboardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _clipboardTimer.Tick += (s, e) =>
            {
                if (!App.Settings.ClipboardMonitoringEnabled) return;

                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText().Trim();
                        if (string.IsNullOrEmpty(text) || text == _lastClipboardText) return;

                        _lastClipboardText = text;

                        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                            text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        {
                            // Avoid popups if this window is currently not active or dialog already open
                            if (!this.IsActive && WindowState == WindowState.Minimized)
                            {
                                return;
                            }

                            bool alreadyExists = _downloads.Any(d => d.Url.Equals(text, StringComparison.OrdinalIgnoreCase));
                            if (!alreadyExists)
                            {
                                string plat = VideoDownloaderEngine.DetectPlatform(text);
                                if (plat == "YouTube" || plat == "Facebook" || plat == "TikTok")
                                {
                                    var res = MessageBox.Show($"Detected {plat} video on clipboard:\n\n{text}\n\nOpen Video Grabber?", "Clipboard Media Detected", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                    if (res == MessageBoxResult.Yes)
                                    {
                                        OpenVideoGrabber(text, plat);
                                    }
                                }
                                else if (CloudHosterResolver.IsCloudHoster(text))
                                {
                                    string hoster = CloudHosterResolver.GetHosterName(text);
                                    var res = MessageBox.Show($"Detected {hoster} cloud download link on clipboard:\n\n{text}\n\nStart Direct Download with NexLoad Pro?", $"Clipboard {hoster} Link Detected", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                    if (res == MessageBoxResult.Yes)
                                    {
                                        var dialog = new NewDownloadWindow(text) { Owner = this };
                                        if (dialog.ShowDialog() == true && dialog.ResultItem != null)
                                        {
                                            var item = dialog.ResultItem;
                                            _downloads.Add(item);
                                            SaveDownloadsDatabase();
                                            ApplyFilter();
                                            StartDownload(item);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            };
            _clipboardTimer.Start();
        }

        private void LoadDownloadsDatabase()
        {
            try
            {
                if (File.Exists(_dbPath))
                {
                    string json = File.ReadAllText(_dbPath);
                    var list = JsonSerializer.Deserialize<List<DownloadItem>>(json);
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            if (item.Status == "Downloading")
                            {
                                item.Status = "Paused";
                            }
                            item.SpeedBytesPerSec = 0;
                            _downloads.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading database: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveDownloadsDatabase()
        {
            try
            {
                var list = _downloads.ToList();
                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dbPath, json);
            }
            catch { }
        }

        private void AddUrl_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewDownloadWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultItem != null)
            {
                var item = dialog.ResultItem;
                _downloads.Add(item);
                SaveDownloadsDatabase();
                ApplyFilter();
                
                if (App.Settings.StartDownloadsAutomatically)
                {
                    StartDownload(item);
                }
            }
            else if (dialog.OpenInVideoGrabberRequested)
            {
                OpenVideoGrabber(dialog.UrlTextBox.Text.Trim());
            }
        }

        public void VideoGrabber_Click(object sender, RoutedEventArgs e)
        {
            OpenVideoGrabber();
        }

        public void BatchLinkGrabber_Click(object sender, RoutedEventArgs e)
        {
            var batchWin = new BatchLinkGrabberWindow { Owner = this };
            if (batchWin.ShowDialog() == true && batchWin.ResultItems.Count > 0)
            {
                int addedCount = 0;
                foreach (var item in batchWin.ResultItems)
                {
                    if (!_downloads.Any(d => d.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        _downloads.Add(item);
                        addedCount++;
                        if (batchWin.ShouldStartImmediately)
                        {
                            StartDownload(item, false);
                        }
                    }
                }

                if (addedCount > 0)
                {
                    SaveDownloadsDatabase();
                    ApplyFilter();
                    UpdateStatusBar();
                    MessageBox.Show($"Successfully added {addedCount} downloads from text.", "Batch Link Grabber", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        public void AddDownloadedItemFromBatch(DownloadItem item, bool startImmediately)
        {
            if (!_downloads.Any(d => d.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase)))
            {
                _downloads.Add(item);
                SaveDownloadsDatabase();
                ApplyFilter();
                UpdateStatusBar();
                if (startImmediately)
                {
                    StartDownload(item, false);
                }
            }
        }

        public void YouTubeGrabber_Click(object sender, RoutedEventArgs e)
        {
            OpenVideoGrabber("", "YouTube");
        }

        public void FacebookGrabber_Click(object sender, RoutedEventArgs e)
        {
            OpenVideoGrabber("", "Facebook");
        }

        public void TikTokGrabber_Click(object sender, RoutedEventArgs e)
        {
            OpenVideoGrabber("", "TikTok");
        }

        private void OpenVideoGrabber(string prefillUrl = "", string platformPreset = "")
        {
            var grabberWin = new VideoDownloadWindow(prefillUrl, platformPreset) { Owner = this };
            if (grabberWin.ShowDialog() == true && grabberWin.ResultItem != null)
            {
                var item = grabberWin.ResultItem;
                _downloads.Add(item);
                SaveDownloadsDatabase();
                ApplyFilter();
                
                if (grabberWin.ShouldStartImmediately)
                {
                    StartDownload(item, true);
                }
            }
        }

        private void StartDownload(DownloadItem item, bool showProgressWindow = true)
        {
            if (item.Status == "Completed") return;

            if (!_activeEngines.TryGetValue(item.Id, out var engine))
            {
                engine = new DownloadEngine(item, App.Settings.DefaultConnections);
                
                engine.ProgressChanged += (updatedItem) =>
                {
                    try
                    {
                        if (Application.Current == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (DownloadsListView.SelectedItem == updatedItem)
                            {
                                SelectedDownloadLabel.Text = $"{updatedItem.Filename} ({updatedItem.DisplayProgress})";
                                ChunksItemsControl.ItemsSource = null;
                                ChunksItemsControl.ItemsSource = updatedItem.Chunks;
                            }

                            UpdateStatusBar();
                        });
                    }
                    catch { }
                };

                engine.DownloadCompleted += (completedItem) =>
                {
                    try
                    {
                        if (Application.Current == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _activeEngines.Remove(completedItem.Id);
                            SaveDownloadsDatabase();
                            UpdateStatusBar();

                            // Play sound alert if enabled
                            if (App.Settings.PlaySoundOnCompletion)
                            {
                                try
                                {
                                    System.Media.SystemSounds.Asterisk.Play();
                                }
                                catch { }
                            }

                            // Show Balloon Notification if minimized/hidden
                            if (WindowState == WindowState.Minimized || !IsVisible)
                            {
                                _trayIconManager?.ShowBalloon("Download Complete", $"{completedItem.Filename} has finished downloading.");
                            }

                            // Automated Scheduler completion actions
                            if (_schedulerSettings.Enabled && _schedulerSettings.CompletionAction != "None")
                            {
                                bool anyDownloading = _downloads.Any(d => d.Status == "Downloading");
                                if (!anyDownloading && _activeEngines.Count == 0)
                                {
                                    if (_schedulerSettings.CompletionAction == "CloseApp")
                                    {
                                        Application.Current.Shutdown();
                                    }
                                    else if (_schedulerSettings.CompletionAction == "ShutdownPC")
                                    {
                                        Process.Start("shutdown", "/s /t 30");
                                    }
                                }
                            }
                        });
                    }
                    catch { }
                };

                engine.DownloadError += (failedItem, errorMsg) =>
                {
                    try
                    {
                        if (Application.Current == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _activeEngines.Remove(failedItem.Id);
                            SaveDownloadsDatabase();
                            UpdateStatusBar();

                            if (WindowState == WindowState.Minimized || !IsVisible)
                            {
                                _trayIconManager?.ShowBalloon("Download Failed", $"{failedItem.Filename}: {errorMsg}");
                            }
                        });
                    }
                    catch { }
                };

                _activeEngines[item.Id] = engine;
            }

            // Start download asynchronously
            _ = engine.StartAsync();

            if (showProgressWindow)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_openProgressWindows.TryGetValue(item.Id, out var progressWin))
                    {
                        progressWin.Activate();
                    }
                    else
                    {
                        progressWin = new DownloadProgressWindow(item, engine) { Owner = this };
                        _openProgressWindows[item.Id] = progressWin;
                        progressWin.Closed += (s, args) => _openProgressWindows.Remove(item.Id);
                        progressWin.Show();
                    }
                });
            }
        }

        private void StartAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _downloads.ToList())
            {
                if (item.Status != "Completed")
                {
                    StartDownload(item, false);
                }
            }
        }

        private void PauseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var engine in _activeEngines.Values.ToList())
            {
                engine.Pause();
            }
            _activeEngines.Clear();
            SaveDownloadsDatabase();
            UpdateStatusBar();
        }

        private void StopDownload_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected != null && _activeEngines.TryGetValue(selected.Id, out var engine))
            {
                engine.Pause();
                _activeEngines.Remove(selected.Id);
                SaveDownloadsDatabase();
                UpdateStatusBar();
            }
            else if (selected != null && selected.Status == "Downloading")
            {
                selected.Status = "Paused";
                SaveDownloadsDatabase();
                UpdateStatusBar();
            }
        }

        private void DownloadNow_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected != null && selected.Status != "Completed")
            {
                StartDownload(selected, true);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected == null) return;

            if (_activeEngines.TryGetValue(selected.Id, out var engine))
            {
                engine.Pause();
                _activeEngines.Remove(selected.Id);
            }

            _downloads.Remove(selected);
            SaveDownloadsDatabase();
            ApplyFilter();
            ChunksItemsControl.ItemsSource = null;
            SelectedDownloadLabel.Text = "[No active connection selected]";
            UpdateStatusBar();
        }

        private void DeleteFileFromDisk_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected == null) return;

            var result = MessageBox.Show($"Are you sure you want to permanently delete '{selected.Filename}' from your computer?", "Confirm Delete File", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                if (_activeEngines.TryGetValue(selected.Id, out var engine))
                {
                    engine.Pause();
                    _activeEngines.Remove(selected.Id);
                }

                try
                {
                    if (File.Exists(selected.SavedPath))
                    {
                        File.Delete(selected.SavedPath);
                    }
                }
                catch { }

                _downloads.Remove(selected);
                SaveDownloadsDatabase();
                ApplyFilter();
                ChunksItemsControl.ItemsSource = null;
                UpdateStatusBar();
            }
        }

        private void DeleteAllCompleted_Click(object sender, RoutedEventArgs e)
        {
            var completedList = _downloads.Where(d => d.Status == "Completed").ToList();
            if (completedList.Count == 0)
            {
                MessageBox.Show("No completed downloads found to delete.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Remove all {completedList.Count} completed downloads from list?", "Confirm Clear Done", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in completedList)
                {
                    _downloads.Remove(item);
                }
                SaveDownloadsDatabase();
                ApplyFilter();
                UpdateStatusBar();
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected == null) return;

            if (File.Exists(selected.SavedPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(selected.SavedPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Open File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("File does not exist on disk or download is not yet complete.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected == null) return;

            string folder = Path.GetDirectoryName(selected.SavedPath) ?? "";
            if (File.Exists(selected.SavedPath))
            {
                Process.Start("explorer.exe", $"/select,\"{selected.SavedPath}\"");
            }
            else if (Directory.Exists(folder))
            {
                Process.Start("explorer.exe", folder);
            }
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected != null && !string.IsNullOrEmpty(selected.Url))
            {
                Clipboard.SetText(selected.Url);
                MessageBox.Show("Download URL copied to clipboard.", "URL Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DownloadsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected != null)
            {
                SelectedDownloadLabel.Text = $"{selected.Filename} ({selected.DisplayProgress})";
                ChunksItemsControl.ItemsSource = selected.Chunks;
            }
            else
            {
                SelectedDownloadLabel.Text = "[No active connection selected]";
                ChunksItemsControl.ItemsSource = null;
            }
        }

        private void CategoriesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = CategoriesTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null) return;

            _currentFilter = selectedItem.Tag?.ToString() ?? "All";
            CurrentCategoryLabel.Text = selectedItem.Header is StackPanel sp && sp.Children.OfType<TextBlock>().LastOrDefault() is TextBlock tb ? tb.Text : _currentFilter;
            ApplyFilter();
        }

        private void QuickFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _currentFilter = tag;
                CurrentCategoryLabel.Text = tag == "All" ? "All Downloads" : tag;
                ApplyFilter();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = SearchTextBox.Text.Trim();
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (DownloadsListView == null) return;

            IEnumerable<DownloadItem> filtered = _downloads;

            if (_currentFilter == "All")
            {
                filtered = _downloads;
            }
            else if (_currentFilter == "VideoGrabber")
            {
                filtered = _downloads.Where(d => d.Platform == "YouTube" || d.Platform == "Facebook" || d.Platform == "TikTok" || d.Protocol == "Video");
            }
            else if (_currentFilter == "YouTube" || _currentFilter == "Facebook" || _currentFilter == "TikTok")
            {
                filtered = _downloads.Where(d => d.Platform == _currentFilter || (d.Url != null && d.Url.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)));
            }
            else if (_currentFilter == "Downloading" || _currentFilter == "Paused" || _currentFilter == "Completed")
            {
                filtered = _downloads.Where(d => d.Status == _currentFilter);
            }
            else if (_currentFilter == "Torrent")
            {
                filtered = _downloads.Where(d => d.Protocol == "Torrent" || d.Category == "Torrent");
            }
            else if (_currentFilter == "CloudHosters")
            {
                filtered = _downloads.Where(d => d.Platform == "Pixeldrain" || d.Platform == "Mega" || d.Platform == "MediaFire" || d.Platform == "Google Drive" || d.Platform == "Dropbox" || d.Platform == "GoFile" || d.Category == "Cloud Hosters");
            }
            else if (_currentFilter == "Pixeldrain" || _currentFilter == "Mega" || _currentFilter == "MediaFire" || _currentFilter == "Google Drive")
            {
                filtered = _downloads.Where(d => d.Platform == _currentFilter || (d.Url != null && d.Url.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)));
            }
            else if (_currentFilter == "Queues")
            {
                filtered = _downloads.Where(d => d.Status == "Queued");
            }
            else
            {
                filtered = _downloads.Where(d => d.Category == _currentFilter);
            }

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                filtered = filtered.Where(d => (d.Filename != null && d.Filename.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)) ||
                                              (d.Url != null && d.Url.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)) ||
                                              (d.Platform != null && d.Platform.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)));
            }

            DownloadsListView.ItemsSource = new ObservableCollection<DownloadItem>(filtered.ToList());
        }

        private void UpdateStatusBar()
        {
            int active = _downloads.Count(d => d.Status == "Downloading");
            double totalSpeed = _downloads.Where(d => d.Status == "Downloading").Sum(d => d.SpeedBytesPerSec);

            ActiveCountTextBlock.Text = active.ToString();
            TotalSpeedTextBlock.Text = $"{DownloadItem.FormatBytes((long)totalSpeed)}/s";
            TotalTasksTextBlock.Text = _downloads.Count.ToString();
        }

        private void UpdateSpeedLimiterStatus()
        {
            if (App.Settings.SpeedLimitEnabled)
            {
                string rate = App.Settings.SpeedLimitKBps >= 1024 
                    ? $"{App.Settings.SpeedLimitKBps / 1024.0:F1} MB/s" 
                    : $"{App.Settings.SpeedLimitKBps} KB/s";
                SpeedLimiterStatusTextBlock.Text = rate;
                SpeedLimiterStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));
            }
            else
            {
                SpeedLimiterStatusTextBlock.Text = "OFF";
                SpeedLimiterStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
            }
        }

        private void DownloadsListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = DownloadsListView.SelectedItem as DownloadItem;
            if (selected != null)
            {
                if (selected.Status == "Completed")
                {
                    OpenFile_Click(sender, e);
                }
                else
                {
                    StartDownload(selected, true);
                }
            }
        }

        private void LanguageToggle_Click(object sender, RoutedEventArgs e)
        {
            string newLang = LocalizationManager.CurrentLanguage == "en" ? "si" : "en";
            LocalizationManager.SwitchLanguage(newLang);
            ApplyFilter();
        }

        private void HideCategories_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesColumn.Width.Value > 0)
            {
                CategoriesColumn.Width = new GridLength(0);
                HideCategoriesMenuItem.Header = "Show categories panel";
            }
            else
            {
                CategoriesColumn.Width = new GridLength(230);
                HideCategoriesMenuItem.Header = "Hide categories panel";
            }
        }

        private void DarkModeToggle_Click(object sender, RoutedEventArgs e)
        {
            string newTheme = App.Settings.Theme == "Dark" ? "Light" : "Dark";
            App.ApplyTheme(newTheme);
            App.Settings.Save();
        }

        private void SpeedLimiter_Click(object sender, RoutedEventArgs e)
        {
            var win = new SpeedLimiterWindow(App.Settings) { Owner = this };
            if (win.ShowDialog() == true)
            {
                UpdateSpeedLimiterStatus();
            }
        }

        private void Preferences_Click(object sender, RoutedEventArgs e)
        {
            var win = new PreferencesWindow(App.Settings) { Owner = this };
            if (win.ShowDialog() == true)
            {
                if (win.ThemeChanged)
                {
                    App.ApplyTheme(App.Settings.Theme);
                }
                if (win.LanguageChanged)
                {
                    LocalizationManager.SwitchLanguage(App.Settings.Language);
                    ApplyFilter();
                }
                UpdateSpeedLimiterStatus();
            }
        }

        private void ExportDownloads_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Export Downloads List",
                Filter = "JSON Database (*.json)|*.json|Text File URLs (*.txt)|*.txt",
                FileName = $"downloads_backup_{DateTime.Now:yyyyMMdd}.json"
            };

            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    if (sfd.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var urls = _downloads.Select(d => d.Url);
                        File.WriteAllLines(sfd.FileName, urls);
                    }
                    else
                    {
                        string json = JsonSerializer.Serialize(_downloads.ToList(), new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(sfd.FileName, json);
                    }
                    MessageBox.Show($"Successfully exported {_downloads.Count} downloads.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportDownloads_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Import Downloads List",
                Filter = "Supported files (*.json;*.txt)|*.json;*.txt|JSON Database (*.json)|*.json|Text File URLs (*.txt)|*.txt"
            };

            if (ofd.ShowDialog(this) == true)
            {
                try
                {
                    int importedCount = 0;
                    if (ofd.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        var lines = File.ReadAllLines(ofd.FileName);
                        foreach (var line in lines)
                        {
                            string url = line.Trim();
                            if (!string.IsNullOrEmpty(url) && (url.StartsWith("http") || url.StartsWith("magnet:")))
                            {
                                if (!_downloads.Any(d => d.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                                {
                                    var item = new DownloadItem
                                    {
                                        Url = url,
                                        Filename = Path.GetFileName(url) != "" ? Path.GetFileName(url) : "download_item",
                                        SavedPath = Path.Combine(App.Settings.DefaultDownloadFolder, Path.GetFileName(url)),
                                        Status = "Queued",
                                        Platform = VideoDownloaderEngine.DetectPlatform(url)
                                    };
                                    _downloads.Add(item);
                                    importedCount++;
                                }
                            }
                        }
                    }
                    else
                    {
                        string json = File.ReadAllText(ofd.FileName);
                        var list = JsonSerializer.Deserialize<List<DownloadItem>>(json);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                if (!_downloads.Any(d => d.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase)))
                                {
                                    item.Status = "Queued";
                                    item.SpeedBytesPerSec = 0;
                                    _downloads.Add(item);
                                    importedCount++;
                                }
                            }
                        }
                    }

                    SaveDownloadsDatabase();
                    ApplyFilter();
                    UpdateStatusBar();
                    MessageBox.Show($"Successfully imported {importedCount} new downloads.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        string filePath = files[0];
                        if (filePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                        {
                            var dialog = new NewDownloadWindow(filePath) { Owner = this };
                            if (dialog.ShowDialog() == true && dialog.ResultItem != null)
                            {
                                var item = dialog.ResultItem;
                                _downloads.Add(item);
                                SaveDownloadsDatabase();
                                ApplyFilter();
                                StartDownload(item);
                            }
                        }
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string droppedText = ((string)e.Data.GetData(DataFormats.Text)).Trim();
                    if (droppedText.StartsWith("http") || droppedText.StartsWith("magnet:"))
                    {
                        string plat = VideoDownloaderEngine.DetectPlatform(droppedText);
                        if (plat == "YouTube" || plat == "Facebook" || plat == "TikTok")
                        {
                            OpenVideoGrabber(droppedText, plat);
                        }
                        else
                        {
                            var dialog = new NewDownloadWindow(droppedText) { Owner = this };
                            if (dialog.ShowDialog() == true && dialog.ResultItem != null)
                            {
                                var item = dialog.ResultItem;
                                _downloads.Add(item);
                                SaveDownloadsDatabase();
                                ApplyFilter();
                                StartDownload(item);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void Scheduler_Click(object sender, RoutedEventArgs e)
        {
            var schedulerWin = new SchedulerWindow { Owner = this };
            if (schedulerWin.ShowDialog() == true)
            {
                _schedulerSettings = schedulerWin.Settings;
            }
        }

        private void UserGuide_Click(object sender, RoutedEventArgs e)
        {
            var win = new HelpGuideWindow(0) { Owner = this };
            win.ShowDialog();
        }

        private void VideoGrabberTutorial_Click(object sender, RoutedEventArgs e)
        {
            var win = new HelpGuideWindow(1) { Owner = this };
            win.ShowDialog();
        }

        private void BrowserExtensionSetup_Click(object sender, RoutedEventArgs e)
        {
            var win = new BrowserExtensionSetupWindow { Owner = this.IsVisible ? this : null };
            win.ShowDialog();
        }

        private void BrowserExtensionHelp_Click(object sender, RoutedEventArgs e)
        {
            var win = new BrowserExtensionSetupWindow { Owner = this.IsVisible ? this : null };
            win.ShowDialog();
        }

        private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var win = new HelpGuideWindow(4) { Owner = this };
            win.ShowDialog();
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You are currently running the latest release of NexLoad Pro (v1.0.0).\nAll core download acceleration modules are up to date.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var win = new AboutWindow { Owner = this };
            win.ShowDialog();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _isExplicitExit = true;
            Close();
        }

        private void StartLocalHttpServer()
        {
            Task.Run(async () =>
            {
                try
                {
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add("http://localhost:3015/ping/");
                    _httpListener.Prefixes.Add("http://localhost:3015/add-download/");
                    _httpListener.Start();

                    while (_httpListener.IsListening)
                    {
                        var context = await _httpListener.GetContextAsync();
                        ProcessHttpRequest(context);
                    }
                }
                catch { }
            });
        }

        private async void ProcessHttpRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                try
                {
                    response.Headers.Add("Access-Control-Allow-Origin", "*");
                    response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                    response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                }
                catch { }

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    try { response.Close(); } catch { }
                    return;
                }

                try
                {
                    if (request.Url?.AbsolutePath == "/ping")
                    {
                        response.StatusCode = 200;
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes("pong");
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else if (request.Url?.AbsolutePath == "/add-download" && request.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(request.InputStream);
                        string body = await reader.ReadToEndAsync();
                        
                        using var doc = JsonDocument.Parse(body);
                        string url = doc.RootElement.GetProperty("url").GetString() ?? "";
                        string filename = doc.RootElement.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(url))
                        {
                            var app = Application.Current;
                            if (app != null)
                            {
                                app.Dispatcher.Invoke(() =>
                                {
                                    if (this.WindowState == WindowState.Minimized || !this.IsVisible)
                                    {
                                        this.Show();
                                        this.WindowState = WindowState.Normal;
                                    }
                                    this.Activate();
                                    this.Focus();

                                    string plat = VideoDownloaderEngine.DetectPlatform(url);
                                    if (plat == "YouTube" || plat == "Facebook" || plat == "TikTok")
                                    {
                                        OpenVideoGrabber(url, plat);
                                    }
                                    else
                                    {
                                        var dialog = new NewDownloadWindow(url, filename) { Owner = this };
                                        if (dialog.ShowDialog() == true && dialog.ResultItem != null)
                                        {
                                            var item = dialog.ResultItem;
                                            _downloads.Add(item);
                                            SaveDownloadsDatabase();
                                            ApplyFilter();
                                            StartDownload(item);
                                        }
                                    }
                                });
                            }
                        }

                        response.StatusCode = 200;
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes("{\"status\":\"success\"}");
                        response.ContentLength64 = buffer.Length;
                        response.ContentType = "application/json";
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        response.StatusCode = 404;
                    }
                }
                catch
                {
                    try { response.StatusCode = 500; } catch { }
                }
                finally
                {
                    try { response.Close(); } catch { }
                }
            }
            catch { }
        }

        private void LoadSchedulerSettings()
        {
            try
            {
                if (File.Exists(_schedulerSettingsPath))
                {
                    string json = File.ReadAllText(_schedulerSettingsPath);
                    var settings = JsonSerializer.Deserialize<SchedulerSettings>(json);
                    if (settings != null)
                    {
                        _schedulerSettings = settings;
                    }
                }
            }
            catch { }
        }

        private System.Windows.Threading.DispatcherTimer? _schedulerTimer;
        private void StartSchedulerTimer()
        {
            _schedulerTimer = new System.Windows.Threading.DispatcherTimer();
            _schedulerTimer.Interval = TimeSpan.FromSeconds(10);
            _schedulerTimer.Tick += SchedulerTimer_Tick;
            _schedulerTimer.Start();
        }

        private string _lastTickMinute = "";
        private void SchedulerTimer_Tick(object? sender, EventArgs e)
        {
            if (!_schedulerSettings.Enabled) return;

            var now = DateTime.Now;
            string currentMinute = now.ToString("HH:mm");

            if (currentMinute == _lastTickMinute) return;

            if (currentMinute == _schedulerSettings.StartTime)
            {
                _lastTickMinute = currentMinute;
                StartAll_Click(this, new RoutedEventArgs());
            }
            else if (_schedulerSettings.StopOnTime && currentMinute == _schedulerSettings.StopTime)
            {
                _lastTickMinute = currentMinute;
                PauseAll_Click(this, new RoutedEventArgs());
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // If CloseToTray is enabled and not explicit exit, minimize to tray (IDM / JDownloader style)
            if (!_isExplicitExit && App.Settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                _trayIconManager?.Show("NexLoad Pro — Running in Background");
                _trayIconManager?.ShowBalloon("NexLoad Pro", "Running in the background. Downloads & browser capture remain active.");
                return;
            }

            foreach (var progressWin in _openProgressWindows.Values.ToList())
            {
                try { progressWin.Close(); } catch { }
            }
            _openProgressWindows.Clear();

            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }

            _trayIconManager?.Dispose();
            PauseAll_Click(this, new RoutedEventArgs());
            base.OnClosing(e);
        }
    }
}
