using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using static System.Windows.Forms.AxHost;
using System.Runtime.InteropServices;

namespace GameSaveManager
{
    public class GlobalConstant
    {
        //项目可执行文件路径
        public static string EXE_PATH = System.Windows.Forms.Application.StartupPath;
        //项目GameInfo文件路径
        public static string GAMEINFO_PATH = System.Windows.Forms.Application.StartupPath + "/GameInfo.json";
        //GameInfo是否为空
        //public static bool GAMEINFO_EMPTY = true;
        //全局Form窗口实例
        public static Form form = null;


        //在程序中用一个计时器，每隔几秒钟调用一次该函数，打开任务管理器，你会有惊奇的发现
        #region 内存回收
        [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
        public static extern int SetProcessWorkingSetSize(IntPtr process, int minSize, int maxSize);
        /// <summary>
        /// 释放内存
        /// </summary>
        public static void ClearMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                //App.SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
            }
        }
        #endregion



        //判断文本是否为空
        public static bool isEmpty(string text)
        {
            return (text == null || text == "") ? true : false;
        }

        //读取Json文件返回List集合
        public static List<T> readJsonFile<T>(string filePath)
        {
            string jsonString = File.ReadAllText(filePath);
            List<T> list = JsonConvert.DeserializeObject<List<T>>(jsonString);
            return list;

        }

        //在集合添加对象以Json序列化集合
        public static string toJsonObject<T>(string filePath, T _object)
        {
            if (!File.Exists(filePath))
            {
                return "文件不存在";
            }
            List<T> list = toObjectFromJson<T>(filePath);
            if (list != null)
            {
                list.Add(_object);
            }
            else
            {
                List<T> _list = new List<T>();
                _list.Add(_object);
                list = _list;
            }
            //序列化为Json格式
            return JsonConvert.SerializeObject(list, Formatting.Indented);

        }

        //添加集合以Json序列化
        //public static string toJsonList<T>(string filePath, List<T> _list)
        //{
        //    if (!File.Exists(filePath))
        //    {
        //        return "文件不存在";
        //    }
        //    List<T> list = toObjectFromJson<T>(filePath);
        //    if (list != null)
        //    {
        //        list.Add(_object);
        //    }
        //    else
        //    {
        //        List<T> _list = new List<T>();
        //        _list.Add(_object);
        //        list = _list;
        //    }
        //    //序列化为Json格式
        //    return JsonConvert.SerializeObject(list, Formatting.Indented);

        //}

        //反序列化Json文件
        public static List<T> toObjectFromJson<T>(string filePath)
        {
            //读取Json文件
            string json = File.ReadAllText(filePath);
            //反序列化
            List<T> list = JsonConvert.DeserializeObject<List<T>>(json);
            return list;
        }

        //刷新页面(哪个页面,以及哪个Panel)
        public static void refreshPage(UserControl page, Panel panel)
        {
            panel.Controls.Clear();
            page.Parent = panel;
            page.Dock = DockStyle.Fill;
            page.Show();
        }

        //删除整个目录
        public static void deleteDirectory(string directoryPath)
        {
            //目录为空，删除目录
            DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
            //directoryInfo.Delete();
            //如果有子目录，先循环删除子目录，再删除当前目录
            directoryInfo.Delete(true);
        }
    }
}
