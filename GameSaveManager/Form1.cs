using cn.thx;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace GameSaveManager
{
    public partial class Form : System.Windows.Forms.Form
    {
        public Form()
        {
            InitializeComponent();
            //打开程序创建数据目录(先判断存在不)
            if (!Directory.Exists(GlobalConstant.EXE_PATH + "/save_data"))
            {
                Directory.CreateDirectory(GlobalConstant.EXE_PATH + "/save_data");
            }
            // 判断是否已有相同文件 
            if (!File.Exists(GlobalConstant.GAMEINFO_PATH))
            {
                File.Create(GlobalConstant.GAMEINFO_PATH).Close();
            }
        }

        public void Form1_Load(object sender, EventArgs e)
        {
            //固定分割线
            splitContainer.IsSplitterFixed = true;
            ColumnHeader ch = new ColumnHeader();
            ch.Text = "存档管理";
            ch.Width = splitContainer.Panel1.Width;
            ch.TextAlign = HorizontalAlignment.Center;
            menuList.Columns.Add(ch);
            //加载菜单
            LoadList();
            menuList.Width = ch.Width;
        }

        //加载菜单列表
        private void LoadList()
        {
            //清空菜单
            menuList.Items.Clear();
            //添加菜单
            ListViewItem 游戏列表 = new ListViewItem("游戏列表");
            ListViewItem 添加游戏 = new ListViewItem("添加游戏");
            游戏列表.ImageIndex = 0;
            添加游戏.ImageIndex = 1;

            menuList.Items.Add(游戏列表);
            menuList.Items.Add(添加游戏);


        }

        //点击菜单事件
        private void menuList_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                string choose = menuList.Items[menuList.FocusedItem.Index].Text;//选中菜单栏的文本
                showPage(choose.Trim());
            }
        }

        //改变页面事件
        private void showPage(string name)
        {
            switch (name)
            {
                case "游戏列表":
                    splitContainer.Panel2.Controls.Clear();
                    GameListPage gameListPage = new GameListPage();
                    gameListPage.Parent = splitContainer.Panel2;
                    gameListPage.Dock = DockStyle.Fill;
                    gameListPage.Show();
                    break;
                case "添加游戏":
                    splitContainer.Panel2.Controls.Clear();
                    AddGamePage addGamePage = new AddGamePage();
                    addGamePage.Parent = splitContainer.Panel2;
                    addGamePage.Dock = DockStyle.Fill;
                    addGamePage.Show();
                    break;
            }
        }
    }
}
