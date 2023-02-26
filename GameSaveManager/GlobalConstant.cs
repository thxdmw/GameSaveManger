using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Qiniu.Util;
using Qiniu.RS.Model;
using Qiniu.RS;
using Qiniu.Http;
using Qiniu.IO;
using Qiniu.Common;
using Qiniu.IO.Model;
using System.Reflection;
using System.Collections;

namespace GameSaveManager
{
    public class GlobalConstant
    {
        //项目可执行文件路径
        public static string EXE_PATH = System.Windows.Forms.Application.StartupPath.Replace("\\", "/");
        //项目GameInfo文件路径
        public static string GAMEINFO_PATH = System.Windows.Forms.Application.StartupPath.Replace("\\", "/") + "/GameInfo.json";
        //项目UserInfo文件路径
        public static string USERINFO_PATH = System.Windows.Forms.Application.StartupPath.Replace("\\", "/") + "/UserInfo.json";
        //全局Form窗口实例
        public static Form form = null;
        //所有目录和文件
        public static List<string> files = new List<string>();


        //全局的用户密匙和需要的配置
        public static string accessKey = "GTeJvYhzMnQCjpIxYmBt-499Xqy9C4xU4yIXY85q";
        public static string secretKey = "pdFE2Rj0ke1NwMXk8Gy17oDjnWuMHk0QJLa2tR8o";
        public static string bucket = "thxdmw";
        public static string domain = "rqh74myiq.hn-bkt.clouddn.com";
        public static Mac mac;

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

        //反序列化Json文件(读取Json文件返回List集合)
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
            //把目录转换成正常的目录
            directoryInfo.Attributes = FileAttributes.Normal;
            //如果有子目录，先循环删除子目录，再删除当前目录
            directoryInfo.Delete(true);
        }

        //复制游戏存档目录下所有文件
        public static void copyAllFilesFromDirectory(string fromPath, string toPath)
        {
            //目标目录不存在创建目标目录
            if (!System.IO.Directory.Exists(toPath))
            {
                System.IO.Directory.CreateDirectory(toPath);
            }
            IEnumerable<string> files = System.IO.Directory.EnumerateFileSystemEntries(fromPath);
            if (files != null && files.Count() > 0)
            {
                foreach (var item in files)
                {
                    string desPath = System.IO.Path.Combine(toPath, System.IO.Path.GetFileName(item));
                    //如果是文件
                    var fileExist = System.IO.File.Exists(item);
                    if (fileExist)
                    {
                        //复制文件到指定目录下                     
                        System.IO.File.Copy(item, desPath, true);
                        continue;
                    }
                    //如果是文件夹                   
                    copyAllFilesFromDirectory(item, desPath);
                }
            }
        }

        //获取一个目录下所有文件夹和文件姓名
        public static void getDirList(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            FileSystemInfo[] filesArray = directory.GetFileSystemInfos();
            foreach (var item in filesArray)
            {
                //是否是一个文件夹
                if (item.Attributes == FileAttributes.Directory)
                {
                    //GlobalConstant.files.Add(item.FullName);
                    getDirList(item.FullName.Replace("\\", "/"));
                }
                else
                {
                    GlobalConstant.files.Add(item.FullName.Replace("\\", "/"));
                }
            }
            //foreach (string name in Directory.GetFileSystemEntries(path,"*"))
            //{
            //    files.Add(name);
            //}
        }
        /**
         * 
         * 网盘相关API
         * 
         */

