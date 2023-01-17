using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Button = System.Windows.Forms.Button;
using ListView = System.Windows.Forms.ListView;
using TextBox = System.Windows.Forms.TextBox;

namespace GameSaveManager
{
    class GameInfoPage : UserControl
    {
        //自定义属性
        private Game game;
        //游戏目录
        private string gameDirectory;
        //Save文件的位置
        private string saveJsonPath;
        //Save文件的数据
        private List<SaveData> saveDataList;
        //当前项
        //ListViewItem item = null;

        private Label gameName;
        private PictureBox startBox;
        private Label label1;
        private Button createButton;
        private TextBox describeBox;
        private Label describeLabel;
        private Button applyButton;
        private Button deleteButton;
        private Button backButton;
        private ListView SaveListViewBox;
        private ColumnHeader columnHeader1;
        private ColumnHeader columnHeader2;
        private System.ComponentModel.IContainer components;
        private PictureBox gamePictureBox;

        public GameInfoPage()
        {
            InitializeComponent();
        }

        //初始化页面
        public GameInfoPage(Game game)
        {
            InitializeComponent();
            //初始化组件数据
            this.game = game;
            this.gameDirectory = GlobalConstant.EXE_PATH + "/save_data/" + game.Name;
            this.saveJsonPath = GlobalConstant.EXE_PATH + "/save_data/" + game.Name + "/Save.json";
            //页面展示数据
            this.gameName.Text = game.Name;
            this.gamePictureBox.ImageLocation = game.PicturePath;
            //展示存储数据
            loadSaveFileAndShow();

        }

