using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace D2RSaveMonitor
{
    internal static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 로깅 시작 / Start logging
            Logger.Info("D2RSaveMonitor 시작 / Application started");

            try
            {
                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                Logger.Critical("Application 최상위 예외 / Top-level application exception", ex);
                throw;
            }
            finally
            {
                Logger.Info("D2RSaveMonitor 종료 / Application exiting");
            }
        }
    }
}
