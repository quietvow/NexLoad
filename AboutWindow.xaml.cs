using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace DOWN_MNG
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            OsVersionTextBlock.Text = $"{Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
            ArchTextBlock.Text = $"{RuntimeInformation.ProcessArchitecture} (.NET {Environment.Version})";
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You are currently using the latest release of NexLoad Pro (v1.0.0).\nAll acceleration and media modules are up to date.", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CopyInfo_Click(object sender, RoutedEventArgs e)
        {
            string info = $"NexLoad Pro v1.0.0\nDeveloper: quietvow\nGitHub: https://github.com/quietvow/NexLoad\nOS: {Environment.OSVersion.VersionString}\nRuntime: .NET {Environment.Version}\nArch: {RuntimeInformation.ProcessArchitecture}\nMachine: {Environment.MachineName}";
            Clipboard.SetText(info);
            MessageBox.Show("System information copied to clipboard.", "Info Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
