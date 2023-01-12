using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace GameSaveManager
{
    class GameListPage : UserControl
    {
        //自定义属性
        //游戏列表
        private List<Game> games;

        //控件
        private System.Windows.Forms.ListView gameList;
        private ImageList imageList;
        private IContainer components;
        private System.Windows.Forms.Label GameListLable;

        //构造方法执行初始化组件
        public GameListPage()
        {
            InitializeComponent();
            showGameList();
        }

        //初始化组件
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GameListPage));
            this.gameList = new System.Windows.Forms.ListView();
            this.GameListLable = new System.Windows.Forms.Label();
            this.imageList = new System.Windows.Forms.ImageList(this.components);
            this.SuspendLayout();
            // 
            // gameList
            // 
            this.gameList.Activation = System.Windows.Forms.ItemActivation.TwoClick;
            this.gameList.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.gameList.HideSelection = false;
            this.gameList.Location = new System.Drawing.Point(0, 60);
            this.gameList.Margin = new System.Windows.Forms.Padding(0);
            this.gameList.Name = "gameList";
            this.gameList.Size = new System.Drawing.Size(731, 461);
            this.gameList.TabIndex = 1;
            this.gameList.UseCompatibleStateImageBehavior = false;
            this.gameList.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.gameList_MouseDoubleClick);
            // 
            // GameListLable
            // 
            this.GameListLable.AutoSize = true;
            this.GameListLable.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.GameListLable.Location = new System.Drawing.Point(0, 12);
            this.GameListLable.Margin = new System.Windows.Forms.Padding(0);
            this.GameListLable.Name = "GameListLable";
            this.GameListLable.Size = new System.Drawing.Size(137, 39);
            this.GameListLable.TabIndex = 2;
            this.GameListLable.Text = "游戏列表";
            this.GameListLable.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // imageList
            // 
            this.imageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList.ImageStream")));
            this.imageList.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList.Images.SetKeyName(0, "image1.jpeg");
            // 
            // GameListPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.GameListLable);
            this.Controls.Add(this.gameList);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MaximumSize = new System.Drawing.Size(731, 540);
            this.MinimumSize = new System.Drawing.Size(731, 540);
            this.Name = "GameListPage";
            this.Size = new System.Drawing.Size(731, 540);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        //展示所有游戏
        private void showGameList()
        {
            this.games = GlobalConstant.toObjectFromJson<Game>(GlobalConstant.GAMEINFO_PATH);
            if (games != null)
            {
                foreach (Game game in games)
                {
                    gameList.Items.Add(game.Name);
                }
                gameList.LargeImageList = imageList;
                foreach (ListViewItem item in gameList.Items)
                {
                    item.ImageIndex = 0;
                }
            }

        }

        //双击游戏展示游戏详细信息
        private void gameList_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            string choose = null;
            Game game = null;
            if (e.Button == MouseButtons.Left)
            {
                choose = gameList.Items[gameList.FocusedItem.Index].Text;//选中菜单栏的文本
                foreach (Game item in games)
                {
                    if (item.Name.Equals(choose))
                    {
                        game = item;
                    }
                }
            }
            //进入界面
            entryGameInfoPage(game);
            //MessageBox.Show(game.Name);
        }

        //进入游戏详情界面
        private void entryGameInfoPage(Game game)
        {
            GlobalConstant.form.getSplitContainer().Panel2.Controls.Clear();
            GameInfoPage gameInfoPage = new GameInfoPage(game);
            gameInfoPage.Parent = GlobalConstant.form.getSplitContainer().Panel2;
            gameInfoPage.Dock = DockStyle.Fill;
            gameInfoPage.Show();
        }
    }
}
