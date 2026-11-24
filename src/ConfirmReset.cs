using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyRank
{
    internal sealed class ConfirmResetForm : Form
    {
        const string Keyword = "RESET";

        readonly TextInput input;
        readonly PillButton okButton;
        readonly PillButton cancelButton;
        readonly Label titleLabel;
        readonly Label bodyLabel;
        float uiScale = 1f;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public static bool Ask(IWin32Window owner)
        {
            using (ConfirmResetForm form = new ConfirmResetForm())
            {
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public ConfirmResetForm()
        {
            Text = "重置所有统计";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoScaleMode = AutoScaleMode.None;

            titleLabel = new Label();
            titleLabel.Text = "确定要重置吗？";
            titleLabel.Font = Theme.H1Font;
            titleLabel.ForeColor = Theme.Text;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.AutoSize = false;

            bodyLabel = new Label();
            bodyLabel.Text = "将清空所有按键与鼠标点击次数，且不可恢复。\n" +
                "日志文件不会被删除。\n\n" +
                "请输入 " + Keyword + " 以确认：";
            bodyLabel.Font = Theme.SmallFont;
            bodyLabel.ForeColor = Theme.Muted;
            bodyLabel.BackColor = Color.Transparent;
            bodyLabel.AutoSize = false;

            input = new TextInput();
            input.Hint = Keyword;
            input.ValueChanged += delegate
            {
                string typed = input.Value.Trim().ToUpperInvariant();
                okButton.Enabled = typed == Keyword;
            };

            okButton = new PillButton();
            okButton.Text = "重置";
            okButton.Accent = true;
            okButton.Enabled = false;
            okButton.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            cancelButton = new PillButton();
            cancelButton.Text = "取消";
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(titleLabel);
            Controls.Add(bodyLabel);
            Controls.Add(input);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            KeyPreview = true;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                if (okButton != null && okButton.Enabled)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                return true;
            }
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint dpi = 96;
            try { dpi = GetDpiForWindow(Handle); } catch { }
            if (dpi < 72) dpi = 96;
            uiScale = dpi / 96f;

            ClientSize = new Size(S(430), S(262));
            ApplyDarkTitle();
            LayoutDialog();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.FocusInput();
        }

        int S(int v)
        {
            return (int)Math.Round(v * uiScale);
        }

        void ApplyDarkTitle()
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch
            {
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutDialog();
        }

        void LayoutDialog()
        {
            if (titleLabel == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int pad = S(22);

            titleLabel.Bounds = new Rectangle(pad, S(18), w - pad * 2, S(28));
            bodyLabel.Bounds = new Rectangle(pad, S(52), w - pad * 2, S(76));
            input.Bounds = new Rectangle(pad, S(132), w - pad * 2, S(38));

            int btnH = S(36);
            int btnY = h - btnH - S(20);
            okButton.Bounds = new Rectangle(w - pad - S(96), btnY, S(96), btnH);
            cancelButton.Bounds = new Rectangle(w - pad - S(96) - S(10) - S(88), btnY, S(88), btnH);
        }
    }
}
