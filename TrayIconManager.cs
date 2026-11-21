using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DOWN_MNG
{
    public class TrayIconManager : IDisposable
    {
        private const int WM_USER = 0x0400;
        public const int WM_TRAYICON = WM_USER + 1024;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_INFO = 0x00000010;

        private const uint NIIF_INFO = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        private readonly Window _window;
        private IntPtr _hWnd;
        private bool _isAdded = false;
        private HwndSource? _hwndSource;

        public event Action? TrayDoubleClicked;
        public event Action? TrayRightClicked;

        public TrayIconManager(Window window)
        {
            _window = window;
        }

        public void Initialize()
        {
            var helper = new WindowInteropHelper(_window);
            _hWnd = helper.EnsureHandle();

            _hwndSource = HwndSource.FromHwnd(_hWnd);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32();
                if (mouseMsg == WM_LBUTTONDBLCLK)
                {
                    TrayDoubleClicked?.Invoke();
                    handled = true;
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    TrayRightClicked?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Show(string tip = "NexLoad Pro — Active")
        {
            if (_hWnd == IntPtr.Zero) Initialize();

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = LoadApplicationIcon(),
                szTip = tip.Length > 127 ? tip.Substring(0, 127) : tip
            };

            if (!_isAdded)
            {
                _isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
            }
            else
            {
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
        }

        public void ShowBalloon(string title, string message)
        {
            if (_hWnd == IntPtr.Zero) Initialize();

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hWnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO,
                uCallbackMessage = WM_TRAYICON,
                hIcon = LoadApplicationIcon(),
                szTip = "NexLoad Pro",
                szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title,
                szInfo = message.Length > 255 ? message.Substring(0, 255) : message,
                dwInfoFlags = NIIF_INFO
            };

            if (!_isAdded)
            {
                _isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
            }
            else
            {
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
        }

        public void Hide()
        {
            if (_isAdded && _hWnd != IntPtr.Zero)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                    hWnd = _hWnd,
                    uID = 1001
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _isAdded = false;
            }
        }

        private IntPtr LoadApplicationIcon()
        {
            try
            {
                IntPtr hIcon = IntPtr.Zero;
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (sysIcon != null)
                    {
                        return sysIcon.Handle;
                    }
                }
            }
            catch { }

            // Fallback: standard application icon (32512 = IDI_APPLICATION)
            return LoadIcon(IntPtr.Zero, new IntPtr(32512));
        }

        public void Dispose()
        {
            Hide();
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
        }
    }
}
