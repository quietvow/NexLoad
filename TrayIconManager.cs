using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace DOWN_MNG
{
    public class TrayIconManager : IDisposable
    {
        private readonly Window _window;
        private readonly NotifyIcon _notifyIcon;
        private System.Drawing.Icon? _iconHolder;
        private bool _disposed = false;

        public event Action? TrayClicked;
        public event Action? TrayDoubleClicked;
        public event Action? TrayRightClicked;

        public TrayIconManager(Window window)
        {
            _window = window;
            _notifyIcon = new NotifyIcon();

            LoadIcon();

            _notifyIcon.DoubleClick += (s, e) =>
            {
                TrayDoubleClicked?.Invoke();
            };

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    TrayRightClicked?.Invoke();
                }
                else if (e.Button == MouseButtons.Left)
                {
                    TrayClicked?.Invoke();
                }
            };
        }

        public void Initialize()
        {
            // Maintained for backward compatibility
        }

        private void LoadIcon()
        {
            try
            {
                var smallSize = SystemInformation.SmallIconSize;

                // 1. Try loading directly from WPF embedded pack resource
                var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute));
                if (sri?.Stream != null)
                {
                    using var stream = sri.Stream;
                    try
                    {
                        _iconHolder = new System.Drawing.Icon(stream, smallSize);
                    }
                    catch
                    {
                        _iconHolder = new System.Drawing.Icon(stream);
                    }
                    _notifyIcon.Icon = _iconHolder;
                    return;
                }
            }
            catch { }

            try
            {
                var smallSize = SystemInformation.SmallIconSize;

                // 2. Try loading from physical file in application directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string directIco = Path.Combine(baseDir, "Resources", "app.ico");
                if (File.Exists(directIco))
                {
                    try
                    {
                        _iconHolder = new System.Drawing.Icon(directIco, smallSize);
                    }
                    catch
                    {
                        _iconHolder = new System.Drawing.Icon(directIco);
                    }
                    _notifyIcon.Icon = _iconHolder;
                    return;
                }
            }
            catch { }

            try
            {
                // 3. Try extracting from running process executable
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    _iconHolder = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (_iconHolder != null)
                    {
                        _notifyIcon.Icon = _iconHolder;
                        return;
                    }
                }
            }
            catch { }

            // 4. Fallback: System standard application icon
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        public void Show(string tip = "NexLoad Pro — Active")
        {
            if (_disposed) return;
            if (_notifyIcon.Icon == null) LoadIcon();

            _notifyIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            _notifyIcon.Visible = true;
        }

        public void ShowBalloon(string title, string message)
        {
            if (_disposed) return;
            if (_notifyIcon.Icon == null) LoadIcon();

            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(
                3500,
                title.Length > 63 ? title.Substring(0, 63) : title,
                message.Length > 255 ? message.Substring(0, 255) : message,
                ToolTipIcon.Info
            );
        }

        public void Hide()
        {
            if (!_disposed)
            {
                _notifyIcon.Visible = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch { }

            try
            {
                _iconHolder?.Dispose();
                _iconHolder = null;
            }
            catch { }
        }
    }
}
