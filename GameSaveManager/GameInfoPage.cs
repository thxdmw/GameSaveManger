using System.IO;
using System.Windows.Forms;

namespace GameSaveManager
{
    class GameInfoPage : UserControl
    {
        //自定义属性
        private Game game;
        //游戏目录
        private string gameDirectory;
        //SaveJson文件的位置
        private string saveJsonPath;

        private Label gameName;
        private PictureBox startBox;
        private Label label1;
        private Button createButton;
        private TextBox describeBox;
        private Label describeLabel;
        private ListBox describeListBox;
        private ListBox dateListBox;
        private Button applyButton;
        private Button deleteButton;
        private Button backButton;
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
            this.describeListBox = new System.Windows.Forms.ListBox();
            this.dateListBox = new System.Windows.Forms.ListBox();
            this.applyButton = new System.Windows.Forms.Button();
            this.deleteButton = new System.Windows.Forms.Button();
            this.backButton = new System.Windows.Forms.Button();
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
            this.gameName.Location = new System.Drawing.Point(68, 175);
            this.gameName.Margin = new System.Windows.Forms.Padding(0);
            this.gameName.Name = "gameName";
            this.gameName.Size = new System.Drawing.Size(67, 15);
            this.gameName.TabIndex = 1;
            this.gameName.Text = "游戏名称";
            // 
            // startBox
            // 
            this.startBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
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
            this.createButton.Location = new System.Drawing.Point(423, 23);
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
            this.describeBox.Location = new System.Drawing.Point(186, 25);
            this.describeBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.describeBox.Name = "describeBox";
            this.describeBox.Size = new System.Drawing.Size(232, 25);
            this.describeBox.TabIndex = 6;
            // 
            // describeLabel
            // 
            this.describeLabel.AutoSize = true;
            this.describeLabel.Location = new System.Drawing.Point(182, 5);
            this.describeLabel.Margin = new System.Windows.Forms.Padding(0);
            this.describeLabel.Name = "describeLabel";
            this.describeLabel.Size = new System.Drawing.Size(45, 15);
            this.describeLabel.TabIndex = 7;
            this.describeLabel.Text = "描述:";
            // 
            // describeListBox
            // 
            this.describeListBox.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.describeListBox.FormattingEnabled = true;
            this.describeListBox.ItemHeight = 32;
            this.describeListBox.Items.AddRange(new object[] {
            "1",
            "2",
            "3",
            "4",
            "5"});
            this.describeListBox.Location = new System.Drawing.Point(48, 214);
            this.describeListBox.Margin = new System.Windows.Forms.Padding(0);
            this.describeListBox.Name = "describeListBox";
            this.describeListBox.Size = new System.Drawing.Size(280, 196);
            this.describeListBox.TabIndex = 8;
            // 
            // dateListBox
            // 
            this.dateListBox.FormattingEnabled = true;
            this.dateListBox.ItemHeight = 15;
            this.dateListBox.Location = new System.Drawing.Point(339, 214);
            this.dateListBox.Margin = new System.Windows.Forms.Padding(0);
            this.dateListBox.Name = "dateListBox";
            this.dateListBox.Size = new System.Drawing.Size(142, 199);
            this.dateListBox.TabIndex = 9;
            // 
            // applyButton
            // 
            this.applyButton.Location = new System.Drawing.Point(482, 348);
            this.applyButton.Name = "applyButton";
            this.applyButton.Size = new System.Drawing.Size(50, 50);
            this.applyButton.TabIndex = 10;
            this.applyButton.Text = "应用";
            this.applyButton.UseVisualStyleBackColor = true;
            // 
            // deleteButton
            // 
            this.deleteButton.Location = new System.Drawing.Point(538, 348);
            this.deleteButton.Name = "deleteButton";
            this.deleteButton.Size = new System.Drawing.Size(50, 50);
            this.deleteButton.TabIndex = 11;
            this.deleteButton.Text = "删除";
            this.deleteButton.UseVisualStyleBackColor = true;
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
            // GameInfoPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
            this.Controls.Add(this.backButton);
            this.Controls.Add(this.deleteButton);
            this.Controls.Add(this.applyButton);
            this.Controls.Add(this.dateListBox);
            this.Controls.Add(this.describeListBox);
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
                System.Diagnostics.Process.Start(game.StartupPath);

            }
            else
            {
                MessageBox.Show("游戏路径不存在!");
            }
        }

        //点击创建存档图标
        private void createButton_MouseClick(object sender, MouseEventArgs e)
        {
            //判断列表项的数量
            if (describeListBox.Items.Count == 5) { 
            
            }
        }


    }
}
