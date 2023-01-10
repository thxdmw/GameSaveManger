using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace 左菜单栏的winfrom
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ImageList image = new ImageList();
            image.ImageSize = new Size(1, 35);//设置每次点击view时以图片的形式
            ColumnHeader ch = new ColumnHeader();
            ch.Text = "菜单";
            ch.Width = splitContainer1.Panel1.Width;
            ch.TextAlign = HorizontalAlignment.Center;
            listView1.Columns.Add(ch);//设置listview的列名，没啥用处
            listView1.SmallImageList = image;//设置每个view的显示形式
            LoadList();//加载一级栏目
            listView1.Width = ch.Width;//设置每个view的宽度都一致
            listView1.Items[0].BackColor = Color.LightGray;//设置主页的选中后的颜色
            //启动首先展示主页
            splitContainer1.Panel2.Controls.Clear();//每次执行时清空panel2
            主页 主页 = new 主页();
            主页.Parent = splitContainer1.Panel2;
            主页.Dock = DockStyle.Fill;//设置用户控件充满panel2
            主页.Show();
        }
        /// <summary>
        /// 一级菜单
        /// </summary>
        private void LoadList()
        {
            listView1.Items.Clear();//清空菜单
            //添加菜单
            ListViewItem 主页 = new ListViewItem("     主页");
            ListViewItem 菜单一 = new ListViewItem("     菜单一");
            ListViewItem 菜单二 = new ListViewItem("     菜单二");
            ListViewItem 菜单三 = new ListViewItem("     菜单三");
            ListViewItem 重启 = new ListViewItem("     重启");
            listView1.Items.Add(主页);
            listView1.Items.Add(菜单一);
            listView1.Items.Add(菜单二);
            listView1.Items.Add(菜单三);
            listView1.Items.Add(重启);
        }
        /// <summary>
        /// 二级菜单
        /// </summary>
        private void ChildList()
        {
            listView1.Items.Clear();//清空菜单
            //添加菜单
            ListViewItem 二级菜单一 = new ListViewItem("     二级菜单一");
            ListViewItem 二级菜单二 = new ListViewItem("     二级菜单二");
            ListViewItem 返回 = new ListViewItem("     返回");
            listView1.Items.Add(二级菜单一);
            listView1.Items.Add(二级菜单二);
            listView1.Items.Add(返回);
        }
        /// <summary>
        /// listview的鼠标单击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listView1_MouseClick(object sender, MouseEventArgs e)
        {
            foreach (ListViewItem item in listView1.Items)
            {
                item.BackColor = Color.WhiteSmoke;//遍历每个菜单栏的颜色
            }
            if (e.Button == MouseButtons.Left)
            {
                if (listView1.SelectedItems.Count > 0)
                {
                    listView1.Items[listView1.FocusedItem.Index].BackColor = Color.LightGray;//设置选中菜单栏的颜色
                    string choose = listView1.Items[listView1.FocusedItem.Index].Text;//选中菜单栏的文本
                    ChangePlanel(choose.Trim());//根据文本名称进行相应的展示
                }
            }
        }
        /// <summary>
        /// 根据文本名称进行相应的展示
        /// </summary>
        /// <param name="name"></param>
        private void ChangePlanel(string name)
        {
            switch (name)
            {
                case "主页":
                    splitContainer1.Panel2.Controls.Clear();
                    主页 zhuye = new 主页();
                    zhuye.Parent = splitContainer1.Panel2;
                    zhuye.Dock = DockStyle.Fill;
                    zhuye.Show();
                    break;
                case "菜单一":
                    splitContainer1.Panel2.Controls.Clear();
                    主菜单1 zhu1 = new 主菜单1();
                    zhu1.Parent = splitContainer1.Panel2;
                    zhu1.Dock = DockStyle.Fill;
                    zhu1.Show();
                    break;
                case "菜单二":
                    ChildList();
                    break;
                case "菜单三":
                    splitContainer1.Panel2.Controls.Clear();
                    主菜单3 zhu3 = new 主菜单3();
                    zhu3.Parent = splitContainer1.Panel2;
                    zhu3.Dock = DockStyle.Fill;
                    zhu3.Show();
                    break;
                case "二级菜单一":
                    splitContainer1.Panel2.Controls.Clear();
                    二级菜单1 er1 = new 二级菜单1();
                    er1.Parent = splitContainer1.Panel2;
                    er1.Dock = DockStyle.Fill;
                    er1.Show();
                    break;
                case "二级菜单二":
                    splitContainer1.Panel2.Controls.Clear();
                    二级菜单2 er2 = new 二级菜单2();
                    er2.Parent = splitContainer1.Panel2;
                    er2.Dock = DockStyle.Fill;
                    er2.Show();
                    break;
                case "返回":
                    LoadList();
                    break;
                case "重启":
                    try
                    {
                        Application.Restart();
                    }
                    catch (Exception)
                    {
                        System.Environment.Exit(0);
                    }
                    break;
            }
        }
    }
}
