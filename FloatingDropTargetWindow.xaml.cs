using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DOWN_MNG
{
    public partial class FloatingDropTargetWindow : Window
    {
        private readonly MainWindow _mainWindow;

        public FloatingDropTargetWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // Position initially in top right of primary screen
            var screenArea = SystemParameters.WorkArea;
            Left = screenArea.Right - Width - 30;
            Top = screenArea.Top + 40;
        }

        public void UpdateStatus(double speedBytesPerSec, int activeCount, double overallProgress)
        {
            Dispatcher.Invoke(() =>
            {
                SpeedTextBlock.Text = speedBytesPerSec > 0 ? $"{DownloadItem.FormatBytes((long)speedBytesPerSec)}/s" : "0 KB/s";
                QueueProgressBar.Value = Math.Clamp(overallProgress, 0, 100);

                if (activeCount > 0)
                {
                    StatusTextBlock.Text = $"{activeCount} active • {overallProgress:F0}%";
                    StatusTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#38BDF8")!;
                }
                else
                {
                    StatusTextBlock.Text = "Drop URL / torrent here";
                    StatusTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#94A3B8")!;
                }
            });
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _mainWindow.RestoreFromTray();
        }

        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var cm = new ContextMenu();

            var openItem = new MenuItem { Header = "Open NexLoad Pro", FontWeight = FontWeights.Bold };
            openItem.Click += (s, ev) => _mainWindow.RestoreFromTray();
            cm.Items.Add(openItem);

            cm.Items.Add(new Separator());

            var startAll = new MenuItem { Header = "Resume All Downloads" };
            startAll.Click += (s, ev) => _mainWindow.StartAll_Click(s, ev);
            cm.Items.Add(startAll);

            var pauseAll = new MenuItem { Header = "Pause All Downloads" };
            pauseAll.Click += (s, ev) => _mainWindow.PauseAll_Click(s, ev);
            cm.Items.Add(pauseAll);

            cm.Items.Add(new Separator());

            var hideItem = new MenuItem { Header = "Hide Mini Widget" };
            hideItem.Click += (s, ev) =>
            {
                Hide();
                App.Settings.ShowFloatingWidget = false;
                App.Settings.Save();
            };
            cm.Items.Add(hideItem);

            cm.PlacementTarget = this;
            cm.IsOpen = true;
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[]? files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        foreach (var file in files)
                        {
                            if (file.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                            {
                                var item = new DownloadItem
                                {
                                    Url = file,
                                    SavedPath = Path.Combine(App.Settings.DefaultDownloadFolder, Path.GetFileNameWithoutExtension(file)),
                                    Filename = Path.GetFileName(file),
                                    TotalSizeBytes = -1,
                                    Category = "Torrent",
                                    Protocol = "Torrent",
                                    Platform = "Torrent",
                                    Status = "Queued",
                                    AddedDate = DateTime.Now
                                };
                                _mainWindow.AddDownloadItemDirect(item, true);
                            }
                        }
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.Text))
                {
                    string? text = e.Data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string trimmed = text.Trim();
                        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        {
                            var dialog = new NewDownloadWindow(trimmed) { Owner = _mainWindow };
                            if (dialog.ShowDialog() == true && dialog.ResultItem != null)
                            {
                                _mainWindow.AddDownloadItemDirect(dialog.ResultItem, true);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
