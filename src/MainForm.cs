using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    public class MainForm : Form
    {
        // 控件成员
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private Label _lblStatusPower;
        private Label _lblStatusGuard;
        private CheckBox _chkKeepAlive;
        private CheckBox _chkMinToTray;
        private CheckBox _chkAutoStart;
        private ComboBox _cboDelay;
        private System.Windows.Forms.Timer _statusTimer;

        // 热键 ID
        private const int HOTKEY_ID_SCREEN_OFF = 1001;
        private const int HOTKEY_ID_BLACK_MASK = 1002;

        public MainForm()
        {
            InitializeComponent();
            InitializeTrayIcon();
            RegisterGlobalHotkeys();
            UpdatePowerStatusDisplay();

            // 启动定时刷新电源状态 (每 5 秒刷新一次)
            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 5000;
            _statusTimer.Tick += delegate(object s, EventArgs e) { UpdatePowerStatusDisplay(); };
            _statusTimer.Start();
        }

        private void InitializeComponent()
        {
            this.Text = "AgentScreenGuard - 屏幕防烧屏与 Agent 保活";
            this.Size = new Size(720, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(18, 19, 22); // 深黑背景
            this.ForeColor = Color.FromArgb(240, 240, 245);
            this.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);

            // 1. 顶部 Header 区域
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 85;
            headerPanel.BackColor = Color.FromArgb(24, 25, 30);
            headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(42, 44, 54)), 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
            };

            Label lblTitle = new Label();
            lblTitle.Text = "AgentScreenGuard";
            lblTitle.Font = new Font("Segoe UI", 15.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(255, 255, 255);
            lblTitle.Location = new Point(22, 14);
            lblTitle.AutoSize = true;
            headerPanel.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Windows 屏幕防烧屏 (DPMS D3 0-nit) 与 Agent 长时间运行保活";
            lblSubtitle.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(156, 163, 175);
            lblSubtitle.Location = new Point(24, 46);
            lblSubtitle.AutoSize = true;
            headerPanel.Controls.Add(lblSubtitle);

            // 电源状态徽章
            _lblStatusPower = new Label();
            _lblStatusPower.Text = "[ ⚡ 交流电源 (AC) ]";
            _lblStatusPower.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            _lblStatusPower.Location = new Point(480, 18);
            _lblStatusPower.Size = new Size(210, 20);
            _lblStatusPower.TextAlign = ContentAlignment.MiddleRight;
            headerPanel.Controls.Add(_lblStatusPower);

            _lblStatusGuard = new Label();
            _lblStatusGuard.Text = "[ 保活模式: 未激活 ]";
            _lblStatusGuard.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _lblStatusGuard.ForeColor = Color.FromArgb(156, 163, 175);
            _lblStatusGuard.Location = new Point(480, 42);
            _lblStatusGuard.Size = new Size(210, 20);
            _lblStatusGuard.TextAlign = ContentAlignment.MiddleRight;
            headerPanel.Controls.Add(_lblStatusGuard);

            this.Controls.Add(headerPanel);

            // 2. 主体功能卡片区域 (4 大核心功能)
            int cardW = 320;
            int cardH = 135;
            int startX = 22;
            int gapX = 30;
            int startY = 105;
            int gapY = 20;

            // 卡片 1: 一键即时熄屏
            Panel card1 = CreateCardPanel(startX, startY, cardW, cardH);
            Label c1Title = CreateCardTitle("1. 一键即时熄屏", Color.FromArgb(56, 189, 248));
            c1Title.Location = new Point(14, 12);
            card1.Controls.Add(c1Title);

            Label c1Desc = CreateCardDesc("硬件级 DPMS D3 关断，OLED 0 nit 熄灭，后台 Agent 全速运行。轻晃鼠标或按键即醒。");
            c1Desc.Location = new Point(14, 38);
            c1Desc.Size = new Size(292, 40);
            card1.Controls.Add(c1Desc);

            Button btnScreenOff = CreateStyledButton("即刻熄屏 (Ctrl+Alt+S)", Color.FromArgb(2, 132, 199), Color.White);
            btnScreenOff.Location = new Point(14, 86);
            btnScreenOff.Size = new Size(292, 34);
            btnScreenOff.Click += delegate(object s, EventArgs e) { ExecuteScreenOff(); };
            card1.Controls.Add(btnScreenOff);
            this.Controls.Add(card1);

            // 卡片 2: Agent 守护防休眠
            Panel card2 = CreateCardPanel(startX + cardW + gapX, startY, cardW, cardH);
            Label c2Title = CreateCardTitle("2. Agent 运行守护 (防休眠)", Color.FromArgb(52, 211, 153));
            c2Title.Location = new Point(14, 12);
            card2.Controls.Add(c2Title);

            Label c2Desc = CreateCardDesc("锁定系统执行状态 (ES_SYSTEM_REQUIRED)，阻止 Modern Standby 休眠，长夜通宵不断线。");
            c2Desc.Location = new Point(14, 38);
            c2Desc.Size = new Size(292, 40);
            card2.Controls.Add(c2Desc);

            Button btnGuardAndOff = CreateStyledButton("守护并立即熄屏", Color.FromArgb(5, 150, 105), Color.White);
            btnGuardAndOff.Location = new Point(14, 86);
            btnGuardAndOff.Size = new Size(292, 34);
            btnGuardAndOff.Click += delegate(object s, EventArgs e)
            {
                PowerManager.SetKeepAlive(true);
                _chkKeepAlive.Checked = true;
                UpdatePowerStatusDisplay();
                ExecuteScreenOff();
            };
            card2.Controls.Add(btnGuardAndOff);
            this.Controls.Add(card2);

            // 卡片 3: 锁定工作站并熄屏
            Panel card3 = CreateCardPanel(startX, startY + cardH + gapY, cardW, cardH);
            Label c3Title = CreateCardTitle("3. 锁定工作站并熄屏", Color.FromArgb(167, 139, 250));
            c3Title.Location = new Point(14, 12);
            card3.Controls.Add(c3Title);

            Label c3Desc = CreateCardDesc("同步执行 LockWorkStation (Win+L) 并切断显示信号，兼顾外出防窥隐私与任务运行。");
            c3Desc.Location = new Point(14, 38);
            c3Desc.Size = new Size(292, 40);
            card3.Controls.Add(c3Desc);

            Button btnLockOff = CreateStyledButton("锁定并熄屏", Color.FromArgb(109, 40, 217), Color.White);
            btnLockOff.Location = new Point(14, 86);
            btnLockOff.Size = new Size(292, 34);
            btnLockOff.Click += delegate(object s, EventArgs e) { ExecuteLockAndTurnOff(); };
            card3.Controls.Add(btnLockOff);
            this.Controls.Add(card3);

            // 卡片 4: OLED 纯黑置顶幕布
            Panel card4 = CreateCardPanel(startX + cardW + gapX, startY + cardH + gapY, cardW, cardH);
            Label c4Title = CreateCardTitle("4. OLED 纯黑置顶幕布", Color.FromArgb(251, 191, 36));
            c4Title.Location = new Point(14, 12);
            card4.Controls.Add(c4Title);

            Label c4Desc = CreateCardDesc("适用视觉/截屏 Agent (pyautogui/CV)，0 nit 像素关断且保持显存渲染不变。按 Esc 退出。");
            c4Desc.Location = new Point(14, 38);
            c4Desc.Size = new Size(292, 40);
            card4.Controls.Add(c4Desc);

            Button btnBlackMask = CreateStyledButton("全屏纯黑幕布 (Ctrl+Alt+B)", Color.FromArgb(217, 119, 6), Color.White);
            btnBlackMask.Location = new Point(14, 86);
            btnBlackMask.Size = new Size(292, 34);
            btnBlackMask.Click += delegate(object s, EventArgs e) { BlackMaskForm.ShowMask(); };
            card4.Controls.Add(btnBlackMask);
            this.Controls.Add(card4);

            // 3. 底部设置与参数控制区
            int bottomY = startY + (cardH + gapY) * 2;
            Panel bottomPanel = CreateCardPanel(startX, bottomY, cardW * 2 + gapX, 85);

            // 防抖延时
            Label lblDelay = new Label();
            lblDelay.Text = "熄屏防抖延时:";
            lblDelay.Location = new Point(16, 16);
            lblDelay.AutoSize = true;
            lblDelay.ForeColor = Color.FromArgb(209, 213, 219);
            bottomPanel.Controls.Add(lblDelay);

            _cboDelay = new ComboBox();
            _cboDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDelay.Items.AddRange(new object[] { "500 ms", "800 ms", "1000 ms (推荐)", "1500 ms", "2000 ms" });
            _cboDelay.SelectedIndex = 2;
            _cboDelay.Location = new Point(110, 13);
            _cboDelay.Size = new Size(130, 24);
            _cboDelay.BackColor = Color.FromArgb(36, 38, 48);
            _cboDelay.ForeColor = Color.White;
            bottomPanel.Controls.Add(_cboDelay);

            // 防休眠常驻复选框
            _chkKeepAlive = new CheckBox();
            _chkKeepAlive.Text = "保持系统防休眠 (常驻阻止 Windows 睡眠)";
            _chkKeepAlive.Location = new Point(265, 14);
            _chkKeepAlive.AutoSize = true;
            _chkKeepAlive.ForeColor = Color.FromArgb(209, 213, 219);
            _chkKeepAlive.CheckedChanged += delegate(object s, EventArgs e)
            {
                PowerManager.SetKeepAlive(_chkKeepAlive.Checked);
                UpdatePowerStatusDisplay();
            };
            bottomPanel.Controls.Add(_chkKeepAlive);

            // 最小化到托盘
            _chkMinToTray = new CheckBox();
            _chkMinToTray.Text = "点击关闭时最小化到系统托盘";
            _chkMinToTray.Checked = true;
            _chkMinToTray.Location = new Point(16, 48);
            _chkMinToTray.AutoSize = true;
            _chkMinToTray.ForeColor = Color.FromArgb(156, 163, 175);
            bottomPanel.Controls.Add(_chkMinToTray);

            // 开机自启动
            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机随系统自动启动";
            _chkAutoStart.Checked = CheckIsAutoStart();
            _chkAutoStart.Location = new Point(265, 48);
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.ForeColor = Color.FromArgb(156, 163, 175);
            _chkAutoStart.CheckedChanged += delegate(object s, EventArgs e)
            {
                SetAutoStart(_chkAutoStart.Checked);
            };
            bottomPanel.Controls.Add(_chkAutoStart);

            // 诊断按钮
            Button btnDiag = new Button();
            btnDiag.Text = "待机策略诊断";
            btnDiag.FlatStyle = FlatStyle.Flat;
            btnDiag.FlatAppearance.BorderColor = Color.FromArgb(75, 85, 99);
            btnDiag.BackColor = Color.FromArgb(36, 38, 48);
            btnDiag.ForeColor = Color.FromArgb(209, 213, 219);
            btnDiag.Location = new Point(540, 26);
            btnDiag.Size = new Size(115, 34);
            btnDiag.Click += delegate(object s, EventArgs e) { ShowDiagnostics(); };
            bottomPanel.Controls.Add(btnDiag);

            this.Controls.Add(bottomPanel);
        }

        // -------------------------------------------------------------
        // UI 构造辅助函数
        // -------------------------------------------------------------
        private Panel CreateCardPanel(int x, int y, int width, int height)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(width, height);
            p.BackColor = Color.FromArgb(26, 27, 33);
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, Color.FromArgb(44, 46, 56), ButtonBorderStyle.Solid);
            };
            return p;
        }

        private Label CreateCardTitle(string text, Color color)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Font = new Font("Segoe UI", 10.25f, FontStyle.Bold);
            lbl.ForeColor = color;
            lbl.AutoSize = true;
            return lbl;
        }

        private Label CreateCardDesc(string text)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            lbl.ForeColor = Color.FromArgb(156, 163, 175);
            return lbl;
        }

        private Button CreateStyledButton(string text, Color bg, Color fg)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        // -------------------------------------------------------------
        // 系统托盘与图标
        // -------------------------------------------------------------
        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.BackColor = Color.FromArgb(30, 31, 38);
            _trayMenu.ForeColor = Color.White;

            ToolStripMenuItem itemShow = new ToolStripMenuItem("显示主窗口", null, delegate { RestoreWindow(); });
            itemShow.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            _trayMenu.Items.Add(itemShow);
            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayMenu.Items.Add("一键即时熄屏 (Ctrl+Alt+S)", null, delegate { ExecuteScreenOff(); });
            _trayMenu.Items.Add("全屏纯黑幕布 (Ctrl+Alt+B)", null, delegate { BlackMaskForm.ShowMask(); });
            _trayMenu.Items.Add("锁定并熄屏", null, delegate { ExecuteLockAndTurnOff(); });

            ToolStripMenuItem itemToggleKeepAlive = new ToolStripMenuItem("切换系统防休眠保活", null, delegate
            {
                _chkKeepAlive.Checked = !_chkKeepAlive.Checked;
            });
            _trayMenu.Items.Add(itemToggleKeepAlive);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出程序", null, delegate
            {
                _trayIcon.Visible = false;
                PowerManager.SetKeepAlive(false);
                Application.Exit();
            });

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "AgentScreenGuard (防烧屏 & 保活)";
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Icon = GenerateTrayIcon();
            this.Icon = _trayIcon.Icon;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { RestoreWindow(); };
        }

        private Icon GenerateTrayIcon()
        {
            // 动态绘制一个精致的 16x16 屏幕监视器矢量图标，免去外部 .ico 资源依赖
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                // 屏幕外边框 (青蓝色)
                using (Pen pen = new Pen(Color.FromArgb(56, 189, 248), 1.5f))
                {
                    g.DrawRectangle(pen, 1, 1, 13, 9);
                }
                // 底座支架
                using (Brush b = new SolidBrush(Color.FromArgb(148, 163, 184)))
                {
                    g.FillRectangle(b, 7, 11, 2, 2);
                    g.FillRectangle(b, 4, 13, 8, 2);
                }
                // 屏幕内部指示点 (绿色代表保活/就绪)
                using (Brush b = new SolidBrush(Color.FromArgb(52, 211, 153)))
                {
                    g.FillEllipse(b, 5, 4, 4, 4);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void RestoreWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        // -------------------------------------------------------------
        // 全局热键注册与窗口事件
        // -------------------------------------------------------------
        private void RegisterGlobalHotkeys()
        {
            // Ctrl + Alt + S = 立即熄屏
            PowerManager.RegisterHotKey(this.Handle, HOTKEY_ID_SCREEN_OFF, PowerManager.MOD_CONTROL | PowerManager.MOD_ALT, (uint)Keys.S);
            // Ctrl + Alt + B = 纯黑幕布
            PowerManager.RegisterHotKey(this.Handle, HOTKEY_ID_BLACK_MASK, PowerManager.MOD_CONTROL | PowerManager.MOD_ALT, (uint)Keys.B);
        }

        private void UnregisterGlobalHotkeys()
        {
            PowerManager.UnregisterHotKey(this.Handle, HOTKEY_ID_SCREEN_OFF);
            PowerManager.UnregisterHotKey(this.Handle, HOTKEY_ID_BLACK_MASK);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == PowerManager.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_SCREEN_OFF)
                {
                    ExecuteScreenOff();
                }
                else if (id == HOTKEY_ID_BLACK_MASK)
                {
                    BlackMaskForm.ShowMask();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _chkMinToTray.Checked)
            {
                e.Cancel = true;
                this.Hide();
                _trayIcon.ShowBalloonTip(2000, "AgentScreenGuard", "已最小化到系统托盘，全局快捷键与保活持续生效中。", ToolTipIcon.Info);
                return;
            }

            UnregisterGlobalHotkeys();
            PowerManager.SetKeepAlive(false);
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }

        // -------------------------------------------------------------
        // 动作执行与电源状态管理
        // -------------------------------------------------------------
        private int GetSelectedDelayMs()
        {
            switch (_cboDelay.SelectedIndex)
            {
                case 0: return 500;
                case 1: return 800;
                case 2: return 1000;
                case 3: return 1500;
                case 4: return 2000;
                default: return 1000;
            }
        }

        private void ExecuteScreenOff()
        {
            int delay = GetSelectedDelayMs();
            PowerManager.TurnOffScreenAsync(delay, null);
        }

        private void ExecuteLockAndTurnOff()
        {
            int delay = GetSelectedDelayMs();
            PowerManager.LockAndTurnOffAsync(delay, null);
        }

        private void UpdatePowerStatusDisplay()
        {
            PowerStatus status = SystemInformation.PowerStatus;
            bool isAc = (status.PowerLineStatus == PowerLineStatus.Online);
            int batteryPct = (int)(status.BatteryLifePercent * 100);

            if (isAc)
            {
                _lblStatusPower.Text = "[ ⚡ 交流电源 (已插电) ]";
                _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else
            {
                _lblStatusPower.Text = string.Format("[ 🔋 电池供电 ({0}%) ]", batteryPct);
                _lblStatusPower.ForeColor = Color.FromArgb(251, 191, 36);
            }

            if (PowerManager.IsKeepAliveActive)
            {
                _lblStatusGuard.Text = "[ 保活: 已激活 (阻止休眠) ]";
                _lblStatusGuard.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else
            {
                _lblStatusGuard.Text = "[ 保活: 未激活 (跟随系统) ]";
                _lblStatusGuard.ForeColor = Color.FromArgb(156, 163, 175);
            }
        }

        private void ShowDiagnostics()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c powercfg /q SCHEME_CURRENT SUB_SLEEP STANDBYIDLE & powercfg /q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE";
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (Process proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    string info = "Windows 当前活动电源策略数值校核:\n\n" +
                                  "【提示】\n" +
                                  "• STANDBYIDLE (待机休眠): 若当前交流电源为 0x0，表示插电永不休眠。\n" +
                                  "• VIDEOIDLE (熄屏超时): 为自动熄屏的倒计时秒数。\n" +
                                  "• 本软件的「Agent守护」模式直接调用底层内核 SetThreadExecutionState，\n" +
                                  "  无需永久改动系统电源计划，即可在运行长任务时 100% 免疫系统睡眠。\n\n" +
                                  "底层原始输出节选:\n" + output.Substring(0, Math.Min(output.Length, 600));

                    MessageBox.Show(info, "电源策略诊断与参数校核", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("执行 powercfg 诊断失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------------------------------------------------------------
        // 开机自启动注册表操作
        // -------------------------------------------------------------
        private const string RUN_REG_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_REG_NAME = "AgentScreenGuard";

        private bool CheckIsAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_REG_KEY, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(APP_REG_NAME);
                        return (val != null);
                    }
                }
            }
            catch { }
            return false;
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_REG_KEY, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string appPath = Application.ExecutablePath;
                            key.SetValue(APP_REG_NAME, "\"" + appPath + "\"");
                        }
                        else
                        {
                            key.DeleteValue(APP_REG_NAME, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机启动失败: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
