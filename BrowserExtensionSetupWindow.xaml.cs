using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace DOWN_MNG
{
    public partial class BrowserExtensionSetupWindow : Window
    {
        private readonly string? _chromePath;
        private readonly string? _edgePath;
        private readonly string? _bravePath;

        public BrowserExtensionSetupWindow()
        {
            InitializeComponent();

            BrowserExtensionManager.EnsureExtensionFiles();

            _chromePath = BrowserExtensionManager.FindChromePath();
            _edgePath = BrowserExtensionManager.FindEdgePath();
            _bravePath = BrowserExtensionManager.FindBravePath();

            if (!string.IsNullOrEmpty(_chromePath))
            {
                ChromeStatusText.Text = "Chrome detected on this PC";
            }
            else
            {
                ChromeStatusText.Text = "Chrome not found in default location";
            }

            if (!string.IsNullOrEmpty(_edgePath))
            {
                EdgeStatusText.Text = "Edge detected on this PC";
            }
            else
            {
                EdgeStatusText.Text = "Edge not found in default location";
            }
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            ServerStatusTextBlock.Text = "Checking connection...";
            bool online = await BrowserExtensionManager.TestLocalServerAsync();
            if (online)
            {
                ServerStatusIndicator.Foreground = new SolidColorBrush(Colors.Green);
                ServerStatusTextBlock.Text = "Local Communication Server: Online & Listening (Port 3015)";
                MessageBox.Show("Local communication server is running and accessible on port 3015.\nBrowser extensions can connect without issues.", "Server Online", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ServerStatusIndicator.Foreground = new SolidColorBrush(Colors.Red);
                ServerStatusTextBlock.Text = "Local Communication Server: Offline / Blocked";
                MessageBox.Show("Could not connect to local server on port 3015.\nPlease ensure NexLoad is running.", "Server Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetupChrome_Click(object sender, RoutedEventArgs e)
        {
            BrowserExtensionManager.OpenExtensionPageAndFolder(_chromePath ?? "chrome.exe", "chrome://extensions/");
            ShowSetupInstructions("Google Chrome");
        }

        private void LaunchChrome_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_chromePath) || !File.Exists(_chromePath))
            {
                MessageBox.Show("Could not find chrome.exe on this computer.", "Chrome Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool launched = BrowserExtensionManager.LaunchBrowserWithExtension(_chromePath);
            if (launched)
            {
                MessageBox.Show("Google Chrome launched with NexLoad extension loaded!\nRight-click any link or video to test downloading.", "Chrome Launched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SetupEdge_Click(object sender, RoutedEventArgs e)
        {
            BrowserExtensionManager.OpenExtensionPageAndFolder(_edgePath ?? "msedge.exe", "edge://extensions/");
            ShowSetupInstructions("Microsoft Edge");
        }

        private void LaunchEdge_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_edgePath) || !File.Exists(_edgePath))
            {
                MessageBox.Show("Could not find msedge.exe on this computer.", "Edge Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool launched = BrowserExtensionManager.LaunchBrowserWithExtension(_edgePath);
            if (launched)
            {
                MessageBox.Show("Microsoft Edge launched with NexLoad extension loaded!\nRight-click any link or video to test downloading.", "Edge Launched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SetupBrave_Click(object sender, RoutedEventArgs e)
        {
            BrowserExtensionManager.OpenExtensionPageAndFolder(_bravePath ?? "brave.exe", "brave://extensions/");
            ShowSetupInstructions("Brave / Chromium");
        }

        private void SetupFirefox_Click(object sender, RoutedEventArgs e)
        {
            BrowserExtensionManager.OpenFirefoxSetup();
            string msg = "Mozilla Firefox debugging page and the Firefox extension folder have been opened.\n\nThe folder path is already copied to your clipboard!\n\nTo complete setup in Firefox:\n1. Click 'This Firefox' on the left menu (about:debugging#/runtime/this-firefox).\n2. Click 'Load Temporary Add-on...'.\n3. Select the 'manifest.json' file inside the opened firefox folder.\n\nNexLoad Pro is now linked with Firefox!";
            MessageBox.Show(msg, "Firefox Integration", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenFirefoxFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", BrowserExtensionManager.FirefoxExtensionDirectory);
            }
            catch { }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(BrowserExtensionManager.ExtensionDirectory);
                MessageBox.Show($"Extension folder path copied to clipboard:\n\n{BrowserExtensionManager.ExtensionDirectory}", "Path Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            BrowserExtensionManager.OpenFolderInExplorer();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowSetupInstructions(string browserName)
        {
            string msg = $"{browserName} Extensions page and the Extension folder have been opened.\n\nThe folder path is already copied to your clipboard!\n\nTo complete setup in {browserName}:\n1. Turn ON 'Developer mode' (toggle in top-right corner).\n2. Click 'Load unpacked' button.\n3. Paste or select the opened folder.\n\nNexLoad Pro will now capture all downloads automatically!";
            MessageBox.Show(msg, $"{browserName} Integration", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