        //列举bucket的相关文件
        public static void listFile()
        {

            string marker = ""; // 首次请求时marker必须为空

            string prefix = null; // 按文件名前缀保留搜索结果

            string delimiter = null; // 目录分割字符(比如"/")

            int limit = 100; // 单次列举数量限制(最大值为1000)

            BucketManager bm = new BucketManager(mac);

            List<FileDesc> items = new List<FileDesc>();

            do
            {
                ListResult result = bm.ListFiles(bucket, prefix, marker, limit, delimiter);
                Console.WriteLine(result);
                marker = result.Result.Marker;
                if (result.Result.Items != null)
                {
                    items.AddRange(result.Result.Items);
                }
            } while (!string.IsNullOrEmpty(marker));
        }
        //获取云存储某个目录的所有文件
        public static List<string> listDir(string dirName)
        {
            string marker = ""; // 首次请求时marker必须为空
            string prefix = dirName; // 按文件名前缀保留搜索结果
            string delimiter = null; // 目录分割字符(比如"/")
                                     //string delimiter = dirName+"/"; // 目录分割字符(比如"/")
            int limit = 100; // 单次列举数量限制(最大值为1000)
            BucketManager bm = new BucketManager(mac);

            ListResult result = null;
            //所有文件名
            List<string> list = new List<string>();
            do
            {
                result = bm.ListFiles(bucket, prefix, marker, limit, delimiter);
            } while (!string.IsNullOrEmpty(marker));
            foreach (FileDesc file in result.Result.Items)
            {
                list.Add(file.Key);
                Console.WriteLine(file.Key);
            }
            return list;
        }
        //一个目录下的下载
        public static string downloadDir(string gameName, string saveFile)
        {
            //列举游戏名下的目录与文件名字
            List<string> fileNameList = listDir(gameName);
            if (fileNameList.Count > 0)
            {
                //文件URL
                string rawUrl = "http://" + domain + "/";
                string downloadURL = null;
                //创建目录
                for (int i = 0; i < fileNameList.Count; i++)
                {
                    if (fileNameList[i].Last() == '/')
                    {
                        //(杀手/shashou/)
                        Directory.CreateDirectory(saveFile + fileNameList[i].Replace(gameName + "/", ""));
                        fileNameList.Remove(fileNameList[i]);
                        i--;
                    }
                }
                HttpResult result = null;
                //下载文件
                foreach (string filePath in fileNameList)
                {
                    downloadURL = rawUrl + filePath;
                    result = DownloadManager.Download(downloadURL, saveFile + filePath.Replace(gameName + "/", ""));
                    //Console.WriteLine(result);
                }
                return "云档存在";
            }
            return "云档不存在";
        }
        //去掉目录前缀
        public static string rename(string gameName, string dirName)
        {
            return dirName.Replace(gameName + "/", "");
        }

        //一个目录的上传
        public static void uploadDir(string dirPath, string gameName)
        {
            //创建凭证
            Mac mac = new Mac(GlobalConstant.accessKey, GlobalConstant.secretKey);
            //上传地区
            Config.ZONE = Zone.GetZone(ZoneID.CN_South);
            //上传文件集合
            getDirList(dirPath);
            //得到本地位置和上传文件名
            Dictionary<string, string> dictionary = renameUpLoadFiles(dirPath, gameName);

            ////截取目录前部分
            //dirPath = dirPath.Substring(0, dirPath.LastIndexOf("\\") + 1);
            //string saveKey = "/无双大蛇/KOEI/Musou OROCHI Z TC/Savedata/save.dat";
            //string saveKey = "save.dat";
            //string localFile = "F:/test/无双大蛇/KOEI/Musou OROCHI Z TC/Savedata/save.dat";

            //上传代码
            PutPolicy putPolicy = new PutPolicy();
            // 上传策略有效期(对应于生成的凭证的有效期)          
            putPolicy.SetExpires(3600);
            // 上传到云端多少天后自动删除该文件，如果不设置（即保持默认默认）则不删除
            //putPolicy.DeleteAfterDays = 1;
            putPolicy.Scope = null;
            string jstr = null;
            string token = null;
            UploadManager um = new UploadManager();
            HttpResult result = null;
            foreach (string local in GlobalConstant.files)
            {
                // 如果需要设置为"覆盖"上传(如果云端已有同名文件则覆盖)，请使用 SCOPE = "BUCKET:KEY"
                putPolicy.Scope = GlobalConstant.bucket + ":" + dictionary[local];
                jstr = putPolicy.ToJsonString();
                token = Auth.CreateUploadToken(mac, jstr);
                result = um.UploadFile(local, dictionary[local], token);
                Console.WriteLine(result);
            }
        }
        //修改文件集合文件名（游戏名/游戏存档目录/开头）
        public static Dictionary<string, string> renameUpLoadFiles(string dirPath, string gameName)
        {
            Dictionary<string, string> dictionary = new Dictionary<string, string>();
            string prefix = gameName + dirPath.Remove(0, dirPath.LastIndexOf("/"));
            for (int i = 0; i < GlobalConstant.files.Count(); i++)
            {
                //files[i] = prefix + files[i].Replace(dirPath, "");
                dictionary.Add(files[i], (prefix + files[i].Replace(dirPath, "")).Replace("\\", "/"));
            }
            return dictionary;
        }
        /**
         * 网盘相关API
         */




    }
}
