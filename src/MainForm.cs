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

        // 紧凑设计基准尺寸 (宽度 430px，高度 248px，完全杜绝大面积空白与重叠)
        private const int BaseWidth = 430;
        private const int BaseHeight = 248;

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
        private const int HOTKEY_ID_GUARD = 1003;

        public MainForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;

            // 1. 获取屏幕 DPI 缩放比
            uint dpi = 96;
            try { dpi = GetDpiForWindow(this.Handle); }
            catch { try { dpi = GetDpiForSystem(); } catch { dpi = 96; } }
            if (dpi == 0) dpi = 96;
            _dpiScale = (float)dpi / 96.0f;

            // 2. 窗口形态：绝对匹配内容的高密度紧凑工具
            this.Text = "AgentScreenGuard";
            this.ClientSize = new Size(S(BaseWidth), S(BaseHeight));
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 19, 24);
            this.ForeColor = Color.FromArgb(240, 240, 245);
            this.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);

            // 3. 构建无重叠精准像素布局
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
        // 核心布局构造 (绝对像素对齐，零多余空白，零控件重叠)
        // =============================================================
        private void InitializeLayout()
        {
            int W = this.ClientSize.Width;

            // 悬停技术说明浮窗
            _infoToolTip = new ToolTip();
            _infoToolTip.AutoPopDelay = 12000;
            _infoToolTip.InitialDelay = 150;
            _infoToolTip.ReshowDelay = 100;
            _infoToolTip.ShowAlways = true;

            // ---------------------------------------------------------
            // 1. 顶部标题栏 (高度 S(32))
            // ---------------------------------------------------------
            Panel headerPanel = new Panel();
            headerPanel.Location = new Point(0, 0);
            headerPanel.Size = new Size(W, S(32));
            headerPanel.BackColor = Color.FromArgb(24, 25, 32);
            headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(38, 40, 52)), 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
            };

            Label lblTitle = new Label();
            lblTitle.Text = "AgentScreenGuard";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.0f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(S(12), S(7));
            headerPanel.Controls.Add(lblTitle);

            Label lblVer = new Label();
            lblVer.Text = "屏幕防烧屏与保活 (v1.0)";
            lblVer.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            lblVer.ForeColor = Color.FromArgb(140, 145, 158);
            lblVer.AutoSize = true;
            lblVer.Location = new Point(W - S(140), S(9));
            headerPanel.Controls.Add(lblVer);

            this.Controls.Add(headerPanel);

            // ---------------------------------------------------------
            // 2. 中间 4 个模式卡片 (每行精准高 S(27)，行间距 S(3)，完全紧贴文字与按钮)
            // ---------------------------------------------------------
            int startY = S(36);
            int rowH = S(27);
            int rowGap = S(3);
            int rowW = W - S(20);

            // 模式 1: 一键即时熄屏
            this.Controls.Add(CreateModeRow(
                S(10), startY + 0 * (rowH + rowGap), rowW, rowH,
                "⚡ 硬件熄屏 (DPMS)",
                "[Ctrl+Alt+S]",
                "⚠ 本机实测警告（AMD 780M + 无 S3 固件）\n这条熄屏指令会被固件直接映射成「进入新型待机 S0ix」：整机挂起、后台 Agent 全部中断，SetThreadExecutionState / PowerSetRequest 都拦不住。\n\n【实现机理】\n广播 SC_MONITORPOWER 指示 GPU 关断显示链路 (DPMS D3)：OLED 像素归零，LCD 背光断电。\n• 仅适合「短暂离开、此刻无长任务」的场景。\n• 通宵跑 Agent 请改用 🌑 护屏保活（下一项）。",
                Color.FromArgb(56, 189, 248),
                Color.FromArgb(2, 132, 199),
                delegate { ExecuteScreenOff(); }
            ));

            // 模式 2: Agent 守护保活 (防休眠)
            this.Controls.Add(CreateModeRow(
                S(10), startY + 1 * (rowH + rowGap), rowW, rowH,
                "🌑 护屏保活 (屏黑·不休眠)",
                "[推荐 · Ctrl+Alt+G]",
                "【护屏保活实现机理 —— 本机唯一可行的「屏灭而不进待机」路径】\n实测结论：本机无 S3，只有 Modern Standby (S0ix)，且固件把 DPMS 硬件熄屏直接映射为进入新型待机，任何内核电源锁都无效。\n所以本模式不向系统发「关屏」指令，而是：\n① 把内置屏背光压到 0（WMI WmiMonitorBrightnessMethods，失败则退化为 gamma 压暗）。\n② 跨屏覆盖一层纯黑置顶窗体，物理上遮住全部像素。\n③ 同时挂三层保活：PowerCreateRequest/PowerSetRequest 内核请求 + 每 10 秒重申 ES_SYSTEM_REQUIRED 的后台线程 + 电源方案空闲超时临时归零。\n• 系统始终认为屏幕开着 → 永不进入新型待机，CPU、内存、网络、后台 Agent 全速运行。\n• 退出（Esc / 双击）即精确还原背光、gamma 与电源策略。",
                Color.FromArgb(52, 211, 153),
                Color.FromArgb(5, 150, 105),
                delegate { StartGuardMode(); }
            ));

            // 模式 3: 锁定工作站并熄屏
            this.Controls.Add(CreateModeRow(
                S(10), startY + 2 * (rowH + rowGap), rowW, rowH,
                "🔒 锁定工作站并熄屏",
                "[Win+L 锁屏]",
                "【锁定工作站并熄屏实现机理】\n先调 Win32 API LockWorkStation() 锁屏，随后硬件切断显示信号。\n• 适合离开工位、外出防窥。\n• 点亮屏幕需输入 Windows 密码。\n⚠ 本机同样会触发新型待机（锁屏+熄屏指令），长任务期间请勿使用。",
                Color.FromArgb(167, 139, 250),
                Color.FromArgb(109, 40, 217),
                delegate { ExecuteLockAndTurnOff(); }
            ));

            // 模式 4: OLED 纯黑置顶幕布
            this.Controls.Add(CreateModeRow(
                S(10), startY + 3 * (rowH + rowGap), rowW, rowH,
                "⬛ 纯黑幕布 (不改背光)",
                "[Ctrl+Alt+B]",
                "【纯黑幕布实现机理】\n跨多屏覆盖无边框全黑窗体 (#000000)，不切断 GPU 显示链路、不调背光。\n• 帧缓冲与分辨率完好保留，pyautogui / CV 截图仍能取到完整桌面画面。\n• 与 🌑 护屏保活的区别：不压背光（LCD 背光仍亮）。\n• 按 Esc 键或双击即可秒速退出。",
                Color.FromArgb(251, 191, 36),
                Color.FromArgb(217, 119, 6),
                delegate { BlackMaskForm.ShowMask(false); }
            ));

            // ---------------------------------------------------------
            // 3. 底部选择项面板 (双栏紧凑排布，高度 S(52))
            // ---------------------------------------------------------
            int settingsY = startY + 4 * (rowH + rowGap) + S(3);
            Panel settingsPanel = new Panel();
            settingsPanel.Location = new Point(0, settingsY);
            settingsPanel.Size = new Size(W, S(52));
            settingsPanel.BackColor = Color.FromArgb(22, 23, 29);
            settingsPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(32, 34, 44)), 0, 0, settingsPanel.Width, 0);
            };

            int leftColX = S(12);
            int rightColX = W / 2 + S(4);

            // 左栏第 1 行：延时
            Label lblDelay = new Label();
            lblDelay.Text = "延时:";
            lblDelay.AutoSize = true;
            lblDelay.Font = new Font("Microsoft YaHei UI", 8.25f);
            lblDelay.ForeColor = Color.FromArgb(200, 205, 215);
            lblDelay.Location = new Point(leftColX, S(6));
            settingsPanel.Controls.Add(lblDelay);

            _cboDelay = new ComboBox();
            _cboDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDelay.Items.AddRange(new object[] { "500 ms", "800 ms", "1000 ms (推荐)", "1500 ms", "2000 ms" });
            _cboDelay.SelectedIndex = 2;
            _cboDelay.Size = new Size(S(115), S(21));
            _cboDelay.Font = new Font("Microsoft YaHei UI", 8.25f);
            _cboDelay.BackColor = Color.FromArgb(34, 36, 44);
            _cboDelay.ForeColor = Color.White;
            _cboDelay.Location = new Point(leftColX + S(36), S(3));
            settingsPanel.Controls.Add(_cboDelay);

            // 右栏第 1 行：保持常驻防休眠
            _chkKeepAlive = new CheckBox();
            _chkKeepAlive.Text = "保持系统常驻防休眠";
            _chkKeepAlive.AutoSize = true;
            _chkKeepAlive.Font = new Font("Microsoft YaHei UI", 8.25f);
            _chkKeepAlive.ForeColor = Color.FromArgb(200, 205, 215);
            _chkKeepAlive.Location = new Point(rightColX, S(5));
            _chkKeepAlive.CheckedChanged += delegate
            {
                PowerManager.SetKeepAlive(_chkKeepAlive.Checked);
                UpdatePowerStatusDisplay();
            };
            settingsPanel.Controls.Add(_chkKeepAlive);

            // 左栏第 2 行：最小化到托盘
            _chkMinToTray = new CheckBox();
            _chkMinToTray.Text = "关闭时最小化到托盘";
            _chkMinToTray.Checked = true;
            _chkMinToTray.AutoSize = true;
            _chkMinToTray.Font = new Font("Microsoft YaHei UI", 8.25f);
            _chkMinToTray.ForeColor = Color.FromArgb(140, 145, 158);
            _chkMinToTray.Location = new Point(leftColX, S(28));
            settingsPanel.Controls.Add(_chkMinToTray);

            // 右栏第 2 行：开机自启 + 诊断
            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机随系统启动";
            _chkAutoStart.Checked = CheckIsAutoStart();
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.Font = new Font("Microsoft YaHei UI", 8.25f);
            _chkAutoStart.ForeColor = Color.FromArgb(140, 145, 158);
            _chkAutoStart.Location = new Point(rightColX, S(28));
            _chkAutoStart.CheckedChanged += delegate
            {
                SetAutoStart(_chkAutoStart.Checked);
            };
            settingsPanel.Controls.Add(_chkAutoStart);

            Button btnDiag = new Button();
            btnDiag.Text = "诊断";
            btnDiag.Size = new Size(S(42), S(19));
            btnDiag.FlatStyle = FlatStyle.Flat;
            btnDiag.FlatAppearance.BorderColor = Color.FromArgb(55, 60, 72);
            btnDiag.BackColor = Color.FromArgb(34, 36, 44);
            btnDiag.ForeColor = Color.FromArgb(170, 175, 185);
            btnDiag.Font = new Font("Microsoft YaHei UI", 7.5f);
            btnDiag.Cursor = Cursors.Hand;
            btnDiag.Location = new Point(rightColX + S(114), S(27));
            btnDiag.Click += delegate { ShowDiagnostics(); };
            settingsPanel.Controls.Add(btnDiag);

            this.Controls.Add(settingsPanel);

            // ---------------------------------------------------------
            // 4. 底部系统状态栏 (高度 S(21))
            // ---------------------------------------------------------
            int statusY = settingsY + S(52);
            Panel bottomStatusBar = new Panel();
            bottomStatusBar.Location = new Point(0, statusY);
            bottomStatusBar.Size = new Size(W, S(21));
            bottomStatusBar.BackColor = Color.FromArgb(16, 17, 22);
            bottomStatusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(30, 32, 42)), 0, 0, bottomStatusBar.Width, 0);
            };

            _lblStatusPower = new Label();
            _lblStatusPower.Text = "⚡ 交流电源 (已插电)";
            _lblStatusPower.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            _lblStatusPower.AutoSize = true;
            _lblStatusPower.Location = new Point(S(12), S(2));
            bottomStatusBar.Controls.Add(_lblStatusPower);

            _lblStatusGuard = new Label();
            _lblStatusGuard.Text = "🛡️ 保活: 未激活";
            _lblStatusGuard.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Regular);
            _lblStatusGuard.ForeColor = Color.FromArgb(140, 145, 158);
            _lblStatusGuard.AutoSize = true;
            _lblStatusGuard.Location = new Point(W - S(105), S(2));
            bottomStatusBar.Controls.Add(_lblStatusGuard);

            this.Controls.Add(bottomStatusBar);

            // 保证窗体客户端高度刚好包住状态栏底部
            this.ClientSize = new Size(W, statusY + S(21));
        }

        // =============================================================
        // 单个模式行组件 (高度完全匹配文字内容，悬停图标展示实现方式)
        // =============================================================
        private Panel CreateModeRow(int x, int y, int width, int height,
            string title, string shortcutHint, string tooltipDetail, Color titleColor, Color btnColor, EventHandler onAction)
        {
            Panel row = new Panel();
            row.Location = new Point(x, y);
            row.Size = new Size(width, height);
            row.BackColor = Color.FromArgb(25, 26, 33);
            row.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, row.ClientRectangle, Color.FromArgb(36, 38, 48), ButtonBorderStyle.Solid);
            };

            // 标题 (精确测量像素宽度，绝无重叠截断)
            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = new Font("Microsoft YaHei UI", 8.75f, FontStyle.Bold);
            lblTitle.ForeColor = titleColor;
            Size titleSize = TextRenderer.MeasureText(title, lblTitle.Font);
            lblTitle.Size = titleSize;
            lblTitle.Location = new Point(S(8), Math.Max(0, (height - titleSize.Height) / 2));

            // 快捷键标签 (紧随标题之后，动态精确排布)
            Label lblHint = new Label();
            lblHint.Text = shortcutHint;
            lblHint.Font = new Font("Microsoft YaHei UI", 7.75f, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(120, 125, 138);
            Size hintSize = TextRenderer.MeasureText(shortcutHint, lblHint.Font);
            lblHint.Size = hintSize;
            lblHint.Location = new Point(lblTitle.Right + S(6), Math.Max(0, (height - hintSize.Height) / 2));

            // 悬停信息图标 (紧随标签之后)
            Label lblInfo = new Label();
            lblInfo.Text = "ⓘ";
            lblInfo.Font = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Bold);
            lblInfo.ForeColor = Color.FromArgb(125, 130, 145);
            Size infoSize = TextRenderer.MeasureText("ⓘ", lblInfo.Font);
            lblInfo.Size = infoSize;
            lblInfo.Cursor = Cursors.Help;
            lblInfo.Location = new Point(lblHint.Right + S(4), Math.Max(0, (height - infoSize.Height) / 2));

            lblInfo.MouseEnter += delegate { lblInfo.ForeColor = Color.FromArgb(56, 189, 248); };
            lblInfo.MouseLeave += delegate { lblInfo.ForeColor = Color.FromArgb(125, 130, 145); };

            _infoToolTip.SetToolTip(lblInfo, tooltipDetail);
            _infoToolTip.SetToolTip(lblTitle, tooltipDetail);

            // 右侧统一“启动”按钮
            Button btnStart = new Button();
            btnStart.Text = "启动";
            btnStart.Size = new Size(S(54), S(21));
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.FlatAppearance.BorderSize = 0;
            btnStart.BackColor = btnColor;
            btnStart.ForeColor = Color.White;
            btnStart.Font = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Bold);
            btnStart.Cursor = Cursors.Hand;
            btnStart.Location = new Point(width - btnStart.Width - S(4), Math.Max(0, (height - btnStart.Height) / 2));
            btnStart.Click += onAction;

            row.Controls.Add(lblTitle);
            row.Controls.Add(lblHint);
            row.Controls.Add(lblInfo);
            row.Controls.Add(btnStart);

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

            _trayMenu.Items.Add("🌑 护屏模式：屏黑·整机不休眠 (Ctrl+Alt+G)", null, delegate { StartGuardMode(); });
            _trayMenu.Items.Add("恢复屏幕（结束护屏）", null, delegate { BlackMaskForm.HideMask(); });
            _trayMenu.Items.Add("⚡ 硬件熄屏（会进新型待机） (Ctrl+Alt+S)", null, delegate { ExecuteScreenOff(); });
            _trayMenu.Items.Add("⬛ 纯黑幕布（不改背光） (Ctrl+Alt+B)", null, delegate { BlackMaskForm.ShowMask(false); });
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
            PowerManager.RegisterHotKey(this.Handle, HOTKEY_ID_GUARD, PowerManager.MOD_CONTROL | PowerManager.MOD_ALT, (uint)Keys.G);
        }

        private void UnregisterGlobalHotkeys()
        {
            PowerManager.UnregisterHotKey(this.Handle, HOTKEY_ID_SCREEN_OFF);
            PowerManager.UnregisterHotKey(this.Handle, HOTKEY_ID_BLACK_MASK);
            PowerManager.UnregisterHotKey(this.Handle, HOTKEY_ID_GUARD);
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
                else if (id == HOTKEY_ID_GUARD)
                {
                    StartGuardMode();
                }
                else if (id == HOTKEY_ID_BLACK_MASK)
                {
                    BlackMaskForm.ShowMask(false);
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

        /// <summary>
        /// 护屏模式：背光归零 + 全黑幕布 + 三层保活。
        /// 本机固件会把 DPMS 熄屏映射成新型待机，这是唯一能做到「屏幕不亮、整机照常跑」的路径。
        /// </summary>
        private void StartGuardMode()
        {
            PowerManager.SetKeepAlive(true);
            if (_chkKeepAlive != null) _chkKeepAlive.Checked = true;
            UpdatePowerStatusDisplay();
            BlackMaskForm.ShowMask(true);
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

                    string info = "本机平台事实（已实测，三次对照实验）:\n" +
                                  "• 无 S3，仅 Modern Standby (S0ix)；AMD 780M 平台把 DPMS 熄屏直接映射为进入新型待机。\n" +
                                  "• 关屏瞬间即写 Kernel-Power 506，SetThreadExecutionState(DISPLAY/SYSTEM/AWAYMODE)、\n" +
                                  "  PowerSetRequest(SystemRequired/ExecutionRequired) 全部被固件无视。\n" +
                                  "• DDC/CI (VCP 0xD6) 返回 ERROR_GEN_FAILURE，不支持显示器级硬关屏。\n" +
                                  "• 因此护屏模式不关屏：背光压 0 + 全黑窗体，系统始终认为屏幕开着。\n\n" +
                                  "电源策略原始输出节选:\n" + output.Substring(0, Math.Min(output.Length, 800));

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
