using System.Windows;

namespace DOWN_MNG
{
    public partial class HelpGuideWindow : Window
    {
        public HelpGuideWindow(int initialTabIndex = 0)
        {
            InitializeComponent();
            if (HelpTabControl != null && initialTabIndex >= 0 && initialTabIndex < HelpTabControl.Items.Count)
            {
                HelpTabControl.SelectedIndex = initialTabIndex;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenExtensionSetup_Click(object sender, RoutedEventArgs e)
        {
            var win = new BrowserExtensionSetupWindow { Owner = this };
            win.ShowDialog();
        }
    }
}
