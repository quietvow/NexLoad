using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace DOWN_MNG
{
    public static class BrowserExtensionManager
    {
        public static string ExtensionDirectory { get; }

        static BrowserExtensionManager()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ExtensionDirectory = Path.Combine(localApp, "NexLoad", "BrowserExtension");
            EnsureExtensionFiles();
        }

        public static void EnsureExtensionFiles()
        {
            try
            {
                if (!Directory.Exists(ExtensionDirectory))
                {
                    Directory.CreateDirectory(ExtensionDirectory);
                }

                string manifestDest = Path.Combine(ExtensionDirectory, "manifest.json");
                string bgDest = Path.Combine(ExtensionDirectory, "background.js");

                // Check app directory first
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string appExtDir = Path.Combine(baseDir, "BrowserExtension");

                if (File.Exists(Path.Combine(appExtDir, "manifest.json")))
                {
                    File.Copy(Path.Combine(appExtDir, "manifest.json"), manifestDest, true);
                }
                else if (!File.Exists(manifestDest))
                {
                    File.WriteAllText(manifestDest, GetDefaultManifest());
                }

                if (File.Exists(Path.Combine(appExtDir, "background.js")))
                {
                    File.Copy(Path.Combine(appExtDir, "background.js"), bgDest, true);
                }
                else if (!File.Exists(bgDest))
                {
                    File.WriteAllText(bgDest, GetDefaultBackgroundJs());
                }
            }
            catch { }
        }

        public static string? FindChromePath()
        {
            return FindBrowserExe("chrome.exe", new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            });
        }

        public static string? FindEdgePath()
        {
            return FindBrowserExe("msedge.exe", new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            });
        }

        public static string? FindBravePath()
        {
            return FindBrowserExe("brave.exe", new[]
            {
                @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\Application\brave.exe")
            });
        }

        public static string? FindFirefoxPath()
        {
            return FindBrowserExe("firefox.exe", new[]
            {
                @"C:\Program Files\Mozilla Firefox\firefox.exe",
                @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Mozilla Firefox\firefox.exe")
            });
        }

        public static string FirefoxExtensionDirectory => Path.Combine(ExtensionDirectory, "firefox");

        public static void OpenFirefoxSetup()
        {
            EnsureExtensionFiles();

            try
            {
                if (!Directory.Exists(FirefoxExtensionDirectory))
                {
                    Directory.CreateDirectory(FirefoxExtensionDirectory);
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string sourceFirefoxDir = Path.Combine(baseDir, "BrowserExtension", "firefox");
                if (Directory.Exists(sourceFirefoxDir))
                {
                    foreach (var f in Directory.GetFiles(sourceFirefoxDir))
                    {
                        File.Copy(f, Path.Combine(FirefoxExtensionDirectory, Path.GetFileName(f)), true);
                    }
                }
            }
            catch { }

            try
            {
                Clipboard.SetText(FirefoxExtensionDirectory);
            }
            catch { }

            try
            {
                string? ffPath = FindFirefoxPath();
                if (!string.IsNullOrEmpty(ffPath) && File.Exists(ffPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ffPath,
                        Arguments = "about:debugging#/runtime/this-firefox",
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "about:debugging#/runtime/this-firefox",
                        UseShellExecute = true
                    });
                }
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{FirefoxExtensionDirectory}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private static string? FindBrowserExe(string exeName, string[] standardPaths)
        {
            foreach (var path in standardPaths)
            {
                if (File.Exists(path)) return path;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                string? val = key?.GetValue("") as string;
                if (!string.IsNullOrEmpty(val) && File.Exists(val)) return val;

                using var hklmKey = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                val = hklmKey?.GetValue("") as string;
                if (!string.IsNullOrEmpty(val) && File.Exists(val)) return val;
            }
            catch { }

            return null;
        }

        public static bool LaunchBrowserWithExtension(string browserExePath)
        {
            try
            {
                EnsureExtensionFiles();
                Process.Start(new ProcessStartInfo
                {
                    FileName = browserExePath,
                    Arguments = $"--load-extension=\"{ExtensionDirectory}\" https://www.google.com",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void OpenExtensionPageAndFolder(string browserExePath, string extensionUrl)
        {
            EnsureExtensionFiles();

            // 1. Copy folder path to clipboard for 1-click paste
            try
            {
                Clipboard.SetText(ExtensionDirectory);
            }
            catch { }

            // 2. Open browser extensions page
            try
            {
                if (!string.IsNullOrEmpty(browserExePath) && File.Exists(browserExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = browserExePath,
                        Arguments = extensionUrl,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = extensionUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch { }

            // 3. Open Explorer showing the Extension folder
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{ExtensionDirectory}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public static void OpenFolderInExplorer()
        {
            EnsureExtensionFiles();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{ExtensionDirectory}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public static async Task<bool> TestLocalServerAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var res = await client.GetAsync("http://localhost:3015/ping");
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string GetDefaultManifest()
        {
            return @"{
  ""manifest_version"": 3,
  ""name"": ""NexLoad Pro Browser Integration"",
  ""version"": ""1.0.0"",
  ""description"": ""High-Speed download capture and universal media grabber integration for NexLoad Pro."",
  ""permissions"": [
    ""downloads"",
    ""downloads.shelf"",
    ""contextMenus""
  ],
  ""host_permissions"": [
    ""http://localhost:3015/*"",
    ""<all_urls>""
  ],
  ""background"": {
    ""service_worker"": ""background.js""
  }
}";
        }

        private static string GetDefaultBackgroundJs()
        {
            return @"// Setup Context Menus on installation
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: ""nexload-download-link"",
    title: ""Download with NexLoad Pro"",
    contexts: [""link"", ""video"", ""audio"", ""image""]
  });

  chrome.contextMenus.create({
    id: ""nexload-grab-page-media"",
    title: ""Grab Media from this page with NexLoad Pro"",
    contexts: [""page"", ""video""]
  });
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  let targetUrl = """";

  if (info.menuItemId === ""nexload-download-link"") {
    targetUrl = info.linkUrl || info.srcUrl || """";
  } else if (info.menuItemId === ""nexload-grab-page-media"") {
    targetUrl = info.srcUrl || info.pageUrl || tab?.url || """";
  }

  if (targetUrl) {
    try {
      await fetch('http://localhost:3015/add-download', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          url: targetUrl,
          filename: """"
        })
      });
    } catch (err) {
      console.warn(""Could not reach NexLoad desktop application. Ensure app is running."");
    }
  }
});

chrome.downloads.onCreated.addListener(async (downloadItem) => {
  if (!downloadItem.url ||
      downloadItem.url.startsWith('http://localhost') || 
      downloadItem.url.startsWith('http://127.0.0.1') ||
      !downloadItem.url.startsWith('http')) {
    return;
  }

  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 250);

    const response = await fetch('http://localhost:3015/ping', {
      method: 'GET',
      signal: controller.signal
    });

    clearTimeout(timeoutId);

    if (response.ok) {
      chrome.downloads.cancel(downloadItem.id);
      chrome.downloads.erase({ id: downloadItem.id });

      await fetch('http://localhost:3015/add-download', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          url: downloadItem.url,
          filename: downloadItem.filename || """"
        })
      });
    }
  } catch (error) {
    console.log(""NexLoad Desktop App is not active. Fallback to native browser download."");
  }
});";
        }
    }
}
