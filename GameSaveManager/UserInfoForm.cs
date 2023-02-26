
using Newtonsoft.Json;
using System;
using System.Collections;
using System.IO;
using System.Windows.Forms;

namespace GameSaveManager
{
    public class UserInfoForm : System.Windows.Forms.Form
    {
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.TextBox textBox3;
        private System.Windows.Forms.TextBox textBox4;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;

        public UserInfoForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.textBox3 = new System.Windows.Forms.TextBox();
            this.textBox4 = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(142, 72);
            this.textBox1.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(205, 28);
            this.textBox1.TabIndex = 0;
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(142, 109);
            this.textBox2.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(205, 28);
            this.textBox2.TabIndex = 1;
            // 
            // textBox3
            // 
            this.textBox3.Location = new System.Drawing.Point(142, 146);
            this.textBox3.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBox3.Name = "textBox3";
            this.textBox3.Size = new System.Drawing.Size(205, 28);
            this.textBox3.TabIndex = 2;
            // 
            // textBox4
            // 
            this.textBox4.Location = new System.Drawing.Point(142, 184);
            this.textBox4.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBox4.Name = "textBox4";
            this.textBox4.Size = new System.Drawing.Size(205, 28);
            this.textBox4.TabIndex = 3;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(34, 76);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(89, 18);
            this.label1.TabIndex = 4;
            this.label1.Text = "accessKey";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(34, 113);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(89, 18);
            this.label2.TabIndex = 5;
            this.label2.Text = "secretKey";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(47, 150);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(62, 18);
            this.label3.TabIndex = 6;
            this.label3.Text = "bucket";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(47, 187);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(62, 18);
            this.label4.TabIndex = 7;
            this.label4.Text = "domain";
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(51, 241);
            this.button1.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(84, 36);
            this.button1.TabIndex = 8;
            this.button1.Text = "确认";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.MouseClick += new System.Windows.Forms.MouseEventHandler(this.button1_MouseClick);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(263, 241);
            this.button2.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(84, 36);
            this.button2.TabIndex = 9;
            this.button2.Text = "取消";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.MouseClick += new System.Windows.Forms.MouseEventHandler(this.button2_MouseClick);
            // 
            // UserInfoForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(389, 302);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBox4);
            this.Controls.Add(this.textBox3);
            this.Controls.Add(this.textBox2);
            this.Controls.Add(this.textBox1);
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "UserInfoForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Load += new System.EventHandler(this.UserInfoForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        //窗口加载
        private void UserInfoForm_Load(object sender, System.EventArgs e)
        {

        }

        //确认事件
        private void button1_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            Hashtable hashtable = new Hashtable();
            string accessKey = this.textBox1.Text.Trim();
            string secretKey = this.textBox2.Text.Trim();
            string bucket = this.textBox3.Text.Trim();
            string domain = this.textBox4.Text.Trim();

            if (String.IsNullOrEmpty(accessKey) ||
                String.IsNullOrEmpty(secretKey) ||
                String.IsNullOrEmpty(bucket) ||
                String.IsNullOrEmpty(domain))
            {
                MessageBox.Show("填写数据不能为空！");
            }
            else
            {
                hashtable.Add("accessKey", accessKey);
                hashtable.Add("secretKey", secretKey);
                hashtable.Add("bucket", bucket);
                hashtable.Add("domain", domain);
                try
                {
                    File.WriteAllText(GlobalConstant.USERINFO_PATH, JsonConvert.SerializeObject(hashtable));
                    MessageBox.Show("修改成功！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("添加异常! " + ex.Message);
                }
                finally { this.Close(); }
            }
        }

        //取消事件
        private void button2_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.Close();
        }

    }
}
