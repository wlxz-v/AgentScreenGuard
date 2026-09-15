using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    public class MainForm : Form
    {
        // Win32 DPI API
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        // 动态 DPI 参数化缩放引擎
        private float _dpiScale = 1.0f;
        public int S(int pixel96) { return (int)Math.Round(pixel96 * _dpiScale); }
        public float Sf(float pixel96) { return (float)(pixel96 * _dpiScale); }

        // 托盘与定时器
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private System.Windows.Forms.Timer _statusTimer;

        // 状态徽章
        private Label _lblStatusPower;
        private Label _lblStatusGuard;

        // 设置控件
        private ComboBox _cboDelay;
        private CheckBox _chkKeepAlive;
        private CheckBox _chkMinToTray;
        private CheckBox _chkAutoStart;

        // 热键 ID
        private const int HOTKEY_ID_SCREEN_OFF = 1001;
        private const int HOTKEY_ID_BLACK_MASK = 1002;

        public MainForm()
        {
            this.AutoScaleMode = AutoScaleMode.None; // 完全受控于 _dpiScale 参数化缩放引擎

            uint dpi = 96;
            try
            {
                dpi = GetDpiForWindow(this.Handle);
            }
            catch
            {
                try { dpi = GetDpiForSystem(); } catch { dpi = 96; }
            }
            if (dpi == 0) dpi = 96;
            _dpiScale = (float)dpi / 96.0f;

            InitializeLayout();
            InitializeTrayIcon();
            RegisterGlobalHotkeys();
            UpdatePowerStatusDisplay();

            // 定时刷新电源状态 (每 5 秒)
            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 5000;
            _statusTimer.Tick += delegate(object s, EventArgs e) { UpdatePowerStatusDisplay(); };
            _statusTimer.Start();
        }

        // =============================================================
        // 核心布局构造 (动态参数化 DPI 缩放，彻底消除文字截断与空间挤压)
        // =============================================================
        private void InitializeLayout()
        {
            this.Text = "AgentScreenGuard - 屏幕防烧屏与 Agent 运行保活";
            this.ClientSize = new Size(S(740), S(750));
            this.MinimumSize = this.Size;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(16, 17, 21); // OLED 极深黑底
            this.ForeColor = Color.FromArgb(240, 240, 245);
            this.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);

            // ---------------------------------------------------------
            // 1. 顶部状态栏
            // ---------------------------------------------------------
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = S(92);
            headerPanel.BackColor = Color.FromArgb(22, 23, 29);
            headerPanel.Padding = new Padding(S(24), S(12), S(24), S(12));
            headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(38, 40, 50)), 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
            };

            TableLayoutPanel headerGrid = new TableLayoutPanel();
            headerGrid.Dock = DockStyle.Fill;
            headerGrid.ColumnCount = 2;
            headerGrid.RowCount = 2;
            headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            headerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(42f)));
            headerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(26f)));

            // 主标题
            Label lblTitle = new Label();
            lblTitle.Text = "AgentScreenGuard";
            lblTitle.Font = new Font("Microsoft YaHei UI", 15.0f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Dock = DockStyle.Fill;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            headerGrid.Controls.Add(lblTitle, 0, 0);

            // 副标题
            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Windows 屏幕防烧屏与 Agent 运行保活平台 (v1.0.0)";
            lblSubtitle.Font = new Font("Microsoft YaHei UI", 8.75f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(156, 163, 175);
            lblSubtitle.Dock = DockStyle.Fill;
            lblSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            headerGrid.Controls.Add(lblSubtitle, 0, 1);

            // 电源状态徽章 (右上)
            _lblStatusPower = new Label();
            _lblStatusPower.Text = "[ ⚡ 交流电源 (已插电) ]";
            _lblStatusPower.Font = new Font("Microsoft YaHei UI", 8.75f, FontStyle.Bold);
            _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            _lblStatusPower.Dock = DockStyle.Fill;
            _lblStatusPower.TextAlign = ContentAlignment.MiddleRight;
            headerGrid.Controls.Add(_lblStatusPower, 1, 0);

            // 保活状态徽章 (右下)
            _lblStatusGuard = new Label();
            _lblStatusGuard.Text = "[ 🛡️ 保活: 未激活 ]";
            _lblStatusGuard.Font = new Font("Microsoft YaHei UI", 8.75f, FontStyle.Bold);
            _lblStatusGuard.ForeColor = Color.FromArgb(156, 163, 175);
            _lblStatusGuard.Dock = DockStyle.Fill;
            _lblStatusGuard.TextAlign = ContentAlignment.MiddleRight;
            headerGrid.Controls.Add(_lblStatusGuard, 1, 1);

            headerPanel.Controls.Add(headerGrid);
            this.Controls.Add(headerPanel);

            // ---------------------------------------------------------
            // 2. 主体卡片容器
            // ---------------------------------------------------------
            TableLayoutPanel contentGrid = new TableLayoutPanel();
            contentGrid.Dock = DockStyle.Fill;
            contentGrid.ColumnCount = 1;
            contentGrid.RowCount = 5;
            contentGrid.Padding = new Padding(S(20), S(12), S(20), S(12));
            contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(116f)));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(116f)));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(116f)));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(116f)));
            contentGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Sf(138f)));

            // 卡片 1: 一键即时熄屏
            Panel card1 = CreateActionCard(
                "⚡ 一键即时熄屏",
                "硬件级切断显示信号 (DPMS D3)，OLED 0 nit 完全熄灭，LCD 背光断电。按键防抖，轻晃鼠标即醒。",
                "全局热键: Ctrl + Alt + S",
                "即刻熄屏",
                Color.FromArgb(56, 189, 248),
                Color.FromArgb(2, 132, 199),
                delegate { ExecuteScreenOff(); }
            );
            contentGrid.Controls.Add(card1, 0, 0);

            // 卡片 2: Agent 运行守护 (防休眠)
            Panel card2 = CreateActionCard(
                "🛡️ Agent 运行守护 (防休眠)",
                "调用 Win32 SetThreadExecutionState，阻止整机进入 Modern Standby 睡眠挂起，CPU/网络全速运转。",
                "离开模式: 屏幕熄灭但后台推理/编译任务绝不中断",
                "守护并熄屏",
                Color.FromArgb(52, 211, 153),
                Color.FromArgb(5, 150, 105),
                delegate
                {
                    PowerManager.SetKeepAlive(true);
                    _chkKeepAlive.Checked = true;
                    UpdatePowerStatusDisplay();
                    ExecuteScreenOff();
                }
            );
            contentGrid.Controls.Add(card2, 0, 1);

            // 卡片 3: 锁定工作站并熄屏
            Panel card3 = CreateActionCard(
                "🔒 锁定工作站并熄屏",
                "先执行 LockWorkStation (Win+L) 锁定当前桌面，再硬件切断显示信号，外出防窥更安全。",
                "适用工位离开、外出防窥与后台无人值守运行",
                "锁定并熄屏",
                Color.FromArgb(167, 139, 250),
                Color.FromArgb(109, 40, 217),
                delegate { ExecuteLockAndTurnOff(); }
            );
            contentGrid.Controls.Add(card3, 0, 2);

            // 卡片 4: OLED 纯黑置顶幕布
            Panel card4 = CreateActionCard(
                "⬛ OLED 纯黑置顶幕布",
                "覆盖全屏纯黑窗体 (#000000)，OLED 0 nit 防烧屏且不切断 DPMS 链路，显存帧缓冲区与分辨率完好保留。",
                "全局热键: Ctrl + Alt + B (按 Esc 键退出) | 适用截屏/视觉识别 Agent",
                "全屏纯黑幕布",
                Color.FromArgb(251, 191, 36),
                Color.FromArgb(217, 119, 6),
                delegate { BlackMaskForm.ShowMask(); }
            );
            contentGrid.Controls.Add(card4, 0, 3);

            // 卡片 5: 系统设置与参数控制
            Panel card5 = CreateSettingsCard();
            contentGrid.Controls.Add(card5, 0, 4);

            this.Controls.Add(contentGrid);
        }

        // =============================================================
        // 卡片组件工厂 (参数化 DPI 布局，动态自适应居中)
        // =============================================================
        private Panel CreateActionCard(string title, string desc, string hint, string buttonText,
            Color accentColor, Color buttonColor, EventHandler onAction)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, S(10));
            card.BackColor = Color.FromArgb(25, 26, 32);
            card.Padding = new Padding(S(18), S(10), S(18), S(10));
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(40, 42, 52), ButtonBorderStyle.Solid);
            };

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sf(160f)));

            TableLayoutPanel textLayout = new TableLayoutPanel();
            textLayout.Dock = DockStyle.Fill;
            textLayout.ColumnCount = 1;
            textLayout.RowCount = 3;
            textLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            textLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            lblTitle.ForeColor = accentColor;
            lblTitle.AutoSize = true;
            lblTitle.Margin = new Padding(0, 0, 0, S(3));
            textLayout.Controls.Add(lblTitle, 0, 0);

            Label lblDesc = new Label();
            lblDesc.Text = desc;
            lblDesc.Font = new Font("Microsoft YaHei UI", 8.75f, FontStyle.Regular);
            lblDesc.ForeColor = Color.FromArgb(156, 163, 175);
            lblDesc.Dock = DockStyle.Fill;
            lblDesc.TextAlign = ContentAlignment.MiddleLeft;
            lblDesc.Margin = new Padding(0, 0, 0, S(3));
            textLayout.Controls.Add(lblDesc, 0, 1);

            Label lblHint = new Label();
            lblHint.Text = hint;
            lblHint.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(107, 114, 128);
            lblHint.AutoSize = true;
            lblHint.Margin = new Padding(0);
            textLayout.Controls.Add(lblHint, 0, 2);

            layout.Controls.Add(textLayout, 0, 0);

            // 右侧按钮容器 (自适应居中)
            Panel btnPanel = new Panel();
            btnPanel.Dock = DockStyle.Fill;

            Button btn = new Button();
            btn.Text = buttonText;
            btn.Size = new Size(S(150), S(42));
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = buttonColor;
            btn.ForeColor = Color.White;
            btn.Font = new Font("Microsoft YaHei UI", 9.25f, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
            btn.Click += onAction;

            EventHandler centerBtn = delegate
            {
                btn.Location = new Point(
                    Math.Max(0, (btnPanel.ClientSize.Width - btn.Width) / 2),
                    Math.Max(0, (btnPanel.ClientSize.Height - btn.Height) / 2)
                );
            };
            btnPanel.SizeChanged += centerBtn;
            centerBtn(null, null);

            btnPanel.Controls.Add(btn);
            layout.Controls.Add(btnPanel, 1, 0);
            card.Controls.Add(layout);
            return card;
        }

        private Panel CreateSettingsCard()
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 0);
            card.BackColor = Color.FromArgb(25, 26, 32);
            card.Padding = new Padding(S(18), S(12), S(18), S(12));
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(40, 42, 52), ButtonBorderStyle.Solid);
            };

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            // 第一行：延时配置 + 常驻防休眠复选框
            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.Dock = DockStyle.Fill;
            row1.WrapContents = false;
            row1.Padding = new Padding(0, S(4), 0, 0);

            Label lblDelay = new Label();
            lblDelay.Text = "防抖延时:";
            lblDelay.AutoSize = true;
            lblDelay.Font = new Font("Microsoft YaHei UI", 9.0f);
            lblDelay.ForeColor = Color.FromArgb(209, 213, 219);
            lblDelay.Margin = new Padding(0, S(5), S(8), 0);
            row1.Controls.Add(lblDelay);

            _cboDelay = new ComboBox();
            _cboDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDelay.Items.AddRange(new object[] { "500 ms", "800 ms", "1000 ms (推荐)", "1500 ms", "2000 ms" });
            _cboDelay.SelectedIndex = 2;
            _cboDelay.Size = new Size(S(140), S(28));
            _cboDelay.Font = new Font("Microsoft YaHei UI", 9.0f);
            _cboDelay.BackColor = Color.FromArgb(34, 36, 44);
            _cboDelay.ForeColor = Color.White;
            _cboDelay.Margin = new Padding(0, S(2), S(24), 0);
            row1.Controls.Add(_cboDelay);

            _chkKeepAlive = new CheckBox();
            _chkKeepAlive.Text = "保持系统防休眠 (常驻阻止 Windows 睡眠挂起)";
            _chkKeepAlive.AutoSize = true;
            _chkKeepAlive.Font = new Font("Microsoft YaHei UI", 9.0f);
            _chkKeepAlive.ForeColor = Color.FromArgb(209, 213, 219);
            _chkKeepAlive.Margin = new Padding(0, S(4), 0, 0);
            _chkKeepAlive.CheckedChanged += delegate
            {
                PowerManager.SetKeepAlive(_chkKeepAlive.Checked);
                UpdatePowerStatusDisplay();
            };
            row1.Controls.Add(_chkKeepAlive);

            layout.Controls.Add(row1, 0, 0);

            // 第二行：托盘设置 + 开机启动 + 诊断按钮
            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.Dock = DockStyle.Fill;
            row2.WrapContents = false;
            row2.Padding = new Padding(0, S(4), 0, 0);

            _chkMinToTray = new CheckBox();
            _chkMinToTray.Text = "关闭时最小化到系统托盘";
            _chkMinToTray.Checked = true;
            _chkMinToTray.AutoSize = true;
            _chkMinToTray.Font = new Font("Microsoft YaHei UI", 9.0f);
            _chkMinToTray.ForeColor = Color.FromArgb(156, 163, 175);
            _chkMinToTray.Margin = new Padding(0, S(6), S(24), 0);
            row2.Controls.Add(_chkMinToTray);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机随系统启动";
            _chkAutoStart.Checked = CheckIsAutoStart();
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.Font = new Font("Microsoft YaHei UI", 9.0f);
            _chkAutoStart.ForeColor = Color.FromArgb(156, 163, 175);
            _chkAutoStart.Margin = new Padding(0, S(6), S(24), 0);
            _chkAutoStart.CheckedChanged += delegate
            {
                SetAutoStart(_chkAutoStart.Checked);
            };
            row2.Controls.Add(_chkAutoStart);

            Button btnDiag = new Button();
            btnDiag.Text = "待机策略诊断";
            btnDiag.Size = new Size(S(135), S(30));
            btnDiag.FlatStyle = FlatStyle.Flat;
            btnDiag.FlatAppearance.BorderColor = Color.FromArgb(75, 85, 99);
            btnDiag.BackColor = Color.FromArgb(34, 36, 44);
            btnDiag.ForeColor = Color.FromArgb(209, 213, 219);
            btnDiag.Font = new Font("Microsoft YaHei UI", 8.75f);
            btnDiag.Margin = new Padding(S(16), S(2), 0, 0);
            btnDiag.Cursor = Cursors.Hand;
            btnDiag.Click += delegate { ShowDiagnostics(); };
            row2.Controls.Add(btnDiag);

            layout.Controls.Add(row2, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        // =============================================================
        // 系统托盘与矢量图标渲染
        // =============================================================
        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.BackColor = Color.FromArgb(30, 31, 38);
            _trayMenu.ForeColor = Color.White;
            _trayMenu.Font = new Font("Microsoft YaHei UI", 9.0f);

            ToolStripMenuItem itemShow = new ToolStripMenuItem("显示主窗口", null, delegate { RestoreWindow(); });
            itemShow.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            _trayMenu.Items.Add(itemShow);
            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayMenu.Items.Add("一键即时熄屏 (Ctrl+Alt+S)", null, delegate { ExecuteScreenOff(); });
            _trayMenu.Items.Add("全屏纯黑幕布 (Ctrl+Alt+B)", null, delegate { BlackMaskForm.ShowMask(); });
            _trayMenu.Items.Add("锁定工作站并熄屏", null, delegate { ExecuteLockAndTurnOff(); });

            ToolStripMenuItem itemToggleKeepAlive = new ToolStripMenuItem("切换防休眠保活", null, delegate
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
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (Pen pen = new Pen(Color.FromArgb(56, 189, 248), 1.5f))
                {
                    g.DrawRectangle(pen, 1, 1, 13, 9);
                }
                using (Brush b = new SolidBrush(Color.FromArgb(148, 163, 184)))
                {
                    g.FillRectangle(b, 7, 11, 2, 2);
                    g.FillRectangle(b, 4, 13, 8, 2);
                }
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

        // =============================================================
        // 全局热键
        // =============================================================
        private void RegisterGlobalHotkeys()
        {
            PowerManager.RegisterHotKey(this.Handle, HOTKEY_ID_SCREEN_OFF, PowerManager.MOD_CONTROL | PowerManager.MOD_ALT, (uint)Keys.S);
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

        // =============================================================
        // 核心功能触发
        // =============================================================
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
                _lblStatusGuard.Text = "[ 🛡️ 保活: 已激活 (阻止休眠) ]";
                _lblStatusGuard.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else
            {
                _lblStatusGuard.Text = "[ 🛡️ 保活: 未激活 (跟随系统) ]";
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

        // =============================================================
        // 开机自启动
        // =============================================================
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
