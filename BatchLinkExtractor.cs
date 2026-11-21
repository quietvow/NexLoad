using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOWN_MNG
{
    public class BatchLinkItem
    {
        public string Url { get; set; } = "";
        public bool IsSelected { get; set; } = true;
        public string Platform { get; set; } = "Direct";
        public string Category { get; set; } = "Others";
        public string Filename { get; set; } = "";
    }

    public static class BatchLinkExtractor
    {
        private static readonly Regex UrlRegex = new Regex(
            @"(?:https?://|ftp://|magnet:\?)[^\s""'<>\)\]]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly char[] TrailingPunctuation = { '.', ',', ';', ':', ')', ']', '>', '"', '\'', '?' };

        public static List<BatchLinkItem> ExtractLinks(string text)
        {
            var result = new List<BatchLinkItem>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = UrlRegex.Matches(text);

            foreach (Match match in matches)
            {
                string rawUrl = match.Value.Trim();
                
                // Trim trailing punctuation unless it's part of query string
                while (rawUrl.Length > 0 && TrailingPunctuation.Contains(rawUrl[^1]))
                {
                    // Don't strip '?' if it's "magnet:?"
                    if (rawUrl.Equals("magnet:?", StringComparison.OrdinalIgnoreCase)) break;
                    rawUrl = rawUrl[..^1];
                }

                if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Length < 6) continue;
                if (!seen.Add(rawUrl)) continue;

                string platform = VideoDownloaderEngine.DetectPlatform(rawUrl);
                if (CloudHosterResolver.IsCloudHoster(rawUrl))
                {
                    platform = CloudHosterResolver.GetHosterName(rawUrl);
                }
                string filename = SuggestFilename(rawUrl, platform);
                string category = DownloadItem.DetermineCategory(filename, platform);

                if (rawUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) || rawUrl.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                {
                    platform = "Torrent";
                    category = "Torrent";
                }

                result.Add(new BatchLinkItem
                {
                    Url = rawUrl,
                    IsSelected = true,
                    Platform = platform,
                    Category = category,
                    Filename = filename
                });
            }

            return result;
        }

        private static string SuggestFilename(string url, string platform)
        {
            try
            {
                if (platform == "YouTube" || platform == "Facebook" || platform == "TikTok")
                {
                    return $"{platform}_Video_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.mp4";
                }

                if (CloudHosterResolver.IsCloudHoster(url))
                {
                    string hoster = CloudHosterResolver.GetHosterName(url);
                    return $"{hoster}_File_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.bin";
                }

                if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    var dnIndex = url.IndexOf("dn=", StringComparison.OrdinalIgnoreCase);
                    if (dnIndex != -1)
                    {
                        var endIndex = url.IndexOf('&', dnIndex);
                        var dnRaw = endIndex != -1 ? url.Substring(dnIndex + 3, endIndex - dnIndex - 3) : url.Substring(dnIndex + 3);
                        return Uri.UnescapeDataString(dnRaw);
                    }
                    return "torrent_item.zip";
                }

                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name))
                {
                    return Uri.UnescapeDataString(name);
                }
            }
            catch { }

            return "download_file";
        }
    }
}
