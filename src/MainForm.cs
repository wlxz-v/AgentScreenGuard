using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentScreenGuard
{
    public class MainForm : Form
    {
        // =============================================================
        // Win32 DPI & 系统 API
        // =============================================================
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        // 紧凑设计尺寸 (宽度 430px，高度 268px，无多余空白，高度完全匹配内容)
        private const int BaseWidth = 430;
        private const int BaseHeight = 268;

        private float _dpiScale = 1.0f;
        public int S(int pixel96) { return (int)Math.Round(pixel96 * _dpiScale); }

        // 控件引用
        private Label _lblStatusPower;
        private Label _lblStatusGuard;
        private ComboBox _cboDelay;
        private CheckBox _chkKeepAlive;
        private CheckBox _chkMinToTray;
        private CheckBox _chkAutoStart;
        private ToolTip _infoToolTip;

        // 托盘与定时器
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private System.Windows.Forms.Timer _statusTimer;

        // 全局热键
        private const int HOTKEY_ID_SCREEN_OFF = 1001;
        private const int HOTKEY_ID_BLACK_MASK = 1002;

        public MainForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;

            // 1. 获取屏幕 DPI 缩放比
            uint dpi = 96;
            try { dpi = GetDpiForWindow(this.Handle); }
            catch { try { dpi = GetDpiForSystem(); } catch { dpi = 96; } }
            if (dpi == 0) dpi = 96;
            _dpiScale = (float)dpi / 96.0f;

            // 2. 窗口形态：紧凑小巧，边距紧凑，高度完全匹配内容
            this.Text = "AgentScreenGuard";
            this.ClientSize = new Size(S(BaseWidth), S(BaseHeight));
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 19, 24);
            this.ForeColor = Color.FromArgb(240, 240, 245);
            this.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);

            // 3. 构建精简界面
            InitializeLayout();
            InitializeTrayIcon();
            RegisterGlobalHotkeys();
            UpdatePowerStatusDisplay();

            // 4. 定时刷新电源状态 (每 5 秒)
            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 5000;
            _statusTimer.Tick += delegate { UpdatePowerStatusDisplay(); };
            _statusTimer.Start();
        }

        // =============================================================
        // 核心布局构造 (高度与内容紧密贴合，零空白浪费)
        // =============================================================
        private void InitializeLayout()
        {
            // 初始化悬停提示工具
            _infoToolTip = new ToolTip();
            _infoToolTip.AutoPopDelay = 12000;
            _infoToolTip.InitialDelay = 200;
            _infoToolTip.ReshowDelay = 100;
            _infoToolTip.ShowAlways = true;

            // ---------------------------------------------------------
            // 1. 顶部极简标题栏 (高度 S(34))
            // ---------------------------------------------------------
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = S(34);
            headerPanel.BackColor = Color.FromArgb(24, 25, 32);
            headerPanel.Padding = new Padding(S(12), 0, S(12), 0);
            headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(38, 40, 52)), 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
            };

            Label lblTitle = new Label();
            lblTitle.Text = "AgentScreenGuard";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Dock = DockStyle.Left;
            lblTitle.AutoSize = true;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;

            Label lblVer = new Label();
            lblVer.Text = "屏幕防烧屏与保活 (v1.0)";
            lblVer.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            lblVer.ForeColor = Color.FromArgb(140, 145, 158);
            lblVer.Dock = DockStyle.Right;
            lblVer.AutoSize = true;
            lblVer.TextAlign = ContentAlignment.MiddleRight;

            headerPanel.Controls.Add(lblVer);
            headerPanel.Controls.Add(lblTitle);
            headerPanel.Resize += delegate
            {
                lblTitle.Top = (headerPanel.ClientSize.Height - lblTitle.Height) / 2;
                lblVer.Top = (headerPanel.ClientSize.Height - lblVer.Height) / 2;
            };

            this.Controls.Add(headerPanel);

            // ---------------------------------------------------------
            // 2. 底部状态栏 (高度 S(22))
            // ---------------------------------------------------------
            Panel bottomStatusBar = new Panel();
            bottomStatusBar.Dock = DockStyle.Bottom;
            bottomStatusBar.Height = S(22);
            bottomStatusBar.BackColor = Color.FromArgb(16, 17, 22);
            bottomStatusBar.Padding = new Padding(S(12), 0, S(12), 0);
            bottomStatusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(32, 34, 44)), 0, 0, bottomStatusBar.Width, 0);
            };

            _lblStatusPower = new Label();
            _lblStatusPower.Text = "⚡ 交流电源 (已插电)";
            _lblStatusPower.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            _lblStatusPower.Dock = DockStyle.Left;
            _lblStatusPower.AutoSize = true;
            _lblStatusPower.TextAlign = ContentAlignment.MiddleLeft;

            _lblStatusGuard = new Label();
            _lblStatusGuard.Text = "🛡️ 保活: 未激活";
            _lblStatusGuard.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            _lblStatusGuard.ForeColor = Color.FromArgb(140, 145, 158);
            _lblStatusGuard.Dock = DockStyle.Right;
            _lblStatusGuard.AutoSize = true;
            _lblStatusGuard.TextAlign = ContentAlignment.MiddleRight;

            bottomStatusBar.Controls.Add(_lblStatusGuard);
            bottomStatusBar.Controls.Add(_lblStatusPower);
            bottomStatusBar.Resize += delegate
            {
                _lblStatusPower.Top = (bottomStatusBar.ClientSize.Height - _lblStatusPower.Height) / 2;
                _lblStatusGuard.Top = (bottomStatusBar.ClientSize.Height - _lblStatusGuard.Height) / 2;
            };

            this.Controls.Add(bottomStatusBar);

            // ---------------------------------------------------------
            // 3. 底部选择项 (双栏紧凑排版，高度 S(52))
            // ---------------------------------------------------------
            Panel settingsPanel = new Panel();
            settingsPanel.Dock = DockStyle.Bottom;
            settingsPanel.Height = S(52);
            settingsPanel.BackColor = Color.FromArgb(22, 23, 29);
            settingsPanel.Padding = new Padding(S(12), S(4), S(12), S(3));
            settingsPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(32, 34, 44)), 0, 0, settingsPanel.Width, 0);
            };

            TableLayoutPanel settingsGrid = new TableLayoutPanel();
            settingsGrid.Dock = DockStyle.Fill;
            settingsGrid.ColumnCount = 2;
            settingsGrid.RowCount = 2;
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            settingsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            settingsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            // 左栏第 1 行：延时配置
            FlowLayoutPanel delayFlow = new FlowLayoutPanel();
            delayFlow.Dock = DockStyle.Fill;
            delayFlow.Margin = new Padding(0);
            delayFlow.WrapContents = false;

            Label lblDelay = new Label();
            lblDelay.Text = "延时:";
            lblDelay.AutoSize = true;
            lblDelay.Font = new Font("Microsoft YaHei UI", 8.25f);
            lblDelay.ForeColor = Color.FromArgb(200, 205, 215);
            lblDelay.Margin = new Padding(0, S(3), S(3), 0);
            delayFlow.Controls.Add(lblDelay);

            _cboDelay = new ComboBox();
            _cboDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDelay.Items.AddRange(new object[] { "500 ms", "800 ms", "1000 ms (推荐)", "1500 ms", "2000 ms" });
            _cboDelay.SelectedIndex = 2;
            _cboDelay.Size = new Size(S(115), S(21));
            _cboDelay.Font = new Font("Microsoft YaHei UI", 8.25f);
            _cboDelay.BackColor = Color.FromArgb(34, 36, 44);
            _cboDelay.ForeColor = Color.White;
            _cboDelay.Margin = new Padding(0);
            delayFlow.Controls.Add(_cboDelay);

            settingsGrid.Controls.Add(delayFlow, 0, 0);

            // 右栏第 1 行：常驻阻止休眠
            _chkKeepAlive = new CheckBox();
            _chkKeepAlive.Text = "保持常驻防休眠";
            _chkKeepAlive.AutoSize = true;
            _chkKeepAlive.Font = new Font("Microsoft YaHei UI", 8.25f);
            _chkKeepAlive.ForeColor = Color.FromArgb(200, 205, 215);
            _chkKeepAlive.Margin = new Padding(0, S(2), 0, 0);
            _chkKeepAlive.CheckedChanged += delegate
            {
                PowerManager.SetKeepAlive(_chkKeepAlive.Checked);
                UpdatePowerStatusDisplay();
            };
            settingsGrid.Controls.Add(_chkKeepAlive, 1, 0);

            // 左栏第 2 行：最小化到系统托盘
            _chkMinToTray = new CheckBox();
            _chkMinToTray.Text = "关闭时最小化到托盘";
            _chkMinToTray.Checked = true;
            _chkMinToTray.AutoSize = true;
            _chkMinToTray.Font = new Font("Microsoft YaHei UI", 8.25f);
            _chkMinToTray.ForeColor = Color.FromArgb(140, 145, 158);
            _chkMinToTray.Margin = new Padding(0, S(1), 0, 0);
            settingsGrid.Controls.Add(_chkMinToTray, 0, 1);

            // 右栏第 2 行：开机自启 + 诊断小按钮
            FlowLayoutPanel autoStartFlow = new FlowLayoutPanel();
            autoStartFlow.Dock = DockStyle.Fill;
            autoStartFlow.Margin = new Padding(0);
            autoStartFlow.WrapContents = false;

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机随系统启动";
            _chkAutoStart.Checked = CheckIsAutoStart();
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.Font = new Font("Microsoft YaHei UI", 8.25f);
            _chkAutoStart.ForeColor = Color.FromArgb(140, 145, 158);
            _chkAutoStart.Margin = new Padding(0, S(1), S(6), 0);
            _chkAutoStart.CheckedChanged += delegate
            {
                SetAutoStart(_chkAutoStart.Checked);
            };
            autoStartFlow.Controls.Add(_chkAutoStart);

            Button btnDiag = new Button();
            btnDiag.Text = "诊断";
            btnDiag.Size = new Size(S(42), S(19));
            btnDiag.FlatStyle = FlatStyle.Flat;
            btnDiag.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 72);
            btnDiag.BackColor = Color.FromArgb(34, 36, 44);
            btnDiag.ForeColor = Color.FromArgb(170, 175, 185);
            btnDiag.Font = new Font("Microsoft YaHei UI", 7.5f);
            btnDiag.Margin = new Padding(0);
            btnDiag.Cursor = Cursors.Hand;
            btnDiag.Click += delegate { ShowDiagnostics(); };
            autoStartFlow.Controls.Add(btnDiag);

            settingsGrid.Controls.Add(autoStartFlow, 1, 1);

            settingsPanel.Controls.Add(settingsGrid);
            this.Controls.Add(settingsPanel);

            // ---------------------------------------------------------
            // 4. 中间模式卡片区 (4行紧凑布局，每个模式高度精准贴合)
            // ---------------------------------------------------------
            Panel mainContainer = new Panel();
            mainContainer.Dock = DockStyle.Fill;
            mainContainer.Padding = new Padding(S(10), S(6), S(10), S(4));

            TableLayoutPanel modesGrid = new TableLayoutPanel();
            modesGrid.Dock = DockStyle.Fill;
            modesGrid.ColumnCount = 1;
            modesGrid.RowCount = 4;
            modesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            modesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            modesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            modesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            modesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            // 模式 1: 一键即时熄屏
            modesGrid.Controls.Add(CreateModeRow(
                "⚡ 一键即时熄屏",
                "[Ctrl+Alt+S]",
                "【一键即时熄屏实现机理】\n底层通过广播发送 SC_MONITORPOWER 消息，指示 GPU 硬件关断显示链路 (DPMS D3)。\n• OLED: 各像素有机发光直接归零 (0.0000 nit)，整屏物理熄灭。\n• LCD: 液晶面板背光完全切断供电。\n• 唤醒方式: 晃动鼠标或敲击任意键盘按键即刻唤醒。\n• 具备微秒级按键防抖，避免松手弹起瞬间误唤醒。",
                Color.FromArgb(56, 189, 248),
                Color.FromArgb(2, 132, 199),
                delegate { ExecuteScreenOff(); }
            ), 0, 0);

            // 模式 2: Agent 守护保活 (防休眠)
            modesGrid.Controls.Add(CreateModeRow(
                "🛡️ Agent 守护保活",
                "[离开模式防休眠]",
                "【Agent 守护保活实现机理】\n调用底层 Win32 内核 API SetThreadExecutionState，注入系统保活锁：\n(ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED)。\n• 强力阻止 Windows 进入 Modern Standby (S0ix) 或睡眠休眠挂起。\n• 屏幕硬件可完全熄灭切断显示，但 CPU、内存与网络保持全速运转，长夜通宵任务不断线。",
                Color.FromArgb(52, 211, 153),
                Color.FromArgb(5, 150, 105),
                delegate
                {
                    PowerManager.SetKeepAlive(true);
                    if (_chkKeepAlive != null) _chkKeepAlive.Checked = true;
                    UpdatePowerStatusDisplay();
                    ExecuteScreenOff();
                }
            ), 0, 1);

            // 模式 3: 锁定工作站并熄屏
            modesGrid.Controls.Add(CreateModeRow(
                "🔒 锁定工作站并熄屏",
                "[Win+L 锁屏]",
                "【锁定工作站并熄屏实现机理】\n先执行 Win32 API LockWorkStation() 锁定当前 Windows 桌面，随后硬件切断显示信号。\n• 适用于离开工位、外出防窥且需要后台长任务持续运转的场景。\n• 点亮屏幕后需输入 Windows 账户密码方可进入桌面。",
                Color.FromArgb(167, 139, 250),
                Color.FromArgb(109, 40, 217),
                delegate { ExecuteLockAndTurnOff(); }
            ), 0, 2);

            // 模式 4: OLED 纯黑置顶幕布
            modesGrid.Controls.Add(CreateModeRow(
                "⬛ OLED 纯黑置顶幕布",
                "[Ctrl+Alt+B]",
                "【OLED 纯黑置顶幕布实现机理】\n跨多屏幕覆盖无边框顶级全黑窗体 (#000000)。\n• OLED 像素纯黑不发光防烧屏，且不切断 GPU 显示链路。\n• 显存帧缓冲区与分辨率完好保留，彻底避免 pyautogui / CV 视觉自动化截取黑帧。\n• 按 Esc 键或鼠标点击即可秒速退出。",
                Color.FromArgb(251, 191, 36),
                Color.FromArgb(217, 119, 6),
                delegate { BlackMaskForm.ShowMask(); }
            ), 0, 3);

            mainContainer.Controls.Add(modesGrid);
            this.Controls.Add(mainContainer);
        }

        // =============================================================
        // 单个模式行组件 (高度紧密贴合文字内容，悬停图标提示技术机理)
        // =============================================================
        private Panel CreateModeRow(string title, string shortcutHint, string tooltipDetail, Color titleColor, Color btnColor, EventHandler onAction)
        {
            Panel row = new Panel();
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0, S(2), 0, S(2));
            row.BackColor = Color.FromArgb(25, 26, 33);
            row.Padding = new Padding(S(10), 0, S(8), 0);
            row.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, row.ClientRectangle, Color.FromArgb(36, 38, 48), ButtonBorderStyle.Solid);
            };

            // 标题
            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold);
            lblTitle.ForeColor = titleColor;
            lblTitle.AutoSize = true;

            // 快捷键标签
            Label lblHint = new Label();
            lblHint.Text = shortcutHint;
            lblHint.Font = new Font("Microsoft YaHei UI", 7.75f, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(120, 125, 138);
            lblHint.AutoSize = true;

            // 悬停信息图标 (鼠标移到此处展示实现方式)
            Label lblInfo = new Label();
            lblInfo.Text = "ⓘ";
            lblInfo.Font = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Bold);
            lblInfo.ForeColor = Color.FromArgb(130, 135, 150);
            lblInfo.AutoSize = true;
            lblInfo.Cursor = Cursors.Help;

            lblInfo.MouseEnter += delegate { lblInfo.ForeColor = Color.FromArgb(56, 189, 248); };
            lblInfo.MouseLeave += delegate { lblInfo.ForeColor = Color.FromArgb(130, 135, 150); };

            // 绑定悬停介绍
            _infoToolTip.SetToolTip(lblInfo, tooltipDetail);
            _infoToolTip.SetToolTip(lblTitle, tooltipDetail);

            // 右侧统一“启动”按钮 (尺寸紧凑精致)
            Button btnStart = new Button();
            btnStart.Text = "启动";
            btnStart.Size = new Size(S(58), S(24));
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.BackColor = btnColor;
            btnStart.ForeColor = Color.White;
            btnStart.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
            btnStart.Cursor = Cursors.Hand;
            btnStart.Click += onAction;

            row.Controls.Add(lblTitle);
            row.Controls.Add(lblHint);
            row.Controls.Add(lblInfo);
            row.Controls.Add(btnStart);

            // 精准垂直居中排布，杜绝任何空白位移
            row.Resize += delegate
            {
                int midY = row.ClientSize.Height / 2;

                lblTitle.Location = new Point(S(10), Math.Max(0, midY - (lblTitle.Height / 2)));
                lblHint.Location = new Point(lblTitle.Right + S(6), Math.Max(0, midY - (lblHint.Height / 2)));
                lblInfo.Location = new Point(lblHint.Right + S(4), Math.Max(0, midY - (lblInfo.Height / 2)));

                btnStart.Location = new Point(row.ClientSize.Width - btnStart.Width - S(8), Math.Max(0, midY - (btnStart.Height / 2)));
            };

            return row;
        }

        // =============================================================
        // 系统托盘与图标渲染
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
                if (_chkKeepAlive != null) _chkKeepAlive.Checked = !_chkKeepAlive.Checked;
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
            if (e.CloseReason == CloseReason.UserClosing && _chkMinToTray != null && _chkMinToTray.Checked)
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
            if (_cboDelay == null) return 1000;
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
                _lblStatusPower.Text = "⚡ 交流电源 (已插电)";
                _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else
            {
                _lblStatusPower.Text = string.Format("🔋 电池供电 ({0}%)", batteryPct);
                _lblStatusPower.ForeColor = Color.FromArgb(251, 191, 36);
            }

            if (PowerManager.IsKeepAliveActive)
            {
                _lblStatusGuard.Text = "🛡️ 保活: 已激活 (阻止休眠)";
                _lblStatusGuard.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else
            {
                _lblStatusGuard.Text = "🛡️ 保活: 未激活";
                _lblStatusGuard.ForeColor = Color.FromArgb(140, 145, 158);
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
                                  "• STANDBYIDLE (待机休眠): 交流电源 0x0 表示插电永不休眠\n" +
                                  "• VIDEOIDLE (熄屏超时): 为自动关屏倒计时秒数\n" +
                                  "• 本软件「Agent守护」模式直接注入 SetThreadExecutionState 内核锁，100% 免疫系统休眠。\n\n" +
                                  "底层原始输出节选:\n" + output.Substring(0, Math.Min(output.Length, 450));

                    MessageBox.Show(info, "待机策略参数校核", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
