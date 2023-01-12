using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GameSaveManager
{
    class GameInfoPage : UserControl
    {
        //自定义属性
        private Game game;

        private Label gameName;
        private PictureBox startBox;
        private Label label1;
        private ListBox listBox1;
        private Button createButton;
        private TextBox describeBox;
        private Label describeLabel;
        private PictureBox gamePictureBox;

        public GameInfoPage()
        {
            InitializeComponent();
        }

        //双击传入的game
        public GameInfoPage(Game game)
        {
            InitializeComponent();
            this.game = game;
            this.gameName.Text = game.Name;
            this.gamePictureBox.ImageLocation = game.PicturePath;
        }

        private void InitializeComponent()
        {
            this.gamePictureBox = new System.Windows.Forms.PictureBox();
            this.gameName = new System.Windows.Forms.Label();
            this.startBox = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.createButton = new System.Windows.Forms.Button();
            this.describeBox = new System.Windows.Forms.TextBox();
            this.describeLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.gamePictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.startBox)).BeginInit();
            this.SuspendLayout();
            // 
            // gamePictureBox
            // 
            this.gamePictureBox.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.gamePictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.gamePictureBox.Location = new System.Drawing.Point(54, 54);
            this.gamePictureBox.Margin = new System.Windows.Forms.Padding(0);
            this.gamePictureBox.Name = "gamePictureBox";
            this.gamePictureBox.Size = new System.Drawing.Size(120, 120);
            this.gamePictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.gamePictureBox.TabIndex = 0;
            this.gamePictureBox.TabStop = false;
            // 
            // gameName
            // 
            this.gameName.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.gameName.AutoSize = true;
            this.gameName.Location = new System.Drawing.Point(77, 182);
            this.gameName.Margin = new System.Windows.Forms.Padding(0);
            this.gameName.Name = "gameName";
            this.gameName.Size = new System.Drawing.Size(80, 18);
            this.gameName.TabIndex = 1;
            this.gameName.Text = "游戏名称";
            // 
            // startBox
            // 
            this.startBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.startBox.Image = global::GameSaveManager.Properties.Resources.startButtonImage;
            this.startBox.Location = new System.Drawing.Point(253, 54);
            this.startBox.Margin = new System.Windows.Forms.Padding(0);
            this.startBox.Name = "startBox";
            this.startBox.Size = new System.Drawing.Size(40, 40);
            this.startBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.startBox.TabIndex = 2;
            this.startBox.TabStop = false;
            this.startBox.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.startBox_MouseDoubleClick);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label1.Location = new System.Drawing.Point(191, 60);
            this.label1.Margin = new System.Windows.Forms.Padding(0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(57, 30);
            this.label1.TabIndex = 3;
            this.label1.Text = "启动";
            // 
            // listBox1
            // 
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 18;
            this.listBox1.Items.AddRange(new object[] {
            "2022-1-13 2:17 第一次",
            "2022-1-13 2:17 第一次",
            "2022-1-13 2:17 第一次",
            "2022-1-13 2:17 第一次"});
            this.listBox1.Location = new System.Drawing.Point(54, 348);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(491, 112);
            this.listBox1.TabIndex = 4;
            // 
            // createButton
            // 
            this.createButton.Location = new System.Drawing.Point(603, 60);
            this.createButton.Name = "createButton";
            this.createButton.Size = new System.Drawing.Size(101, 34);
            this.createButton.TabIndex = 5;
            this.createButton.Text = "创建存档";
            this.createButton.UseVisualStyleBackColor = true;
            // 
            // describeBox
            // 
            this.describeBox.Location = new System.Drawing.Point(336, 62);
            this.describeBox.Name = "describeBox";
            this.describeBox.Size = new System.Drawing.Size(261, 28);
            this.describeBox.TabIndex = 6;
            // 
            // describeLabel
            // 
            this.describeLabel.AutoSize = true;
            this.describeLabel.Location = new System.Drawing.Point(332, 38);
            this.describeLabel.Margin = new System.Windows.Forms.Padding(0);
            this.describeLabel.Name = "describeLabel";
            this.describeLabel.Size = new System.Drawing.Size(53, 18);
            this.describeLabel.TabIndex = 7;
            this.describeLabel.Text = "描述:";
            // 
            // GameInfoPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
            this.Controls.Add(this.describeLabel);
            this.Controls.Add(this.describeBox);
            this.Controls.Add(this.createButton);
            this.Controls.Add(this.listBox1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.startBox);
            this.Controls.Add(this.gameName);
            this.Controls.Add(this.gamePictureBox);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MaximumSize = new System.Drawing.Size(731, 540);
            this.MinimumSize = new System.Drawing.Size(731, 540);
            this.Name = "GameInfoPage";
            this.Size = new System.Drawing.Size(731, 540);
            ((System.ComponentModel.ISupportInitialize)(this.gamePictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.startBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        
        //双击开始图标运行游戏
        private void startBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            System.Diagnostics.Process.Start(game.StartupPath);
        }

        //
    }
}
