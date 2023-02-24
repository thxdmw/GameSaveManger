using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace GameSaveManager
{
    public class Monitor
    {
        public Monitor() { }

        //监听进程
        public static void monitorProcess()
        {
            //Process[] processes = Process.GetProcesses();
            //foreach (Process process in processes)
            //{
            //    if (process.ProcessName.Contains("QQ"))
            //    {
            //        Console.WriteLine($"ProcessName = ({process.ProcessName}), Id = {process.Id}");
            //        process.Close();
            //    }
            //}
            //启动一个进程
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.WorkingDirectory = "F:\\单机游戏\\Game\\OROCHI Z";
            psi.FileName = "F:\\单机游戏\\Game\\OROCHI Z\\OROCHI_Z_TC.exe";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process process = System.Diagnostics.Process.Start(psi);
            process.Exited += new EventHandler(Process_Exited);

        }

        public static void Process_Exited(object sender, EventArgs e)
        {
            Console.WriteLine("游戏已经关闭");
        }


        //监听文件
        public static void monitorDir()
        {
            string path = "F:/test";
            string filter = "*.*";
            FileSystemWatcher watcher = new FileSystemWatcher(path, filter);
            watcher.IncludeSubdirectories = true;
            watcher.Changed += new FileSystemEventHandler(Watcher_Changed);
            watcher.Created += new FileSystemEventHandler(Watcher_Changed);
            watcher.Deleted += new FileSystemEventHandler(Watcher_Changed);
            watcher.EnableRaisingEvents= true;



        }

        public static void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine("文件发生改变");
        }
    }
}
