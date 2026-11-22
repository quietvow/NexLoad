using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DOWN_MNG;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new AppSettings();
    public static bool StartInBackground { get; private set; } = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 0. Check background startup flag
        if (e.Args != null && Array.Exists(e.Args, a => 
            a.Equals("--background", StringComparison.OrdinalIgnoreCase) || 
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || 
            a.Equals("-b", StringComparison.OrdinalIgnoreCase)))
        {
            StartInBackground = true;
        }

        // 1. Dispatcher Unhandled Exceptions (UI Thread)
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. Background Thread Exceptions
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. Task Scheduler Unobserved Exceptions (async/await background tasks)
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // 4. Load persisted user settings
        Settings = AppSettings.Load();

        // 5. Apply saved theme & language
        ApplyTheme(Settings.Theme);
        LocalizationManager.SwitchLanguage(Settings.Language);

        base.OnStartup(e);
    }

    public static void ApplyTheme(string themeName)
    {
        try
        {
            string themeFile = themeName == "Dark" ? "Themes/DarkStyles.xaml" : "Themes/LightStyles.xaml";
            var dictUri = new Uri(themeFile, UriKind.Relative);
            var newThemeDict = new ResourceDictionary { Source = dictUri };

            if (Application.Current != null)
            {
                var merged = Application.Current.Resources.MergedDictionaries;
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    var d = merged[i];
                    if (d.Source != null && (d.Source.OriginalString.Contains("DarkStyles.xaml") || d.Source.OriginalString.Contains("LightStyles.xaml")))
                    {
                        merged.RemoveAt(i);
                    }
                }
                merged.Add(newThemeDict);
                Settings.Theme = themeName;

                // Propagate theme directly to all active open windows so Popups, Menus, and controls adapt immediately
                foreach (Window win in Application.Current.Windows)
                {
                    try
                    {
                        win.Resources.MergedDictionaries.Clear();
                        win.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = dictUri });
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogAndShowException(e.Exception, "UI Thread Unhandled Exception");
        e.Handled = true; // Prevent application from crashing/auto-closing!
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogAndShowException(ex, "Background Domain Unhandled Exception");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogAndShowException(e.Exception, "Task Scheduler Unobserved Exception");
        e.SetObserved(); // Prevent application from crashing/auto-closing!
    }

    private void LogAndShowException(Exception ex, string context)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logFolder = Path.Combine(appData, "NexLoad");
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }
            string logFile = Path.Combine(logFolder, "error_log.txt");
            
            string logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Context: {context}\r\nException: {ex.GetType().Name}\r\nMessage: {ex.Message}\r\nStack Trace:\r\n{ex.StackTrace}\r\n\r\nInner Exception: {ex.InnerException?.Message}\r\n--------------------------------------------------\r\n";
            File.AppendAllText(logFile, logText);
        }
        catch { }

        // Show a helpful error to the user on the UI thread
        try
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"An unexpected error occurred in the application:\n\n" +
                    $"[{context}] {ex.Message}\n\n" +
                    $"The error has been logged. The application will attempt to continue running.", 
                    "Application Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            });
        }
        catch { }
    }
}