        //组件
        private void InitializeComponent()
        {
            this.gamePictureBox = new System.Windows.Forms.PictureBox();
            this.gameName = new System.Windows.Forms.Label();
            this.startBox = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.createButton = new System.Windows.Forms.Button();
            this.describeBox = new System.Windows.Forms.TextBox();
            this.describeLabel = new System.Windows.Forms.Label();
            this.applyButton = new System.Windows.Forms.Button();
            this.deleteButton = new System.Windows.Forms.Button();
            this.backButton = new System.Windows.Forms.Button();
            this.SaveListViewBox = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            ((System.ComponentModel.ISupportInitialize)(this.gamePictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.startBox)).BeginInit();
            this.SuspendLayout();
            // 
            // gamePictureBox
            // 
            this.gamePictureBox.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.gamePictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.gamePictureBox.Location = new System.Drawing.Point(48, 68);
            this.gamePictureBox.Margin = new System.Windows.Forms.Padding(0);
            this.gamePictureBox.Name = "gamePictureBox";
            this.gamePictureBox.Size = new System.Drawing.Size(107, 100);
            this.gamePictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.gamePictureBox.TabIndex = 0;
            this.gamePictureBox.TabStop = false;
            // 
            // gameName
            // 
            this.gameName.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.gameName.AutoSize = true;
            this.gameName.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.gameName.Location = new System.Drawing.Point(180, 141);
            this.gameName.Margin = new System.Windows.Forms.Padding(0);
            this.gameName.Name = "gameName";
            this.gameName.Size = new System.Drawing.Size(92, 27);
            this.gameName.TabIndex = 1;
            this.gameName.Text = "游戏名称";
            // 
            // startBox
            // 
            this.startBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.startBox.Cursor = System.Windows.Forms.Cursors.Hand;
            this.startBox.Image = global::GameSaveManager.Properties.Resources.startButtonImage;
            this.startBox.Location = new System.Drawing.Point(236, 97);
            this.startBox.Margin = new System.Windows.Forms.Padding(0);
            this.startBox.Name = "startBox";
            this.startBox.Size = new System.Drawing.Size(36, 34);
            this.startBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.startBox.TabIndex = 2;
            this.startBox.TabStop = false;
            this.startBox.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.startBox_MouseDoubleClick);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label1.Location = new System.Drawing.Point(181, 102);
            this.label1.Margin = new System.Windows.Forms.Padding(0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(50, 25);
            this.label1.TabIndex = 3;
            this.label1.Text = "启动";
            // 
            // createButton
            // 
            this.createButton.Location = new System.Drawing.Point(540, 32);
            this.createButton.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.createButton.Name = "createButton";
            this.createButton.Size = new System.Drawing.Size(90, 28);
            this.createButton.TabIndex = 5;
            this.createButton.Text = "创建存档";
            this.createButton.UseVisualStyleBackColor = true;
            this.createButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.createButton_MouseClick);
            // 
            // describeBox
            // 
            this.describeBox.Location = new System.Drawing.Point(302, 32);
            this.describeBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.describeBox.Name = "describeBox";
            this.describeBox.Size = new System.Drawing.Size(232, 25);
            this.describeBox.TabIndex = 6;
            // 
            // describeLabel
            // 
            this.describeLabel.AutoSize = true;
            this.describeLabel.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.describeLabel.Location = new System.Drawing.Point(298, 12);
            this.describeLabel.Margin = new System.Windows.Forms.Padding(0);
            this.describeLabel.Name = "describeLabel";
            this.describeLabel.Size = new System.Drawing.Size(80, 15);
            this.describeLabel.TabIndex = 7;
            this.describeLabel.Text = "存档描述:";
            // 
            // applyButton
            // 
            this.applyButton.Location = new System.Drawing.Point(525, 159);
            this.applyButton.Name = "applyButton";
            this.applyButton.Size = new System.Drawing.Size(50, 50);
            this.applyButton.TabIndex = 10;
            this.applyButton.Text = "应用";
            this.applyButton.UseVisualStyleBackColor = true;
            this.applyButton.Click += new System.EventHandler(this.applyButton_Click);
            // 
            // deleteButton
            // 
            this.deleteButton.Location = new System.Drawing.Point(581, 159);
            this.deleteButton.Name = "deleteButton";
            this.deleteButton.Size = new System.Drawing.Size(50, 50);
            this.deleteButton.TabIndex = 11;
            this.deleteButton.Text = "删除";
            this.deleteButton.UseVisualStyleBackColor = true;
            this.deleteButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.deleteButton_MouseClick);
            // 
            // backButton
            // 
            this.backButton.Location = new System.Drawing.Point(3, 3);
            this.backButton.Name = "backButton";
            this.backButton.Size = new System.Drawing.Size(75, 32);
            this.backButton.TabIndex = 12;
            this.backButton.Text = "返回";
            this.backButton.UseVisualStyleBackColor = true;
            this.backButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.backButton_MouseClick);
            // 
            // SaveListViewBox
            // 
            this.SaveListViewBox.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2});
            this.SaveListViewBox.Font = new System.Drawing.Font("宋体", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.SaveListViewBox.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.SaveListViewBox.HideSelection = false;
            this.SaveListViewBox.Location = new System.Drawing.Point(48, 212);
            this.SaveListViewBox.Margin = new System.Windows.Forms.Padding(0);
            this.SaveListViewBox.MultiSelect = false;
            this.SaveListViewBox.Name = "SaveListViewBox";
            this.SaveListViewBox.Scrollable = false;
            this.SaveListViewBox.Size = new System.Drawing.Size(583, 221);
            this.SaveListViewBox.TabIndex = 13;
            this.SaveListViewBox.UseCompatibleStateImageBehavior = false;
            this.SaveListViewBox.View = System.Windows.Forms.View.Details;
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "描述";
            this.columnHeader1.Width = 220;
            // 
            // columnHeader2
            // 
            this.columnHeader2.Text = "时间";
            this.columnHeader2.Width = 250;
            // 
            // GameInfoPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
            this.Controls.Add(this.SaveListViewBox);
            this.Controls.Add(this.backButton);
            this.Controls.Add(this.deleteButton);
            this.Controls.Add(this.applyButton);
            this.Controls.Add(this.describeLabel);
            this.Controls.Add(this.describeBox);
            this.Controls.Add(this.createButton);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.startBox);
            this.Controls.Add(this.gameName);
            this.Controls.Add(this.gamePictureBox);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MaximumSize = new System.Drawing.Size(650, 450);
            this.MinimumSize = new System.Drawing.Size(650, 450);
            this.Name = "GameInfoPage";
            this.Size = new System.Drawing.Size(650, 450);
            ((System.ComponentModel.ISupportInitialize)(this.gamePictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.startBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        //点击返回图标
        private void backButton_MouseClick(object sender, MouseEventArgs e)
        {
            GlobalConstant.refreshPage(new GameListPage(), GlobalConstant.form.getSplitContainer().Panel2);
        }

        //双击开始图标运行游戏
        private void startBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (File.Exists(game.StartupPath))
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.WorkingDirectory = this.game.GameDirectorPath;
                psi.FileName = this.game.StartupPath;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                System.Diagnostics.Process.Start(psi);
            }
            else
            {
                MessageBox.Show("游戏路径不存在!");
            }
        }

        //点击创建存档图标
        private void createButton_MouseClick(object sender, MouseEventArgs e)
        {
            //判断列表项的数量(最多创建5个备份)
            if (SaveListViewBox.Items.Count <= 5)
            {
                if (describeBox.Text != null && describeBox.Text != "")
                {
                    //封装一个SaveData的对象
                    SaveData saveData = new SaveData();
                    saveData.Describe = describeBox.Text.Trim();
                    saveData.Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    //创建的文件位置
                    saveData.FilePath = this.gameDirectory + "/" + Convert.ToDateTime(saveData.Date).ToString("yyyy-MM-dd-HH-mm-ss");
                    //然后转Json字符串存入文件
                    File.WriteAllText(this.saveJsonPath, GlobalConstant.toJsonObject<SaveData>(this.saveJsonPath, saveData));
                    //复制存档
                    GlobalConstant.copyAllFilesFromDirectory(game.SaveDirectorPath, saveData.FilePath);
                    MessageBox.Show("创建成功!");
                    loadSaveFileAndShow();
                }
                MessageBox.Show("描述不能为空!");
            }
            MessageBox.Show("游戏备份数量最多为5个!");
        }

        //加载Save文件数据并展示在列表
        public void loadSaveFileAndShow()
        {
            //读取Save文件
            this.saveDataList = GlobalConstant.readJsonFile<SaveData>(this.saveJsonPath);
            //判断文件是否为空
            if (this.saveDataList != null)
            {
                //每次加载列表清除数据
                SaveListViewBox.Items.Clear();
                //逆序遍历
                for (int i = this.saveDataList.Count - 1; i >= 0; i--)
                {
                    SaveListViewBox.Items.Add(this.saveDataList[i].Describe).SubItems.Add(this.saveDataList[i].Date);
                }
            }
        }

        //点击删除按钮
        private void deleteButton_MouseClick(object sender, MouseEventArgs e)
        {
            if (SaveListViewBox.SelectedItems.Count > 0)
            {
                //选中的存档(以时间来判定)
                string time = SaveListViewBox.SelectedItems[0].SubItems[1].Text;
                if (MessageBox.Show("您真的要删除吗？", "此删除不可恢复",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    //删除Save文件中的信息
                    List<SaveData> data = GlobalConstant.toObjectFromJson<SaveData>(this.saveJsonPath);
                    for (int i = 0; i < data.Count; i++)
                    {
                        if (data[i].Date.Equals(time))
                        {
                            //删除游戏对应存档
                            GlobalConstant.deleteDirectory(data[i].FilePath);
                            //删除Save文件中的数据
                            data.Remove(data[i]);
                            break;
                        }
                    }
                    File.WriteAllText(this.saveJsonPath, JsonConvert.SerializeObject(data));
                    MessageBox.Show("删除成功!");
                    //重新加载列表
                    loadSaveFileAndShow();
                }
            }
            MessageBox.Show("你没有选中一个存档!");
        }

        //点击应用按钮
        private void applyButton_Click(object sender, EventArgs e)
        {
            if (SaveListViewBox.SelectedItems.Count > 0)
            {
                //选中的存档(以时间来判定)
                string time = SaveListViewBox.SelectedItems[0].SubItems[1].Text;
                if (MessageBox.Show("您确定要应用吗？", "应用对话框",
                   MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    //确认应用的数据
                    List<SaveData> data = GlobalConstant.toObjectFromJson<SaveData>(this.saveJsonPath);
                    for (int i = 0; i < data.Count; i++)
                    {
                        if (data[i].Date.Equals(time))
                        {
                            //删除对应游戏存档目录
                            GlobalConstant.deleteDirectory(this.game.SaveDirectorPath);
                            //复制备份数据数据到游戏存档目录
                            GlobalConstant.copyAllFilesFromDirectory(data[i].FilePath, this.game.SaveDirectorPath);
                            break;
                        }
                    }
                    MessageBox.Show("应用成功!");
                    //重新加载列表
                    loadSaveFileAndShow();
                }
            }
            MessageBox.Show("你没有选中一个存档!");
        }
    }
}
