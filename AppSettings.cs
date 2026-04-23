using System.Windows.Forms;
namespace MouseMaster
{
    public class AppSettings
    {
        public int HotkeyAutoClick { get; set; } = (int)Keys.F6;
        public int ActivationMode { get; set; } = 0;
        public int HoldDelayMs { get; set; } = 200;
        public int MouseButtonIndex { get; set; } = 0;
        public bool IsManualInterval { get; set; } = true;
        public decimal IntervalSeconds { get; set; } = 0.070M;
        public decimal TargetCPS { get; set; } = 14;
        public int StartDelay { get; set; } = 0;
        public bool Randomize { get; set; } = true;
        public int RandomStrength { get; set; } = 8;
        public bool Jitter { get; set; } = true;
        public int JitterX { get; set; } = 3;
        public int JitterY { get; set; } = 3;
        public bool FixedLocation { get; set; } = false;
        public int FixedX { get; set; } = 0;
        public int FixedY { get; set; } = 0;
        public bool AutoStop { get; set; } = false;
        public int AutoStopLimit { get; set; } = 1000;
        public int InputMethod { get; set; } = 0; // 0 = mouse_event, 1 = interception
    }
}