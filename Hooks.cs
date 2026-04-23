#nullable disable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MouseMaster
{
    public class LowLevelKeyboardHook
    {
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelProc _proc;
        public event EventHandler<Keys> KeyDown;
        public Func<Keys, bool> OnKeyDown;
        public LowLevelKeyboardHook()
        {
            _proc = HookCallback;
        }
        public void Install() => _hookID = SetHook(_proc);
        public void Uninstall() => UnhookWindowsHookEx(_hookID);
        private IntPtr SetHook(LowLevelProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
                return SetWindowsHookEx(13, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == 0x0100)
            {
                Keys k = (Keys)Marshal.ReadInt32(lParam);
                if (OnKeyDown != null && OnKeyDown(k)) return (IntPtr)1;
                KeyDown?.Invoke(this, k);
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
    public class LowLevelMouseHook
    {
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelProc _proc;
        public event Action<MouseButtons> MouseDown;
        public event Action<MouseButtons> MouseUp;
        public volatile bool Suppress;
        public LowLevelMouseHook()
        {
            _proc = HookCallback;
        }
        public void Install() => _hookID = SetHook(_proc);
        public void Uninstall() => UnhookWindowsHookEx(_hookID);
        private IntPtr SetHook(LowLevelProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
                return SetWindowsHookEx(14, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hs = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                if ((hs.flags & 1) == 0 && !Suppress)
                {
                    int msg = (int)wParam;
                    if (msg == 0x0201) MouseDown?.Invoke(MouseButtons.Left);
                    else if (msg == 0x0202) MouseUp?.Invoke(MouseButtons.Left);
                    else if (msg == 0x0204) MouseDown?.Invoke(MouseButtons.Right);
                    else if (msg == 0x0205) MouseUp?.Invoke(MouseButtons.Right);
                    else if (msg == 0x0207) MouseDown?.Invoke(MouseButtons.Middle);
                    else if (msg == 0x0208) MouseUp?.Invoke(MouseButtons.Middle);
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}