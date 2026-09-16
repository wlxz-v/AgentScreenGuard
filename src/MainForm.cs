using System;
using System.Collections.Generic;
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

        // 基础设计参考尺寸 (96 DPI 下)
        private const int BaseDesignWidth = 760;
        private const int BaseDesignHeight = 780;

        // 动态 DPI 与拖动缩放引擎
        private float _baseDpiScale = 1.0f;
        private float _userZoom = 1.0f;
        private bool _autoScaleWithWindow = true;
        private bool _isUpdatingScale = false;
        private bool _isInitializing = true;

        public float CurrentScale
        {
            get { return _baseDpiScale * _userZoom; }
        }

        public int S(int pixel96)
        {
            return (int)Math.Round(pixel96 * CurrentScale);
        }

        public float Sf(float pixel96)
        {
            return (float)(pixel96 * CurrentScale);
        }

        // 字体安全缓存 (避免在频繁缩放与重绘中泄漏 GDI 对象)
        private readonly Dictionary<string, Font> _fontCache = new Dictionary<string, Font>();

        public Font GetScaledFont(string family, float basePoint, FontStyle style)
        {
            // 字体磅值按用户缩放比自适应缩放 (0.5pt 阶梯对齐，GDI 渲染极其平滑)
            float targetSize = Math.Max(7.5f, (float)Math.Round(basePoint * _userZoom * 2.0f) / 2.0f);
            string key = string.Format("{0}_{1:F1}_{2}", family, targetSize, (int)style);
            Font font;
            if (!_fontCache.TryGetValue(key, out font))
            {
                font = new Font(family, targetSize, style);
                _fontCache[key] = font;
            }
            return font;
        }

        // =============================================================
        // 控件引用与视图定义
        // =============================================================
        private class ActionCardView
        {
            public Panel CardPanel;
            public Label LblTitle;
            public Label LblDesc;
            public Label LblHint;
            public Panel BtnPanel;
            public Button ActionBtn;

            public string TitleText;
            public string DescText;
            public string HintText;
            public string ButtonText;
            public Color AccentColor;
            public Color ButtonColor;

            public float BaseTitlePt = 10.5f;
            public float BaseDescPt = 8.75f;
            public float BaseHintPt = 8.0f;
            public float BaseBtnPt = 9.25f;

            public void UpdateFonts(Func<string, float, FontStyle, Font> getFont)
            {
                LblTitle.Font = getFont("Microsoft YaHei UI", BaseTitlePt, FontStyle.Bold);
                LblDesc.Font = getFont("Microsoft YaHei UI", BaseDescPt, FontStyle.Regular);
                LblHint.Font = getFont("Microsoft YaHei UI", BaseHintPt, FontStyle.Regular);
                ActionBtn.Font = getFont("Microsoft YaHei UI", BaseBtnPt, FontStyle.Bold);
            }

            public void UpdateDimensions(int cardWidth, Func<int, int> S)
            {
                int paddingX = S(18);
                int paddingY = S(10);
                CardPanel.Padding = new Padding(paddingX, paddingY, paddingX, paddingY);

                int btnWidth = S(150);
                int btnHeight = S(42);
                int btnColWidth = S(165);

                ActionBtn.Size = new Size(btnWidth, btnHeight);
                BtnPanel.Size = new Size(btnColWidth, CardPanel.Height);

                // 计算文本区域可用宽度
                int textWidth = cardWidth - (paddingX * 2) - btnColWidth - S(14);
                if (textWidth < S(180)) textWidth = S(180);

                LblTitle.MaximumSize = new Size(textWidth, 0);
                LblDesc.MaximumSize = new Size(textWidth, 0);
                LblHint.MaximumSize = new Size(textWidth, 0);

                // 动态计算文字完整显示所需的实际像素高
                int titleH = LblTitle.PreferredHeight;
                int descH = LblDesc.PreferredHeight;
                int hintH = LblHint.PreferredHeight;

                int textTotalH = titleH + S(4) + descH + S(4) + hintH;
                int minH = Math.Max(S(115), btnHeight + (paddingY * 2) + S(8));
                int targetH = Math.Max(minH, textTotalH + (paddingY * 2));

                CardPanel.Height = targetH;
                BtnPanel.Height = targetH;

                // 按钮垂直水平居中
                ActionBtn.Location = new Point(
                    Math.Max(0, (BtnPanel.Width - ActionBtn.Width) / 2),
                    Math.Max(0, (BtnPanel.Height - ActionBtn.Height) / 2)
                );

                // 文字坐标自适应排布
                LblTitle.Location = new Point(paddingX, paddingY);
                LblTitle.Size = new Size(textWidth, titleH);

                LblDesc.Location = new Point(paddingX, paddingY + titleH + S(4));
                LblDesc.Size = new Size(textWidth, descH);

                LblHint.Location = new Point(paddingX, paddingY + titleH + S(4) + descH + S(4));
                LblHint.Size = new Size(textWidth, hintH);

                BtnPanel.Location = new Point(cardWidth - paddingX - btnColWidth, 0);
            }
        }

        // 顶栏与容器
        private Panel _headerPanel;
        private Label _lblAppTitle;
        private Label _lblSubtitle;
        private Label _lblStatusPower;
        private Label _lblStatusGuard;
        private Label _lblHeaderZoomBadge;

        private Panel _scrollContainer;
        private Panel _contentHost;
        private readonly List<ActionCardView> _actionCards = new List<ActionCardView>();

        // 卡片 5: 设置与缩放控制
        private Panel _cardSettings;
        private Label _lblDelay;
        private ComboBox _cboDelay;
        private CheckBox _chkKeepAlive;
        private CheckBox _chkMinToTray;
        private CheckBox _chkAutoStart;
        private CheckBox _chkAutoScale;
        private Label _lblZoom;
        private TrackBar _tbZoom;
        private Label _lblZoomVal;
        private Button _btnResetZoom;
        private Button _btnDiag;

        // 托盘与定时器
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private System.Windows.Forms.Timer _statusTimer;

        // 全局热键
        private const int HOTKEY_ID_SCREEN_OFF = 1001;
        private const int HOTKEY_ID_BLACK_MASK = 1002;

        public MainForm()
        {
            this.AutoScaleMode = AutoScaleMode.None; // 完全受控于高精度动态缩放引擎

            // 1. 初始化检测宿主显示器 DPI
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
            _baseDpiScale = (float)dpi / 96.0f;

            // 2. 窗口形态与抗频闪特性 (支持任意自由拖动缩放与最大化)
            this.Text = "AgentScreenGuard - 屏幕防烧屏与 Agent 运行保活";
            this.ClientSize = new Size(S(BaseDesignWidth), S(BaseDesignHeight));
            this.MinimumSize = new Size((int)(BaseDesignWidth * 0.82f * _baseDpiScale), (int)(BaseDesignHeight * 0.75f * _baseDpiScale));
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable; // 开启窗口拖动缩放
            this.MaximizeBox = true;                       // 开启最大化支持
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            this.BackColor = Color.FromArgb(16, 17, 21); // OLED 极深黑底
            this.ForeColor = Color.FromArgb(240, 240, 245);
            this.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);

            // 3. 构造界面与功能模块
            InitializeComponents();
            InitializeTrayIcon();
            RegisterGlobalHotkeys();
            UpdatePowerStatusDisplay();

            _isInitializing = false;

            // 4. 初次呈现自适应校核
            ApplyScale(1.0f, false);

            // 5. 电源状态周期守护 (每 5 秒刷新)
            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 5000;
            _statusTimer.Tick += delegate(object s, EventArgs e) { UpdatePowerStatusDisplay(); };
            _statusTimer.Start();
        }

        // =============================================================
        // 核心布局构造 (动态弹性流式布局，彻底消除文字截断与大面积重叠)
        // =============================================================
        private void InitializeComponents()
        {
            // ---------------------------------------------------------
            // 1. 顶部状态栏
            // ---------------------------------------------------------
            _headerPanel = new Panel();
            _headerPanel.Dock = DockStyle.Top;
            _headerPanel.Height = S(92);
            _headerPanel.BackColor = Color.FromArgb(22, 23, 29);
            _headerPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(38, 40, 50)), 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
            };

            // 主标题
            _lblAppTitle = new Label();
            _lblAppTitle.Text = "AgentScreenGuard";
            _lblAppTitle.ForeColor = Color.White;
            _lblAppTitle.TextAlign = ContentAlignment.MiddleLeft;
            _headerPanel.Controls.Add(_lblAppTitle);

            // 副标题
            _lblSubtitle = new Label();
            _lblSubtitle.Text = "Windows 屏幕防烧屏与 Agent 运行保活平台 (v1.0.0)";
            _lblSubtitle.ForeColor = Color.FromArgb(156, 163, 175);
            _lblSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            _headerPanel.Controls.Add(_lblSubtitle);

            // 电源状态徽章
            _lblStatusPower = new Label();
            _lblStatusPower.Text = "[ ⚡ 交流电源 (已插电) ]";
            _lblStatusPower.ForeColor = Color.FromArgb(56, 189, 248);
            _lblStatusPower.TextAlign = ContentAlignment.MiddleRight;
            _headerPanel.Controls.Add(_lblStatusPower);

            // 保活状态徽章
            _lblStatusGuard = new Label();
            _lblStatusGuard.Text = "[ 🛡️ 保活: 未激活 ]";
            _lblStatusGuard.ForeColor = Color.FromArgb(156, 163, 175);
            _lblStatusGuard.TextAlign = ContentAlignment.MiddleRight;
            _headerPanel.Controls.Add(_lblStatusGuard);

            // 顶栏实时缩放比率指示器 (点击可快速重置为 100%)
            _lblHeaderZoomBadge = new Label();
            _lblHeaderZoomBadge.Text = "[ 🔍 100% ]";
            _lblHeaderZoomBadge.ForeColor = Color.FromArgb(147, 197, 253);
            _lblHeaderZoomBadge.TextAlign = ContentAlignment.MiddleRight;
            _lblHeaderZoomBadge.Cursor = Cursors.Hand;
            _lblHeaderZoomBadge.Click += delegate
            {
                ApplyScale(1.0f, false);
            };
            _headerPanel.Controls.Add(_lblHeaderZoomBadge);

            this.Controls.Add(_headerPanel);

            // ---------------------------------------------------------
            // 2. 主体滚动卡片视口 (支持超限智能平滑滚动，无论怎么缩放均保证完整显示)
            // ---------------------------------------------------------
            _scrollContainer = new Panel();
            _scrollContainer.Dock = DockStyle.Fill;
            _scrollContainer.AutoScroll = true;
            _scrollContainer.BackColor = Color.FromArgb(16, 17, 21);

            _contentHost = new Panel();
            _contentHost.Location = new Point(0, 0);
            _contentHost.BackColor = Color.Transparent;
            _scrollContainer.Controls.Add(_contentHost);

            this.Controls.Add(_scrollContainer);

            // ---------------------------------------------------------
            // 3. 构建 4 大核心功能操作卡片
            // ---------------------------------------------------------
            // 卡片 1: 一键即时熄屏
            _actionCards.Add(CreateActionCard(
                "⚡ 一键即时熄屏",
                "硬件级切断显示信号 (DPMS D3)，OLED 0 nit 完全熄灭，LCD 背光断电。按键防抖，轻晃鼠标即醒。",
                "全局热键: Ctrl + Alt + S",
                "即刻熄屏",
                Color.FromArgb(56, 189, 248),
                Color.FromArgb(2, 132, 199),
                delegate { ExecuteScreenOff(); }
            ));

            // 卡片 2: Agent 运行守护 (防休眠)
            _actionCards.Add(CreateActionCard(
                "🛡️ Agent 运行守护 (防休眠)",
                "调用 Win32 SetThreadExecutionState，阻止整机进入 Modern Standby 睡眠挂起，CPU/网络全速运转。",
                "离开模式: 屏幕熄灭但后台推理/编译任务绝不中断",
                "守护并熄屏",
                Color.FromArgb(52, 211, 153),
                Color.FromArgb(5, 150, 105),
                delegate
                {
                    PowerManager.SetKeepAlive(true);
                    if (_chkKeepAlive != null) _chkKeepAlive.Checked = true;
                    UpdatePowerStatusDisplay();
                    ExecuteScreenOff();
                }
            ));

            // 卡片 3: 锁定工作站并熄屏
            _actionCards.Add(CreateActionCard(
                "🔒 锁定工作站并熄屏",
                "先执行 LockWorkStation (Win+L) 锁定当前桌面，再硬件切断显示信号，外出防窥更安全。",
                "适用工位离开、外出防窥与后台无人值守运行",
                "锁定并熄屏",
                Color.FromArgb(167, 139, 250),
                Color.FromArgb(109, 40, 217),
                delegate { ExecuteLockAndTurnOff(); }
            ));

            // 卡片 4: OLED 纯黑置顶幕布
            _actionCards.Add(CreateActionCard(
                "⬛ OLED 纯黑置顶幕布",
                "覆盖全屏纯黑窗体 (#000000)，OLED 0 nit 防烧屏且不切断 DPMS 链路，显存帧缓冲区与分辨率完好保留。",
                "全局热键: Ctrl + Alt + B (按 Esc 键退出) | 适用截屏/视觉识别 Agent",
                "全屏纯黑幕布",
                Color.FromArgb(251, 191, 36),
                Color.FromArgb(217, 119, 6),
                delegate { BlackMaskForm.ShowMask(); }
            ));

            foreach (var card in _actionCards)
            {
                _contentHost.Controls.Add(card.CardPanel);
            }

            // ---------------------------------------------------------
            // 4. 构建卡片 5: 设置、待机诊断与拖动缩放控制面板
            // ---------------------------------------------------------
            _cardSettings = CreateSettingsCard();
            _contentHost.Controls.Add(_cardSettings);
        }

        // =============================================================
        // 卡片组件工厂 (参数化高保真渲染，支持动态文本多行自适应折行)
        // =============================================================
        private ActionCardView CreateActionCard(string title, string desc, string hint, string buttonText,
            Color accentColor, Color buttonColor, EventHandler onAction)
        {
            var view = new ActionCardView();
            view.TitleText = title;
            view.DescText = desc;
            view.HintText = hint;
            view.ButtonText = buttonText;
            view.AccentColor = accentColor;
            view.ButtonColor = buttonColor;

            Panel card = new Panel();
            card.BackColor = Color.FromArgb(25, 26, 32);
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(40, 42, 52), ButtonBorderStyle.Solid);
            };
            view.CardPanel = card;

            // 标题
            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.ForeColor = accentColor;
            lblTitle.AutoSize = true;
            view.LblTitle = lblTitle;
            card.Controls.Add(lblTitle);

            // 描述 (支持自动折行与完整测量)
            Label lblDesc = new Label();
            lblDesc.Text = desc;
            lblDesc.ForeColor = Color.FromArgb(156, 163, 175);
            lblDesc.AutoSize = true;
            view.LblDesc = lblDesc;
            card.Controls.Add(lblDesc);

            // 提示/热键
            Label lblHint = new Label();
            lblHint.Text = hint;
            lblHint.ForeColor = Color.FromArgb(107, 114, 128);
            lblHint.AutoSize = true;
            view.LblHint = lblHint;
            card.Controls.Add(lblHint);

            // 右侧按钮容器
            Panel btnPanel = new Panel();
            btnPanel.BackColor = Color.Transparent;
            view.BtnPanel = btnPanel;

            Button btn = new Button();
            btn.Text = buttonText;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = buttonColor;
            btn.ForeColor = Color.White;
            btn.Cursor = Cursors.Hand;
            btn.Click += onAction;
            view.ActionBtn = btn;
            btnPanel.Controls.Add(btn);

            card.Controls.Add(btnPanel);

            return view;
        }

        // =============================================================
        // 卡片 5: 系统设置、诊断与缩放控制
        // =============================================================
        private Panel CreateSettingsCard()
        {
            Panel card = new Panel();
            card.BackColor = Color.FromArgb(25, 26, 32);
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(40, 42, 52), ButtonBorderStyle.Solid);
            };

            // 第一行：延时与防休眠
            _lblDelay = new Label();
            _lblDelay.Text = "防抖延时:";
            _lblDelay.AutoSize = true;
            _lblDelay.ForeColor = Color.FromArgb(209, 213, 219);
            card.Controls.Add(_lblDelay);

            _cboDelay = new ComboBox();
            _cboDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDelay.Items.AddRange(new object[] { "500 ms", "800 ms", "1000 ms (推荐)", "1500 ms", "2000 ms" });
            _cboDelay.SelectedIndex = 2;
            _cboDelay.BackColor = Color.FromArgb(34, 36, 44);
            _cboDelay.ForeColor = Color.White;
            card.Controls.Add(_cboDelay);

            _chkKeepAlive = new CheckBox();
            _chkKeepAlive.Text = "保持系统防休眠 (常驻阻止 Windows 睡眠挂起)";
            _chkKeepAlive.AutoSize = true;
            _chkKeepAlive.ForeColor = Color.FromArgb(209, 213, 219);
            _chkKeepAlive.CheckedChanged += delegate
            {
                PowerManager.SetKeepAlive(_chkKeepAlive.Checked);
                UpdatePowerStatusDisplay();
            };
            card.Controls.Add(_chkKeepAlive);

            // 第二行：托盘、开机自启、随窗口自适应缩放开关
            _chkMinToTray = new CheckBox();
            _chkMinToTray.Text = "关闭时最小化到系统托盘";
            _chkMinToTray.Checked = true;
            _chkMinToTray.AutoSize = true;
            _chkMinToTray.ForeColor = Color.FromArgb(156, 163, 175);
            card.Controls.Add(_chkMinToTray);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机随系统启动";
            _chkAutoStart.Checked = CheckIsAutoStart();
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.ForeColor = Color.FromArgb(156, 163, 175);
            _chkAutoStart.CheckedChanged += delegate
            {
                SetAutoStart(_chkAutoStart.Checked);
            };
            card.Controls.Add(_chkAutoStart);

            _chkAutoScale = new CheckBox();
            _chkAutoScale.Text = "随窗口拖动动态缩放";
            _chkAutoScale.Checked = _autoScaleWithWindow;
            _chkAutoScale.AutoSize = true;
            _chkAutoScale.ForeColor = Color.FromArgb(56, 189, 248);
            _chkAutoScale.CheckedChanged += delegate
            {
                _autoScaleWithWindow = _chkAutoScale.Checked;
                if (_autoScaleWithWindow)
                {
                    PerformWindowScaleAdaptation();
                }
            };
            card.Controls.Add(_chkAutoScale);

            // 第三行：拖动缩放滑块控制组 + 待机策略诊断
            _lblZoom = new Label();
            _lblZoom.Text = "🔍 界面缩放:";
            _lblZoom.AutoSize = true;
            _lblZoom.ForeColor = Color.FromArgb(209, 213, 219);
            card.Controls.Add(_lblZoom);

            _tbZoom = new TrackBar();
            _tbZoom.Minimum = 80;
            _tbZoom.Maximum = 200;
            _tbZoom.TickFrequency = 20;
            _tbZoom.SmallChange = 5;
            _tbZoom.LargeChange = 10;
            _tbZoom.Value = 100;
            _tbZoom.AutoSize = false;
            _tbZoom.BackColor = Color.FromArgb(25, 26, 32);
            _tbZoom.Scroll += delegate
            {
                float targetZoom = _tbZoom.Value / 100.0f;
                ApplyScale(targetZoom, false);
            };
            card.Controls.Add(_tbZoom);

            _lblZoomVal = new Label();
            _lblZoomVal.Text = "100%";
            _lblZoomVal.ForeColor = Color.FromArgb(56, 189, 248);
            _lblZoomVal.TextAlign = ContentAlignment.MiddleCenter;
            card.Controls.Add(_lblZoomVal);

            _btnResetZoom = new Button();
            _btnResetZoom.Text = "重置";
            _btnResetZoom.FlatStyle = FlatStyle.Flat;
            _btnResetZoom.FlatAppearance.BorderColor = Color.FromArgb(75, 85, 99);
            _btnResetZoom.BackColor = Color.FromArgb(34, 36, 44);
            _btnResetZoom.ForeColor = Color.FromArgb(209, 213, 219);
            _btnResetZoom.Cursor = Cursors.Hand;
            _btnResetZoom.Click += delegate
            {
                ApplyScale(1.0f, false);
            };
            card.Controls.Add(_btnResetZoom);

            _btnDiag = new Button();
            _btnDiag.Text = "待机策略诊断";
            _btnDiag.FlatStyle = FlatStyle.Flat;
            _btnDiag.FlatAppearance.BorderColor = Color.FromArgb(75, 85, 99);
            _btnDiag.BackColor = Color.FromArgb(34, 36, 44);
            _btnDiag.ForeColor = Color.FromArgb(209, 213, 219);
            _btnDiag.Cursor = Cursors.Hand;
            _btnDiag.Click += delegate { ShowDiagnostics(); };
            card.Controls.Add(_btnDiag);

            return card;
        }

        // =============================================================
        // 动态缩放引擎：字体更新与卡片排版重算
        // =============================================================
        public void ApplyScale(float newZoom, bool resizeWindow)
        {
            if (_isUpdatingScale) return;
            _isUpdatingScale = true;

            try
            {
                _userZoom = Math.Max(0.80f, Math.Min(2.20f, newZoom));
                int pct = (int)Math.Round(_userZoom * 100);

                if (_tbZoom != null && _tbZoom.Value != pct)
                {
                    _tbZoom.Value = Math.Max(80, Math.Min(200, pct));
                }
                if (_lblZoomVal != null)
                {
                    _lblZoomVal.Text = string.Format("{0}%", pct);
                }
                if (_lblHeaderZoomBadge != null)
                {
                    _lblHeaderZoomBadge.Text = string.Format("[ 🔍 {0}% ]", pct);
                }

                if (resizeWindow && this.WindowState == FormWindowState.Normal)
                {
                    this.ClientSize = new Size(S(BaseDesignWidth), S(BaseDesignHeight));
                }

                UpdateAllFonts();
                UpdateAllLayouts();
            }
            finally
            {
                _isUpdatingScale = false;
            }
        }

        private void UpdateAllFonts()
        {
            // 顶栏字体
            _lblAppTitle.Font = GetScaledFont("Microsoft YaHei UI", 15.0f, FontStyle.Bold);
            _lblSubtitle.Font = GetScaledFont("Microsoft YaHei UI", 8.75f, FontStyle.Regular);
            _lblStatusPower.Font = GetScaledFont("Microsoft YaHei UI", 8.75f, FontStyle.Bold);
            _lblStatusGuard.Font = GetScaledFont("Microsoft YaHei UI", 8.75f, FontStyle.Bold);
            _lblHeaderZoomBadge.Font = GetScaledFont("Microsoft YaHei UI", 8.5f, FontStyle.Bold);

            // 操作卡片字体
            foreach (var card in _actionCards)
            {
                card.UpdateFonts(GetScaledFont);
            }

            // 设置卡片字体
            if (_cardSettings != null)
            {
                _lblDelay.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
                _cboDelay.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
                _chkKeepAlive.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
                _chkMinToTray.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
                _chkAutoStart.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
                _chkAutoScale.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
                _lblZoom.Font = GetScaledFont("Microsoft YaHei UI", 9.0f, FontStyle.Bold);
                _lblZoomVal.Font = GetScaledFont("Microsoft YaHei UI", 8.75f, FontStyle.Bold);
                _btnResetZoom.Font = GetScaledFont("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
                _btnDiag.Font = GetScaledFont("Microsoft YaHei UI", 8.75f, FontStyle.Regular);
            }
        }

        private void UpdateAllLayouts()
        {
            if (_headerPanel == null || _scrollContainer == null) return;

            this.SuspendLayout();
            _scrollContainer.SuspendLayout();

            try
            {
                int clientW = this.ClientSize.Width;

                // 1. 顶栏排版
                UpdateHeaderLayout(clientW);

                // 2. 主体视口排版
                int paddingX = S(20);
                int availableWidth = Math.Max(S(480), _scrollContainer.ClientSize.Width - (paddingX * 2));
                int currentY = S(12);

                // 3. 逐一测量与排版各个操作卡片
                for (int i = 0; i < _actionCards.Count; i++)
                {
                    var card = _actionCards[i];
                    card.CardPanel.Width = availableWidth;
                    card.UpdateDimensions(availableWidth, S);
                    card.CardPanel.Location = new Point(paddingX, currentY);
                    currentY += card.CardPanel.Height + S(10);
                }

                // 4. 排版设置卡片 5
                if (_cardSettings != null)
                {
                    _cardSettings.Width = availableWidth;
                    UpdateSettingsCardLayout(availableWidth);
                    _cardSettings.Location = new Point(paddingX, currentY);
                    currentY += _cardSettings.Height + S(16);
                }

                // 5. 扩展视口总高度 (保证滚动条平滑工作)
                _contentHost.Size = new Size(_scrollContainer.ClientSize.Width, currentY);
            }
            finally
            {
                _scrollContainer.ResumeLayout(true);
                this.ResumeLayout(true);
            }
        }

        private void UpdateHeaderLayout(int width)
        {
            int paddingX = S(24);
            int paddingY = S(12);
            _headerPanel.Height = S(92);

            _lblAppTitle.Location = new Point(paddingX, paddingY);
            _lblAppTitle.Size = new Size(Math.Max(S(200), width / 2), S(34));

            _lblSubtitle.Location = new Point(paddingX, paddingY + S(34));
            _lblSubtitle.Size = new Size(Math.Max(S(240), width / 2 + S(60)), S(24));

            int rightEdge = width - paddingX;

            int powerW = S(180);
            _lblStatusPower.Size = new Size(powerW, S(24));
            _lblStatusPower.Location = new Point(rightEdge - powerW, paddingY + S(2));

            int guardW = S(210);
            _lblStatusGuard.Size = new Size(guardW, S(24));
            _lblStatusGuard.Location = new Point(rightEdge - guardW, paddingY + S(28));

            int zoomBadgeW = S(85);
            _lblHeaderZoomBadge.Size = new Size(zoomBadgeW, S(22));
            _lblHeaderZoomBadge.Location = new Point(rightEdge - guardW - zoomBadgeW - S(8), paddingY + S(29));
        }

        private void UpdateSettingsCardLayout(int cardWidth)
        {
            int paddingX = S(18);
            int paddingY = S(12);
            _cardSettings.Padding = new Padding(paddingX, paddingY, paddingX, paddingY);

            // 第一行：延时与常驻保活
            int curY = paddingY;
            _lblDelay.Location = new Point(paddingX, curY + S(4));
            _lblDelay.Size = _lblDelay.PreferredSize;

            int cboX = _lblDelay.Right + S(8);
            _cboDelay.Size = new Size(S(140), S(28));
            _cboDelay.Location = new Point(cboX, curY + S(1));

            int chkKeepX = _cboDelay.Right + S(20);
            _chkKeepAlive.Size = _chkKeepAlive.PreferredSize;

            if (chkKeepX + _chkKeepAlive.Width > cardWidth - paddingX)
            {
                // 空间不足时自动优雅换行，绝不重叠或被截断
                curY += Math.Max(_cboDelay.Height, _lblDelay.Height) + S(6);
                _chkKeepAlive.Location = new Point(paddingX, curY + S(2));
                curY += _chkKeepAlive.Height + S(8);
            }
            else
            {
                _chkKeepAlive.Location = new Point(chkKeepX, curY + S(3));
                curY += Math.Max(_cboDelay.Height, _chkKeepAlive.Height) + S(10);
            }

            // 第二行：复选框控制组 (托盘/自启/自适应缩放)
            _chkMinToTray.Size = _chkMinToTray.PreferredSize;
            _chkAutoStart.Size = _chkAutoStart.PreferredSize;
            _chkAutoScale.Size = _chkAutoScale.PreferredSize;

            int r2X = paddingX;
            _chkMinToTray.Location = new Point(r2X, curY);
            r2X += _chkMinToTray.Width + S(18);

            if (r2X + _chkAutoStart.Width > cardWidth - paddingX)
            {
                curY += _chkMinToTray.Height + S(6);
                r2X = paddingX;
            }
            _chkAutoStart.Location = new Point(r2X, curY);
            r2X += _chkAutoStart.Width + S(18);

            if (r2X + _chkAutoScale.Width > cardWidth - paddingX)
            {
                curY += _chkAutoStart.Height + S(6);
                r2X = paddingX;
            }
            _chkAutoScale.Location = new Point(r2X, curY);
            curY += Math.Max(_chkMinToTray.Height, Math.Max(_chkAutoStart.Height, _chkAutoScale.Height)) + S(12);

            // 第三行：缩放滑块 + 重置按钮 + 待机策略诊断按钮
            _lblZoom.Size = _lblZoom.PreferredSize;
            _lblZoom.Location = new Point(paddingX, curY + S(5));

            int tbX = _lblZoom.Right + S(8);
            _tbZoom.Size = new Size(S(135), S(28));
            _tbZoom.Location = new Point(tbX, curY + S(2));

            int valX = _tbZoom.Right + S(6);
            _lblZoomVal.Size = new Size(S(48), S(24));
            _lblZoomVal.Location = new Point(valX, curY + S(5));

            int resetX = _lblZoomVal.Right + S(4);
            _btnResetZoom.Size = new Size(S(56), S(28));
            _btnResetZoom.Location = new Point(resetX, curY + S(2));

            _btnDiag.Size = new Size(S(130), S(30));
            int diagX = cardWidth - paddingX - _btnDiag.Width;

            if (diagX < resetX + _btnResetZoom.Width + S(10))
            {
                curY += Math.Max(_tbZoom.Height, _btnResetZoom.Height) + S(8);
                _btnDiag.Location = new Point(paddingX, curY);
                curY += _btnDiag.Height;
            }
            else
            {
                _btnDiag.Location = new Point(diagX, curY + S(1));
                curY += Math.Max(_tbZoom.Height, _btnDiag.Height);
            }

            _cardSettings.Height = curY + paddingY;
        }

        // =============================================================
        // 拖动缩放交互事件监听
        // =============================================================
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_isInitializing) return;

            if (_autoScaleWithWindow && !_isUpdatingScale && this.WindowState != FormWindowState.Minimized)
            {
                PerformWindowScaleAdaptation();
            }
            else
            {
                UpdateAllLayouts();
            }
        }

        private void PerformWindowScaleAdaptation()
        {
            // 基于基准窗口尺寸动态计算用户理想缩放比
            float fitW = (float)this.ClientSize.Width / (BaseDesignWidth * _baseDpiScale);
            float fitH = (float)this.ClientSize.Height / (BaseDesignHeight * _baseDpiScale);
            float newZoom = Math.Min(fitW, fitH);

            // 0.05 步长对齐，杜绝鼠标拖拽微小抖动时的频繁重绘
            newZoom = (float)Math.Round(newZoom * 20.0f) / 20.0f;
            newZoom = Math.Max(0.80f, Math.Min(2.20f, newZoom));

            if (Math.Abs(newZoom - _userZoom) >= 0.04f)
            {
                ApplyScale(newZoom, false);
            }
            else
            {
                UpdateAllLayouts();
            }
        }

        // 支持 Ctrl + 滚轮平滑缩放
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                int delta = e.Delta > 0 ? 5 : -5;
                int targetVal = Math.Max(80, Math.Min(200, _tbZoom.Value + delta));
                if (targetVal != _tbZoom.Value)
                {
                    _tbZoom.Value = targetVal;
                    float targetZoom = targetVal / 100.0f;
                    ApplyScale(targetZoom, false);
                }
                return;
            }
            base.OnMouseWheel(e);
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

            // 清理缓存字体，释放 GDI 句柄
            foreach (var font in _fontCache.Values)
            {
                try { font.Dispose(); } catch { }
            }
            _fontCache.Clear();

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
