using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GameSaveManager
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form());

            //新功能测试
            //WangPan.listFile();
            //WangPan.listDir("无双大蛇");
            //WangPan.downloadDir("无双大蛇");
            //WangPan.uploadDir("无双大蛇");
            //Monitor.monitorProcess();
            //Monitor.monitorDir();

        }
    }
}
