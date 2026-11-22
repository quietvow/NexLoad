using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace DOWN_MNG
{
    public static class ArchiveExtractor
    {
        public static bool IsArchive(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar" || ext == ".gz";
        }

        public static async Task<bool> ExtractArchiveAsync(string filePath, string? targetDirectory = null)
        {
            if (!File.Exists(filePath)) return false;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string folderName = Path.GetFileNameWithoutExtension(filePath);
            string destination = targetDirectory ?? Path.Combine(Path.GetDirectoryName(filePath) ?? "", folderName);

            try
            {
                if (!Directory.Exists(destination))
                {
                    Directory.CreateDirectory(destination);
                }

                if (ext == ".zip")
                {
                    await Task.Run(() =>
                    {
                        ZipFile.ExtractToDirectory(filePath, destination, overwriteFiles: true);
                    });
                    return true;
                }
                else
                {
                    // Check for 7-Zip, WinRAR or tar installed on Windows
                    string[] possibleExtractors = {
                        @"C:\Program Files\7-Zip\7z.exe",
                        @"C:\Program Files (x86)\7-Zip\7z.exe",
                        @"C:\Program Files\WinRAR\WinRAR.exe",
                        @"tar.exe"
                    };

                    foreach (var extractor in possibleExtractors)
                    {
                        if (extractor == "tar.exe" || File.Exists(extractor))
                        {
                            string args = extractor.Contains("7z") ? $"x \"{filePath}\" -o\"{destination}\" -y" :
                                          extractor.Contains("WinRAR") ? $"x -ibck \"{filePath}\" \"{destination}\\\"" :
                                          $"-xf \"{filePath}\" -C \"{destination}\"";

                            var psi = new ProcessStartInfo
                            {
                                FileName = extractor,
                                Arguments = args,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };

                            using var p = Process.Start(psi);
                            if (p != null)
                            {
                                await p.WaitForExitAsync();
                                if (p.ExitCode == 0) return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
