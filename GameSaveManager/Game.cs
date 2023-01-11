using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameSaveManager
{
    //游戏类
    public class Game
    {
        //游戏名称
        private string name;
        //图片路径
        private string picturePath;
        //存档目录
        private string saveDirectorPath;
        //启动文件路径 
        private string startupPath;

        public Game() { }
        public Game(string name, string picturePath, string saveDirectorPath, string startupPath)
        {
            this.name = name;
            this.picturePath = picturePath;
            this.saveDirectorPath = saveDirectorPath;
            this.startupPath = startupPath;
        }

        public string Name
        {
            get { return name; }
            set { name = value; }
        }
        public string PicturePath
        {
            get { return picturePath; }
            set { picturePath = value; }
        }
        public string SaveDirectorPath
        {
            get { return saveDirectorPath; }
            set { saveDirectorPath = value; }
        }
        public string StartupPath
        {
            get { return startupPath; }
            set { startupPath = value; }
        }

        public override string ToString()
        {
            return "naem: " + this.Name + " picturePath: " + this.picturePath
                + " saveDirectorPath: " + this.saveDirectorPath + " startupPath: " + this.startupPath;
        }
    }
}
