using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyRank
{
    internal enum ExportFailureAction
    {
        RetryAdmin = 0,
        SaveToDataDir = 1,
        Cancel = 2
    }

    internal sealed class SaveAsForm : Form
    {
        readonly TextInput pathInput;
        readonly PillButton browseButton;
        readonly PillButton saveButton;
        readonly PillButton cancelButton;
        readonly Label titleLabel;
        readonly Label hintLabel;
        readonly string defaultFileName;
        float uiScale = 1f;

        public string SelectedPath { get; private set; }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public SaveAsForm(string title, string suggestedPath)
        {
            Text = "另存为";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            defaultFileName = Path.GetFileName(suggestedPath);

            titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = Theme.H1Font;
            titleLabel.ForeColor = Theme.Text;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.AutoSize = false;

            hintLabel = new Label();
            hintLabel.Text = "保存到（可输入任意目录，支持相对路径）：";
            hintLabel.Font = Theme.SmallFont;
            hintLabel.ForeColor = Theme.Muted;
            hintLabel.BackColor = Color.Transparent;
            hintLabel.AutoSize = false;

            pathInput = new TextInput();
            pathInput.Value = suggestedPath;
            pathInput.Hint = "例如 D:\\导出\\按键统计.csv";

            browseButton = new PillButton();
            browseButton.Text = "浏览…";
            browseButton.Click += delegate { Browse(); };

            saveButton = new PillButton();
            saveButton.Text = "保存";
            saveButton.Accent = true;
            saveButton.Click += delegate { TryAccept(); };

            cancelButton = new PillButton();
            cancelButton.Text = "取消";
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(titleLabel);
            Controls.Add(hintLabel);
            Controls.Add(pathInput);
            Controls.Add(browseButton);
            Controls.Add(saveButton);
            Controls.Add(cancelButton);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint dpi = 96;
            try { dpi = GetDpiForWindow(Handle); } catch { }
            if (dpi < 72) dpi = 96;
            uiScale = dpi / 96f;

            ClientSize = new Size(S(560), S(226));
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { }
            LayoutDialog();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            pathInput.FocusInput();
            pathInput.Inner.SelectAll();
        }

        int S(int v)
        {
            return (int)Math.Round(v * uiScale);
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

            titleLabel.Bounds = new Rectangle(pad, S(16), w - pad * 2, S(26));
            hintLabel.Bounds = new Rectangle(pad, S(48), w - pad * 2, S(18));
            pathInput.Bounds = new Rectangle(pad, S(74), w - pad * 2, S(40));

            int btnY = h - S(52);
            browseButton.Bounds = new Rectangle(pad, btnY, S(88), S(36));
            saveButton.Bounds = new Rectangle(w - pad - S(96), btnY, S(96), S(36));
            cancelButton.Bounds = new Rectangle(w - pad - S(96) - S(10) - S(88), btnY, S(88), S(36));
        }

        void Browse()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择保存目录";
                dialog.ShowNewFolderButton = true;
                string current = pathInput.Value.Trim();
                try
                {
                    string dir = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.SelectedPath = dir;
                }
                catch { }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string name = defaultFileName;
                    try
                    {
                        string existing = Path.GetFileName(pathInput.Value.Trim());
                        if (!string.IsNullOrEmpty(existing)) name = existing;
                    }
                    catch { }
                    pathInput.Value = Path.Combine(dialog.SelectedPath, name);
                    pathInput.Inner.SelectionStart = pathInput.Value.Length;
                }
            }
        }

        void TryAccept()
        {
            string raw = pathInput.Value.Trim();
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                raw = raw.Substring(1, raw.Length - 2).Trim();
            if (raw.Length == 0)
            {
                Warn("请输入保存路径");
                return;
            }

            raw = Environment.ExpandEnvironmentVariables(raw);
            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch (Exception ex)
            {
                Warn("路径无效：" + ex.Message);
                return;
            }

            if (Directory.Exists(full))
                full = Path.Combine(full, defaultFileName);

            string dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                DialogResult create = MessageBox.Show(this,
                    "目录不存在：\n" + dir + "\n\n要创建它吗？",
                    "另存为", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (create != DialogResult.Yes) return;
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    Warn("无法创建目录：" + ex.Message);
                    return;
                }
            }

            if (File.Exists(full))
            {
                DialogResult overwrite = MessageBox.Show(this,
                    "文件已存在，要覆盖吗？\n\n" + full,
                    "另存为", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (overwrite != DialogResult.Yes) return;
            }

            SelectedPath = full;
            DialogResult = DialogResult.OK;
            Close();
        }

        void Warn(string message)
        {
            hintLabel.Text = message;
            hintLabel.ForeColor = Theme.Danger;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                TryAccept();
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

        internal static bool Ask(IWin32Window owner, string title, string suggestedPath, out string selectedPath)
        {
            using (SaveAsForm form = new SaveAsForm(title, suggestedPath))
            {
                bool ok = form.ShowDialog(owner) == DialogResult.OK;
                selectedPath = form.SelectedPath;
                return ok;
            }
        }
    }

    internal sealed class ExportErrorForm : Form
    {
        readonly Label messageLabel;
        readonly Label titleLabel;
        readonly PillButton retryButton;
        readonly PillButton dataDirButton;
        readonly PillButton cancelButton;
        float uiScale = 1f;

        public ExportFailureAction Action { get; private set; }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        ExportErrorForm(string target, string error)
        {
            Text = "导出失败";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoScaleMode = AutoScaleMode.None;
            Action = ExportFailureAction.Cancel;

            titleLabel = new Label();
            titleLabel.Text = "没有权限写入目标位置";
            titleLabel.Font = Theme.H1Font;
            titleLabel.ForeColor = Theme.Text;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.AutoSize = false;

            messageLabel = new Label();
            messageLabel.Text = "目标：\n" + target + "\n\n原因：" + error +
                "\n\n可以：以管理员身份重试（完整权限），或改存到程序数据目录。";
            messageLabel.Font = Theme.SmallFont;
            messageLabel.ForeColor = Theme.Muted;
            messageLabel.BackColor = Color.Transparent;
            messageLabel.AutoSize = false;

            retryButton = new PillButton();
            retryButton.Text = "以管理员身份重试";
            retryButton.Accent = true;
            retryButton.Click += delegate { Action = ExportFailureAction.RetryAdmin; DialogResult = DialogResult.OK; Close(); };

            dataDirButton = new PillButton();
            dataDirButton.Text = "改存到数据目录";
            dataDirButton.Click += delegate { Action = ExportFailureAction.SaveToDataDir; DialogResult = DialogResult.OK; Close(); };

            cancelButton = new PillButton();
            cancelButton.Text = "取消";
            cancelButton.Click += delegate { Action = ExportFailureAction.Cancel; DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(titleLabel);
            Controls.Add(messageLabel);
            Controls.Add(retryButton);
            Controls.Add(dataDirButton);
            Controls.Add(cancelButton);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint dpi = 96;
            try { dpi = GetDpiForWindow(Handle); } catch { }
            if (dpi < 72) dpi = 96;
            uiScale = dpi / 96f;

            ClientSize = new Size(S(588), S(246));
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { }
            LayoutDialog();
        }

        int S(int v)
        {
            return (int)Math.Round(v * uiScale);
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

            titleLabel.Bounds = new Rectangle(pad, S(16), w - pad * 2, S(26));
            messageLabel.Bounds = new Rectangle(pad, S(48), w - pad * 2, h - S(48) - S(56));

            int btnY = h - S(52);
            retryButton.Bounds = new Rectangle(pad, btnY, S(160), S(36));
            dataDirButton.Bounds = new Rectangle(pad + S(170), btnY, S(150), S(36));
            cancelButton.Bounds = new Rectangle(w - pad - S(88), btnY, S(88), S(36));
        }

        internal static ExportFailureAction Ask(IWin32Window owner, string target, string error)
        {
            using (ExportErrorForm form = new ExportErrorForm(target, error))
            {
                form.ShowDialog(owner);
                return form.Action;
            }
        }
    }
}
