#nullable enable
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MouseMaster
{
    public enum ToastPosition
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3,
        Center = 4
    }
    public class ToastNotification : Form
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private Label lblMessage;
        private CancellationTokenSource? _cts;
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
                cp.Parent = IntPtr.Zero;
                return cp;
            }
        }
        public ToastNotification()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 44, 52);
            Size = new Size(220, 50);
            lblMessage = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            Controls.Add(lblMessage);
        }
        public void ShowToast(string message, ToastPosition position, int opacity)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            lblMessage.Text = message;
            Opacity = opacity / 100.0;
            PositionWindow(position);
            if (!Visible)
            {
                Show();
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            else
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            _ = HideAfterDelay(_cts.Token);
        }
        private void PositionWindow(ToastPosition position)
        {
            Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Rectangle wa = screen.WorkingArea;
            int x = position switch
            {
                ToastPosition.TopLeft => wa.Left + 20,
                ToastPosition.TopRight => wa.Right - Width - 20,
                ToastPosition.BottomLeft => wa.Left + 20,
                ToastPosition.BottomRight => wa.Right - Width - 20,
                ToastPosition.Center => wa.Left + (wa.Width - Width) / 2,
                _ => wa.Left + 20
            };
            int y = position switch
            {
                ToastPosition.TopLeft => wa.Top + 20,
                ToastPosition.TopRight => wa.Top + 20,
                ToastPosition.BottomLeft => wa.Bottom - Height - 20,
                ToastPosition.BottomRight => wa.Bottom - Height - 20,
                ToastPosition.Center => wa.Top + 20,
                _ => wa.Top + 20
            };
            Location = new Point(x, y);
        }
        private async Task HideAfterDelay(CancellationToken token)
        {
            try
            {
                await Task.Delay(1500, token);
                for (double d = Opacity; d >= 0; d -= 0.1)
                {
                    token.ThrowIfCancellationRequested();
                    Opacity = Math.Max(0, d);
                    await Task.Delay(30, token);
                }
                if (!token.IsCancellationRequested)
                {
                    Hide();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            base.OnFormClosing(e);
        }
    }
    public class ToastManager
    {
        private ToastPosition _position;
        private int _opacity;
        private bool _enabled;
        private ToastNotification? _toast;
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public ToastPosition Position { get => _position; set => _position = value; }
        public int Opacity { get => _opacity; set => _opacity = Math.Max(10, Math.Min(100, value)); }
        public void Notify(string message)
        {
            if (!_enabled) return;
            try
            {
                if (_toast == null || _toast.IsDisposed)
                {
                    _toast = new ToastNotification();
                }
                _toast.ShowToast(message, _position, _opacity);
            }
            catch { }
        }
    }
}