using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

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
        //Form窗口实例
        public static Form form = null;

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

        //添加对象以Json序列化
        public static string toJsonList<T>(string filePath, T _object)
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
        
        //反序列化Json文件
        public static List<T> toObjectFromJson<T>(string filePath)
        {
            //读取Json文件
            string json = File.ReadAllText(filePath);
            //反序列化
            List<T> list = JsonConvert.DeserializeObject<List<T>>(json);
            return list;
        }
    }
}
