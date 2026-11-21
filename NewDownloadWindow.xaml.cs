using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MonoTorrent;

namespace DOWN_MNG
{
    public partial class NewDownloadWindow : Window
    {
        public DownloadItem? ResultItem { get; private set; }
        public bool OpenInVideoGrabberRequested { get; private set; }
        private readonly string _defaultDownloadFolder;
        private long _detectedSize = -1;
        private string _resolvedDirectUrl = "";
        private CloudHosterResult? _cloudHosterResult;

        public NewDownloadWindow()
        {
            InitializeComponent();
            
            // Default to system Downloads folder
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _defaultDownloadFolder = Path.Combine(userProfile, "Downloads", "SDM-Downloads");
            
            if (!Directory.Exists(_defaultDownloadFolder))
            {
                Directory.CreateDirectory(_defaultDownloadFolder);
            }

            Loaded += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string clipText = Clipboard.GetText().Trim();
                            if (clipText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                clipText.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                clipText.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                                clipText.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                            {
                                UrlTextBox.Text = clipText;
                                _ = AnalyzeLinkAsync(clipText);
                            }
                        }
                    }
                    catch { }
                }
            };
        }

        public NewDownloadWindow(string url, string filename = "") : this()
        {
            UrlTextBox.Text = url;
            if (!string.IsNullOrEmpty(filename))
            {
                FileNameTextBox.Text = filename;
                PathTextBox.Text = Path.Combine(_defaultDownloadFolder, filename);
            }

            Loaded += (s, e) => AutoAnalyzeLink(url);
        }

        private void AutoAnalyzeLink(string url)
        {
            _ = AnalyzeLinkAsync(url);
        }

        private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            string plat = VideoDownloaderEngine.DetectPlatform(url);

            if (plat == "YouTube" || plat == "Facebook" || plat == "TikTok")
            {
                VideoBanner.Visibility = Visibility.Visible;
                VideoBannerText.Text = $"✨ {plat} media detected! Open in Video Grabber for 1080p, 720p, or MP3 options?";
                CloudBanner.Visibility = Visibility.Collapsed;
                CategoryBadgeText.Text = plat;
            }
            else if (CloudHosterResolver.IsCloudHoster(url))
            {
                string hoster = CloudHosterResolver.GetHosterName(url);
                VideoBanner.Visibility = Visibility.Collapsed;
                CloudBanner.Visibility = Visibility.Visible;
                CloudBannerText.Text = $"⚡ {hoster} cloud hoster detected. Ready to query direct stream.";
                CategoryBadgeText.Text = hoster;
            }
            else
            {
                VideoBanner.Visibility = Visibility.Collapsed;
                CloudBanner.Visibility = Visibility.Collapsed;
                CategoryBadgeText.Text = "Direct";
            }
        }

        private void FolderBatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cloudHosterResult != null && _cloudHosterResult.AdditionalFiles.Count > 0)
            {
                var urls = string.Join(Environment.NewLine, _cloudHosterResult.AdditionalFiles.Select(f => f.OriginalUrl));
                var batchWin = new BatchLinkGrabberWindow(urls) { Owner = this.Owner };
                if (batchWin.ShowDialog() == true && batchWin.ResultItems.Count > 0)
                {
                    if (Owner is MainWindow mainWin)
                    {
                        foreach (var item in batchWin.ResultItems)
                        {
                            mainWin.AddDownloadedItemFromBatch(item, batchWin.ShouldStartImmediately);
                        }
                    }
                }
                DialogResult = false;
                Close();
            }
        }

        private void OpenVideoGrabber_Click(object sender, RoutedEventArgs e)
        {
            OpenInVideoGrabberRequested = true;
            DialogResult = false;
            Close();
        }

        private async Task AnalyzeLinkAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            StatusTextBlock.Text = "Analyzing link and querying server metadata...";
            DownloadButton.IsEnabled = false;

            try
            {
                string plat = VideoDownloaderEngine.DetectPlatform(url);
                if (plat == "YouTube" || plat == "Facebook" || plat == "TikTok")
                {
                    VideoBanner.Visibility = Visibility.Visible;
                    VideoBannerText.Text = $"✨ {plat} media detected! Open in Video Grabber for 1080p, 720p, or MP3 options?";
                    CategoryBadgeText.Text = plat;
                    _detectedSize = -1;
                    FileNameTextBox.Text = $"{plat}_Video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    FileSizeTextBlock.Text = "Media Stream (Dynamic)";
                    PathTextBox.Text = Path.Combine(_defaultDownloadFolder, FileNameTextBox.Text);
                    StatusTextBlock.Text = "✓ Video link ready. (Click 'Open Video Grabber' for HD/Audio formats)";
                    DownloadButton.IsEnabled = true;
                    return;
                }

                if (CloudHosterResolver.IsCloudHoster(url))
                {
                    string hoster = CloudHosterResolver.GetHosterName(url);
                    StatusTextBlock.Text = $"Connecting to {hoster} API and fetching file stream metadata...";
                    CloudBanner.Visibility = Visibility.Visible;
                    CategoryBadgeText.Text = hoster;

                    var cloudRes = await CloudHosterResolver.ResolveAsync(url);
                    _cloudHosterResult = cloudRes;

                    if (cloudRes != null)
                    {
                        _resolvedDirectUrl = cloudRes.DirectDownloadUrl;
                        _detectedSize = cloudRes.FileSize;
                        FileNameTextBox.Text = cloudRes.FileName;
                        FileSizeTextBlock.Text = cloudRes.FileSize > 0 ? DownloadItem.FormatBytes(cloudRes.FileSize) : "Direct Cloud Stream";
                        PathTextBox.Text = Path.Combine(_defaultDownloadFolder, cloudRes.FileName);

                        if (cloudRes.IsFolderOrList && cloudRes.AdditionalFiles.Count > 1)
                        {
                            CloudBannerText.Text = $"⚡ {hoster} container detected ({cloudRes.AdditionalFiles.Count} files).";
                            FolderBatchButton.Visibility = Visibility.Visible;
                            StatusTextBlock.Text = $"✓ {hoster} album/folder resolved ({cloudRes.AdditionalFiles.Count} files). Click 'Open in Batch Grabber' to download all.";
                        }
                        else
                        {
                            CloudBannerText.Text = $"⚡ Direct {hoster} stream resolved successfully!";
                            FolderBatchButton.Visibility = Visibility.Collapsed;
                            StatusTextBlock.Text = $"✓ {hoster} direct download link ready.";
                        }

                        DownloadButton.IsEnabled = true;
                        return;
                    }
                }

                if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    string filename = "torrent_download.zip";
                    try
                    {
                        var dnIndex = url.IndexOf("dn=", StringComparison.OrdinalIgnoreCase);
                        if (dnIndex != -1)
                        {
                            var endIndex = url.IndexOf('&', dnIndex);
                            var dnRaw = endIndex != -1 ? url.Substring(dnIndex + 3, endIndex - dnIndex - 3) : url.Substring(dnIndex + 3);
                            filename = Uri.UnescapeDataString(dnRaw);
                        }
                    }
                    catch { }

                    _detectedSize = -1;
                    FileNameTextBox.Text = filename;
                    FileSizeTextBlock.Text = "Torrent (Metadata pending)";
                    CategoryBadgeText.Text = "Torrent";
                    PathTextBox.Text = Path.Combine(_defaultDownloadFolder, filename);
                    StatusTextBlock.Text = "✓ Magnet link is ready.";
                    DownloadButton.IsEnabled = true;
                    return;
                }

                if (url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) || (File.Exists(url) && url.Contains(".torrent")))
                {
                    string filename = Path.GetFileName(url);
                    if (string.IsNullOrEmpty(filename) || !filename.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                    {
                        filename = "download.torrent";
                    }
                    _detectedSize = -1;
                    FileNameTextBox.Text = filename;
                    FileSizeTextBlock.Text = "Torrent File";
                    CategoryBadgeText.Text = "Torrent";
                    PathTextBox.Text = Path.Combine(_defaultDownloadFolder, filename);
                    StatusTextBlock.Text = "✓ Torrent details ready.";
                    DownloadButton.IsEnabled = true;
                    return;
                }

                if (url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                {
                    string filename = "ftp_download_file";
                    try
                    {
                        var uri = new Uri(url);
                        filename = Path.GetFileName(uri.AbsolutePath);
                        if (string.IsNullOrEmpty(filename)) filename = "ftp_download_file";
                    }
                    catch { }

                    long size = -1;
                    try
                    {
#pragma warning disable SYSLIB0014
                        var sizeReq = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(url);
                        sizeReq.Method = System.Net.WebRequestMethods.Ftp.GetFileSize;
                        sizeReq.Credentials = new System.Net.NetworkCredential("anonymous", "anonymous");
                        using var sizeRes = (System.Net.FtpWebResponse)await sizeReq.GetResponseAsync();
                        size = sizeRes.ContentLength;
#pragma warning restore SYSLIB0014
                    }
                    catch { }

                    _detectedSize = size;
                    FileNameTextBox.Text = filename;
                    FileSizeTextBlock.Text = size > 0 ? DownloadItem.FormatBytes(size) : "Unknown / FTP";
                    CategoryBadgeText.Text = "FTP";
                    PathTextBox.Text = Path.Combine(_defaultDownloadFolder, filename);
                    StatusTextBlock.Text = "✓ FTP file details ready.";
                    DownloadButton.IsEnabled = true;
                    return;
                }

                // Default HTTP / HTTPS logic
                using var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                string responseFilename = ExtractFilenameFromUrl(url, response);
                long httpSize = -1;

                if (response.Content.Headers.ContentRange != null && response.Content.Headers.ContentRange.Length.HasValue)
                {
                    httpSize = response.Content.Headers.ContentRange.Length.Value;
                }
                else if (response.Content.Headers.ContentLength.HasValue)
                {
                    httpSize = response.Content.Headers.ContentLength.Value;
                }

                _detectedSize = httpSize;
                FileNameTextBox.Text = responseFilename;
                FileSizeTextBlock.Text = httpSize > 0 ? DownloadItem.FormatBytes(httpSize) : "Unknown";
                CategoryBadgeText.Text = DownloadItem.DetermineCategory(responseFilename);
                PathTextBox.Text = Path.Combine(_defaultDownloadFolder, responseFilename);

                StatusTextBlock.Text = "✓ File is ready to download.";
                DownloadButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error: {ex.Message}";
                DownloadButton.IsEnabled = false;
            }
        }

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a valid URL.", "Empty URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await AnalyzeLinkAsync(url);
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

        private void BrowseTorrentButton_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Torrent files (*.torrent)|*.torrent|All files (*.*)|*.*",
                Title = "Select Local Torrent File"
            };

            if (ofd.ShowDialog(this) == true)
            {
                UrlTextBox.Text = ofd.FileName;
                _ = AnalyzeLinkAsync(ofd.FileName);
            }
        }

        private string ExtractFilenameFromUrl(string url, HttpResponseMessage response)
        {
            try
            {
                var contentDisposition = response.Content.Headers.ContentDisposition;
                if (contentDisposition != null && !string.IsNullOrEmpty(contentDisposition.FileName))
                {
                    return contentDisposition.FileName.Trim('"');
                }
            }
            catch { }

            try
            {
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string filename = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(filename))
                {
                    return Uri.UnescapeDataString(filename);
                }
            }
            catch { }

            return "download_file";
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FileNameTextBox.Text)) return;

            var sfd = new SaveFileDialog
            {
                FileName = FileNameTextBox.Text,
                InitialDirectory = _defaultDownloadFolder
            };

            if (sfd.ShowDialog(this) == true)
            {
                PathTextBox.Text = sfd.FileName;
                FileNameTextBox.Text = Path.GetFileName(sfd.FileName);
            }
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            string path = PathTextBox.Text.Trim();
            string filename = FileNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(path)) return;

            string plat = VideoDownloaderEngine.DetectPlatform(url);
            if (CloudHosterResolver.IsCloudHoster(url))
            {
                plat = CloudHosterResolver.GetHosterName(url);
            }

            string protocol = "HTTP";

            if (plat == "YouTube" || plat == "Facebook" || plat == "TikTok")
            {
                protocol = "Video";
            }
            else if (plat == "Mega")
            {
                protocol = "Mega";
            }
            else if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) || (File.Exists(url) && url.Contains(".torrent")))
            {
                protocol = "Torrent";
            }
            else if (url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
            {
                protocol = "FTP";
            }

            List<string>? selectedFiles = null;
            if (protocol == "Torrent" && !url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string torrentPath = url;
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        string tempTorrentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.torrent");
                        using (var webClient = new System.Net.Http.HttpClient())
                        {
                            var data = Task.Run(() => webClient.GetByteArrayAsync(url)).GetAwaiter().GetResult();
                            File.WriteAllBytes(tempTorrentPath, data);
                        }
                        torrentPath = tempTorrentPath;
                    }

                    if (File.Exists(torrentPath))
                    {
                        var torrent = Task.Run(() => Torrent.LoadAsync(torrentPath)).GetAwaiter().GetResult();
                        var files = torrent.Files.Select(f => (f.Path, f.Length)).ToList();
                        
                        var selectionWin = new TorrentFileSelectionWindow(files) { Owner = this };
                        if (selectionWin.ShowDialog() == true)
                        {
                            selectedFiles = selectionWin.SelectedFiles;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to parse torrent file: {ex.Message}", "Torrent Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            string finalUrl = url;
            if (CloudHosterResolver.IsCloudHoster(url) && plat != "Mega" && !string.IsNullOrEmpty(_resolvedDirectUrl))
            {
                finalUrl = _resolvedDirectUrl;
            }

            ResultItem = new DownloadItem
            {
                Url = finalUrl,
                SavedPath = path,
                Filename = filename,
                TotalSizeBytes = _detectedSize,
                Category = DownloadItem.DetermineCategory(filename, plat),
                Status = "Queued",
                AddedDate = DateTime.Now,
                Protocol = protocol,
                Platform = plat,
                SelectedFiles = selectedFiles
            };

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
