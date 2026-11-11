using System;
using System.Threading;
using System.Windows.Forms;

namespace AgentScreenGuard
{
    static class Program
    {
        private const string MUTEX_NAME = "AgentScreenGuard_SingleInstance_Mutex";

        [STAThread]
        static void Main()
        {
            bool isNewInstance;
            using (Mutex mutex = new Mutex(true, MUTEX_NAME, out isNewInstance))
            {
                if (!isNewInstance)
                {
                    MessageBox.Show("AgentScreenGuard 已经在运行中。\n可在屏幕右下角系统托盘中找到并展开它。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 上次若被强杀，电源方案的“空闲睡眠/休眠”可能仍被改成 0，这里先精确还原
                try { PowerManager.RestoreLeftoverOverrides(); }
                catch { }

                // 进程退出时兜底还原电源超时（MainForm 正常关闭也会调用，重复调用无害）
                Application.ApplicationExit += delegate(object sender, EventArgs e)
                {
                    try { PowerManager.SetKeepAlive(false); }
                    catch { }
                };

                // 全局未处理异常保护
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("程序发生异常: " + e.Exception.Message, "AgentScreenGuard 错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                Application.Run(new MainForm());
            }
        }
    }
}
