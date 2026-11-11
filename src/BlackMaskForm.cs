using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgentScreenGuard
{
    /// <summary>
    /// 纯黑置顶护屏幕布（假黑屏方案）。
    ///
    /// 本机实测结论：AMD 平台固件把 DPMS 硬件熄屏直接映射为进入新型待机 (S0ix)，
    /// 任何 SetThreadExecutionState / PowerSetRequest 都拦不住，DDC/CI 也不支持。
    /// 因此这里不向系统发“关屏”指令，而是：
    /// 1. 把内置屏背光压到 0（BlackMaskForm 关闭时精确还原）；
    /// 2. 覆盖所有活动显示器，铺一层纯黑置顶窗体；
    /// 3. 系统始终认为显示器是开着的，于是永远不会触发新型待机，CPU/网络/Agent 全程满速。
    /// 4. 按 Esc / 空格 / 回车，或鼠标单击立即退出并还原背光。
    /// </summary>
    public class BlackMaskForm : Form
    {
        private static BlackMaskForm _currentInstance = null;
        private bool _isCursorHidden = false;
        private bool _dim;
        private DateTime _shownAt;

        public BlackMaskForm(bool dim)
        {
            _dim = dim;

            this.Text = "AgentScreenGuard - 纯黑护屏";
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;

            // 跨多显示器联合虚拟屏幕覆盖
            this.Bounds = SystemInformation.VirtualScreen;

            this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(OnKeyDownHandler);
            this.MouseDown += new MouseEventHandler(OnMouseDownHandler);
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                ExitMask();
            }
        }

        // 只认双击：pyautogui / 自动化脚本的单击不会误关护屏
        private void OnMouseDownHandler(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 1200) return;
            if (e.Clicks >= 2) ExitMask();
        }

        private void ExitMask()
        {
            RestoreCursor();
            this.Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _shownAt = DateTime.UtcNow;

            if (_dim)
            {
                try { BrightnessControl.DimForMask(); }
                catch { }
            }

            if (!_isCursorHidden)
            {
                try
                {
                    Cursor.Hide();
                    _isCursorHidden = true;
                }
                catch
                {
                }
            }
        }

        private void RestoreCursor()
        {
            if (_isCursorHidden)
            {
                try
                {
                    Cursor.Show();
                }
                catch
                {
                }
                _isCursorHidden = false;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            RestoreCursor();

            if (_dim)
            {
                try { BrightnessControl.RestoreAfterMask(); }
                catch { }
            }

            _currentInstance = null;
        }

        public static bool IsShowing
        {
            get { return _currentInstance != null && !_currentInstance.IsDisposed; }
        }

        public static void ShowMask()
        {
            ShowMask(false);
        }

        /// <summary>dim=true 时同时把背光压到 0（安全模式：屏灭但系统绝不进待机）</summary>
        public static void ShowMask(bool dim)
        {
            if (_currentInstance != null && !_currentInstance.IsDisposed)
            {
                if (dim) BrightnessControl.DimForMask();
                _currentInstance.Activate();
                return;
            }

            _currentInstance = new BlackMaskForm(dim);
            _currentInstance.Show();
        }

        public static void HideMask()
        {
            if (_currentInstance != null && !_currentInstance.IsDisposed)
            {
                _currentInstance.Close();
            }
        }
    }
}
