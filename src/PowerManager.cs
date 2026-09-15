using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgentScreenGuard
{
    public static class PowerManager
    {
        // -------------------------------------------------------------
        // Win32 API P/Invoke
        // -------------------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // -------------------------------------------------------------
        // Win32 Constants
        // -------------------------------------------------------------
        public const int HWND_BROADCAST = 0xFFFF;
        public const uint WM_SYSCOMMAND = 0x0112;
        public const int SC_MONITORPOWER = 0xF170;

        public const int MONITOR_ON = -1;
        public const int MONITOR_STANDBY = 1;
        public const int MONITOR_OFF = 2;

        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;
        public const uint ES_AWAYMODE_REQUIRED = 0x00000040;

        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_MBUTTON = 0x04;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WM_HOTKEY = 0x0312;

        // Current keep-alive state
        private static bool _isKeepAliveActive = false;
        public static bool IsKeepAliveActive
        {
            get { return _isKeepAliveActive; }
        }

        // -------------------------------------------------------------
        // Public Actions
        // -------------------------------------------------------------

        /// <summary>
        /// 等待物理按键释放，并延时指定毫秒数
        /// </summary>
        public static void WaitInputRelease(int delayMs)
        {
            int start = Environment.TickCount;
            // 轮询等待鼠标按键全部释放（防抖 1500ms 保护）
            while (Environment.TickCount - start < 1500)
            {
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool rightDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
                bool midDown = (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
                if (!leftDown && !rightDown && !midDown)
                {
                    break;
                }
                Thread.Sleep(50);
            }

            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }

        /// <summary>
        /// 异步执行硬件熄屏 (DPMS D3 硬件切断)
        /// </summary>
        public static void TurnOffScreenAsync(int delayMs, Action onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    WaitInputRelease(delayMs);
                    SendMessage(new IntPtr(HWND_BROADCAST), WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(MONITOR_OFF));
                }
                catch
                {
                }
                finally
                {
                    if (onComplete != null)
                    {
                        onComplete();
                    }
                }
            });
        }

        /// <summary>
        /// 锁定 Windows 工作站并切断显示
        /// </summary>
        public static void LockAndTurnOffAsync(int delayMs, Action onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    LockWorkStation();
                    WaitInputRelease(delayMs);
                    SendMessage(new IntPtr(HWND_BROADCAST), WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(MONITOR_OFF));
                }
                catch
                {
                }
                finally
                {
                    if (onComplete != null)
                    {
                        onComplete();
                    }
                }
            });
        }

        /// <summary>
        /// 切换系统防休眠保活状态
        /// </summary>
        public static bool SetKeepAlive(bool enable)
        {
            if (enable)
            {
                uint flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED;
                uint result = SetThreadExecutionState(flags);
                _isKeepAliveActive = (result != 0);
                return _isKeepAliveActive;
            }
            else
            {
                SetThreadExecutionState(ES_CONTINUOUS);
                _isKeepAliveActive = false;
                return true;
            }
        }
    }
}
