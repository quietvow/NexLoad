using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DOWN_MNG
{
    public class VideoFormatItem
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Extension { get; set; } = "mp4";
        public string FormatArg { get; set; } = "bv*[ext=mp4]+ba[ext=m4a]/bv*+ba/best[ext=mp4]/best";
        public bool IsAudioOnly { get; set; }
        public string FileSizeApprox { get; set; } = "Auto";

        public override string ToString() => Label;
    }

    public class VideoInfo
    {
        public string Title { get; set; } = "Video Download";
        public string ThumbnailUrl { get; set; } = "";
        public string Duration { get; set; } = "Unknown";
        public string Uploader { get; set; } = "Unknown";
        public string Platform { get; set; } = "Video"; // YouTube, Facebook, TikTok
        public string OriginalUrl { get; set; } = "";
        public string DirectStreamUrl { get; set; } = "";
        public List<VideoFormatItem> Formats { get; set; } = new List<VideoFormatItem>();
    }

    public class VideoDownloaderEngine
    {
        private readonly DownloadItem _item;
        private Process? _process;
        private CancellationTokenSource? _cts;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public event Action<DownloadItem>? ProgressChanged;
        public event Action<DownloadItem, string>? DownloadError;
        public event Action<DownloadItem>? DownloadCompleted;

        public VideoDownloaderEngine(DownloadItem item)
        {
            _item = item;
        }

        public static string GetToolsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string toolsDir = Path.Combine(appData, "NexLoad", "tools");
            string legacyDir = Path.Combine(appData, "DOWN-MNG", "tools");
            if (!Directory.Exists(toolsDir))
            {
                Directory.CreateDirectory(toolsDir);
                if (Directory.Exists(legacyDir))
                {
                    foreach (var f in Directory.GetFiles(legacyDir))
                    {
                        try { File.Copy(f, Path.Combine(toolsDir, Path.GetFileName(f)), true); } catch { }
                    }
                }
            }
            return toolsDir;
        }

        public static string GetYtDlpPath()
        {
            string toolsDir = GetToolsDirectory();
            string localExe = Path.Combine(toolsDir, "yt-dlp.exe");
            if (File.Exists(localExe)) return localExe;

            string appDirExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            if (File.Exists(appDirExe)) return appDirExe;

            return localExe;
        }

        public static string? GetFfmpegPath()
        {
            string toolsDir = GetToolsDirectory();
            string localFfmpeg = Path.Combine(toolsDir, "ffmpeg.exe");
            if (File.Exists(localFfmpeg)) return localFfmpeg;

            string appDirFfmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(appDirFfmpeg)) return appDirFfmpeg;

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var paths = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var dir in paths)
                {
                    try
                    {
                        string p = Path.Combine(dir.Trim(), "ffmpeg.exe");
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }

            return null;
        }

        public static bool IsFfmpegInstalled()
        {
            string? path = GetFfmpegPath();
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static (string RuntimeType, string ExecutablePath)? FindJsRuntime()
        {
            try
            {
                // 1. Check tools directory first
                string toolsDir = GetToolsDirectory();
                string toolsNode = Path.Combine(toolsDir, "node.exe");
                if (File.Exists(toolsNode)) return ("node", toolsNode);

                string toolsDeno = Path.Combine(toolsDir, "deno.exe");
                if (File.Exists(toolsDeno)) return ("deno", toolsDeno);

                // 2. Check standard Node.js installation paths
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                string[] candidateNodePaths = new[]
                {
                    Path.Combine(progFiles, "nodejs", "node.exe"),
                    Path.Combine(progFiles86, "nodejs", "node.exe"),
                    Path.Combine(localAppData, "Programs", "nodejs", "node.exe"),
                    Path.Combine(appData, "npm", "node.exe"),
                    Path.Combine(userProfile, ".nvm", "current", "node.exe")
                };

                foreach (var p in candidateNodePaths)
                {
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        return ("node", p);
                    }
                }

                // 3. Check standard Deno paths
                string[] candidateDenoPaths = new[]
                {
                    Path.Combine(userProfile, ".deno", "bin", "deno.exe"),
                    Path.Combine(progFiles, "deno", "deno.exe"),
                    Path.Combine(localAppData, "deno", "deno.exe")
                };

                foreach (var p in candidateDenoPaths)
                {
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        return ("deno", p);
                    }
                }

                // 4. Check PATH environment variable
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    var paths = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dir in paths)
                    {
                        try
                        {
                            string nodeInPath = Path.Combine(dir.Trim(), "node.exe");
                            if (File.Exists(nodeInPath)) return ("node", nodeInPath);

                            string denoInPath = Path.Combine(dir.Trim(), "deno.exe");
                            if (File.Exists(denoInPath)) return ("deno", denoInPath);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return null;
        }

        public static List<string> GetCommonYtDlpArgs()
        {
            var args = new List<string>();

            var jsRuntime = FindJsRuntime();
            if (jsRuntime.HasValue)
            {
                args.Add("--js-runtimes");
                args.Add($"{jsRuntime.Value.RuntimeType}:{jsRuntime.Value.ExecutablePath}");
            }
            else
            {
                // Fallback client args for YouTube when no standalone JS engine is detected
                args.Add("--extractor-args");
                args.Add("youtube:player_client=android_creator,android,web,ios");
            }

            string? ffmpegPath = GetFfmpegPath();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                string ffmpegDir = Path.GetDirectoryName(ffmpegPath) ?? GetToolsDirectory();
                args.Add("--ffmpeg-location");
                args.Add(ffmpegDir);
            }

            args.Add("--no-check-certificates");
            args.Add("--geo-bypass");

            return args;
        }

        public static bool IsYtDlpInstalled()
        {
            string path = GetYtDlpPath();
            return File.Exists(path) && new FileInfo(path).Length > 1000000;
        }

        public static async Task<bool> EnsureYtDlpAsync(IProgress<double>? progress = null, Action<string>? statusCallback = null)
        {
            if (IsYtDlpInstalled()) return true;

            string targetPath = GetYtDlpPath();
            string tempPath = targetPath + ".tmp";

            statusCallback?.Invoke("Downloading video grabber engine components (yt-dlp)...");

            try
            {
                string downloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[65536];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report((double)totalRead / totalBytes * 100.0);
                    }
                }

                fileStream.Close();

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempPath, targetPath);

                statusCallback?.Invoke("Grabber engine ready.");
                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                statusCallback?.Invoke($"Engine download failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> EnsureFfmpegAsync(IProgress<double>? progress = null, Action<string>? statusCallback = null)
        {
            if (IsFfmpegInstalled()) return true;

            string toolsDir = GetToolsDirectory();
            string tempZipPath = Path.Combine(toolsDir, "ffmpeg_download.tmp.zip");

            statusCallback?.Invoke("Downloading FFmpeg audio/video muxer engine...");

            try
            {
                string downloadUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[65536];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report((double)totalRead / totalBytes * 100.0);
                    }
                }

                fileStream.Close();

                statusCallback?.Invoke("Extracting FFmpeg components...");

                using (var archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = Path.Combine(toolsDir, "ffmpeg.exe");
                            entry.ExtractToFile(dest, true);
                        }
                        else if (entry.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = Path.Combine(toolsDir, "ffprobe.exe");
                            entry.ExtractToFile(dest, true);
                        }
                    }
                }

                try { File.Delete(tempZipPath); } catch { }

                statusCallback?.Invoke("FFmpeg muxer ready.");
                return IsFfmpegInstalled();
            }
            catch (Exception ex)
            {
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { }
                }
                statusCallback?.Invoke($"FFmpeg download failed: {ex.Message}");
                return false;
            }
        }

        public static string DetectPlatform(string url)
        {
            if (string.IsNullOrEmpty(url)) return "Direct";
            string lower = url.ToLowerInvariant();
            if (lower.Contains("youtube.com") || lower.Contains("youtu.be")) return "YouTube";
            if (lower.Contains("facebook.com") || lower.Contains("fb.watch") || lower.Contains("fb.com")) return "Facebook";
            if (lower.Contains("tiktok.com")) return "TikTok";
            if (lower.Contains("instagram.com") || lower.Contains("instagr.am")) return "Instagram";
            if (lower.Contains("twitter.com") || lower.Contains("x.com")) return "Twitter";
            if (lower.Contains("reddit.com")) return "Reddit";
            if (lower.Contains("vimeo.com")) return "Vimeo";
            return "Video";
        }

        public static async Task<VideoInfo> FetchVideoInfoAsync(string url, Action<string>? statusCallback = null, CancellationToken cancellationToken = default)
        {
            string platform = DetectPlatform(url);
            var info = new VideoInfo
            {
                Platform = platform,
                OriginalUrl = url,
                Title = $"{platform} Video"
            };

            // If TikTok, try fast web API first
            if (platform == "TikTok")
            {
                try
                {
                    statusCallback?.Invoke("Fetching TikTok video details...");
                    var tikTokInfo = await FetchTikTokDirectAsync(url);
                    if (tikTokInfo != null)
                    {
                        return tikTokInfo;
                    }
                }
                catch { }
            }

            // Ensure yt-dlp exists
            bool ready = await EnsureYtDlpAsync(null, statusCallback);
            if (!ready)
            {
                info.Title = $"{platform}_Video_{DateTime.Now:yyyyMMdd_HHmmss}";
                PopulateStandardFormats(info);
                return info;
            }

            statusCallback?.Invoke($"Extracting {platform} stream details...");

            string ytDlpPath = GetYtDlpPath();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var arg in GetCommonYtDlpArgs())
                {
                    psi.ArgumentList.Add(arg);
                }

                psi.ArgumentList.Add("--no-playlist");
                psi.ArgumentList.Add("--dump-single-json");
                psi.ArgumentList.Add("--no-warnings");
                psi.ArgumentList.Add(url);

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };

                // Asynchronously drain standard error to avoid buffer deadlocks!
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                await process.WaitForExitAsync(linkedCts.Token);

                string json = outputBuilder.ToString().Trim();
                if (!string.IsNullOrEmpty(json))
                {
                    ParseYtDlpJson(json, info);
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Fetch metadata timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fetch metadata error: {ex.Message}");
            }

            if (info.Formats == null || info.Formats.Count == 0)
            {
                PopulateStandardFormats(info);
            }

            if (string.IsNullOrWhiteSpace(info.Title) || info.Title == $"{platform} Video")
            {
                info.Title = $"{platform}_Download_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            return info;
        }

        private static void ParseYtDlpJson(string json, VideoInfo info)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("title", out var titleProp))
                {
                    string t = titleProp.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(t)) info.Title = SanitizeFilename(t);
                }

                if (root.TryGetProperty("thumbnail", out var thumbProp))
                {
                    info.ThumbnailUrl = thumbProp.GetString() ?? "";
                }
                else if (root.TryGetProperty("thumbnails", out var thumbsProp) && thumbsProp.ValueKind == JsonValueKind.Array)
                {
                    var last = thumbsProp.EnumerateArray().LastOrDefault();
                    if (last.TryGetProperty("url", out var u))
                    {
                        info.ThumbnailUrl = u.GetString() ?? "";
                    }
                }

                if (root.TryGetProperty("uploader", out var uploaderProp))
                {
                    info.Uploader = uploaderProp.GetString() ?? "";
                }
                else if (root.TryGetProperty("channel", out var chanProp))
                {
                    info.Uploader = chanProp.GetString() ?? "";
                }

                if (root.TryGetProperty("duration_string", out var durStrProp))
                {
                    string ds = durStrProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(ds)) info.Duration = ds;
                }
                else if (root.TryGetProperty("duration", out var durProp))
                {
                    if (durProp.TryGetInt64(out var durSec))
                    {
                        var ts = TimeSpan.FromSeconds(durSec);
                        info.Duration = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
                    }
                }

                // Check available heights from json
                var availableHeights = new HashSet<int>();
                if (root.TryGetProperty("formats", out var formatsProp) && formatsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in formatsProp.EnumerateArray())
                    {
                        if (f.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out int h) && h > 0)
                        {
                            availableHeights.Add(h);
                        }
                    }
                }

                PopulateFormatsFromHeights(info, availableHeights);
            }
            catch
            {
                PopulateStandardFormats(info);
            }
        }

        private static void PopulateFormatsFromHeights(VideoInfo info, HashSet<int> availableHeights)
        {
            var list = new List<VideoFormatItem>();
            bool hasFfmpeg = IsFfmpegInstalled();

            // 1. Best Quality Available
            list.Add(new VideoFormatItem
            {
                Id = "best",
                Label = "Best Quality Available (MP4)",
                Extension = "mp4",
                FormatArg = "bv*+ba/b/best",
                IsAudioOnly = false,
                FileSizeApprox = "Maximum Quality"
            });

            // 2. 4K / 2160p if present
            if (availableHeights.Contains(2160) || availableHeights.Any(h => h >= 2160))
            {
                list.Add(new VideoFormatItem
                {
                    Id = "2160p",
                    Label = "4K Ultra HD (2160p MP4)",
                    Extension = "mp4",
                    FormatArg = "bv*[height<=2160]+ba/bv*[height<=2160]/best[height<=2160]/best",
                    IsAudioOnly = false,
                    FileSizeApprox = "4K UHD"
                });
            }

            // 3. 2K / 1440p if present
            if (availableHeights.Contains(1440) || availableHeights.Any(h => h >= 1440 && h < 2160))
            {
                list.Add(new VideoFormatItem
                {
                    Id = "1440p",
                    Label = "2K Quad HD (1440p MP4)",
                    Extension = "mp4",
                    FormatArg = "bv*[height<=1440]+ba/bv*[height<=1440]/best[height<=1440]/best",
                    IsAudioOnly = false,
                    FileSizeApprox = "2K QHD"
                });
            }

            // 4. 1080p
            if (availableHeights.Count == 0 || availableHeights.Contains(1080) || availableHeights.Any(h => h >= 1080))
            {
                list.Add(new VideoFormatItem
                {
                    Id = "1080p",
                    Label = "1080p Full HD (MP4)",
                    Extension = "mp4",
                    FormatArg = "bv*[height<=1080]+ba/b[height<=1080]/best[height<=1080]/best",
                    IsAudioOnly = false,
                    FileSizeApprox = "1080p FHD"
                });
            }

            // 5. 720p
            if (availableHeights.Count == 0 || availableHeights.Contains(720) || availableHeights.Any(h => h >= 720))
            {
                list.Add(new VideoFormatItem
                {
                    Id = "720p",
                    Label = "720p HD (MP4)",
                    Extension = "mp4",
                    FormatArg = "bv*[height<=720]+ba/b[height<=720]/best[height<=720]/best",
                    IsAudioOnly = false,
                    FileSizeApprox = "720p HD"
                });
            }

            // 6. 480p
            list.Add(new VideoFormatItem
            {
                Id = "480p",
                Label = "480p Standard (MP4)",
                Extension = "mp4",
                FormatArg = "bv*[height<=480]+ba/b[height<=480]/best[height<=480]/best",
                IsAudioOnly = false,
                FileSizeApprox = "480p SD"
            });

            // 7. 360p
            list.Add(new VideoFormatItem
            {
                Id = "360p",
                Label = "360p Mobile (MP4)",
                Extension = "mp4",
                FormatArg = "bv*[height<=360]+ba/b[height<=360]/best[height<=360]/best",
                IsAudioOnly = false,
                FileSizeApprox = "360p Mobile"
            });

            // 8. MP3 Audio
            list.Add(new VideoFormatItem
            {
                Id = "mp3",
                Label = "MP3 Audio (High Quality)",
                Extension = "mp3",
                FormatArg = "ba[ext=m4a]/ba/best",
                IsAudioOnly = true,
                FileSizeApprox = "Audio MP3"
            });

            // 9. M4A Audio
            list.Add(new VideoFormatItem
            {
                Id = "m4a",
                Label = "Audio Track (Lossless M4A)",
                Extension = "m4a",
                FormatArg = "ba[ext=m4a]/ba/best",
                IsAudioOnly = true,
                FileSizeApprox = "Audio M4A"
            });

            info.Formats = list;
        }

        private static void PopulateStandardFormats(VideoInfo info)
        {
            PopulateFormatsFromHeights(info, new HashSet<int>());
        }

        private static async Task<VideoInfo?> FetchTikTokDirectAsync(string url)
        {
            try
            {
                string apiUrl = $"https://www.tikwm.com/api/?url={Uri.EscapeDataString(url)}";
                using var response = await _httpClient.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var data))
                    {
                        var info = new VideoInfo
                        {
                            Platform = "TikTok",
                            OriginalUrl = url
                        };

                        if (data.TryGetProperty("title", out var titleProp))
                        {
                            string t = titleProp.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(t)) info.Title = SanitizeFilename(t);
                        }

                        if (data.TryGetProperty("cover", out var coverProp))
                        {
                            info.ThumbnailUrl = coverProp.GetString() ?? "";
                        }

                        if (data.TryGetProperty("author", out var authorProp) && authorProp.TryGetProperty("nickname", out var nick))
                        {
                            info.Uploader = nick.GetString() ?? "TikTok Creator";
                        }

                        if (data.TryGetProperty("duration", out var durProp) && durProp.TryGetInt32(out int durSec))
                        {
                            var ts = TimeSpan.FromSeconds(durSec);
                            info.Duration = ts.ToString(@"mm\:ss");
                        }

                        if (data.TryGetProperty("play", out var playProp))
                        {
                            info.DirectStreamUrl = playProp.GetString() ?? "";
                        }

                        PopulateStandardFormats(info);
                        return info;
                    }
                }
            }
            catch { }
            return null;
        }

        public static string SanitizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return "download";
            string invalid = new string(Path.GetInvalidFileNameChars()) + ":*?\"<>|/\\#%&{}~";
            foreach (char c in invalid)
            {
                filename = filename.Replace(c, '_');
            }
            if (filename.Length > 120) filename = filename.Substring(0, 120);
            return filename.Trim();
        }

        public void Pause()
        {
            if (_item.Status == "Downloading")
            {
                _cts?.Cancel();
                try
                {
                    if (_process != null && !_process.HasExited)
                    {
                        _process.Kill(true);
                    }
                }
                catch { }

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
            ProgressChanged?.Invoke(_item);

            bool isReady = await EnsureYtDlpAsync(null, (status) => { });
            if (!isReady)
            {
                _item.Status = "Failed";
                DownloadError?.Invoke(_item, "Video grabber engine (yt-dlp) could not be initialized.");
                return;
            }

            // Ensure FFmpeg is present for merging and encoding
            await EnsureFfmpegAsync(null, null);

            string ytDlpPath = GetYtDlpPath();
            string outputDir = Path.GetDirectoryName(_item.SavedPath) ?? "";
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Remove any pre-existing 0-byte placeholder to prevent write/merge collisions
            try
            {
                if (File.Exists(_item.SavedPath) && new FileInfo(_item.SavedPath).Length == 0)
                {
                    File.Delete(_item.SavedPath);
                }
            }
            catch { }

            // Determine format argument
            string formatArg = !string.IsNullOrWhiteSpace(_item.FormatCode) ? _item.FormatCode : "bv*+ba/b/best";
            bool isMp3 = _item.FormatCode?.Contains("mp3", StringComparison.OrdinalIgnoreCase) == true ||
                         _item.VideoQuality?.Contains("mp3", StringComparison.OrdinalIgnoreCase) == true ||
                         _item.SavedPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in GetCommonYtDlpArgs())
            {
                psi.ArgumentList.Add(arg);
            }

            psi.ArgumentList.Add("--force-overwrites");
            psi.ArgumentList.Add("--no-playlist");
            psi.ArgumentList.Add("--concurrent-fragments");
            psi.ArgumentList.Add("4");
            psi.ArgumentList.Add("--extractor-retries");
            psi.ArgumentList.Add("3");
            psi.ArgumentList.Add("--fragment-retries");
            psi.ArgumentList.Add("5");

            if (isMp3)
            {
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("ba/best");
                psi.ArgumentList.Add("-x");
                psi.ArgumentList.Add("--audio-format");
                psi.ArgumentList.Add("mp3");
            }
            else
            {
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(formatArg);
                psi.ArgumentList.Add("--merge-output-format");
                psi.ArgumentList.Add("mp4");
            }

            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(_item.SavedPath);
            psi.ArgumentList.Add("--newline");
            psi.ArgumentList.Add("--progress-template");
            psi.ArgumentList.Add("%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|%(progress.downloaded_bytes)s|%(progress.total_bytes|progress.total_bytes_estimate)s");
            psi.ArgumentList.Add(_item.Url);

            var lastErrorLines = new List<string>();

            try
            {
                _process = new Process { StartInfo = psi };

                _process.OutputDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    ParseProgressLine(e.Data);
                };

                _process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (lastErrorLines)
                        {
                            if (lastErrorLines.Count > 10) lastErrorLines.RemoveAt(0);
                            lastErrorLines.Add(e.Data);
                        }
                        System.Diagnostics.Debug.WriteLine($"yt-dlp stderr: {e.Data}");
                    }
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                await _process.WaitForExitAsync(_cts.Token);

                if (_process.ExitCode == 0)
                {
                    // Check if file was saved or renamed by yt-dlp (e.g. .mp4 / .webm / .mkv)
                    VerifyDownloadedOutputFile();

                    if (!File.Exists(_item.SavedPath) || new FileInfo(_item.SavedPath).Length == 0)
                    {
                        _item.Status = "Failed";
                        _item.SpeedBytesPerSec = 0;
                        try { if (File.Exists(_item.SavedPath) && new FileInfo(_item.SavedPath).Length == 0) File.Delete(_item.SavedPath); } catch { }
                        
                        string errorDetail = string.Join(" ", lastErrorLines.Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)));
                        if (string.IsNullOrWhiteSpace(errorDetail))
                        {
                            errorDetail = "Downloaded file is empty (0 bytes). The media stream may be unavailable or expired.";
                        }
                        DownloadError?.Invoke(_item, errorDetail);
                        return;
                    }

                    _item.Status = "Completed";
                    _item.SpeedBytesPerSec = 0;
                    _item.BytesDownloaded = _item.TotalSizeBytes > 0 ? _item.TotalSizeBytes : _item.BytesDownloaded;
                    
                    if (_item.Chunks != null && _item.Chunks.Count > 0)
                    {
                        _item.Chunks[0].IsCompleted = true;
                        _item.Chunks[0].IsActive = false;
                        _item.Chunks[0].BytesDownloaded = _item.BytesDownloaded;
                    }

                    ProgressChanged?.Invoke(_item);
                    DownloadCompleted?.Invoke(_item);
                }
                else if (_cts.IsCancellationRequested)
                {
                    _item.Status = "Paused";
                    _item.SpeedBytesPerSec = 0;
                    ProgressChanged?.Invoke(_item);
                }
                else
                {
                    _item.Status = "Failed";
                    _item.SpeedBytesPerSec = 0;
                    try { if (File.Exists(_item.SavedPath) && new FileInfo(_item.SavedPath).Length == 0) File.Delete(_item.SavedPath); } catch { }

                    string errorDetail = string.Join(" ", lastErrorLines.Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)));
                    if (string.IsNullOrWhiteSpace(errorDetail))
                    {
                        errorDetail = $"Process exited with code {_process.ExitCode}";
                    }
                    DownloadError?.Invoke(_item, errorDetail);
                }
            }
            catch (OperationCanceledException)
            {
                _item.Status = "Paused";
                _item.SpeedBytesPerSec = 0;
                ProgressChanged?.Invoke(_item);
            }
            catch (Exception ex)
            {
                _item.Status = "Failed";
                _item.SpeedBytesPerSec = 0;
                DownloadError?.Invoke(_item, ex.Message);
            }
        }

        private void VerifyDownloadedOutputFile()
        {
            try
            {
                if (File.Exists(_item.SavedPath))
                {
                    _item.TotalSizeBytes = new FileInfo(_item.SavedPath).Length;
                    _item.BytesDownloaded = _item.TotalSizeBytes;
                    return;
                }

                // Check possible extension variants (.mp4, .mkv, .webm, .mp3, .m4a)
                string dir = Path.GetDirectoryName(_item.SavedPath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(_item.SavedPath);
                
                if (Directory.Exists(dir))
                {
                    var matching = Directory.GetFiles(dir, $"{baseName}.*");
                    if (matching.Length > 0)
                    {
                        string foundPath = matching[0];
                        _item.SavedPath = foundPath;
                        _item.Filename = Path.GetFileName(foundPath);
                        _item.TotalSizeBytes = new FileInfo(foundPath).Length;
                        _item.BytesDownloaded = _item.TotalSizeBytes;
                    }
                }
            }
            catch { }
        }

        private void ParseProgressLine(string line)
        {
            try
            {
                if (line.Contains("|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 5)
                    {
                        string percentStr = parts[0].Replace("%", "").Trim();
                        string speedStr = parts[1].Trim();
                        string downloadedStr = parts[3].Trim();
                        string totalStr = parts[4].Trim();

                        if (double.TryParse(percentStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                        {
                            if (long.TryParse(totalStr, out long totalBytes) && totalBytes > 0)
                            {
                                _item.TotalSizeBytes = totalBytes;
                            }

                            if (long.TryParse(downloadedStr, out long downloadedBytes) && downloadedBytes > 0)
                            {
                                _item.BytesDownloaded = downloadedBytes;
                                if (_item.TotalSizeBytes <= 0 && percent > 0)
                                {
                                    _item.TotalSizeBytes = (long)(downloadedBytes / (percent / 100.0));
                                }
                            }
                            else if (_item.TotalSizeBytes > 0)
                            {
                                _item.BytesDownloaded = (long)(_item.TotalSizeBytes * (percent / 100.0));
                            }
                        }

                        _item.SpeedBytesPerSec = ParseSpeed(speedStr);

                        // Ensure chunk visualizer has a live segment
                        if (_item.Chunks == null || _item.Chunks.Count == 0)
                        {
                            _item.Chunks = new List<DownloadChunk>
                            {
                                new DownloadChunk
                                {
                                    Id = 0,
                                    StartByte = 0,
                                    EndByte = _item.TotalSizeBytes > 0 ? _item.TotalSizeBytes : 0,
                                    BytesDownloaded = _item.BytesDownloaded,
                                    IsActive = true
                                }
                            };
                        }
                        else
                        {
                            _item.Chunks[0].BytesDownloaded = _item.BytesDownloaded;
                            _item.Chunks[0].EndByte = _item.TotalSizeBytes > 0 ? _item.TotalSizeBytes : _item.BytesDownloaded;
                            _item.Chunks[0].IsActive = true;
                        }

                        ProgressChanged?.Invoke(_item);
                    }
                }
                else if (line.Contains("[download]") && line.Contains("%"))
                {
                    var match = Regex.Match(line, @"([0-9\.]+)%\s+of\s+~?([0-9\.]+)([KMGT]?i?B)\s+at\s+([0-9\.]+)([KMGT]?i?B/s)");
                    if (match.Success)
                    {
                        double percent = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        double sizeVal = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        string sizeUnit = match.Groups[3].Value;
                        double speedVal = double.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                        string speedUnit = match.Groups[5].Value;

                        long total = ConvertToBytes(sizeVal, sizeUnit);
                        _item.TotalSizeBytes = total;
                        _item.BytesDownloaded = (long)(total * (percent / 100.0));
                        _item.SpeedBytesPerSec = ConvertToBytes(speedVal, speedUnit.Replace("/s", ""));

                        if (_item.Chunks == null || _item.Chunks.Count == 0)
                        {
                            _item.Chunks = new List<DownloadChunk>
                            {
                                new DownloadChunk
                                {
                                    Id = 0,
                                    StartByte = 0,
                                    EndByte = total,
                                    BytesDownloaded = _item.BytesDownloaded,
                                    IsActive = true
                                }
                            };
                        }
                        else
                        {
                            _item.Chunks[0].BytesDownloaded = _item.BytesDownloaded;
                            _item.Chunks[0].EndByte = total;
                            _item.Chunks[0].IsActive = true;
                        }

                        ProgressChanged?.Invoke(_item);
                    }
                }
            }
            catch { }
        }

        private static double ParseSpeed(string speedStr)
        {
            if (string.IsNullOrWhiteSpace(speedStr) || speedStr == "N/A") return 0;
            var match = Regex.Match(speedStr, @"([0-9\.]+)\s*([KMGT]?i?B)/s", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                double val = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                string unit = match.Groups[2].Value;
                return ConvertToBytes(val, unit);
            }
            return 0;
        }

        private static long ConvertToBytes(double value, string unit)
        {
            string u = unit.ToUpperInvariant();
            if (u.StartsWith("K")) return (long)(value * 1024);
            if (u.StartsWith("M")) return (long)(value * 1024 * 1024);
            if (u.StartsWith("G")) return (long)(value * 1024 * 1024 * 1024);
            if (u.StartsWith("T")) return (long)(value * 1024 * 1024 * 1024 * 1024);
            return (long)value;
        }
    }
}
