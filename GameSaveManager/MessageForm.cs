using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameSaveManager
{
    public class MessageForm : System.Windows.Forms.Form
    {
        public MessageForm() { }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MessageForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(278, 244);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MessageForm";
            this.ResumeLayout(false);

        }
    }
}
