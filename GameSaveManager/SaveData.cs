using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameSaveManager
{
    class SaveData
    {
        //创建描述
        private string describe;
        //创建时间("yyyy-MM-dd  hh:mm:ss")
        private string date;
        //创建的文件位置
        private string filePath;
        public SaveData()
        {

        }
        public SaveData(string date, string describe, string filePath)
        {
            this.date = date;
            this.describe = describe;
            this.filePath = filePath;
        }

        public string Date { get { return this.date; } set { this.date = value; } }
        public string Describe { get { return this.describe; } set { this.describe = value; } }
        public string FilePath { get { return this.filePath; } set { this.filePath = value; } }

        public override string ToString()
        {
            return "describe: " + this.describe + " date: " + this.date
                + " filePath: " + this.filePath;
        }
    }
}
