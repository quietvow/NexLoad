using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CG.Web.MegaApiClient;

namespace DOWN_MNG
{
    public class CloudHosterResult
    {
        public bool IsCloudHoster { get; set; }
        public string HosterName { get; set; } = "";
        public string OriginalUrl { get; set; } = "";
        public string DirectDownloadUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public long FileSize { get; set; } = -1;
        public string Platform { get; set; } = "Direct";
        public string Category { get; set; } = "Others";
        public bool RequiresCustomEngine { get; set; } = false;
        public bool IsFolderOrList { get; set; } = false;
        public List<CloudHosterResult> AdditionalFiles { get; set; } = new List<CloudHosterResult>();
        public string ErrorMessage { get; set; } = "";
    }

    public static class CloudHosterResolver
    {
        private static readonly HttpClient _httpClient;

        static CloudHosterResolver()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        public static bool IsCloudHoster(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string lower = url.ToLowerInvariant();

            return lower.Contains("pixeldrain.com") ||
                   lower.Contains("mega.nz") ||
                   lower.Contains("mega.co.nz") ||
                   lower.Contains("mediafire.com") ||
                   lower.Contains("drive.google.com") ||
                   (lower.Contains("docs.google.com") && lower.Contains("/uc?")) ||
                   lower.Contains("dropbox.com") ||
                   lower.Contains("gofile.io") ||
                   lower.Contains("catbox.moe");
        }

        public static string GetHosterName(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "Direct";
            string lower = url.ToLowerInvariant();

            if (lower.Contains("pixeldrain.com")) return "Pixeldrain";
            if (lower.Contains("mega.nz") || lower.Contains("mega.co.nz")) return "Mega";
            if (lower.Contains("mediafire.com")) return "MediaFire";
            if (lower.Contains("drive.google.com") || lower.Contains("docs.google.com")) return "Google Drive";
            if (lower.Contains("dropbox.com")) return "Dropbox";
            if (lower.Contains("gofile.io")) return "GoFile";
            if (lower.Contains("catbox.moe")) return "Catbox";

            return "Direct";
        }

        public static async Task<CloudHosterResult?> ResolveAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!IsCloudHoster(url)) return null;

            string hoster = GetHosterName(url);
            try
            {
                switch (hoster)
                {
                    case "Pixeldrain":
                        return await ResolvePixeldrainAsync(url, cancellationToken);
                    case "Mega":
                        return await ResolveMegaAsync(url, cancellationToken);
                    case "MediaFire":
                        return await ResolveMediafireAsync(url, cancellationToken);
                    case "Google Drive":
                        return await ResolveGoogleDriveAsync(url, cancellationToken);
                    case "Dropbox":
                        return await ResolveDropboxAsync(url, cancellationToken);
                    case "GoFile":
                        return await ResolveGoFileAsync(url, cancellationToken);
                    case "Catbox":
                        return await ResolveCatboxAsync(url, cancellationToken);
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                return new CloudHosterResult
                {
                    IsCloudHoster = true,
                    HosterName = hoster,
                    Platform = hoster,
                    OriginalUrl = url,
                    DirectDownloadUrl = url,
                    FileName = SuggestFallbackFilename(url, hoster),
                    ErrorMessage = ex.Message
                };
            }
        }

