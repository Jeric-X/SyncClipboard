using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class InputBox : System.Windows.Forms.Form
    {

        private System.Windows.Forms.TextBox _textBox;
        private System.Windows.Forms.Label _infoLabel;

        private InputBox()
        {
            InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._textBox = new System.Windows.Forms.TextBox();
            this._infoLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();

            this._textBox.Location = new System.Drawing.Point(19, 8);
            this._textBox.Name = "txtData";
            this._textBox.Size = new System.Drawing.Size(317, 23);
            this._textBox.TabIndex = 0;
            this._textBox.Text = "";
            this._textBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtData_KeyDown);

            this._infoLabel.BackColor = System.Drawing.SystemColors.ScrollBar;
            this._infoLabel.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this._infoLabel.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this._infoLabel.ForeColor = System.Drawing.Color.Gray;
            this._infoLabel.Location = new System.Drawing.Point(19, 32);
            this._infoLabel.Name = "lblInfo";
            this._infoLabel.Size = new System.Drawing.Size(317, 16);
            this._infoLabel.TabIndex = 1;
            this._infoLabel.Text = "[Enter] OK  |  [Esc] Cancel";

            this.AutoScaleBaseSize = new System.Drawing.Size(6, 14);
            this.ClientSize = new System.Drawing.Size(350, 48);
            this.ControlBox = false;
            this.Controls.Add(this._infoLabel);
            this.Controls.Add(this._textBox);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Name = "InputBox";
            this.Text = "InputBox";
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

            this.ResumeLayout(false);
        }

        //对键盘进行响应
        private void txtData_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _textBox.Text = string.Empty;
                this.Close();
            }
        }

        //显示InputBox
        public static string Show(string Title)
        {
            InputBox inputbox = new InputBox();
            inputbox.Text = Title;
            inputbox.ShowDialog();
            return inputbox._textBox.Text;
        }
    }
}