#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MouseMaster
{
    public static class InputSimulator
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_is_mouse(int device);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_send(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);
        [StructLayout(LayoutKind.Sequential)]
        private struct InterceptionMouseStroke
        {
            public ushort state;
            public ushort flags;
            public short rolling;
            public int x, y;
            public uint information;
        }
        private static IntPtr _icContext = IntPtr.Zero;
        private static int _icDevice = 0;
        private static bool _useInterception = false;
        public static bool UseInterception => _useInterception;
        public static bool TryEnableInterception()
        {
            if (_icContext != IntPtr.Zero) { _useInterception = true; return true; }
            try
            {
                _icContext = interception_create_context();
                if (_icContext == IntPtr.Zero) return false;
                for (int d = 11; d <= 20; d++)
                {
                    if (interception_is_mouse(d) != 0)
                    {
                        _icDevice = d;
                        _useInterception = true;
                        return true;
                    }
                }
                interception_destroy_context(_icContext);
                _icContext = IntPtr.Zero;
                return false;
            }
            catch { _icContext = IntPtr.Zero; return false; }
        }
        public static void DisableInterception()
        {
            _useInterception = false;
            if (_icContext != IntPtr.Zero)
            {
                interception_destroy_context(_icContext);
                _icContext = IntPtr.Zero;
            }
        }
        public static void Shutdown() => DisableInterception();
        public static void Move(int x, int y)
        {
            if (_useInterception && _icContext != IntPtr.Zero)
            {
                GetCursorPos(out POINT cur);
                var stroke = new InterceptionMouseStroke { state = 0, flags = 0, x = x - cur.x, y = y - cur.y };
                interception_send(_icContext, _icDevice, ref stroke, 1);
            }
            else SetCursorPos(x, y);
        }
        public static void EventMouse(MouseButtons btn, bool down, int x, int y)
        {
            if (_useInterception && _icContext != IntPtr.Zero)
            {
                ushort state = 0;
                if (btn == MouseButtons.Left) state = (ushort)(down ? 0x001 : 0x002);
                else if (btn == MouseButtons.Right) state = (ushort)(down ? 0x004 : 0x008);
                else if (btn == MouseButtons.Middle) state = (ushort)(down ? 0x010 : 0x020);
                var stroke = new InterceptionMouseStroke { state = state, flags = 0, x = 0, y = 0 };
                interception_send(_icContext, _icDevice, ref stroke, 1);
            }
            else
            {
                int flag = 0;
                if (btn == MouseButtons.Left) flag = down ? 0x02 : 0x04;
                if (btn == MouseButtons.Right) flag = down ? 0x08 : 0x10;
                if (btn == MouseButtons.Middle) flag = down ? 0x20 : 0x40;
                mouse_event(flag, x, y, 0, 0);
            }
        }
    }
}