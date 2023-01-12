namespace GameSaveManager
{
    partial class Form
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form));
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.menuList = new System.Windows.Forms.ListView();
            this.MenuImageList = new System.Windows.Forms.ImageList(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainer
            // 
            this.splitContainer.BackColor = System.Drawing.SystemColors.Control;
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Location = new System.Drawing.Point(0, 0);
            this.splitContainer.Margin = new System.Windows.Forms.Padding(0);
            this.splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            this.splitContainer.Panel1.Controls.Add(this.menuList);
            // 
            // splitContainer.Panel2
            // 
            this.splitContainer.Panel2.BackColor = System.Drawing.SystemColors.Control;
            this.splitContainer.Size = new System.Drawing.Size(862, 450);
            this.splitContainer.SplitterDistance = 195;
            this.splitContainer.SplitterWidth = 5;
            this.splitContainer.TabIndex = 0;
            // 
            // menuList
            // 
            this.menuList.Alignment = System.Windows.Forms.ListViewAlignment.Left;
            this.menuList.BackColor = System.Drawing.Color.White;
            this.menuList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.menuList.HideSelection = false;
            this.menuList.LargeImageList = this.MenuImageList;
            this.menuList.Location = new System.Drawing.Point(0, 0);
            this.menuList.Margin = new System.Windows.Forms.Padding(0);
            this.menuList.Name = "menuList";
            this.menuList.Size = new System.Drawing.Size(195, 450);
            this.menuList.TabIndex = 0;
            this.menuList.UseCompatibleStateImageBehavior = false;
            this.menuList.MouseClick += new System.Windows.Forms.MouseEventHandler(this.menuList_MouseClick);
            // 
            // MenuImageList
            // 
            this.MenuImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("MenuImageList.ImageStream")));
            this.MenuImageList.TransparentColor = System.Drawing.Color.Transparent;
            this.MenuImageList.Images.SetKeyName(0, "gameListImage.jpeg");
            this.MenuImageList.Images.SetKeyName(1, "addGameImage.jpeg");
            // 
            // Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(862, 450);
            this.Controls.Add(this.splitContainer);
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(880, 497);
            this.MinimumSize = new System.Drawing.Size(880, 497);
            this.Name = "Form";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "GameSaveManager";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.splitContainer.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.ListView menuList;
        private System.Windows.Forms.ImageList MenuImageList;
    }
}

