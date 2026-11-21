using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DOWN_MNG
{
    public partial class BatchLinkGrabberWindow : Window
    {
        private List<BatchLinkItem> _allItems = new List<BatchLinkItem>();
        private ObservableCollection<BatchLinkItem> _displayedItems = new ObservableCollection<BatchLinkItem>();
        private string _targetFolder;

        public List<DownloadItem> ResultItems { get; private set; } = new List<DownloadItem>();
        public bool ShouldStartImmediately { get; private set; } = true;

        public BatchLinkGrabberWindow(string prefillText = "")
        {
            InitializeComponent();

            _targetFolder = !string.IsNullOrEmpty(App.Settings.DefaultDownloadFolder) 
                ? App.Settings.DefaultDownloadFolder 
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SDM-Downloads");

            if (!Directory.Exists(_targetFolder))
            {
                try { Directory.CreateDirectory(_targetFolder); } catch { }
            }

            SavePathTextBox.Text = _targetFolder;
            StartImmediatelyCheckBox.IsChecked = App.Settings.StartDownloadsAutomatically;
            LinksListView.ItemsSource = _displayedItems;
            PlatformFilterComboBox.SelectedIndex = 0;

            if (!string.IsNullOrWhiteSpace(prefillText))
            {
                InputTextBox.Text = prefillText;
                ProcessExtraction(prefillText);
            }
            else
            {
                Loaded += (s, e) =>
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string clip = Clipboard.GetText().Trim();
                            if (clip.Contains("http://") || clip.Contains("https://") || clip.Contains("magnet:"))
                            {
                                InputTextBox.Text = clip;
                                ProcessExtraction(clip);
                            }
                        }
                    }
                    catch { }
                };
            }
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Optional live extraction if small text
        }

        private void PasteClipboardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText();
                    InputTextBox.Text = clip;
                    ProcessExtraction(clip);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Clear();
            _allItems.Clear();
            _displayedItems.Clear();
            UpdateCounts();
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            string text = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Please paste or type text containing links to extract.", "No Text", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ProcessExtraction(text);
        }

        private async void ProcessExtraction(string text)
        {
            _allItems = BatchLinkExtractor.ExtractLinks(text);
            ApplyPlatformFilter();
            UpdateCounts();

            var cloudLinks = _allItems.Where(i => CloudHosterResolver.IsCloudHoster(i.Url)).ToList();
            if (cloudLinks.Count > 0)
            {
                ResultsGroupBox.Header = $"Extracted Links ({_allItems.Count} found) — Resolving Cloud Hosters...";
                foreach (var link in cloudLinks)
                {
                    try
                    {
                        var res = await CloudHosterResolver.ResolveAsync(link.Url);
                        if (res != null)
                        {
                            link.Filename = res.FileName;
                            link.Platform = res.HosterName;
                            link.Category = DownloadItem.DetermineCategory(res.FileName, res.HosterName);
                        }
                    }
                    catch { }
                }
                LinksListView.Items.Refresh();
                UpdateCounts();
            }
        }

        private void ApplyPlatformFilter()
        {
            if (_displayedItems == null || PlatformFilterComboBox == null) return;

            _displayedItems.Clear();
            string selectedPlatform = "All Platforms";

            if (PlatformFilterComboBox.SelectedItem is ComboBoxItem item && item.Content is string content)
            {
                selectedPlatform = content;
            }

            IEnumerable<BatchLinkItem> filtered = _allItems ?? Enumerable.Empty<BatchLinkItem>();

            if (selectedPlatform.StartsWith("Cloud Hosters"))
            {
                filtered = filtered.Where(i => CloudHosterResolver.IsCloudHoster(i.Url) || i.Platform == "Pixeldrain" || i.Platform == "Mega" || i.Platform == "MediaFire" || i.Platform == "Google Drive");
            }
            else if (selectedPlatform == "YouTube")
            {
                filtered = filtered.Where(i => i.Platform == "YouTube");
            }
            else if (selectedPlatform == "Facebook")
            {
                filtered = filtered.Where(i => i.Platform == "Facebook");
            }
            else if (selectedPlatform == "TikTok")
            {
                filtered = filtered.Where(i => i.Platform == "TikTok");
            }
            else if (selectedPlatform == "Torrents")
            {
                filtered = filtered.Where(i => i.Platform == "Torrent" || i.Category == "Torrent");
            }
            else if (selectedPlatform == "Direct Links")
            {
                filtered = filtered.Where(i => i.Platform != "YouTube" && i.Platform != "Facebook" && i.Platform != "TikTok" && i.Platform != "Torrent");
            }

            foreach (var link in filtered)
            {
                _displayedItems.Add(link);
            }
        }

        private void PlatformFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyPlatformFilter();
            UpdateCounts();
        }

        private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateCounts();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedItems)
            {
                item.IsSelected = true;
            }
            LinksListView.Items.Refresh();
            UpdateCounts();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedItems)
            {
                item.IsSelected = false;
            }
            LinksListView.Items.Refresh();
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            if (AddButton == null || ResultsGroupBox == null || _allItems == null) return;

            int selectedCount = _allItems.Count(i => i.IsSelected);
            ResultsGroupBox.Header = $"Extracted Links ({_allItems.Count} found, {selectedCount} selected)";
            AddButton.Content = $"Add Selected ({selectedCount})";
            AddButton.IsEnabled = selectedCount > 0;
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder",
                InitialDirectory = Directory.Exists(_targetFolder) ? _targetFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog(this) == true)
            {
                _targetFolder = dialog.FolderName;
                SavePathTextBox.Text = _targetFolder;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allItems.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            ResultItems.Clear();
            foreach (var item in selected)
            {
                string protocol = "HTTP";
                if (item.Platform == "YouTube" || item.Platform == "Facebook" || item.Platform == "TikTok")
                {
                    protocol = "Video";
                }
                else if (item.Platform == "Mega")
                {
                    protocol = "Mega";
                }
                else if (item.Platform == "Torrent" || item.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = "Torrent";
                }
                else if (item.Url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = "FTP";
                }

                string savedPath = Path.Combine(_targetFolder, item.Filename);

                ResultItems.Add(new DownloadItem
                {
                    Url = item.Url,
                    Filename = item.Filename,
                    SavedPath = savedPath,
                    Platform = item.Platform,
                    Category = item.Category,
                    Protocol = protocol,
                    Status = "Queued",
                    AddedDate = DateTime.Now
                });
            }

            ShouldStartImmediately = StartImmediatelyCheckBox.IsChecked == true;
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