        #region Pixeldrain Resolver
        private static async Task<CloudHosterResult> ResolvePixeldrainAsync(string url, CancellationToken ct)
        {
            // Patterns: pixeldrain.com/u/{id}, pixeldrain.com/api/file/{id}, or pixeldrain.com/l/{id}
            var listMatch = Regex.Match(url, @"pixeldrain\.com/l/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (listMatch.Success)
            {
                string listId = listMatch.Groups[1].Value;
                string apiUrl = $"https://pixeldrain.com/api/list/{listId}";
                using var response = await _httpClient.GetAsync(apiUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? $"Pixeldrain_List_{listId}" : $"Pixeldrain_List_{listId}";

                    var result = new CloudHosterResult
                    {
                        IsCloudHoster = true,
                        HosterName = "Pixeldrain",
                        Platform = "Pixeldrain",
                        OriginalUrl = url,
                        FileName = $"{title}.zip",
                        IsFolderOrList = true
                    };

                    if (root.TryGetProperty("files", out var filesArray) && filesArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in filesArray.EnumerateArray())
                        {
                            string fileId = f.GetProperty("id").GetString() ?? "";
                            string fileName = f.GetProperty("name").GetString() ?? $"file_{fileId}";
                            long fileSize = f.TryGetProperty("size", out var s) ? s.GetInt64() : -1;

                            result.AdditionalFiles.Add(new CloudHosterResult
                            {
                                IsCloudHoster = true,
                                HosterName = "Pixeldrain",
                                Platform = "Pixeldrain",
                                OriginalUrl = $"https://pixeldrain.com/u/{fileId}",
                                DirectDownloadUrl = $"https://pixeldrain.com/api/file/{fileId}?download=1",
                                FileName = fileName,
                                FileSize = fileSize,
                                Category = DownloadItem.DetermineCategory(fileName, "Pixeldrain")
                            });
                        }
                    }

                    if (result.AdditionalFiles.Count > 0)
                    {
                        result.DirectDownloadUrl = result.AdditionalFiles[0].DirectDownloadUrl;
                        result.FileName = result.AdditionalFiles[0].FileName;
                        result.FileSize = result.AdditionalFiles[0].FileSize;
                        result.Category = result.AdditionalFiles[0].Category;
                    }

                    return result;
                }
            }

            var fileMatch = Regex.Match(url, @"pixeldrain\.com/(?:u|api/file)/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (fileMatch.Success)
            {
                string fileId = fileMatch.Groups[1].Value;
                string apiUrl = $"https://pixeldrain.com/api/file/{fileId}/info";
                using var response = await _httpClient.GetAsync(apiUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? $"pixeldrain_{fileId}" : $"pixeldrain_{fileId}";
                    long size = root.TryGetProperty("size", out var s) ? s.GetInt64() : -1;

                    return new CloudHosterResult
                    {
                        IsCloudHoster = true,
                        HosterName = "Pixeldrain",
                        Platform = "Pixeldrain",
                        OriginalUrl = url,
                        DirectDownloadUrl = $"https://pixeldrain.com/api/file/{fileId}?download=1",
                        FileName = name,
                        FileSize = size,
                        Category = DownloadItem.DetermineCategory(name, "Pixeldrain")
                    };
                }
                else
                {
                    // Fallback to direct download endpoint
                    return new CloudHosterResult
                    {
                        IsCloudHoster = true,
                        HosterName = "Pixeldrain",
                        Platform = "Pixeldrain",
                        OriginalUrl = url,
                        DirectDownloadUrl = $"https://pixeldrain.com/api/file/{fileId}?download=1",
                        FileName = $"pixeldrain_{fileId}.bin",
                        Category = "Others"
                    };
                }
            }

            return new CloudHosterResult
            {
                IsCloudHoster = true,
                HosterName = "Pixeldrain",
                Platform = "Pixeldrain",
                OriginalUrl = url,
                DirectDownloadUrl = url,
                FileName = SuggestFallbackFilename(url, "Pixeldrain")
            };
        }
        #endregion

        #region MEGA Resolver
        private static async Task<CloudHosterResult> ResolveMegaAsync(string url, CancellationToken ct)
        {
            await MegaDownloadEngine.EnsureLoggedInAsync();
            var client = MegaDownloadEngine.GetClient();
            var uri = new Uri(url);

            bool isFolder = url.Contains("/folder/") || url.Contains("/#F!");
            if (isFolder)
            {
                var nodes = await client.GetNodesFromLinkAsync(uri);
                var fileNodes = nodes.Where(x => x.Type == NodeType.File).ToList();

                var result = new CloudHosterResult
                {
                    IsCloudHoster = true,
                    HosterName = "Mega",
                    Platform = "Mega",
                    OriginalUrl = url,
                    DirectDownloadUrl = url,
                    FileName = fileNodes.Count > 0 ? $"{fileNodes[0].Name}" : "MEGA_Folder",
                    FileSize = fileNodes.Count > 0 ? fileNodes[0].Size : -1,
                    Category = "Others",
                    RequiresCustomEngine = true,
                    IsFolderOrList = true
                };

                foreach (var fn in fileNodes)
                {
                    result.AdditionalFiles.Add(new CloudHosterResult
                    {
                        IsCloudHoster = true,
                        HosterName = "Mega",
                        Platform = "Mega",
                        OriginalUrl = url,
                        DirectDownloadUrl = url,
                        FileName = fn.Name,
                        FileSize = fn.Size,
                        Category = DownloadItem.DetermineCategory(fn.Name, "Mega"),
                        RequiresCustomEngine = true
                    });
                }

                return result;
            }
            else
            {
                INode node = await client.GetNodeFromLinkAsync(uri);
                if (node == null)
                {
                    throw new Exception("File node not found on MEGA. Link may be removed or decryption key invalid.");
                }

                return new CloudHosterResult
                {
                    IsCloudHoster = true,
                    HosterName = "Mega",
                    Platform = "Mega",
                    OriginalUrl = url,
                    DirectDownloadUrl = url,
                    FileName = node.Name,
                    FileSize = node.Size,
                    Category = DownloadItem.DetermineCategory(node.Name, "Mega"),
                    RequiresCustomEngine = true
                };
            }
        }
        #endregion

        #region MediaFire Resolver
        private static async Task<CloudHosterResult> ResolveMediafireAsync(string url, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"MediaFire returned status code {response.StatusCode}");
            }

