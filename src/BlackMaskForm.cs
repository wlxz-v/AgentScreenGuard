using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgentScreenGuard
{
    /// <summary>
    /// OLED 纯黑置顶防烧屏幕布
    /// 针对依赖屏幕截取 (pyautogui/CV 视觉识别) 的自动化 Agent 设计：
    /// 1. 覆盖所有活动显示器（支持多显示器跨屏）；
    /// 2. OLED 屏幕各有机子像素发光强度直接归零 (0.0000 nits)；
    /// 3. 不切断 GPU 显示链路 (不触发 DPMS D3)，显存与原生分辨率完好保留；
    /// 4. 按 Esc、空格或鼠标单击立即退出。
    /// </summary>
    public class BlackMaskForm : Form
    {
        private static BlackMaskForm _currentInstance = null;

        public BlackMaskForm()
        {
            this.Text = "AgentScreenGuard - OLED Black Mask";
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;

            // 计算跨多显示器的联合虚拟屏幕尺寸
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.X < minX) minX = screen.Bounds.X;
                if (screen.Bounds.Y < minY) minY = screen.Bounds.Y;
                if (screen.Bounds.Right > maxX) maxX = screen.Bounds.Right;
                if (screen.Bounds.Bottom > maxY) maxY = screen.Bounds.Bottom;
            }
            this.Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(OnKeyDownHandler);
            this.Click += new EventHandler(OnClickHandler);
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                ExitMask();
            }
        }

        private void OnClickHandler(object sender, EventArgs e)
        {
            ExitMask();
        }

        private void ExitMask()
        {
            Cursor.Show();
            this.Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Cursor.Hide();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Cursor.Show();
            _currentInstance = null;
        }

        public static void ShowMask()
        {
            if (_currentInstance != null && !_currentInstance.IsDisposed)
            {
                _currentInstance.Activate();
                return;
            }

            _currentInstance = new BlackMaskForm();
            _currentInstance.Show();
        }
    }
}
