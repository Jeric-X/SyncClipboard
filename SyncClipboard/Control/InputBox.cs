using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public sealed class InputBox : Form
    {
        private TextBox _textBox;
        private Label _infoLabel;

        private InputBox()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this._textBox = new TextBox();
            this._infoLabel = new Label();
            this.SuspendLayout();

            this._textBox.Location = new System.Drawing.Point(19, 8);
            this._textBox.Name = "txtData";
            this._textBox.Size = new System.Drawing.Size(317, 23);
            this._textBox.TabIndex = 0;
            this._textBox.Text = "";
            this._textBox.KeyDown += this.TextBoxKeyDownHandler;

            this._infoLabel.BackColor = System.Drawing.SystemColors.ScrollBar;
            this._infoLabel.BorderStyle = BorderStyle.Fixed3D;
            this._infoLabel.FlatStyle = FlatStyle.System;
            this._infoLabel.ForeColor = System.Drawing.Color.Gray;
            this._infoLabel.Location = new System.Drawing.Point(19, 32);
            this._infoLabel.Name = "lblInfo";
            this._infoLabel.Size = new System.Drawing.Size(317, 16);
            this._infoLabel.TabIndex = 1;
            this._infoLabel.Text = "[Enter] OK  |  [Esc] Cancel";

            this.AutoScaleBaseSize = new System.Drawing.Size(6, 14);
            this.ClientSize = new System.Drawing.Size(350, 48);
            this.Controls.Add(this._infoLabel);
            this.Controls.Add(this._textBox);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.Name = "InputBox";
            this.Text = "InputBox";
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DialogResult = DialogResult.None;
            this.ResumeLayout(false);
        }

        public void SetText(string text)
        {
            _textBox.Text = text;
            _textBox.Select(_textBox.TextLength, 0);
        }

        //对键盘进行响应
        private void TextBoxKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }

        //显示InputBox
        public static string Show(string Title, string defaultText = "")
        {
            InputBox inputbox = new() { Text = Title };
            inputbox.SetText(defaultText);
            if (inputbox.ShowDialog() == DialogResult.OK)
            {
                return inputbox._textBox.Text;
            }
            return string.Empty;
        }
    }
}