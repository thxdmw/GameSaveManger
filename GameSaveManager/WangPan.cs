using System;
using System.Collections.Generic;
using System.Linq;
using Qiniu.RS.Model;
using Qiniu.RS;
using Qiniu.Util;
using Qiniu.Http;
using Qiniu.IO;
using System.IO;
using Qiniu.IO.Model;
using Qiniu.Common;

namespace GameSaveManager
{
    public class WangPan
    {
        //密匙
        public static string accessKey = "GTeJvYhzMnQCjpIxYmBt-499Xqy9C4xU4yIXY85q";
        public static string secretKey = "pdFE2Rj0ke1NwMXk8Gy17oDjnWuMHk0QJLa2tR8o";
        public static string bucket = "thxdmw";
        public static string domain = "rqh74myiq.hn-bkt.clouddn.com";
        // 这个示例单独使用了一个Settings类，其中包含AccessKey和SecretKey
        // 实际应用中，请自行设置您的AccessKey和SecretKey
        public static Mac mac = new Mac(accessKey, secretKey);
        public WangPan() { }

        //列举文件
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

        //F:\test
        //获取某个目录的所有文件
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
        public static void downloadDir(string dirName)
        {
            List<string> fileNameList = listDir(dirName);

            if (fileNameList.Count > 0)
            {
                //文件URL
                string rawUrl = "http://" + domain + "/";
                string downloadURL = null;
                //下载位置
                string saveFile = "F:/test/";

                //创建目录
                for (int i = 0; i < fileNameList.Count; i++)
                {
                    if (fileNameList[i].Last() == '/')
                    {
                        Directory.CreateDirectory(saveFile + fileNameList[i]);
                        fileNameList.Remove(fileNameList[i]);
                        i--;
                    }
                }

                //下载文件
                foreach (string filePath in fileNameList)
                {
                    downloadURL = rawUrl + filePath;
                    HttpResult result = DownloadManager.Download(downloadURL, saveFile + filePath);
                    result.Data = null;
                    Console.WriteLine(result);
                }
            }
        }


        //一个目录的上传
        public static void uploadDir(string dirName)
        {
            Mac mac = new Mac(accessKey, secretKey);
            Config.ZONE = Zone.GetZone(ZoneID.CN_South);

            string saveKey = "/无双大蛇/KOEI/Musou OROCHI Z TC/Savedata/save.dat";
            //string saveKey = "save.dat";
            string localFile = "F:/test/无双大蛇/KOEI/Musou OROCHI Z TC/Savedata/save.dat";

            PutPolicy putPolicy = new PutPolicy();

            // 如果需要设置为"覆盖"上传(如果云端已有同名文件则覆盖)，请使用 SCOPE = "BUCKET:KEY"
            putPolicy.Scope = bucket + ":" + saveKey;
            //putPolicy.Scope = bucket;

            // 上传策略有效期(对应于生成的凭证的有效期)          
            putPolicy.SetExpires(3600);

            // 上传到云端多少天后自动删除该文件，如果不设置（即保持默认默认）则不删除
            //putPolicy.DeleteAfterDays = 1;
            string jstr = putPolicy.ToJsonString();
            string token = Auth.CreateUploadToken(mac, jstr);

            UploadManager um = new UploadManager();
            HttpResult result = um.UploadFile(localFile, saveKey, token);

            Console.WriteLine(result);
        }

        //生成
    }
}


