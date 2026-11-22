using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DOWN_MNG
{
    public class HashResult
    {
        public string MD5 { get; set; } = "";
        public string SHA1 { get; set; } = "";
        public string SHA256 { get; set; } = "";
        public bool Success { get; set; }
        public string Error { get; set; } = "";
    }

    public static class FileHashVerifier
    {
        public static async Task<HashResult> ComputeHashesAsync(string filePath)
        {
            var result = new HashResult();
            if (!File.Exists(filePath))
            {
                result.Error = "File not found.";
                return result;
            }

            try
            {
                await Task.Run(() =>
                {
                    using var stream = File.OpenRead(filePath);

                    using var md5 = MD5.Create();
                    using var sha1 = SHA1.Create();
                    using var sha256 = SHA256.Create();

                    byte[] buffer = new byte[81920]; // 80 KB
                    int bytesRead;

                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }

                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                    result.MD5 = ToHexString(md5.Hash);
                    result.SHA1 = ToHexString(sha1.Hash);
                    result.SHA256 = ToHexString(sha256.Hash);
                    result.Success = true;
                });
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Success = false;
            }

            return result;
        }

        public static bool VerifyHash(HashResult hashes, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash)) return false;
            string clean = expectedHash.Trim().ToLowerInvariant();

            return hashes.MD5.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                   hashes.SHA1.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                   hashes.SHA256.Equals(clean, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToHexString(byte[]? bytes)
        {
            if (bytes == null) return "";
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
