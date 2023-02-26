using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

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
        private Button deleteGameButton;
        private Button configUserButton;
        private ErrorProvider errorProvider1;
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
            this.deleteGameButton = new System.Windows.Forms.Button();
            this.configUserButton = new System.Windows.Forms.Button();
            this.errorProvider1 = new System.Windows.Forms.ErrorProvider(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).BeginInit();
            this.SuspendLayout();
            // 
            // gameList
            // 
            this.gameList.Activation = System.Windows.Forms.ItemActivation.TwoClick;
            this.gameList.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.gameList.HideSelection = false;
            this.gameList.Location = new System.Drawing.Point(0, 50);
            this.gameList.Margin = new System.Windows.Forms.Padding(0);
            this.gameList.MultiSelect = false;
            this.gameList.Name = "gameList";
            this.gameList.Size = new System.Drawing.Size(650, 400);
            this.gameList.TabIndex = 1;
            this.gameList.UseCompatibleStateImageBehavior = false;
            this.gameList.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.gameList_MouseDoubleClick);
            // 
            // GameListLable
            // 
            this.GameListLable.AutoSize = true;
            this.GameListLable.Font = new System.Drawing.Font("微软雅黑", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.GameListLable.Location = new System.Drawing.Point(-4, 10);
            this.GameListLable.Margin = new System.Windows.Forms.Padding(0);
            this.GameListLable.Name = "GameListLable";
            this.GameListLable.Size = new System.Drawing.Size(114, 32);
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
            // deleteGameButton
            // 
            this.deleteGameButton.AutoSize = true;
            this.deleteGameButton.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.deleteGameButton.Location = new System.Drawing.Point(515, 0);
            this.deleteGameButton.Margin = new System.Windows.Forms.Padding(0);
            this.deleteGameButton.Name = "deleteGameButton";
            this.deleteGameButton.Size = new System.Drawing.Size(125, 50);
            this.deleteGameButton.TabIndex = 3;
            this.deleteGameButton.Text = "删除选定的游戏";
            this.deleteGameButton.UseVisualStyleBackColor = true;
            this.deleteGameButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.deleteGameButton_MouseClick);
            // 
            // configUserButton
            // 
            this.configUserButton.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.configUserButton.Location = new System.Drawing.Point(113, 0);
            this.configUserButton.Margin = new System.Windows.Forms.Padding(0);
            this.configUserButton.Name = "configUserButton";
            this.configUserButton.Size = new System.Drawing.Size(125, 50);
            this.configUserButton.TabIndex = 4;
            this.configUserButton.Text = "配置用户文件";
            this.configUserButton.UseVisualStyleBackColor = true;
            this.configUserButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.configUserButton_MouseClick);
            // 
            // errorProvider1
            // 
            this.errorProvider1.ContainerControl = this;
            // 
            // GameListPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Controls.Add(this.configUserButton);
            this.Controls.Add(this.deleteGameButton);
            this.Controls.Add(this.GameListLable);
            this.Controls.Add(this.gameList);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.MaximumSize = new System.Drawing.Size(650, 450);
            this.MinimumSize = new System.Drawing.Size(650, 450);
            this.Name = "GameListPage";
            this.Size = new System.Drawing.Size(648, 448);
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).EndInit();
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

        //删除选定的游戏
        private void deleteGameButton_MouseClick(object sender, MouseEventArgs e)
        {
            if (gameList.SelectedItems.Count > 0)
            {
                //选中的游戏名
                string name = gameList.SelectedItems[0].Text;
                if (MessageBox.Show("您真的要删除 " + name + " 吗？", "此删除不可恢复", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    //删除GameInfo文件中的信息
                    List<Game> games = GlobalConstant.toObjectFromJson<Game>(GlobalConstant.GAMEINFO_PATH);
                    for (int i = 0; i < games.Count; i++)
                    {
                        if (games[i].Name.Equals(name))
                        {
                            games.Remove(games[i]);
                            break;
                        }
                    }
                    File.WriteAllText(GlobalConstant.GAMEINFO_PATH, JsonConvert.SerializeObject(games));
                    //删除游戏目录
                    GlobalConstant.deleteDirectory(GlobalConstant.EXE_PATH + "/save_data/" + name);
                    MessageBox.Show("删除成功:" + name);
                    //刷新页面
                    GlobalConstant.refreshPage(new GameListPage(), GlobalConstant.form.getSplitContainer().Panel2);
                }
                return;
            }
            MessageBox.Show("你没有选中任何游戏!");
        }

        //点击配置用户文件按钮
        private void configUserButton_MouseClick(object sender, MouseEventArgs e)
        {
            UserInfoForm form = new UserInfoForm();
            //form.Show();
            form.ShowDialog(this);
        }
    }
}
