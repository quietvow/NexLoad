using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace DOWN_MNG
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _url = "";
        private string _filename = "";
        private string _savedPath = "";
        private long _totalSizeBytes = -1;
        private long _bytesDownloaded = 0;
        private double _speedBytesPerSec = 0;
        private string _status = "Queued"; // Queued, Downloading, Paused, Completed, Failed
        private string _category = "Others";
        private List<DownloadChunk> _chunks = new List<DownloadChunk>();
        private DateTime _addedDate = DateTime.Now;
        private string _protocol = "HTTP"; // HTTP, FTP, Torrent, Video, YouTube, Facebook, TikTok
        private string _platform = "Direct"; // YouTube, Facebook, TikTok, Torrent, Direct, FTP
        private string _thumbnailUrl = "";
        private string _videoQuality = "";
        private string _duration = "";
        private string _formatCode = "";
        private List<string>? _selectedFiles;

        public List<string>? SelectedFiles
        {
            get => _selectedFiles;
            set { _selectedFiles = value; OnPropertyChanged(nameof(SelectedFiles)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(nameof(Url)); }
        }

        public string Filename
        {
            get => _filename;
            set { _filename = value; OnPropertyChanged(nameof(Filename)); }
        }

        public string SavedPath
        {
            get => _savedPath;
            set { _savedPath = value; OnPropertyChanged(nameof(SavedPath)); }
        }

        public long TotalSizeBytes
        {
            get => _totalSizeBytes;
            set 
            { 
                _totalSizeBytes = value; 
                OnPropertyChanged(nameof(TotalSizeBytes)); 
                OnPropertyChanged(nameof(DisplaySize)); 
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(DisplayProgress));
            }
        }

        public long BytesDownloaded
        {
            get => _bytesDownloaded;
            set 
            { 
                _bytesDownloaded = value; 
                OnPropertyChanged(nameof(BytesDownloaded)); 
                OnPropertyChanged(nameof(ProgressPercentage)); 
                OnPropertyChanged(nameof(DisplayProgress));
                OnPropertyChanged(nameof(DisplayDownloaded));
            }
        }

        public double SpeedBytesPerSec
        {
            get => _speedBytesPerSec;
            set 
            { 
                _speedBytesPerSec = value; 
                OnPropertyChanged(nameof(SpeedBytesPerSec)); 
                OnPropertyChanged(nameof(DisplaySpeed)); 
                OnPropertyChanged(nameof(DisplayTimeLeft)); 
            }
        }

        public string Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status)); 
                OnPropertyChanged(nameof(DisplaySpeed));
                OnPropertyChanged(nameof(DisplayTimeLeft));
            }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(nameof(Category)); }
        }

        public string Platform
        {
            get => _platform;
            set { _platform = value; OnPropertyChanged(nameof(Platform)); }
        }

        public string ThumbnailUrl
        {
            get => _thumbnailUrl;
            set { _thumbnailUrl = value; OnPropertyChanged(nameof(ThumbnailUrl)); }
        }

        public string VideoQuality
        {
            get => _videoQuality;
            set { _videoQuality = value; OnPropertyChanged(nameof(VideoQuality)); }
        }

        public string Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(nameof(Duration)); }
        }

        public string FormatCode
        {
            get => _formatCode;
            set { _formatCode = value; OnPropertyChanged(nameof(FormatCode)); }
        }

        public List<DownloadChunk> Chunks
        {
            get => _chunks;
            set { _chunks = value; OnPropertyChanged(nameof(Chunks)); }
        }

        public DateTime AddedDate
        {
            get => _addedDate;
            set { _addedDate = value; OnPropertyChanged(nameof(AddedDate)); }
        }

        public string Protocol
        {
            get => _protocol;
            set { _protocol = value; OnPropertyChanged(nameof(Protocol)); }
        }

        // Formatting Helpers
        public double ProgressPercentage
        {
            get
            {
                if (TotalSizeBytes <= 0)
                {
                    return Status == "Completed" ? 100 : 0;
                }
                double pct = (double)BytesDownloaded / TotalSizeBytes * 100.0;
                return Math.Clamp(pct, 0.0, 100.0);
            }
        }

        public string DisplayProgress
        {
            get
            {
                if (Status == "Completed") return "100%";
                if (TotalSizeBytes <= 0) return "0%";
                return $"{ProgressPercentage:F1}%";
            }
        }

        public string DisplayDownloaded
        {
            get
            {
                if (BytesDownloaded <= 0) return "0 B";
                return FormatBytes(BytesDownloaded);
            }
        }

        public string DisplaySize
        {
            get
            {
                if (TotalSizeBytes <= 0) return "Unknown";
                return FormatBytes(TotalSizeBytes);
            }
        }

        public string DisplaySpeed
        {
            get
            {
                if (Status != "Downloading") return "-";
                return $"{FormatBytes((long)SpeedBytesPerSec)}/s";
            }
        }

        public string DisplayTimeLeft
        {
            get
            {
                if (Status != "Downloading") return "-";
                if (SpeedBytesPerSec <= 0) return "Estimating...";
                long remainingBytes = TotalSizeBytes - BytesDownloaded;
                if (remainingBytes <= 0) return "0s";
                double seconds = remainingBytes / SpeedBytesPerSec;
                var time = TimeSpan.FromSeconds(seconds);
                if (time.TotalDays >= 1) return $"{time.Days}d {time.Hours}h";
                if (time.TotalHours >= 1) return $"{time.Hours}h {time.Minutes}m";
                if (time.TotalMinutes >= 1) return $"{time.Minutes}m {time.Seconds}s";
                return $"{time.Seconds}s";
            }
        }

        public string PlatformBadge
        {
            get
            {
                if (!string.IsNullOrEmpty(Platform) && Platform != "Direct") return Platform;
                return DeterminePlatform(Url);
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
                if (counter >= suffixes.Length - 1) break;
            }
            return $"{number:F2} {suffixes[counter]}";
        }

        public static string DeterminePlatform(string url)
        {
            if (string.IsNullOrEmpty(url)) return "Direct";
            string lower = url.ToLowerInvariant();
            if (lower.Contains("youtube.com") || lower.Contains("youtu.be")) return "YouTube";
            if (lower.Contains("facebook.com") || lower.Contains("fb.watch") || lower.Contains("fb.com")) return "Facebook";
            if (lower.Contains("tiktok.com")) return "TikTok";
            if (lower.Contains("pixeldrain.com")) return "Pixeldrain";
            if (lower.Contains("mega.nz") || lower.Contains("mega.co.nz")) return "Mega";
            if (lower.Contains("mediafire.com")) return "MediaFire";
            if (lower.Contains("drive.google.com") || lower.Contains("docs.google.com")) return "Google Drive";
            if (lower.Contains("dropbox.com")) return "Dropbox";
            if (lower.Contains("gofile.io")) return "GoFile";
            if (lower.StartsWith("magnet:") || lower.EndsWith(".torrent") || lower.Contains(".torrent")) return "Torrent";
            if (lower.StartsWith("ftp://")) return "FTP";
            return "Direct";
        }

        public static string DetermineCategory(string filename, string platform = "")
        {
            string ext = Path.GetExtension(filename).ToLower();
            if (ext == ".mp3" || ext == ".m4a" || ext == ".flac" || ext == ".wav" || ext == ".ogg" || ext == ".wma" || ext == ".aac")
            {
                return "Music";
            }

            if (platform == "YouTube" || platform == "Facebook" || platform == "TikTok" || ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".wmv" || ext == ".flv" || ext == ".webm")
            {
                return "Video";
            }

            switch (ext)
            {
                case ".pdf":
                case ".doc":
                case ".docx":
                case ".xls":
                case ".xlsx":
                case ".ppt":
                case ".pptx":
                case ".txt":
                case ".rtf":
                    return "Document";
                case ".zip":
                case ".rar":
                case ".7z":
                case ".tar":
                case ".gz":
                case ".bz2":
                case ".iso":
                    return "Compressed";
                case ".exe":
                case ".msi":
                case ".apk":
                case ".bat":
                    return "Programs";
                default:
                    return "Others";
            }
        }
    }
}
