using GameSaveManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace cn.thx
{
    class AddGamePage : UserControl
    {
        /*
         * 自定义属性
         */
        //图片路径
        private string picturePath;

        //控件
        private Label gamePictureLabel;
        private Label gameName;
        private Label archiveDirectory;
        private Label startupFile;
        private TextBox gameNameBox;
        private TextBox archiveDirectoryBox;
        private TextBox startupFileBox;
        private Button selectDirectoryButton;
        private Button selectFileButton;
        private FolderBrowserDialog folderBrowserDialog;
        private OpenFileDialog openFileDialog;
        private Button addButton;
        private OpenFileDialog openFilePicture;
        private PictureBox gamePictureBox;

        //构造方法执行初始化组件
        public AddGamePage()
        {
            InitializeComponent();
        }

        //初始化组件
        private void InitializeComponent()
        {
            this.gamePictureBox = new System.Windows.Forms.PictureBox();
            this.gamePictureLabel = new System.Windows.Forms.Label();
            this.gameName = new System.Windows.Forms.Label();
            this.archiveDirectory = new System.Windows.Forms.Label();
            this.startupFile = new System.Windows.Forms.Label();
            this.gameNameBox = new System.Windows.Forms.TextBox();
            this.archiveDirectoryBox = new System.Windows.Forms.TextBox();
            this.startupFileBox = new System.Windows.Forms.TextBox();
            this.selectDirectoryButton = new System.Windows.Forms.Button();
            this.selectFileButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
            this.openFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.addButton = new System.Windows.Forms.Button();
            this.openFilePicture = new System.Windows.Forms.OpenFileDialog();
            ((System.ComponentModel.ISupportInitialize)(this.gamePictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // gamePictureBox
            // 
            this.gamePictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.gamePictureBox.Location = new System.Drawing.Point(182, 21);
            this.gamePictureBox.Margin = new System.Windows.Forms.Padding(0);
            this.gamePictureBox.Name = "gamePictureBox";
            this.gamePictureBox.Size = new System.Drawing.Size(200, 200);
            this.gamePictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.gamePictureBox.TabIndex = 0;
            this.gamePictureBox.TabStop = false;
            this.gamePictureBox.Click += new System.EventHandler(this.gamePicture_Click);
            // 
            // gamePictureLabel
            // 
            this.gamePictureLabel.AutoSize = true;
            this.gamePictureLabel.Location = new System.Drawing.Point(228, 227);
            this.gamePictureLabel.Name = "gamePictureLabel";
            this.gamePictureLabel.Size = new System.Drawing.Size(97, 15);
            this.gamePictureLabel.TabIndex = 1;
            this.gamePictureLabel.Text = "选择游戏图片";
            // 
            // gameName
            // 
            this.gameName.AutoSize = true;
            this.gameName.Location = new System.Drawing.Point(49, 287);
            this.gameName.Name = "gameName";
            this.gameName.Size = new System.Drawing.Size(67, 15);
            this.gameName.TabIndex = 2;
            this.gameName.Text = "游戏名称";
            // 
            // archiveDirectory
            // 
            this.archiveDirectory.AutoSize = true;
            this.archiveDirectory.Location = new System.Drawing.Point(49, 342);
            this.archiveDirectory.Name = "archiveDirectory";
            this.archiveDirectory.Size = new System.Drawing.Size(67, 15);
            this.archiveDirectory.TabIndex = 3;
            this.archiveDirectory.Text = "存档目录";
            // 
            // startupFile
            // 
            this.startupFile.AutoSize = true;
            this.startupFile.Location = new System.Drawing.Point(49, 397);
            this.startupFile.Name = "startupFile";
            this.startupFile.Size = new System.Drawing.Size(67, 15);
            this.startupFile.TabIndex = 4;
            this.startupFile.Text = "启动文件";
            // 
            // gameNameBox
            // 
            this.gameNameBox.AllowDrop = true;
            this.gameNameBox.Location = new System.Drawing.Point(122, 284);
            this.gameNameBox.Name = "gameNameBox";
            this.gameNameBox.Size = new System.Drawing.Size(327, 25);
            this.gameNameBox.TabIndex = 5;
            // 
            // archiveDirectoryBox
            // 
            this.archiveDirectoryBox.Location = new System.Drawing.Point(122, 339);
            this.archiveDirectoryBox.Margin = new System.Windows.Forms.Padding(0);
            this.archiveDirectoryBox.Name = "archiveDirectoryBox";
            this.archiveDirectoryBox.ReadOnly = true;
            this.archiveDirectoryBox.Size = new System.Drawing.Size(327, 25);
            this.archiveDirectoryBox.TabIndex = 6;
            // 
            // startupFileBox
            // 
            this.startupFileBox.Location = new System.Drawing.Point(122, 394);
            this.startupFileBox.Margin = new System.Windows.Forms.Padding(0);
            this.startupFileBox.Name = "startupFileBox";
            this.startupFileBox.ReadOnly = true;
            this.startupFileBox.Size = new System.Drawing.Size(327, 25);
            this.startupFileBox.TabIndex = 7;
            // 
            // selectDirectoryButton
            // 
            this.selectDirectoryButton.AutoSize = true;
            this.selectDirectoryButton.Location = new System.Drawing.Point(450, 339);
            this.selectDirectoryButton.Margin = new System.Windows.Forms.Padding(0);
            this.selectDirectoryButton.Name = "selectDirectoryButton";
            this.selectDirectoryButton.Size = new System.Drawing.Size(80, 25);
            this.selectDirectoryButton.TabIndex = 8;
            this.selectDirectoryButton.Text = "选择目录";
            this.selectDirectoryButton.UseVisualStyleBackColor = true;
            this.selectDirectoryButton.Click += new System.EventHandler(this.selectDirectoryButton_Click);
            // 
            // selectFileButton
            // 
            this.selectFileButton.AutoSize = true;
            this.selectFileButton.Location = new System.Drawing.Point(450, 394);
            this.selectFileButton.Margin = new System.Windows.Forms.Padding(0);
            this.selectFileButton.Name = "selectFileButton";
            this.selectFileButton.Size = new System.Drawing.Size(80, 25);
            this.selectFileButton.TabIndex = 9;
            this.selectFileButton.Text = "选择文件";
            this.selectFileButton.UseVisualStyleBackColor = true;
            this.selectFileButton.Click += new System.EventHandler(this.selectFileButton_Click);
            // 
            // openFileDialog
            // 
            this.openFileDialog.FileName = "openFileDialog";
            // 
            // addButton
            // 
            this.addButton.AutoSize = true;
            this.addButton.Location = new System.Drawing.Point(546, 339);
            this.addButton.Name = "addButton";
            this.addButton.Size = new System.Drawing.Size(78, 80);
            this.addButton.TabIndex = 10;
            this.addButton.Text = "添加";
            this.addButton.UseVisualStyleBackColor = true;
            this.addButton.Click += new System.EventHandler(this.addButton_Click);
            // 
            // openFilePicture
            // 
            this.openFilePicture.FileName = "openFileDialog1";
            // 
            // AddGamePage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.addButton);
            this.Controls.Add(this.selectFileButton);
            this.Controls.Add(this.selectDirectoryButton);
            this.Controls.Add(this.startupFileBox);
            this.Controls.Add(this.archiveDirectoryBox);
            this.Controls.Add(this.gameNameBox);
            this.Controls.Add(this.startupFile);
            this.Controls.Add(this.archiveDirectory);
            this.Controls.Add(this.gameName);
            this.Controls.Add(this.gamePictureLabel);
            this.Controls.Add(this.gamePictureBox);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MaximumSize = new System.Drawing.Size(650, 450);
            this.MinimumSize = new System.Drawing.Size(650, 450);
            this.Name = "AddGamePage";
            this.Size = new System.Drawing.Size(648, 448);
            ((System.ComponentModel.ISupportInitialize)(this.gamePictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        //选择目录按钮
        private void selectDirectoryButton_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.Description = "请选择文件夹";
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog.ShowNewFolderButton = true;
            //判断之前地址存在就默认（相当于缓存一下）
            if (archiveDirectoryBox.Text.Length > 0) folderBrowserDialog.SelectedPath = archiveDirectoryBox.Text;
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                archiveDirectoryBox.Text = folderBrowserDialog.SelectedPath;
            }
        }

        //选择文件按钮
        private void selectFileButton_Click(object sender, EventArgs e)
        {

            openFileDialog.Title = "请选择文件";
            openFileDialog.Filter = "可执行文件(*.exe)|*.exe";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                //返回文件的完整路径
                startupFileBox.Text = openFileDialog.FileName;
            }

            //return file;
        }

        //添加游戏按钮
        private void addButton_Click(object sender, EventArgs e)
        {
            bool flag = true;
            if (GlobalConstant.isEmpty(gameNameBox.Text))
            {
                MessageBox.Show("游戏名称不能为空！");
                flag = false;
            }
            else if (isExistGame(gameNameBox.Text))
            {
                MessageBox.Show("游戏已经存在!！");
                flag = false;
            }
            //else if (GlobalConstant.isEmpty(archiveDirectoryBox.Text))
            //{
            //    MessageBox.Show("游戏目录不能为空！");
            //    flag = false;
            //}
            //else if (GlobalConstant.isEmpty(startupFileBox.Text))
            //{
            //    MessageBox.Show("游戏文件不能为空！");
            //    flag = false;
            //}

            if (flag)
            {
                addSaveData();
                //GlobalConstant.GAMEINFO_EMPTY = false;
                MessageBox.Show("添加成功！");

            }



        }

        //选择图片
        private void gamePicture_Click(object sender, EventArgs e)
        {
            openFilePicture.Title = "请选择图片";
            openFilePicture.Filter = "图片(*.jpg;*.jpeg)|*.jpg;*jpeg";
            if (openFilePicture.ShowDialog() == DialogResult.OK)
            {
                gamePictureBox.Load(openFilePicture.FileName);
                this.picturePath = openFilePicture.FileName;
                //MessageBox.Show(this.picturePath);
            };

        }

        //添加保存游戏
        private void addSaveData()
        {
            Game game = new Game();
            game.Name = gameNameBox.Text.Trim();//去除首位空白字符
            game.PicturePath = this.picturePath;
            game.SaveDirectorPath = archiveDirectoryBox.Text;
            game.StartupPath = startupFileBox.Text;

            //创建游戏文件夹
            Directory.CreateDirectory(GlobalConstant.EXE_PATH + "/save_data/" + game.Name);
            //创建Save.json文件并关闭文件
            File.Create(GlobalConstant.EXE_PATH + "/save_data/" + game.Name + "/Save.json").Close();
            //写入文件
            try { File.WriteAllText(GlobalConstant.GAMEINFO_PATH, GlobalConstant.toJsonObject<Game>(GlobalConstant.GAMEINFO_PATH, game)); }
            catch (Exception e) { MessageBox.Show("添加异常! " + e.Message); }

            MessageBox.Show(game.ToString());

        }

        //判断游戏名是否有重复
        private bool isExistGame(string name)
        {
            List<Game> games = GlobalConstant.toObjectFromJson<Game>(GlobalConstant.GAMEINFO_PATH);
            if (games != null)
            {
                foreach (Game game in games)
                {
                    if (game.Name.Equals(name))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

    }
}