            string html = await response.Content.ReadAsStringAsync(ct);

            // 1. Direct download button
            var matchBtn = Regex.Match(html, @"id=""downloadButton""[\s\S]*?href=""(?<url>https?://download\d*\.mediafire\.com/[^""]+)""", RegexOptions.IgnoreCase);
            if (!matchBtn.Success)
            {
                matchBtn = Regex.Match(html, @"(?<url>https?://download\d*\.mediafire\.com/[^\s""'<>]+)", RegexOptions.IgnoreCase);
            }

            if (!matchBtn.Success)
            {
                throw new Exception("Could not find direct download stream on MediaFire page.");
            }

            string directUrl = matchBtn.Groups["url"].Value;

            // 2. Extract filename
            string filename = "";
            var matchFilename = Regex.Match(html, @"<div class=""filename"">(?<name>[^<]+)</div>", RegexOptions.IgnoreCase);
            if (matchFilename.Success)
            {
                filename = matchFilename.Groups["name"].Value.Trim();
            }
            else
            {
                var matchOg = Regex.Match(html, @"<meta property=""og:title"" content=""(?<name>[^""]+)""", RegexOptions.IgnoreCase);
                if (matchOg.Success)
                {
                    filename = matchOg.Groups["name"].Value.Trim();
                }
                else
                {
                    try
                    {
                        filename = Path.GetFileName(new Uri(directUrl).AbsolutePath);
                    }
                    catch { }
                }
            }

            if (string.IsNullOrEmpty(filename)) filename = "MediaFire_Download.bin";

            // 3. Size check via HEAD on direct URL
            long fileSize = -1;
            try
            {
                var headReq = new HttpRequestMessage(HttpMethod.Head, directUrl);
                using var headRes = await _httpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
                if (headRes.Content.Headers.ContentLength.HasValue)
                {
                    fileSize = headRes.Content.Headers.ContentLength.Value;
                }
            }
            catch { }

