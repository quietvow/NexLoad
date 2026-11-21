using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace DOWN_MNG
{
    public partial class TorrentFileSelectionWindow : Window
    {
        public class TorrentFileItem
        {
            public string Path { get; set; } = "";
            public long Size { get; set; }
            public bool IsSelected { get; set; } = true;
            public string DisplaySize => DownloadItem.FormatBytes(Size);
        }

        private ObservableCollection<TorrentFileItem> _files = new ObservableCollection<TorrentFileItem>();
        public List<string> SelectedFiles { get; private set; } = new List<string>();

        public TorrentFileSelectionWindow(IEnumerable<(string Path, long Size)> files)
        {
            InitializeComponent();

            foreach (var file in files)
            {
                _files.Add(new TorrentFileItem
                {
                    Path = file.Path,
                    Size = file.Size,
                    IsSelected = true
                });
            }

            FilesListView.ItemsSource = _files;
            UpdateSelectedSize();
        }

        private void UpdateSelectedSize()
        {
            long totalSelectedSize = _files.Where(f => f.IsSelected).Sum(f => f.Size);
            SelectedSizeTextBlock.Text = DownloadItem.FormatBytes(totalSelectedSize);
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectedSize();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var file in _files)
            {
                file.IsSelected = true;
            }
            FilesListView.Items.Refresh();
            UpdateSelectedSize();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var file in _files)
            {
                file.IsSelected = false;
            }
            FilesListView.Items.Refresh();
            UpdateSelectedSize();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            SelectedFiles = _files.Where(f => f.IsSelected).Select(f => f.Path).ToList();
            if (SelectedFiles.Count == 0)
            {
                var result = MessageBox.Show("You have not selected any files to download. Do you want to cancel the download?", "No Files Selected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    DialogResult = false;
                    Close();
                }
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
