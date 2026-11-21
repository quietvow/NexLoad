using System;

namespace DOWN_MNG
{
    public class DownloadChunk
    {
        public int Id { get; set; }
        public long StartByte { get; set; }
        public long EndByte { get; set; }
        public long BytesDownloaded { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsActive { get; set; }

        public long Length => EndByte - StartByte + 1;
        public double ProgressPercentage => Length > 0 ? (double)BytesDownloaded / Length * 100 : 0;
    }
}
