using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameSaveManager
{
    class SaveData
    {
        //创建时间
        DateTime date;
        //创建描述
        string describe;
        //创建的文件位置
        string filePath;

        public SaveData()
        {

        }
        public SaveData(DateTime date, string describe, string filePath)
        {
            this.date = date;
            this.describe = describe;
            this.filePath = filePath;
        }


    }
}