            return new CloudHosterResult
            {
                IsCloudHoster = true,
                HosterName = "MediaFire",
                Platform = "MediaFire",
                OriginalUrl = url,
                DirectDownloadUrl = directUrl,
                FileName = filename,
                FileSize = fileSize,
                Category = DownloadItem.DetermineCategory(filename, "MediaFire")
            };
        }
        #endregion

        #region Google Drive Resolver
        private static async Task<CloudHosterResult> ResolveGoogleDriveAsync(string url, CancellationToken ct)
        {
            // Extract file ID
            string fileId = "";
            var matchId = Regex.Match(url, @"(?:/file/d/|id=|/uc\?id=)([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (matchId.Success)
            {
                fileId = matchId.Groups[1].Value;
            }
            else
            {
                throw new Exception("Invalid Google Drive file ID.");
            }

            string directExportUrl = $"https://drive.google.com/uc?id={fileId}&export=download";

            // Send request to check if virus scan bypass warning is presented
            using var response = await _httpClient.GetAsync(directExportUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            string contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            string directUrl = directExportUrl;
            string filename = $"GDrive_{fileId}";
            long fileSize = response.Content.Headers.ContentLength ?? -1;

            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                // Likely a confirmation page for large files
                string html = await response.Content.ReadAsStringAsync(ct);
                var matchConfirm = Regex.Match(html, @"href=""(?<link>/uc\?export=download&amp;confirm=[^""]+)""", RegexOptions.IgnoreCase);
                if (!matchConfirm.Success)
                {
                    matchConfirm = Regex.Match(html, @"id=""uc-download-link""[\s\S]*?href=""(?<link>[^""]+)""", RegexOptions.IgnoreCase);
                }

                if (matchConfirm.Success)
                {
                    string confirmRel = matchConfirm.Groups["link"].Value.Replace("&amp;", "&");
                    directUrl = confirmRel.StartsWith("http", StringComparison.OrdinalIgnoreCase) 
                        ? confirmRel 
                        : $"https://drive.google.com{confirmRel}";
                }
            }

            // Extract content-disposition filename if present
            if (response.Content.Headers.ContentDisposition?.FileName != null)
            {
                filename = response.Content.Headers.ContentDisposition.FileName.Trim('"');
            }

            return new CloudHosterResult
            {
                IsCloudHoster = true,
                HosterName = "Google Drive",
                Platform = "Google Drive",
                OriginalUrl = url,
                DirectDownloadUrl = directUrl,
                FileName = filename,
                FileSize = fileSize,
                Category = DownloadItem.DetermineCategory(filename, "Google Drive")
            };
        }
        #endregion

        #region Dropbox Resolver
        private static Task<CloudHosterResult> ResolveDropboxAsync(string url, CancellationToken ct)
        {
            // Simply replacing dl=0 with dl=1 converts Dropbox to direct file download
            string directUrl = url;
            if (directUrl.Contains("dl=0"))
            {
                directUrl = directUrl.Replace("dl=0", "dl=1");
            }
            else if (!directUrl.Contains("dl=1"))
            {
                directUrl += directUrl.Contains("?") ? "&dl=1" : "?dl=1";
            }

            string filename = "Dropbox_File";
            try
            {
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string segName = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(segName) && segName != "fi" && segName != "s")
                {
                    filename = Uri.UnescapeDataString(segName);
                }
            }
            catch { }

            return Task.FromResult(new CloudHosterResult
            {
                IsCloudHoster = true,
                HosterName = "Dropbox",
                Platform = "Dropbox",
                OriginalUrl = url,
                DirectDownloadUrl = directUrl,
                FileName = filename,
                Category = DownloadItem.DetermineCategory(filename, "Dropbox")
            });
        }
        #endregion

        #region GoFile Resolver
        private static async Task<CloudHosterResult> ResolveGoFileAsync(string url, CancellationToken ct)
        {
            var match = Regex.Match(url, @"gofile\.io/d/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                throw new Exception("Invalid GoFile URL.");
            }

            string contentId = match.Groups[1].Value;
            string apiUrl = $"https://api.gofile.io/contents/{contentId}?wt=4fd6sg89d7s6";
            using var response = await _httpClient.GetAsync(apiUrl, ct);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var data) && data.TryGetProperty("children", out var children))
                {
                    foreach (var prop in children.EnumerateObject())
                    {
                        var child = prop.Value;
                        string fileName = child.TryGetProperty("name", out var n) ? n.GetString() ?? $"GoFile_{contentId}" : $"GoFile_{contentId}";
                        string link = child.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                        long size = child.TryGetProperty("size", out var s) ? s.GetInt64() : -1;

                        if (!string.IsNullOrEmpty(link))
                        {
                            return new CloudHosterResult
                            {
                                IsCloudHoster = true,
                                HosterName = "GoFile",
                                Platform = "GoFile",
                                OriginalUrl = url,
                                DirectDownloadUrl = link,
                                FileName = fileName,
                                FileSize = size,
                                Category = DownloadItem.DetermineCategory(fileName, "GoFile")
                            };
                        }
                    }
                }
            }

            return new CloudHosterResult
            {
                IsCloudHoster = true,
                HosterName = "GoFile",
                Platform = "GoFile",
                OriginalUrl = url,
                DirectDownloadUrl = url,
                FileName = $"GoFile_{contentId}"
            };
        }
        #endregion

        #region Catbox Resolver
        private static async Task<CloudHosterResult> ResolveCatboxAsync(string url, CancellationToken ct)
        {
            string filename = "";
            try
            {
                var uri = new Uri(url);
                filename = Path.GetFileName(uri.AbsolutePath);
            }
            catch { }

            if (string.IsNullOrEmpty(filename)) filename = $"Catbox_file_{DateTime.Now:yyyyMMdd_HHmmss}.bin";

            long size = -1;
            try
            {
                var head = new HttpRequestMessage(HttpMethod.Head, url);
                using var res = await _httpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.Content.Headers.ContentLength.HasValue)
                {
                    size = res.Content.Headers.ContentLength.Value;
                }
            }
            catch { }

            return new CloudHosterResult
            {
                IsCloudHoster = true,
                HosterName = "Catbox",
                Platform = "Catbox",
                OriginalUrl = url,
                DirectDownloadUrl = url,
                FileName = filename,
                FileSize = size,
                Category = DownloadItem.DetermineCategory(filename, "Catbox")
            };
        }
        #endregion

        private static string SuggestFallbackFilename(string url, string hoster)
        {
            try
            {
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string seg = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(seg) && seg.Length > 2)
                {
                    return Uri.UnescapeDataString(seg);
                }
            }
            catch { }

            return $"{hoster}_Download_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
    }
}
