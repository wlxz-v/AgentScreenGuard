using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgentScreenGuard
{
    /// <summary>
    /// 电源与屏幕控制核心。
    ///
    /// 目标：屏幕熄灭，但整机不进入睡眠 / 新型待机（Modern Standby S0ix），
    /// 后台 Agent 任务、CPU、网络照常运转。
    ///
    /// 本机（无 S3 的 Modern Standby 机型）实测：仅调用 SetThreadExecutionState 时，
    /// 一旦显示器关闭（SC_MONITORPOWER），系统仍会进入「新型待机」。因此保活采用三层防线：
    ///   1) PowerCreateRequest + PowerSetRequest(SystemRequired / ExecutionRequired)
    ///      进程级内核电源请求，句柄存活期间系统不会入睡、进程不会被挂起。
    ///   2) 常驻心跳线程持续持有 ES_CONTINUOUS | ES_SYSTEM_REQUIRED
    ///      （该状态是“线程级”的，必须由长活线程持有并周期性重申）。
    ///   3) 临时把当前电源方案的“空闲睡眠 / 休眠”超时改为 0（永不），退出时精确还原。
    /// 第 3 层写入备份文件，即使程序被强杀，下次启动也会自动还原原值。
    /// </summary>
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
        // 电源请求 API (Windows 8+，请求句柄生命周期 = 进程生命周期)
        // -------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct REASON_CONTEXT
        {
            public uint Version;
            public uint Flags;
            public IntPtr SimpleReasonString;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        // -------------------------------------------------------------
        // 电源方案读写 API (powrprof.dll)
        // -------------------------------------------------------------
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint valueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint valueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint valueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint valueIndex);

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

        // POWER_REQUEST_TYPE
        private const int PowerRequestSystemRequired = 1;
        private const int PowerRequestExecutionRequired = 3;

        // REASON_CONTEXT
        private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
        private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 1;

        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_MBUTTON = 0x04;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12; // Alt key
        public const int VK_S = 0x53;
        public const int VK_B = 0x42;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WM_HOTKEY = 0x0312;

        // 电源设置 GUID（非 readonly：P/Invoke 需要按 ref 传参）
        private static Guid SUB_SLEEP = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static Guid STANDBYIDLE = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        private static Guid HIBERNATEIDLE = new Guid("9d7815a6-7ee4-497e-8888-515a05f02364");

        // -------------------------------------------------------------
        // 保活状态
        // -------------------------------------------------------------
        private static readonly object _sync = new object();
        private static bool _isKeepAliveActive = false;
        private static Thread _heartbeatThread = null;
        private static IntPtr _powerRequest = IntPtr.Zero;
        private static bool _timeoutsOverridden = false;

        private static Guid _savedScheme;
        private static uint _savedStandbyAc;
        private static uint _savedStandbyDc;
        private static uint _savedHibernateAc;
        private static uint _savedHibernateDc;

        private const int HeartbeatIntervalMs = 10000;

        public static bool IsKeepAliveActive
        {
            get { lock (_sync) { return _isKeepAliveActive; } }
        }

        public static bool IsTimeoutsOverridden
        {
            get { lock (_sync) { return _timeoutsOverridden; } }
        }

        private static string BackupDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgentScreenGuard");
            }
        }

        private static string BackupFile
        {
            get { return Path.Combine(BackupDirectory, "guard-backup.ini"); }
        }

        // -------------------------------------------------------------
        // Public Actions
        // -------------------------------------------------------------

        /// <summary>
        /// 等待物理输入（鼠标按键及快捷键按键）释放，并延时指定毫秒数
        /// 防止按键弹起事件被 Windows 误判为用户活动从而瞬间唤醒屏幕
        /// </summary>
        public static void WaitInputRelease(int delayMs)
        {
            int start = Environment.TickCount;
            // 轮询等待鼠标按键与常用热键按键全部物理释放（防抖 2000ms 保护）
            while (Environment.TickCount - start < 2000)
            {
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                bool rightDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
                bool midDown = (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
                bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                bool sDown = (GetAsyncKeyState(VK_S) & 0x8000) != 0;
                bool bDown = (GetAsyncKeyState(VK_B) & 0x8000) != 0;

                if (!leftDown && !rightDown && !midDown && !ctrlDown && !altDown && !shiftDown && !sDown && !bDown)
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
        /// 硬件熄屏 (DPMS D3 硬件切断)
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

        // -------------------------------------------------------------
        // 保活开关
        // -------------------------------------------------------------

        /// <summary>
        /// 开启 / 关闭系统防休眠保活。返回 true 表示最终处于目标状态。
        /// </summary>
        public static bool SetKeepAlive(bool enable)
        {
            lock (_sync)
            {
                if (enable)
                {
                    if (_isKeepAliveActive) return true;
                    return StartGuard();
                }

                if (!_isKeepAliveActive)
                {
                    RestoreTimeouts();
                    return true;
                }
                return StopGuard();
            }
        }

        private static bool StartGuard()
        {
            bool anyApiRequest = false;

            // ---- 第 1 层：进程级电源请求（SystemRequired + ExecutionRequired） ----
            try
            {
                REASON_CONTEXT reason = new REASON_CONTEXT();
                reason.Version = POWER_REQUEST_CONTEXT_VERSION;
                reason.Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING;
                reason.SimpleReasonString = Marshal.StringToHGlobalUni("AgentScreenGuard keep-awake");

                _powerRequest = PowerCreateRequest(ref reason);
                Marshal.FreeHGlobal(reason.SimpleReasonString);

                if (_powerRequest != IntPtr.Zero)
                {
                    if (PowerSetRequest(_powerRequest, PowerRequestSystemRequired)) anyApiRequest = true;
                    if (PowerSetRequest(_powerRequest, PowerRequestExecutionRequired)) anyApiRequest = true;
                }
            }
            catch
            {
                _powerRequest = IntPtr.Zero;
            }

            // ---- 第 2 层：长活心跳线程持续持有 ES_CONTINUOUS | ES_SYSTEM_REQUIRED ----
            try
            {
                _isKeepAliveActive = true;
                _heartbeatThread = new Thread(new ThreadStart(HeartbeatLoop));
                _heartbeatThread.IsBackground = true;
                _heartbeatThread.Name = "AgentScreenGuard.KeepAlive";
                _heartbeatThread.Start();
                if (SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED) != 0) anyApiRequest = true;
            }
            catch
            {
            }

            // ---- 第 3 层：临时把“空闲睡眠 / 休眠”超时改为 0（永不） ----
            OverrideTimeouts();

            return anyApiRequest || _timeoutsOverridden;
        }

        private static bool StopGuard()
        {
            _isKeepAliveActive = false;

            if (_powerRequest != IntPtr.Zero)
            {
                try
                {
                    PowerClearRequest(_powerRequest, PowerRequestSystemRequired);
                    PowerClearRequest(_powerRequest, PowerRequestExecutionRequired);
                    CloseHandle(_powerRequest);
                }
                catch
                {
                }
                _powerRequest = IntPtr.Zero;
            }

            Thread hb = _heartbeatThread;
            _heartbeatThread = null;
            if (hb != null)
            {
                try { hb.Join(500); } catch { }
            }

            SetThreadExecutionState(ES_CONTINUOUS);
            RestoreTimeouts();

            return !_isKeepAliveActive;
        }

        private static void HeartbeatLoop()
        {
            while (true)
            {
                lock (_sync)
                {
                    if (!_isKeepAliveActive) break;
                }

                try
                {
                    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                }
                catch
                {
                }

                Thread.Sleep(HeartbeatIntervalMs);
            }

            try
            {
                SetThreadExecutionState(ES_CONTINUOUS);
            }
            catch
            {
            }
        }

        // -------------------------------------------------------------
        // 电源方案空闲超时覆盖 / 还原
        // -------------------------------------------------------------

        private static void OverrideTimeouts()
        {
            try
            {
                IntPtr schemePtr;
                if (PowerGetActiveScheme(IntPtr.Zero, out schemePtr) != 0 || schemePtr == IntPtr.Zero)
                {
                    return;
                }

                Guid scheme = (Guid)Marshal.PtrToStructure(schemePtr, typeof(Guid));
                LocalFree(schemePtr);

                uint standbyAc, standbyDc, hibernateAc, hibernateDc;
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref STANDBYIDLE, out standbyAc);
                PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref STANDBYIDLE, out standbyDc);
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref HIBERNATEIDLE, out hibernateAc);
                PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref HIBERNATEIDLE, out hibernateDc);

                _savedScheme = scheme;
                _savedStandbyAc = standbyAc;
                _savedStandbyDc = standbyDc;
                _savedHibernateAc = hibernateAc;
                _savedHibernateDc = hibernateDc;

                // 先落盘备份：即使进程被强杀，下次启动也能还原
                SaveBackup(scheme, standbyAc, standbyDc, hibernateAc, hibernateDc);

                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref STANDBYIDLE, 0);
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref STANDBYIDLE, 0);
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref HIBERNATEIDLE, 0);
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref HIBERNATEIDLE, 0);

                PowerSetActiveScheme(IntPtr.Zero, ref scheme);

                _timeoutsOverridden = true;
            }
            catch
            {
                _timeoutsOverridden = false;
            }
        }

        private static void RestoreTimeouts()
        {
            try
            {
                bool hadBackup = File.Exists(BackupFile);

                if (!_timeoutsOverridden && !hadBackup)
                {
                    return;
                }

                Guid scheme = _savedScheme;
                uint standbyAc = _savedStandbyAc;
                uint standbyDc = _savedStandbyDc;
                uint hibernateAc = _savedHibernateAc;
                uint hibernateDc = _savedHibernateDc;

                // 内存里没有备份（例如上次被强杀）时，从备份文件恢复
                if (!_timeoutsOverridden)
                {
                    uint sAc, sDc, hAc, hDc;
                    LoadBackup(out scheme, out sAc, out sDc, out hAc, out hDc);
                    standbyAc = sAc;
                    standbyDc = sDc;
                    hibernateAc = hAc;
                    hibernateDc = hDc;
                }

                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref STANDBYIDLE, standbyAc);
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref STANDBYIDLE, standbyDc);
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref HIBERNATEIDLE, hibernateAc);
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SUB_SLEEP, ref HIBERNATEIDLE, hibernateDc);
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);

                _timeoutsOverridden = false;
                DeleteBackup();
            }
            catch
            {
                _timeoutsOverridden = false;
            }
        }

        /// <summary>
        /// 程序启动时调用：若上次异常退出导致电源超时仍被改，这里自动还原。
        /// </summary>
        public static void RestoreLeftoverOverrides()
        {
            try
            {
                if (!File.Exists(BackupFile)) return;

                lock (_sync)
                {
                    if (_isKeepAliveActive) return;
                    _timeoutsOverridden = false;
                    RestoreTimeouts();
                }
            }
            catch
            {
            }
        }

        // -------------------------------------------------------------
        // 备份文件 (极简 INI，避免额外依赖)
        // -------------------------------------------------------------
        private static void SaveBackup(Guid scheme, uint standbyAc, uint standbyDc, uint hibernateAc, uint hibernateDc)
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                string[] lines = new string[]
                {
                    "scheme=" + scheme.ToString(),
                    "standby_ac=" + standbyAc.ToString(),
                    "standby_dc=" + standbyDc.ToString(),
                    "hibernate_ac=" + hibernateAc.ToString(),
                    "hibernate_dc=" + hibernateDc.ToString()
                };
                File.WriteAllLines(BackupFile, lines);
            }
            catch
            {
            }
        }

        private static void LoadBackup(out Guid scheme, out uint standbyAc, out uint standbyDc, out uint hibernateAc, out uint hibernateDc)
        {
            scheme = Guid.Empty;
            standbyAc = 0;
            standbyDc = 0;
            hibernateAc = 0;
            hibernateDc = 0;

            try
            {
                foreach (string raw in File.ReadAllLines(BackupFile))
                {
                    string line = raw.Trim();
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string key = line.Substring(0, idx).Trim();
                    string val = line.Substring(idx + 1).Trim();

                    switch (key)
                    {
                        case "scheme":
                            try { scheme = new Guid(val); } catch { }
                            break;
                        case "standby_ac": uint.TryParse(val, out standbyAc); break;
                        case "standby_dc": uint.TryParse(val, out standbyDc); break;
                        case "hibernate_ac": uint.TryParse(val, out hibernateAc); break;
                        case "hibernate_dc": uint.TryParse(val, out hibernateDc); break;
                    }
                }
            }
            catch
            {
            }
        }

        private static void DeleteBackup()
        {
            try
            {
                if (File.Exists(BackupFile)) File.Delete(BackupFile);
            }
            catch
            {
            }
        }
    }
}
