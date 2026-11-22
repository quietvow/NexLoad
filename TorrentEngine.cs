using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;

namespace DOWN_MNG
{
    public class TorrentEngine
    {
        private static ClientEngine? _globalEngine;
        private static readonly object _engineLock = new object();

        private readonly DownloadItem _item;
        private TorrentManager? _manager;
        private System.Timers.Timer? _progressTimer;
        private CancellationTokenSource? _cts;

        public event Action<DownloadItem>? ProgressChanged;
        public event Action<DownloadItem, string>? DownloadError;
        public event Action<DownloadItem>? DownloadCompleted;

        public TorrentEngine(DownloadItem item)
        {
            _item = item;
            InitializeGlobalEngine();
        }

        private static void InitializeGlobalEngine()
        {
            if (_globalEngine != null) return;

            lock (_engineLock)
            {
                if (_globalEngine == null)
                {
                    var settingsBuilder = new EngineSettingsBuilder
                    {
                        AllowPortForwarding = true,
                        AutoSaveLoadDhtCache = true,
                        AutoSaveLoadFastResume = true,
                        AutoSaveLoadMagnetLinkMetadata = true
                    };
                    _globalEngine = new ClientEngine(settingsBuilder.ToSettings());
                }
            }
        }

        public void Pause()
        {
            if (_item.Status == "Downloading" && _manager != null)
            {
                _cts?.Cancel();
                _progressTimer?.Stop();
                _item.Status = "Paused";
                _item.SpeedBytesPerSec = 0;
                ProgressChanged?.Invoke(_item);
                
                Task.Run(async () =>
                {
                    try
                    {
                        await _manager.StopAsync();
                    }
                    catch { }
                });
            }
        }

