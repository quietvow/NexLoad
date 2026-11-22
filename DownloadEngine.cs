using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DOWN_MNG
{
    public class DownloadEngine
    {
        private readonly DownloadItem _item;
        private CancellationTokenSource? _cts;
        private TorrentEngine? _torrentEngine;
        public TorrentEngine? TorrentEngineInstance => _torrentEngine;
        private VideoDownloaderEngine? _videoEngine;
        private MegaDownloadEngine? _megaEngine;
        private readonly int _connectionLimit = 8;
        private readonly System.Timers.Timer _speedTimer;
        private readonly System.Timers.Timer _progressTimer;
        private long _lastBytes = 0;
        private readonly HttpClient _httpClient;

        public event Action<DownloadItem>? ProgressChanged;
        public event Action<DownloadItem, string>? DownloadError;
        public event Action<DownloadItem>? DownloadCompleted;

        public DownloadEngine(DownloadItem item, int connections = 8)
        {
            _item = item;
            _connectionLimit = connections > 0 ? connections : App.Settings.DefaultConnections;
            
            var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
            _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            _speedTimer = new System.Timers.Timer(1000);
            _speedTimer.Elapsed += CalculateSpeed;

            _progressTimer = new System.Timers.Timer(200); // UI updates 5 times per second - smooth & extremely fast
            _progressTimer.Elapsed += (s, e) => ProgressChanged?.Invoke(_item);
        }

        public void Pause()
        {
            if (_item.Protocol == "Torrent")
            {
                _torrentEngine?.Pause();
                return;
            }

            if (_item.Protocol == "Video" || _item.Platform == "YouTube" || _item.Platform == "Facebook" || _item.Platform == "TikTok")
            {
                _videoEngine?.Pause();
                return;
            }

            if (_item.Protocol == "Mega" || _item.Platform == "Mega")
            {
                _megaEngine?.Pause();
                return;
            }

            if (_item.Status == "Downloading")
            {
                _cts?.Cancel();
                _speedTimer.Stop();
                _progressTimer.Stop();
                _item.Status = "Paused";
                _item.SpeedBytesPerSec = 0;
                ProgressChanged?.Invoke(_item);
            }
        }

        public async Task StartAsync()
        {
            if (_item.Status == "Downloading") return;

            if (_item.Protocol == "Torrent")
            {
                if (_torrentEngine == null)
                {
                    _torrentEngine = new TorrentEngine(_item);
                    _torrentEngine.ProgressChanged += (item) => ProgressChanged?.Invoke(item);
                    _torrentEngine.DownloadCompleted += (item) => DownloadCompleted?.Invoke(item);
                    _torrentEngine.DownloadError += (item, msg) => DownloadError?.Invoke(item, msg);
                }
                await _torrentEngine.StartAsync();
                return;
            }

            if (_item.Protocol == "Video" || _item.Platform == "YouTube" || _item.Platform == "Facebook" || _item.Platform == "TikTok")
            {
                if (_videoEngine == null)
                {
                    _videoEngine = new VideoDownloaderEngine(_item);
                    _videoEngine.ProgressChanged += (item) => ProgressChanged?.Invoke(item);
                    _videoEngine.DownloadCompleted += (item) => DownloadCompleted?.Invoke(item);
                    _videoEngine.DownloadError += (item, msg) => DownloadError?.Invoke(item, msg);
                }
                await _videoEngine.StartAsync();
                return;
            }

            if (_item.Protocol == "Mega" || _item.Platform == "Mega")
            {
                if (_megaEngine == null)
                {
                    _megaEngine = new MegaDownloadEngine(_item);
                    _megaEngine.ProgressChanged += (item) => ProgressChanged?.Invoke(item);
                    _megaEngine.DownloadCompleted += (item) => DownloadCompleted?.Invoke(item);
                    _megaEngine.DownloadError += (item, msg) => DownloadError?.Invoke(item, msg);
                }
                await _megaEngine.StartAsync();
                return;
            }

            // Auto-resolve cloud hoster links to direct download streams (e.g. Pixeldrain / MediaFire / GDrive)
            if (CloudHosterResolver.IsCloudHoster(_item.Url) && _item.Platform != "Mega")
            {
                try
                {
                    var cloudRes = await CloudHosterResolver.ResolveAsync(_item.Url);
                    if (cloudRes != null && !string.IsNullOrEmpty(cloudRes.DirectDownloadUrl))
                    {
                        _item.Url = cloudRes.DirectDownloadUrl;
                        if (_item.TotalSizeBytes <= 0 && cloudRes.FileSize > 0)
                        {
                            _item.TotalSizeBytes = cloudRes.FileSize;
                        }
                    }
                }
                catch { }
            }

            _item.Status = "Downloading";
            _cts = new CancellationTokenSource();
            _lastBytes = _item.BytesDownloaded;
            _speedTimer.Start();
            _progressTimer.Start();

            ProgressChanged?.Invoke(_item);

            try
            {
                if (_item.Protocol == "FTP")
                {
                    await DownloadFtpAsync(_cts.Token);
                    return;
                }

                // Step 1: Query server for file length and range support
                var (totalLength, supportsRanges) = await QueryFileMetadataAsync(_item.Url);
                
                if (totalLength > 0)
                {
                    _item.TotalSizeBytes = totalLength;
                }

                // Ensure the directory exists
                string? dir = Path.GetDirectoryName(_item.SavedPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Step 2: Disk space verification
                if (_item.TotalSizeBytes > 0 && !string.IsNullOrEmpty(dir))
                {
                    VerifyDiskSpace(dir, _item.TotalSizeBytes);
                }

                // Step 3: Single-stream direct download fallback if server doesn't support ranges
                if (!supportsRanges || _item.TotalSizeBytes <= 0)
                {
                    await DownloadSingleStreamAsync(_cts.Token);
                    return;
                }

                // Step 4: Pre-allocate file on disk if ranges supported and file size known
                PreallocateFile(_item.SavedPath, _item.TotalSizeBytes);

                // Step 5: Initialize or load chunks
                if (_item.Chunks == null || _item.Chunks.Count == 0)
                {
                    _item.Chunks = InitializeChunks(_item.TotalSizeBytes, _connectionLimit);
                }

                // Step 6: Start chunk tasks with automatic retries
                var chunkTasks = new List<Task>();
                for (int i = 0; i < _item.Chunks.Count; i++)
                {
                    var chunk = _item.Chunks[i];
                    if (!chunk.IsCompleted)
                    {
                        chunkTasks.Add(DownloadChunkWithRetryAsync(chunk, _cts.Token));
                    }
                }

                await Task.WhenAll(chunkTasks);

                // Step 7: Check if fully completed
                if (_item.Chunks.All(c => c.IsCompleted))
                {
                    _item.Status = "Completed";
                    _item.BytesDownloaded = _item.TotalSizeBytes > 0 ? _item.TotalSizeBytes : _item.BytesDownloaded;
                    _item.SpeedBytesPerSec = 0;
                    _speedTimer.Stop();
                    _progressTimer.Stop();
                    ProgressChanged?.Invoke(_item);
                    DownloadCompleted?.Invoke(_item);
                }
            }
            catch (OperationCanceledException)
            {
                _item.Status = "Paused";
                _item.SpeedBytesPerSec = 0;
                _speedTimer.Stop();
                _progressTimer.Stop();
                ProgressChanged?.Invoke(_item);
            }
            catch (Exception ex)
            {
                _item.Status = "Failed";
                _item.SpeedBytesPerSec = 0;
                _speedTimer.Stop();
                _progressTimer.Stop();
                DownloadError?.Invoke(_item, ex.Message);
            }
        }

        private void VerifyDiskSpace(string directoryPath, long requiredBytes)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(directoryPath)) ?? "";
                if (!string.IsNullOrEmpty(root))
                {
                    var driveInfo = new DriveInfo(root);
                    if (driveInfo.IsReady && driveInfo.AvailableFreeSpace < requiredBytes + 10485760) // 10 MB safety buffer
                    {
                        throw new IOException($"Insufficient disk space on drive {driveInfo.Name}. Free: {DownloadItem.FormatBytes(driveInfo.AvailableFreeSpace)}, Required: {DownloadItem.FormatBytes(requiredBytes)}");
                    }
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch { }
        }

        private async Task<(long Length, bool SupportsRanges)> QueryFileMetadataAsync(string url)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.IsSuccessStatusCode)
                {
                    long length = response.Content.Headers.ContentLength ?? -1;
                    bool acceptRanges = response.Headers.AcceptRanges.Contains("bytes") || 
                                       (response.Headers.Contains("Accept-Ranges") && response.Headers.GetValues("Accept-Ranges").Contains("bytes"));
                    return (length, acceptRanges);
                }
            }
            catch { }

            // If HEAD fails, try a short range request to check range capability and file size
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(0, 0);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    long totalLength = -1;
                    if (response.Content.Headers.ContentRange != null && response.Content.Headers.ContentRange.Length.HasValue)
                    {
                        totalLength = response.Content.Headers.ContentRange.Length.Value;
                    }
                    bool supportsRanges = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    return (totalLength, supportsRanges);
                }
            }
            catch { }

            // Fallback: standard request header inspection
            try
            {
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                return (response.Content.Headers.ContentLength ?? -1, false);
            }
            catch (Exception ex)
            {
                throw new Exception($"Cannot connect to server: {ex.Message}");
            }
        }

        private void PreallocateFile(string path, long size)
        {
            if (File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                if (fs.Length == size) return;
                fs.SetLength(size);
            }
            else
            {
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                fs.SetLength(size);
            }
        }

        private List<DownloadChunk> InitializeChunks(long totalSize, int count)
        {
            var chunks = new List<DownloadChunk>();
            if (totalSize <= 0 || count <= 1)
            {
                chunks.Add(new DownloadChunk { Id = 0, StartByte = 0, EndByte = -1, BytesDownloaded = 0 });
                return chunks;
            }

            long chunkSize = totalSize / count;
            for (int i = 0; i < count; i++)
            {
                long start = i * chunkSize;
                long end = (i == count - 1) ? totalSize - 1 : start + chunkSize - 1;
                chunks.Add(new DownloadChunk
                {
                    Id = i,
                    StartByte = start,
                    EndByte = end,
                    BytesDownloaded = 0,
                    IsCompleted = false,
                    IsActive = false
                });
            }
            return chunks;
        }

        private async Task DownloadChunkWithRetryAsync(DownloadChunk chunk, CancellationToken token)
        {
            int maxRetries = 3;
            int attempt = 0;

            while (attempt < maxRetries && !chunk.IsCompleted)
            {
                attempt++;
                try
                {
                    await DownloadChunkAsync(chunk, token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (attempt >= maxRetries || token.IsCancellationRequested)
                    {
                        throw;
                    }
                    await Task.Delay(1000 * attempt, token); // Exponential backoff retry
                }
            }
        }

        private async Task DownloadChunkAsync(DownloadChunk chunk, CancellationToken token)
        {
            chunk.IsActive = true;
            int bufferSize = 8192;
            byte[] buffer = new byte[bufferSize];

            try
            {
                long currentStart = chunk.StartByte + chunk.BytesDownloaded;
                
                if (chunk.EndByte != -1 && currentStart > chunk.EndByte)
                {
                    chunk.IsCompleted = true;
                    chunk.IsActive = false;
                    return;
                }

                var request = new HttpRequestMessage(HttpMethod.Get, _item.Url);
                
                if (chunk.EndByte != -1)
                {
                    request.Headers.Range = new RangeHeaderValue(currentStart, chunk.EndByte);
                }
                else if (currentStart > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(currentStart, null);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                using var networkStream = await response.Content.ReadAsStreamAsync(token);
                using var fileStream = new FileStream(_item.SavedPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);

                int bytesRead;
                while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    token.ThrowIfCancellationRequested();

                    lock (fileStream)
                    {
                        fileStream.Seek(chunk.StartByte + chunk.BytesDownloaded, SeekOrigin.Begin);
                        fileStream.Write(buffer, 0, bytesRead);
                    }

                    chunk.BytesDownloaded += bytesRead;

                    lock (_item)
                    {
                        _item.BytesDownloaded += bytesRead;
                    }

                    // Speed Limiter Throttling
                    if (App.Settings.SpeedLimitEnabled && App.Settings.SpeedLimitKBps > 0)
                    {
                        ApplySpeedThrottle(bytesRead);
                    }
                }

                chunk.IsCompleted = true;
            }
            finally
            {
                chunk.IsActive = false;
            }
        }

        private void ApplySpeedThrottle(int bytesRead)
        {
            try
            {
                long maxBytesPerSecTotal = App.Settings.SpeedLimitKBps * 1024;
                long maxBytesPerSecChunk = Math.Max(2048, maxBytesPerSecTotal / Math.Max(1, _connectionLimit));

                double expectedSecs = (double)bytesRead / maxBytesPerSecChunk;
                int delayMs = (int)(expectedSecs * 1000);
                if (delayMs > 0 && delayMs < 500)
                {
                    Thread.Sleep(delayMs);
                }
            }
            catch { }
        }

        private async Task DownloadSingleStreamAsync(CancellationToken token)
        {
            if (_item.Chunks == null || _item.Chunks.Count == 0)
            {
                _item.Chunks = new List<DownloadChunk>
                {
                    new DownloadChunk { Id = 0, StartByte = 0, EndByte = _item.TotalSizeBytes > 0 ? _item.TotalSizeBytes - 1 : -1, BytesDownloaded = _item.BytesDownloaded, IsActive = true }
                };
            }

            var chunk = _item.Chunks[0];
            chunk.IsActive = true;

            var request = new HttpRequestMessage(HttpMethod.Get, _item.Url);
            if (chunk.BytesDownloaded > 0)
            {
                request.Headers.Range = new RangeHeaderValue(chunk.BytesDownloaded, null);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            if (_item.TotalSizeBytes <= 0 && response.Content.Headers.ContentLength.HasValue)
            {
                _item.TotalSizeBytes = response.Content.Headers.ContentLength.Value;
                chunk.EndByte = _item.TotalSizeBytes - 1;
            }

            using var networkStream = await response.Content.ReadAsStreamAsync(token);
            using var fileStream = new FileStream(_item.SavedPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);

            if (chunk.BytesDownloaded > 0)
            {
                fileStream.Seek(chunk.BytesDownloaded, SeekOrigin.Begin);
            }

            byte[] buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                token.ThrowIfCancellationRequested();

                fileStream.Write(buffer, 0, bytesRead);
                chunk.BytesDownloaded += bytesRead;

                lock (_item)
                {
                    _item.BytesDownloaded = chunk.BytesDownloaded;
                }

                if (App.Settings.SpeedLimitEnabled && App.Settings.SpeedLimitKBps > 0)
                {
                    ApplySpeedThrottle(bytesRead);
                }
            }

            chunk.IsCompleted = true;
            chunk.IsActive = false;
            _item.Status = "Completed";
            _item.SpeedBytesPerSec = 0;
            _speedTimer.Stop();
            _progressTimer.Stop();
            ProgressChanged?.Invoke(_item);
            DownloadCompleted?.Invoke(_item);
        }

        private async Task DownloadFtpAsync(CancellationToken token)
        {
            string? dir = Path.GetDirectoryName(_item.SavedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (_item.Chunks == null || _item.Chunks.Count == 0)
            {
                _item.Chunks = new List<DownloadChunk>
                {
                    new DownloadChunk { Id = 0, StartByte = 0, EndByte = -1, BytesDownloaded = 0, IsCompleted = false, IsActive = true }
                };
            }

            var chunk = _item.Chunks[0];
            long startOffset = chunk.BytesDownloaded;

#pragma warning disable SYSLIB0014
            var request = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(_item.Url);
            request.Method = System.Net.WebRequestMethods.Ftp.DownloadFile;
            request.Credentials = new System.Net.NetworkCredential("anonymous", "anonymous");

            if (startOffset > 0)
            {
                request.ContentOffset = startOffset;
            }

            using var response = (System.Net.FtpWebResponse)await request.GetResponseAsync();
            using var ftpStream = response.GetResponseStream();
#pragma warning restore SYSLIB0014

            if (_item.TotalSizeBytes <= 0)
            {
                try
                {
#pragma warning disable SYSLIB0014
                    var sizeReq = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(_item.Url);
                    sizeReq.Method = System.Net.WebRequestMethods.Ftp.GetFileSize;
                    sizeReq.Credentials = new System.Net.NetworkCredential("anonymous", "anonymous");
                    using var sizeRes = (System.Net.FtpWebResponse)await sizeReq.GetResponseAsync();
                    _item.TotalSizeBytes = sizeRes.ContentLength;
#pragma warning restore SYSLIB0014
                }
                catch { }
            }

            using var fileStream = new FileStream(_item.SavedPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            if (startOffset > 0)
            {
                fileStream.Seek(startOffset, SeekOrigin.Begin);
            }
            else if (_item.TotalSizeBytes > 0)
            {
                fileStream.SetLength(_item.TotalSizeBytes);
            }

            int bufferSize = 8192;
            byte[] buffer = new byte[bufferSize];
            int bytesRead;

            while ((bytesRead = await ftpStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                token.ThrowIfCancellationRequested();

                fileStream.Write(buffer, 0, bytesRead);
                chunk.BytesDownloaded += bytesRead;
                lock (_item)
                {
                    _item.BytesDownloaded = chunk.BytesDownloaded;
                }
            }

            chunk.IsCompleted = true;
            _item.Status = "Completed";
            _item.SpeedBytesPerSec = 0;
            _speedTimer.Stop();
            _progressTimer.Stop();
            DownloadCompleted?.Invoke(_item);
        }

        private void CalculateSpeed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_item)
            {
                long currentBytes = _item.BytesDownloaded;
                long downloadedThisSecond = currentBytes - _lastBytes;
                _lastBytes = currentBytes;

                _item.SpeedBytesPerSec = downloadedThisSecond >= 0 ? downloadedThisSecond : 0;
            }
            ProgressChanged?.Invoke(_item);
        }
    }
}
