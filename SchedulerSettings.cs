using System;

namespace DOWN_MNG
{
    public class SchedulerSettings
    {
        public bool Enabled { get; set; } = false;
        public string StartTime { get; set; } = "02:00";
        public string StopTime { get; set; } = "05:00";
        public bool StopOnTime { get; set; } = false;
        public string CompletionAction { get; set; } = "None"; // None, CloseApp, ShutdownPC
    }
}
