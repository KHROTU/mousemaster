#nullable disable
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MouseMaster
{
    public class HotkeyInputDialog : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private readonly Color C_Background = Color.FromArgb(45, 48, 55);
        private readonly Color C_Accent = Color.FromArgb(100, 149, 237);
        private readonly Color C_Text = Color.FromArgb(230, 230, 230);
        private readonly Color C_TextDim = Color.FromArgb(160, 160, 160);
        private readonly Color C_Pressed = Color.FromArgb(80, 120, 200);
        private Label lblTitle;
        private Label lblKeyName;
        private Label lblHint;
        private Keys? capturedKey;
        public Keys? CapturedKey => capturedKey;
        public HotkeyInputDialog()
        {
            InitializeComponent();
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(this.Handle, 19, ref dark, sizeof(int));
        }
        private void InitializeComponent()
        {
            this.Text = "Set Hotkey";
            this.Size = new Size(450, 220);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = C_Background;
            this.ForeColor = C_Text;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            this.ShowInTaskbar = false;
            this.TopMost = true;
            Panel container = new Panel { Dock = DockStyle.Fill, BackColor = C_Background, Padding = new Padding(30) };
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = C_Background };
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            lblTitle = new Label { Text = "Press any key to set the hotkey", Font = new Font("Segoe UI", 12f, FontStyle.Regular), ForeColor = C_Text, AutoSize = false, Size = new Size(350, 30), TextAlign = ContentAlignment.MiddleCenter };
            lblKeyName = new Label { Text = "...", Font = new Font("Segoe UI", 32f, FontStyle.Bold), ForeColor = C_Accent, AutoSize = false, Size = new Size(350, 50), TextAlign = ContentAlignment.MiddleCenter };
            lblHint = new Label { Text = "Press ESC to cancel", Font = new Font("Segoe UI", 10f), ForeColor = C_TextDim, AutoSize = false, Size = new Size(350, 25), TextAlign = ContentAlignment.MiddleCenter };
            table.Controls.Add(lblTitle, 0, 0);
            table.Controls.Add(lblKeyName, 0, 1);
            table.Controls.Add(lblHint, 0, 2);
            container.Controls.Add(table);
            this.Controls.Add(container);
            this.KeyDown += OnKeyDown;
            this.Shown += (s, e) => this.ActiveControl = null;
        }
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape)
            {
                capturedKey = null;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }
        public void OnKeyDownInternal(Keys k)
        {
            if (capturedKey.HasValue) return;
            this.Invoke((MethodInvoker)delegate
            {
                lblKeyName.Text = FormatKeyName(k);
                lblKeyName.ForeColor = C_Pressed;
                capturedKey = k;
                this.DialogResult = DialogResult.OK;
                this.Close();
            });
        }
        private string FormatKeyName(Keys k)
        {
            string s = k.ToString();
            if (s.StartsWith("Oem3")) return "~";
            if (s.StartsWith("D") && s.Length == 2 && char.IsDigit(s[1])) return s[1].ToString();
            return s;
        }
    }
}