#nullable disable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
namespace MouseMaster
{
    public partial class Form1 : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private readonly Color C_Background = Color.FromArgb(32, 33, 36);
        private readonly Color C_Surface = Color.FromArgb(45, 48, 55);
        private readonly Color C_Input = Color.FromArgb(60, 64, 72);
        private readonly Color C_Accent = Color.FromArgb(100, 149, 237);
        private readonly Color C_Text = Color.FromArgb(230, 230, 230);
        private readonly Color C_TextDim = Color.FromArgb(160, 160, 160);
        private AppSettings settings = new AppSettings();
        private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseMaster", "settings.xml");
        private static LowLevelKeyboardHook _keyboardHook;
        private static LowLevelMouseHook _mouseHook;
        private CancellationTokenSource autoClickToken;
        private CancellationTokenSource holdTimerToken;
        private bool isAutoClicking = false;
        private bool isHoldArmed = false;
        private bool _isApplyingSettings = false;
        private ToastManager _toastManager;
        private Panel pnlSidebar, pnlContent;
        private FlowLayoutPanel viewClicker, viewSettings;
        private Button btnMenuClicker, btnMenuSettings;
        private ComboBox cmbClickButton, cmbActMode, cmbInputMethod;
        private NumericUpDown numInterval, numCPS, numStartDelay, numRandStrength, numJitX, numJitY, numFixX, numFixY, numStopLim, numHoldDelay;
        private Label lblInputMethodStatus;
        private RadioButton rbManual, rbCPS;
        private Button btnSetAutoClickHotkey;
        private Label lblAutoClickStatus;
        private Label lblHotkeyListening;
        private CheckBox chkRandom, chkJitter, chkFixed, chkStop;
        private CheckBox chkNotification;
        private ComboBox cmbNotificationPosition;
        private NumericUpDown numNotificationOpacity;
        public Form1()
        {
            InitializeComponent();
            this.Controls.Clear();
            LoadSettings();
            this.Text = "MouseMaster v0.2.0";
            this.Size = new Size(680, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = C_Background;
            this.ForeColor = C_Text;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            this.ShowIcon = false;
            this.DoubleBuffered = true;
            InitializeHooks();
            _toastManager = new ToastManager();
            SetupUI();
            ApplySettingsToUI();
            SwitchView(viewClicker, btnMenuClicker);
            this.FormClosing += (s, e) => { SaveUItoSettings(); SaveSettings(); Shutdown(); };
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(this.Handle, 19, ref dark, sizeof(int));
        }
        private async Task RunAutoClicker(CancellationToken token)
        {
            try
            {
                if (settings.StartDelay > 0) await Task.Delay(settings.StartDelay, token);
                var engine = new ClickerEngine(settings);
                var jitterEngine = new JitterEngine(settings);
                int clickCount = 0;
                while (!token.IsCancellationRequested)
                {
                    var sample = engine.Next();
                    int targetX = settings.FixedLocation ? settings.FixedX : Cursor.Position.X;
                    int targetY = settings.FixedLocation ? settings.FixedY : Cursor.Position.Y;
                    var (jx, jy) = jitterEngine.AdvanceAndGet();
                    if (jx != 0 || jy != 0)
                    {
                        targetX += jx;
                        targetY += jy;
                        InputSimulator.Move(targetX, targetY);
                    }
                    long loopStart = Stopwatch.GetTimestamp();
                    if (InputSimulator.UseInterception) _mouseHook.Suppress = true;
                    InputSimulator.EventMouse(GetMouseButton(settings.MouseButtonIndex), true, targetX, targetY);
                    PreciseWait(sample.HoldMs, token);
                    InputSimulator.EventMouse(GetMouseButton(settings.MouseButtonIndex), false, targetX, targetY);
                    if (InputSimulator.UseInterception) _mouseHook.Suppress = false;
                    clickCount++;
                    if (settings.AutoStop && clickCount >= settings.AutoStopLimit)
                    {
                        this.Invoke((MethodInvoker)delegate { if (settings.ActivationMode == 0) ToggleAutoClick(); else StopHoldClicker(); });
                        break;
                    }
                    double elapsedMs = (Stopwatch.GetTimestamp() - loopStart) * 1000.0 / Stopwatch.Frequency;
                    PreciseWait(sample.IntervalMs - elapsedMs, token);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                isAutoClicking = false;
                Color c = (settings.ActivationMode == 1 && isHoldArmed) ? C_Accent : Color.IndianRed;
                string txt = (settings.ActivationMode == 1 && isHoldArmed) ? "Status: ARMED" : "Status: STOPPED";
                UpdateStatus(lblAutoClickStatus, txt, c);
            }
        }
        private void PreciseWait(double ms, CancellationToken token)
        {
            if (ms <= 0) return;
            long ticksNeeded = (long)(ms * Stopwatch.Frequency / 1000.0);
            long startTicks = Stopwatch.GetTimestamp();
            if (ms > 20) Thread.Sleep((int)(ms - 20));
            while (Stopwatch.GetTimestamp() - startTicks < ticksNeeded)
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException();
                Thread.SpinWait(10);
            }
        }
        private MouseButtons GetMouseButton(int index) => index == 0 ? MouseButtons.Left : index == 1 ? MouseButtons.Right : MouseButtons.Middle;
        private void SetupUI()
        {
            pnlSidebar = new Panel { Dock = DockStyle.Left, Width = 180, BackColor = C_Surface };
            pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = C_Background };
            btnMenuSettings = CreateMenuButton("Settings", () => SwitchView(viewSettings, btnMenuSettings));
            btnMenuClicker = CreateMenuButton("Auto-click", () => SwitchView(viewClicker, btnMenuClicker));
            pnlSidebar.Controls.Add(btnMenuSettings);
            pnlSidebar.Controls.Add(btnMenuClicker);
            viewClicker = BuildAutoClickView();
            viewSettings = BuildSettingsView();
            pnlContent.Controls.Add(viewClicker);
            pnlContent.Controls.Add(viewSettings);
            this.Controls.Add(pnlContent);
            this.Controls.Add(pnlSidebar);
        }
        private Button CreateMenuButton(string text, Action onClick)
        {
            Button b = new Button { Text = text, Dock = DockStyle.Top, Height = 60, FlatStyle = FlatStyle.Flat, BackColor = C_Surface, ForeColor = C_TextDim, Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(25, 0, 0, 0) };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = b.BackColor;
            b.FlatAppearance.MouseDownBackColor = b.BackColor;
            b.Click += (s, e) => onClick();
            return b;
        }
        private void SwitchView(Control view, Button btn)
        {
            foreach (Control c in pnlContent.Controls) c.Visible = false;
            view.Visible = true;
            btnMenuClicker.ForeColor = C_TextDim;
            btnMenuSettings.ForeColor = C_TextDim;
            btn.ForeColor = C_Accent;
        }
        private FlowLayoutPanel CreateMainPanel() => new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(30), BackColor = C_Background };
        private FlowLayoutPanel CreateCard(string title)
        {
            FlowLayoutPanel card = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, MinimumSize = new Size(420, 0), BackColor = C_Surface, Margin = new Padding(0, 0, 0, 25), Padding = new Padding(25) };
            card.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = C_Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 15) });
            return card;
        }
        private Control MakeRow(string text, Control ctrl)
        {
            TableLayoutPanel row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 5, 0, 5) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(new Label { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = C_TextDim, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 15, 0) }, 0, 0);
            row.Controls.Add(ctrl, 1, 0);
            return row;
        }
        private FlowLayoutPanel BuildAutoClickView()
        {
            FlowLayoutPanel main = CreateMainPanel();
            FlowLayoutPanel pnlStatus = CreateCard("Current Status");
            lblAutoClickStatus = new Label { Text = "Status: STOPPED", ForeColor = Color.IndianRed, Font = new Font("Segoe UI", 12f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            pnlStatus.Controls.Add(lblAutoClickStatus);
            main.Controls.Add(pnlStatus);
            FlowLayoutPanel pnlSelect = CreateCard("Target Button");
            cmbClickButton = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, BackColor = C_Input, ForeColor = C_Text, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f) };
            cmbClickButton.Items.AddRange(new object[] { "Left Click", "Right Click", "Middle Click" });
            cmbClickButton.SelectedIndexChanged += (s, e) => { SaveUItoSettings(); DisarmAndStop(); };
            pnlSelect.Controls.Add(cmbClickButton);
            main.Controls.Add(pnlSelect);
            FlowLayoutPanel pnlSpeed = CreateCard("Click Speed");
            TableLayoutPanel table = new TableLayoutPanel { RowCount = 2, ColumnCount = 2, AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rbManual = new RadioButton { Text = "Manual Interval (sec)", Checked = true, AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 5, 15, 5) };
            numInterval = CreateNum(0.001M, 9999M, 0.1M, 3);
            rbCPS = new RadioButton { Text = "Target CPS", AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 5, 15, 5) };
            numCPS = CreateNum(1, 1000, 10, 0);
            rbManual.CheckedChanged += (s, e) => { numInterval.Enabled = rbManual.Checked; numCPS.Enabled = rbCPS.Checked; SaveUItoSettings(); };
            rbCPS.CheckedChanged += (s, e) => { numInterval.Enabled = rbManual.Checked; numCPS.Enabled = rbCPS.Checked; SaveUItoSettings(); };
            table.Controls.Add(rbManual, 0, 0); table.Controls.Add(numInterval, 1, 0);
            table.Controls.Add(rbCPS, 0, 1); table.Controls.Add(numCPS, 1, 1);
            pnlSpeed.Controls.Add(table);
            main.Controls.Add(pnlSpeed);
            return main;
        }
        private FlowLayoutPanel BuildSettingsView()
        {
            FlowLayoutPanel main = CreateMainPanel();
            FlowLayoutPanel pnlAct = CreateCard("Activation");
            cmbActMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, BackColor = C_Input, ForeColor = C_Text, FlatStyle = FlatStyle.Flat };
            cmbActMode.Items.AddRange(new object[] { "Toggle", "Hold" });
            cmbActMode.SelectedIndexChanged += (s, e) => { numHoldDelay.Enabled = cmbActMode.SelectedIndex == 1; SaveUItoSettings(); DisarmAndStop(); };
            numHoldDelay = CreateNum(0, 10000, 200);
            btnSetAutoClickHotkey = CreateActionButton("Set Hotkey", (s, e) => AssignHotkey(k => { settings.HotkeyAutoClick = (int)k; btnSetAutoClickHotkey.Text = $"Hotkey: {FormatKeyName(k)}"; }));
            pnlAct.Controls.Add(MakeRow("Mode:", cmbActMode));
            pnlAct.Controls.Add(MakeRow("Hold Delay (ms):", numHoldDelay));
            pnlAct.Controls.Add(btnSetAutoClickHotkey);
            main.Controls.Add(pnlAct);
            FlowLayoutPanel pnlGen = CreateCard("General");
            numStartDelay = CreateNum(0, 10000, 0);
            numStartDelay.ValueChanged += (s, e) => SaveUItoSettings();
            pnlGen.Controls.Add(MakeRow("Start Delay (ms):", numStartDelay));
            main.Controls.Add(pnlGen);
            FlowLayoutPanel pnlRand = CreateCard("Humanization");
            chkRandom = new CheckBox { Text = "Enable Randomization", AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 5, 0, 15) };
            numRandStrength = CreateNum(1, 200, 8);
            chkRandom.CheckedChanged += (s, e) => { numRandStrength.Enabled = chkRandom.Checked; SaveUItoSettings(); };
            chkJitter = new CheckBox { Text = "Enable Cursor Jitter", AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 15, 0, 15) };
            numJitX = CreateNum(0, 100, 3);
            numJitY = CreateNum(0, 100, 3);
            chkJitter.CheckedChanged += (s, e) => { numJitX.Enabled = chkJitter.Checked; numJitY.Enabled = chkJitter.Checked; SaveUItoSettings(); };
            pnlRand.Controls.Add(chkRandom); pnlRand.Controls.Add(MakeRow("Random Factor (%):", numRandStrength));
            pnlRand.Controls.Add(chkJitter); pnlRand.Controls.Add(MakeRow("Jitter Width (px):", numJitX)); pnlRand.Controls.Add(MakeRow("Jitter Height (px):", numJitY));
            main.Controls.Add(pnlRand);
            FlowLayoutPanel pnlCon = CreateCard("Constraints");
            chkFixed = new CheckBox { Text = "Lock Cursor Position", AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 5, 0, 15) };
            numFixX = CreateNum(0, 10000, 0); numFixY = CreateNum(0, 10000, 0);
            chkFixed.CheckedChanged += (s, e) => { numFixX.Enabled = chkFixed.Checked; numFixY.Enabled = chkFixed.Checked; SaveUItoSettings(); };
            chkStop = new CheckBox { Text = "Limit Total Clicks", AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 15, 0, 15) };
            numStopLim = CreateNum(1, 1000000, 1000);
            chkStop.CheckedChanged += (s, e) => { numStopLim.Enabled = chkStop.Checked; SaveUItoSettings(); };
            pnlCon.Controls.Add(chkFixed); pnlCon.Controls.Add(MakeRow("X Coordinate:", numFixX)); pnlCon.Controls.Add(MakeRow("Y Coordinate:", numFixY));
            pnlCon.Controls.Add(chkStop); pnlCon.Controls.Add(MakeRow("Maximum Clicks:", numStopLim));
            main.Controls.Add(pnlCon);
            FlowLayoutPanel pnlInput = CreateCard("Input Method");
            cmbInputMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, BackColor = C_Input, ForeColor = C_Text, FlatStyle = FlatStyle.Flat };
            cmbInputMethod.Items.AddRange(new object[] { "Default", "Interception" });
            lblInputMethodStatus = new Label { Text = "", AutoSize = true, ForeColor = Color.IndianRed, Margin = new Padding(0, 5, 0, 0), Visible = false };
            cmbInputMethod.SelectedIndexChanged += (s, e) => { SaveUItoSettings(); ApplyInputMethod(); };
            pnlInput.Controls.Add(MakeRow("Backend:", cmbInputMethod));
            pnlInput.Controls.Add(lblInputMethodStatus);
            main.Controls.Add(pnlInput);
            FlowLayoutPanel pnlNotif = CreateCard("Notifications");
            chkNotification = new CheckBox { Text = "Enable Toast Notifications", AutoSize = true, ForeColor = C_Text, Margin = new Padding(0, 5, 0, 15) };
            chkNotification.CheckedChanged += (s, e) => { cmbNotificationPosition.Enabled = chkNotification.Checked; numNotificationOpacity.Enabled = chkNotification.Checked; SaveUItoSettings(); };
            cmbNotificationPosition = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, BackColor = C_Input, ForeColor = C_Text, FlatStyle = FlatStyle.Flat };
            cmbNotificationPosition.Items.AddRange(new object[] { "Top Left", "Top Right", "Bottom Left", "Bottom Right", "Center" });
            cmbNotificationPosition.SelectedIndexChanged += (s, e) => SaveUItoSettings();
            numNotificationOpacity = CreateNum(10, 100, 100, 0);
            numNotificationOpacity.ValueChanged += (s, e) => SaveUItoSettings();
            pnlNotif.Controls.Add(chkNotification);
            pnlNotif.Controls.Add(MakeRow("Position:", cmbNotificationPosition));
            pnlNotif.Controls.Add(MakeRow("Opacity (%):", numNotificationOpacity));
            main.Controls.Add(pnlNotif);
            return main;
        }
        private void ApplyInputMethod()
        {
            lblInputMethodStatus.Visible = false;
            if (settings.InputMethod == 1)
            {
                if (InputSimulator.TryEnableInterception())
                {
                    // interception works
                }
                else
                {
                    lblInputMethodStatus.Text = "Please install Interception drivers to use it.";
                    lblInputMethodStatus.Visible = true;
                    cmbInputMethod.SelectedIndex = 0;
                    settings.InputMethod = 0;
                }
            }
            else
            {
                InputSimulator.DisableInterception();
            }
        }
        private void ToggleAutoClick()
        {
            this.Invoke((MethodInvoker)SaveUItoSettings);
            if (isAutoClicking)
            {
                autoClickToken?.Cancel();
                isAutoClicking = false;
                UpdateStatus(lblAutoClickStatus, "Status: STOPPED", Color.IndianRed);
                _toastManager.Notify("Auto-click OFF");
            }
            else
            {
                isAutoClicking = true;
                autoClickToken = new CancellationTokenSource();
                UpdateStatus(lblAutoClickStatus, "Status: RUNNING", Color.LimeGreen);
                _toastManager.Notify("Auto-click ON");
                Task.Run(() => RunAutoClicker(autoClickToken.Token));
            }
        }
        private void StopHoldClicker()
        {
            holdTimerToken?.Cancel();
            if (isAutoClicking)
            {
                autoClickToken?.Cancel();
                isAutoClicking = false;
                _toastManager.Notify("Auto-click OFF");
            }
            if (settings.ActivationMode == 1 && isHoldArmed) UpdateStatus(lblAutoClickStatus, "Status: ARMED", C_Accent);
        }
        private void DisarmAndStop()
        {
            isHoldArmed = false;
            holdTimerToken?.Cancel();
            if (isAutoClicking)
            {
                autoClickToken?.Cancel();
                isAutoClicking = false;
            }
            UpdateStatus(lblAutoClickStatus, "Status: STOPPED", Color.IndianRed);
        }
        private void OnGlobalKeyDown(object sender, Keys key)
        {
            if (key == (Keys)settings.HotkeyAutoClick)
            {
                if (settings.ActivationMode == 0) ToggleAutoClick();
                else
                {
                    isHoldArmed = !isHoldArmed;
                    if (!isHoldArmed) { DisarmAndStop(); _toastManager.Notify("Armed OFF"); }
                    else { UpdateStatus(lblAutoClickStatus, "Status: ARMED", C_Accent); _toastManager.Notify("Armed ON"); }
                }
            }
        }
        private async void OnMouseDown(MouseButtons btn)
        {
            if (settings.ActivationMode == 1 && isHoldArmed && btn == GetMouseButton(settings.MouseButtonIndex))
            {
                holdTimerToken = new CancellationTokenSource();
                try
                {
                    await Task.Delay(settings.HoldDelayMs, holdTimerToken.Token);
                    if (!holdTimerToken.IsCancellationRequested && !isAutoClicking)
                    {
                        this.Invoke((MethodInvoker)SaveUItoSettings);
                        isAutoClicking = true;
                        autoClickToken = new CancellationTokenSource();
                        UpdateStatus(lblAutoClickStatus, "Status: RUNNING", Color.LimeGreen);
                        _ = Task.Run(() => RunAutoClicker(autoClickToken.Token));
                    }
                }
                catch (TaskCanceledException) { }
            }
        }
        private void OnMouseUp(MouseButtons btn) { if (settings.ActivationMode == 1 && isHoldArmed && btn == GetMouseButton(settings.MouseButtonIndex)) StopHoldClicker(); }
        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            try
            {
                XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                using StreamReader sr = new StreamReader(settingsPath);
                settings = (AppSettings)xs.Deserialize(sr);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load settings:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                using StreamWriter sw = new StreamWriter(settingsPath);
                xs.Serialize(sw, settings);
            }
            catch { }
        }
        private void SaveUItoSettings()
        {
            if (_isApplyingSettings || cmbClickButton == null) return;
            settings.MouseButtonIndex = cmbClickButton.SelectedIndex;
            settings.IsManualInterval = rbManual.Checked;
            settings.IntervalSeconds = numInterval.Value;
            settings.TargetCPS = numCPS.Value;
            settings.StartDelay = (int)numStartDelay.Value;
            settings.Randomize = chkRandom.Checked;
            settings.RandomStrength = (int)numRandStrength.Value;
            settings.Jitter = chkJitter.Checked;
            settings.JitterX = (int)numJitX.Value;
            settings.JitterY = (int)numJitY.Value;
            settings.FixedLocation = chkFixed.Checked;
            settings.FixedX = (int)numFixX.Value;
            settings.FixedY = (int)numFixY.Value;
            settings.AutoStop = chkStop.Checked;
            settings.AutoStopLimit = (int)numStopLim.Value;
            settings.ActivationMode = cmbActMode.SelectedIndex;
            settings.HoldDelayMs = (int)numHoldDelay.Value;
            settings.InputMethod = cmbInputMethod.SelectedIndex;
            settings.NotificationEnabled = chkNotification.Checked;
            settings.NotificationPosition = cmbNotificationPosition.SelectedIndex;
            settings.NotificationOpacity = (int)numNotificationOpacity.Value;
            UpdateToastManager();
        }
        private void ApplySettingsToUI()
        {
            _isApplyingSettings = true;
            try
            {
                cmbClickButton.SelectedIndex = Math.Max(0, Math.Min(2, settings.MouseButtonIndex));
                rbManual.Checked = settings.IsManualInterval;
                rbCPS.Checked = !settings.IsManualInterval;
                numInterval.Value = settings.IntervalSeconds;
                numInterval.Enabled = settings.IsManualInterval;
                numCPS.Value = settings.TargetCPS;
                numCPS.Enabled = !settings.IsManualInterval;
                btnSetAutoClickHotkey.Text = $"Hotkey: {FormatKeyName((Keys)settings.HotkeyAutoClick)}";
                numStartDelay.Value = settings.StartDelay;
                chkRandom.Checked = settings.Randomize;
                numRandStrength.Value = settings.RandomStrength;
                numRandStrength.Enabled = settings.Randomize;
                chkJitter.Checked = settings.Jitter;
                numJitX.Value = settings.JitterX;
                numJitY.Value = settings.JitterY;
                numJitX.Enabled = settings.Jitter;
                numJitY.Enabled = settings.Jitter;
                chkFixed.Checked = settings.FixedLocation;
                numFixX.Value = settings.FixedX;
                numFixY.Value = settings.FixedY;
                numFixX.Enabled = settings.FixedLocation;
                numFixY.Enabled = settings.FixedLocation;
                chkStop.Checked = settings.AutoStop;
                numStopLim.Value = settings.AutoStopLimit;
                numStopLim.Enabled = settings.AutoStop;
                cmbActMode.SelectedIndex = Math.Max(0, Math.Min(1, settings.ActivationMode));
                numHoldDelay.Value = settings.HoldDelayMs;
                numHoldDelay.Enabled = settings.ActivationMode == 1;
                cmbInputMethod.SelectedIndex = Math.Max(0, Math.Min(1, settings.InputMethod));
                ApplyInputMethod();
                chkNotification.Checked = settings.NotificationEnabled;
                cmbNotificationPosition.SelectedIndex = Math.Max(0, Math.Min(4, settings.NotificationPosition));
                numNotificationOpacity.Value = settings.NotificationOpacity;
                cmbNotificationPosition.Enabled = settings.NotificationEnabled;
                numNotificationOpacity.Enabled = settings.NotificationEnabled;
                UpdateToastManager();
            }
            finally
            {
                _isApplyingSettings = false;
            }
        }
        private void InitializeHooks()
        {
            _keyboardHook = new LowLevelKeyboardHook(); _keyboardHook.Install(); _keyboardHook.KeyDown += OnGlobalKeyDown;
            _mouseHook = new LowLevelMouseHook(); _mouseHook.Install(); _mouseHook.MouseDown += OnMouseDown; _mouseHook.MouseUp += OnMouseUp;
        }
        private void AssignHotkey(Action<Keys> setter)
        {
            using (HotkeyInputDialog dialog = new HotkeyInputDialog())
            {
                _keyboardHook.OnKeyDown = (k) => { this.Invoke((MethodInvoker)delegate { dialog.OnKeyDownInternal(k); }); return true; };
                dialog.ShowDialog();
                _keyboardHook.OnKeyDown = null;
                if (dialog.CapturedKey.HasValue)
                {
                    setter(dialog.CapturedKey.Value);
                }
            }
        }
        private string FormatKeyName(Keys k)
        {
            string s = k.ToString();
            if (s.StartsWith("Oem3")) return "~";
            if (s.StartsWith("D") && s.Length == 2 && char.IsDigit(s[1])) return s[1].ToString();
            return s;
        }
        private Button CreateActionButton(string text, EventHandler action)
        {
            Button btn = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = C_Input, ForeColor = C_Text, Margin = new Padding(0, 5, 0, 0), Padding = new Padding(20, 8, 20, 8) };
            btn.FlatAppearance.BorderSize = 0; btn.FlatAppearance.MouseOverBackColor = btn.BackColor; btn.FlatAppearance.MouseDownBackColor = btn.BackColor; btn.Click += action; return btn;
        }
        private NumericUpDown CreateNum(decimal min, decimal max, decimal val, int decimalPlaces = 0)
        {
            NumericUpDown num = new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = decimalPlaces, BackColor = C_Input, ForeColor = C_Text, Width = 100, BorderStyle = BorderStyle.FixedSingle };
            num.ValueChanged += (s, e) => SaveUItoSettings();
            return num;
        }
        private void UpdateStatus(Label lbl, string text, Color c) { if (InvokeRequired) Invoke(new Action(() => UpdateStatus(lbl, text, c))); else { lbl.Text = text; lbl.ForeColor = c; } }
        private void UpdateToastManager()
        {
            _toastManager.Enabled = settings.NotificationEnabled;
            _toastManager.Position = (ToastPosition)settings.NotificationPosition;
            _toastManager.Opacity = settings.NotificationOpacity;
        }
        private void Shutdown() { _keyboardHook?.Uninstall(); _mouseHook?.Uninstall(); InputSimulator.Shutdown(); }
    }
}