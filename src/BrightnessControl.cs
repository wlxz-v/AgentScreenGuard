using System;
using System.Management;
using System.Runtime.InteropServices;

namespace AgentScreenGuard
{
    /// <summary>
    /// 背光亮度控制（面板“假黑屏”方案的核心）。
    ///
    /// 本机实测：AMD 平台的固件会把「DPMS 硬件熄屏」直接映射成进入新型待机 (S0ix)，
    /// 任何 SetThreadExecutionState / PowerSetRequest 标志都拦不住，DDC/CI 也不支持。
    /// 因此改为「不告诉系统关屏」：把背光压到 0 并盖一层纯黑置顶窗体，
    /// 显示器在物理上仍然“开着”，系统永远不会判定为屏幕关闭，从而全程不进入待机。
    ///
    /// 优先走 WMI (root\WMI:WmiMonitorBrightnessMethods)；不支持时退化为 gamma ramp 压暗。
    /// </summary>
    public static class BrightnessControl
    {
        private static readonly object _sync = new object();
        private static int _savedBrightness = -1;
        private static ushort[] _savedGamma = null;
        private static bool _dimmed = false;

        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] lpRamp);

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] lpRamp);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        public static bool IsDimmed
        {
            get { lock (_sync) { return _dimmed; } }
        }

        /// <summary>读取当前亮度；不支持时返回 -1</summary>
        public static int GetBrightness()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object v = mo["CurrentBrightness"];
                        if (v != null) return Convert.ToInt32(v);
                    }
                }
            }
            catch
            {
            }
            return -1;
        }

        /// <summary>设置亮度（0-100）；返回是否成功</summary>
        public static bool SetBrightness(int value)
        {
            if (value < 0) value = 0;
            if (value > 100) value = 100;

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                {
                    bool applied = false;
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        using (mo)
                        {
                            try
                            {
                                mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)value });
                                applied = true;
                            }
                            catch
                            {
                            }
                        }
                    }
                    if (applied) return true;
                }
            }
            catch
            {
            }

            // 退化方案：gamma ramp 压到接近全黑
            return SetGammaDim(value == 0);
        }

        private static bool SetGammaDim(bool dim)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;

            try
            {
                ushort[] ramp = new ushort[3 * 256];
                if (dim)
                {
                    for (int i = 0; i < 256; i++)
                    {
                        ushort v = (ushort)Math.Max(0, Math.Min(65535, i * 8));
                        ramp[i] = v;
                        ramp[256 + i] = v;
                        ramp[512 + i] = v;
                    }
                }
                else
                {
                    for (int i = 0; i < 256; i++)
                    {
                        ushort v = (ushort)(i * 257);
                        ramp[i] = v;
                        ramp[256 + i] = v;
                        ramp[512 + i] = v;
                    }
                }
                return SetDeviceGammaRamp(hdc, ramp);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        /// <summary>进入“假黑屏”：背光归零（并相机保留原值）</summary>
        public static void DimForMask()
        {
            lock (_sync)
            {
                if (_dimmed) return;

                int cur = GetBrightness();
                _savedBrightness = cur;

                // 备份 gamma，作为最后一道兜底
                try
                {
                    IntPtr hdc = GetDC(IntPtr.Zero);
                    if (hdc != IntPtr.Zero)
                    {
                        ushort[] ramp = new ushort[3 * 256];
                        if (GetDeviceGammaRamp(hdc, ramp)) _savedGamma = ramp;
                        ReleaseDC(IntPtr.Zero, hdc);
                    }
                }
                catch
                {
                }

                if (cur >= 0)
                {
                    SetBrightness(0);
                }
                else
                {
                    SetGammaDim(true);
                }

                _dimmed = true;
            }
        }

        /// <summary>退出“假黑屏”：还原原始亮度 / gamma</summary>
        public static void RestoreAfterMask()
        {
            lock (_sync)
            {
                if (!_dimmed) return;

                if (_savedBrightness >= 0)
                {
                    SetBrightness(_savedBrightness);
                }
                else
                {
                    try
                    {
                        IntPtr hdc = GetDC(IntPtr.Zero);
                        if (hdc != IntPtr.Zero)
                        {
                            if (_savedGamma != null) SetDeviceGammaRamp(hdc, _savedGamma);
                            else SetGammaDim(false);
                            ReleaseDC(IntPtr.Zero, hdc);
                        }
                    }
                    catch
                    {
                    }
                }

                _dimmed = false;
                _savedBrightness = -1;
                _savedGamma = null;
            }
        }
    }
}
