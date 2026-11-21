using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CG.Web.MegaApiClient;

namespace DOWN_MNG
{
    public class MegaDownloadEngine
    {
        private readonly DownloadItem _item;
        private CancellationTokenSource? _cts;
        private static readonly MegaApiClient _client = new MegaApiClient();
        private static bool _isLoggedIn = false;
        private static readonly SemaphoreSlim _loginLock = new SemaphoreSlim(1, 1);
        private readonly System.Timers.Timer _speedTimer;
        private long _lastBytes = 0;

        public event Action<DownloadItem>? ProgressChanged;
        public event Action<DownloadItem, string>? DownloadError;
        public event Action<DownloadItem>? DownloadCompleted;

        public MegaDownloadEngine(DownloadItem item)
        {
            _item = item;
            _speedTimer = new System.Timers.Timer(1000);
            _speedTimer.Elapsed += CalculateSpeed;
        }

        public static async Task EnsureLoggedInAsync()
        {
            if (_isLoggedIn) return;
            await _loginLock.WaitAsync();
            try
            {
                if (!_isLoggedIn)
                {
                    await _client.LoginAnonymousAsync();
                    _isLoggedIn = true;
                }
            }
            finally
            {
                _loginLock.Release();
            }
        }

        public static MegaApiClient GetClient() => _client;

        public void Pause()
        {
            if (_item.Status == "Downloading")
            {
                _cts?.Cancel();
                _speedTimer.Stop();
                _item.Status = "Paused";
                _item.SpeedBytesPerSec = 0;
                ProgressChanged?.Invoke(_item);
            }
        }

        public async Task StartAsync()
        {
            if (_item.Status == "Downloading") return;

            _item.Status = "Downloading";
            _cts = new CancellationTokenSource();
            _speedTimer.Start();
            ProgressChanged?.Invoke(_item);

            try
            {
                await EnsureLoggedInAsync();

                var uri = new Uri(_item.Url);
                var node = await _client.GetNodeFromLinkAsync(uri);
                if (node == null)
                {
                    throw new Exception("Could not resolve or access MEGA file node. The link or decryption key may be invalid or expired.");
                }

                _item.TotalSizeBytes = node.Size;
                if (string.IsNullOrEmpty(_item.Filename) || _item.Filename == "download_file")
                {
                    _item.Filename = node.Name;
                    _item.SavedPath = Path.Combine(Path.GetDirectoryName(_item.SavedPath) ?? "", node.Name);
                }

                string dir = Path.GetDirectoryName(_item.SavedPath) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Check if already completely downloaded
                if (File.Exists(_item.SavedPath))
                {
                    var fi = new FileInfo(_item.SavedPath);
                    if (fi.Length == node.Size && node.Size > 0)
                    {
                        _item.BytesDownloaded = node.Size;
                        _item.Status = "Completed";
                        _item.SpeedBytesPerSec = 0;
                        _speedTimer.Stop();
                        ProgressChanged?.Invoke(_item);
                        DownloadCompleted?.Invoke(_item);
                        return;
                    }
                }

                // Streaming decrypted content from MEGA
                using var megaStream = await _client.DownloadAsync(node, new Progress<double>(), _cts.Token);
                
                // Read from stream in 128KB chunks
                byte[] buffer = new byte[128 * 1024];
                using (var fileStream = new FileStream(_item.SavedPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int bytesRead;
                    while ((bytesRead = await megaStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, _cts.Token);
                        _item.BytesDownloaded += bytesRead;
                    }
                }

                _speedTimer.Stop();
                _item.SpeedBytesPerSec = 0;
                _item.BytesDownloaded = _item.TotalSizeBytes;
                _item.Status = "Completed";
                ProgressChanged?.Invoke(_item);
                DownloadCompleted?.Invoke(_item);
            }
            catch (OperationCanceledException)
            {
                _speedTimer.Stop();
                _item.Status = "Paused";
                _item.SpeedBytesPerSec = 0;
                ProgressChanged?.Invoke(_item);
            }
            catch (Exception ex)
            {
                _speedTimer.Stop();
                _item.Status = "Failed";
                _item.SpeedBytesPerSec = 0;
                ProgressChanged?.Invoke(_item);
                DownloadError?.Invoke(_item, $"MEGA download error: {ex.Message}");
            }
        }

        private void CalculateSpeed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (_item.Status != "Downloading") return;

            long currentBytes = _item.BytesDownloaded;
            long diff = currentBytes - _lastBytes;
            _lastBytes = currentBytes;

            _item.SpeedBytesPerSec = diff > 0 ? diff : 0;
            ProgressChanged?.Invoke(_item);
        }
    }
}