        public async Task StartAsync()
        {
            if (_item.Status == "Downloading") return;

            _item.Status = "Downloading";
            _cts = new CancellationTokenSource();

            ProgressChanged?.Invoke(_item);

            try
            {
                string downloadDir = Path.GetDirectoryName(_item.SavedPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!Directory.Exists(downloadDir))
                {
                    Directory.CreateDirectory(downloadDir);
                }

                if (_manager == null)
                {
                    if (_item.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    {
                        var magnet = MagnetLink.Parse(_item.Url);
                        _manager = await _globalEngine!.AddAsync(magnet, downloadDir);
                    }
                    else
                    {
                        // Check if the URL is a local file path
                        if (File.Exists(_item.Url))
                        {
                            var torrent = await Torrent.LoadAsync(_item.Url);
                            _manager = await _globalEngine!.AddAsync(torrent, downloadDir);
                        }
                        else
                        {
                            // If it's a torrent web link, we download it first
                            string tempTorrentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.torrent");
                            using (var webClient = new System.Net.Http.HttpClient())
                            {
                                var data = await webClient.GetByteArrayAsync(_item.Url);
                                File.WriteAllBytes(tempTorrentPath, data);
                            }
                            var torrent = await Torrent.LoadAsync(tempTorrentPath);
                            _manager = await _globalEngine!.AddAsync(torrent, downloadDir);
                        }
                    }
                }

                if (_manager.HasMetadata)
                {
                    await ApplyFileSelectionAsync();
                }

                // Emulate chunk list for visual compatibility with existing connection visualizer
                if (_item.Chunks == null || _item.Chunks.Count == 0)
                {
                    _item.Chunks = new List<DownloadChunk>();
                    for (int i = 0; i < 8; i++)
                    {
                        _item.Chunks.Add(new DownloadChunk
                        {
                            Id = i,
                            StartByte = 0,
                            EndByte = -1,
                            BytesDownloaded = 0,
                            IsCompleted = false,
                            IsActive = true
                        });
                    }
                }

                await _manager.StartAsync();

                _progressTimer = new System.Timers.Timer(500);
                _progressTimer.Elapsed += (s, e) => UpdateProgress();
                _progressTimer.Start();
            }
            catch (Exception ex)
            {
                _item.Status = "Failed";
                _item.SpeedBytesPerSec = 0;
                _progressTimer?.Stop();
                DownloadError?.Invoke(_item, ex.Message);
            }
        }

        private void UpdateProgress()
        {
            if (_manager == null) return;

            lock (_item)
            {
                double progress = _manager.Progress;
                _item.BytesDownloaded = (long)(_manager.Monitor.DataBytesReceived);
                _item.SpeedBytesPerSec = _manager.Monitor.DownloadRate;

                if (_manager.HasMetadata && _manager.Torrent != null)
                {
                    _item.TotalSizeBytes = _manager.Torrent.Size;
                    
                    // Update the local SavedPath filename to match torrent target if it is not custom
                    if (string.IsNullOrEmpty(_item.Filename) || _item.Filename == "torrent_download.zip")
                    {
                        _item.Filename = _manager.Torrent.Name;
                        _item.SavedPath = Path.Combine(Path.GetDirectoryName(_item.SavedPath) ?? "", _item.Filename);
                    }

                    // Prompt file selection if it is a magnet link and we haven't prompted yet
                    if (_item.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) && _item.SelectedFiles == null)
                    {
                        // Set a dummy list immediately to prevent multiple popups
                        _item.SelectedFiles = new List<string>();

                        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                        {
                            try
                            {
                                // Pause the engine
                                Pause();

                                var files = _manager.Files.Select(f => (f.Path, f.Length)).ToList();
                                var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive) 
                                            ?? System.Windows.Application.Current.MainWindow;

                                var selectionWin = new TorrentFileSelectionWindow(files) { Owner = owner };
                                if (selectionWin.ShowDialog() == true)
                                {
                                    _item.SelectedFiles = selectionWin.SelectedFiles;
                                    await ApplyFileSelectionAsync();
                                }
                                else
                                {
                                    // Default to all files if cancelled
                                    _item.SelectedFiles = files.Select(f => f.Path).ToList();
                                    await ApplyFileSelectionAsync();
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error during magnet metadata prompt: {ex.Message}");
                            }
                            finally
                            {
                                // Resume downloading
                                _ = StartAsync();
                            }
                        });
                    }
                }

                if (_item.TotalSizeBytes > 0)
                {
                    _item.BytesDownloaded = (long)(_item.TotalSizeBytes * (progress / 100.0));
                }

                // Update emulated chunks visually
                if (_item.Chunks != null && _item.Chunks.Count > 0)
                {
                    long totalSize = _item.TotalSizeBytes > 0 ? _item.TotalSizeBytes : 100000000; // placeholder if unknown
                    long chunkSize = totalSize / _item.Chunks.Count;

                    for (int i = 0; i < _item.Chunks.Count; i++)
                    {
                        var chunk = _item.Chunks[i];
                        chunk.StartByte = i * chunkSize;
                        chunk.EndByte = (i == _item.Chunks.Count - 1) ? totalSize - 1 : chunk.StartByte + chunkSize - 1;
                        
                        double chunkProgress = Math.Min(100.0, Math.Max(0.0, progress));
                        chunk.BytesDownloaded = (long)(chunk.Length * (chunkProgress / 100.0));
                        chunk.IsCompleted = chunkProgress >= 100.0;
                        chunk.IsActive = !chunk.IsCompleted;
                    }
                }

                // Set status text beautifully
                if (_manager.State == TorrentState.Downloading)
                {
                    _item.Status = "Downloading";
                }
                else if (_manager.State == TorrentState.Metadata)
                {
                    _item.Status = "Downloading"; // metadata fetching
                }
                else if (_manager.State == TorrentState.Hashing)
                {
                    _item.Status = "Downloading"; // hashing
                }
                else if (_manager.State == TorrentState.Stopped)
                {
                    _item.Status = "Paused";
                }
                else if (_manager.State == TorrentState.Seeding)
                {
                    _item.Status = "Completed";
                    _item.SpeedBytesPerSec = 0;
                    _progressTimer?.Stop();
                    DownloadCompleted?.Invoke(_item);
                }
            }

            ProgressChanged?.Invoke(_item);
        }

        private async Task ApplyFileSelectionAsync()
        {
            if (_manager == null || !_manager.HasMetadata) return;

            if (_item.SelectedFiles != null && _item.SelectedFiles.Count > 0)
            {
                foreach (var file in _manager.Files)
                {
                    if (_item.SelectedFiles.Contains(file.Path))
                    {
                        await _manager.SetFilePriorityAsync(file, Priority.Normal);
                    }
                    else
                    {
                        await _manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
                    }
                }
            }
        }

        public async Task<bool> EnableSequentialStreamAsync(string? specificFilePath = null)
        {
            if (_manager == null || !_manager.HasMetadata) return false;

            var targetFile = _manager.Files.FirstOrDefault(f => !string.IsNullOrEmpty(specificFilePath) && f.Path.Equals(specificFilePath, StringComparison.OrdinalIgnoreCase)) ??
                             _manager.Files.FirstOrDefault(f => f.Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                                                f.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                                                f.Path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                                                f.Path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) ??
                             _manager.Files.OrderByDescending(f => f.Length).FirstOrDefault();

            if (targetFile != null)
            {
                await _manager.SetFilePriorityAsync(targetFile, Priority.Highest);
                return true;
            }

            return false;
        }
    }
}
