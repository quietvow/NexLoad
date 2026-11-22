using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DOWN_MNG
{
    public static class SystemPowerActions
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LockWorkStation();

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        public static void ExecutePowerAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action) || action.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "shutdown" or "shutdownpc":
                        // Initiates a 30-second graceful shutdown so user can cancel if desired
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown",
                            Arguments = "/s /t 30 /c \"NexLoad Pro completed all queue downloads.\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        break;

                    case "sleep" or "sleeppc":
                        // Standby / Sleep
                        SetSuspendState(false, true, true);
                        break;

                    case "hibernate" or "hibernatepc":
                        // Hibernate
                        SetSuspendState(true, true, true);
                        break;

                    case "lock" or "lockpc":
                        // Lock workstation
                        LockWorkStation();
                        break;

                    case "closeapp":
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            System.Windows.Application.Current.Shutdown();
                        });
                        break;
                }
            }
            catch { }
        }
    }
}
