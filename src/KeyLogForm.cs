using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyRank
{
    internal sealed class KeyLogForm : Form
    {
        readonly InputLog log;
        readonly Label titleLabel;
        readonly Label subtitleLabel;
        readonly Label dateLabel;
        readonly Label statusLabel;
        readonly PillButton prevButton;
        readonly PillButton nextButton;
        readonly PillButton todayButton;
        readonly PillButton copyButton;
        readonly PillButton exportButton;
        readonly PillButton refreshButton;
        readonly PillButton closeButton;
        readonly Panel viewPanel;
        readonly TextBox view;
        readonly List<DateTime> days = new List<DateTime>();
        DateTime currentDay;
        float uiScale = 1f;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public KeyLogForm(InputLog inputLog, DateTime day)
        {
            log = inputLog;
            currentDay = day.Date;

            Text = "按键记录";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(660, 560);
            MinimumSize = new Size(520, 400);

            titleLabel = NewLabel("按键记录", Theme.H1Font, Theme.Text);
            subtitleLabel = NewLabel("以 5 分钟为一段，按键按发生顺序排列", Theme.SmallFont, Theme.Muted);
            dateLabel = NewLabel("", Theme.BoldFont, Theme.Text);
            statusLabel = NewLabel("", Theme.SmallFont, Theme.Muted);

            prevButton = NewButton("◀", "上一天");
            prevButton.Click += delegate { Navigate(-1); };
            nextButton = NewButton("▶", "下一天");
            nextButton.Click += delegate { Navigate(1); };
            todayButton = NewButton("今天", "回到今天");
            todayButton.Click += delegate { LoadDay(DateTime.Today); };

            copyButton = NewButton("复制全部", "复制当前文本");
            copyButton.Click += delegate { CopyAll(); };
            exportButton = NewButton("导出 TXT", "保存为 txt 文件");
            exportButton.Click += delegate { ExportTxt(); };
            refreshButton = NewButton("刷新", "重新读取");
            refreshButton.Click += delegate { LoadDay(currentDay); };
            closeButton = NewButton("关闭", "关闭");
            closeButton.Click += delegate { Close(); };

            viewPanel = new Panel();
            viewPanel.BackColor = Theme.Card;
            viewPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, viewPanel.Width - 1, viewPanel.Height - 1);
                Theme.FillRound(e.Graphics, r, 12, Theme.Card);
                Theme.StrokeRound(e.Graphics, r, 12, Theme.Stroke);
            };

            view = new TextBox();
            view.Multiline = true;
            view.ReadOnly = true;
            view.WordWrap = true;
            view.ScrollBars = ScrollBars.Vertical;
            view.BorderStyle = BorderStyle.None;
            view.BackColor = Theme.Card;
            view.ForeColor = Theme.Text;
            view.Font = Theme.BodyFont;
            viewPanel.Controls.Add(view);

            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(prevButton);
            Controls.Add(nextButton);
            Controls.Add(dateLabel);
            Controls.Add(todayButton);
            Controls.Add(viewPanel);
            Controls.Add(statusLabel);
            Controls.Add(copyButton);
            Controls.Add(exportButton);
            Controls.Add(refreshButton);
            Controls.Add(closeButton);

            LoadDays();
            LoadDay(currentDay);
        }

        Label NewLabel(string text, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            return label;
        }

        PillButton NewButton(string text, string tip)
        {
            PillButton button = new PillButton();
            button.Text = text;
            button.Font = Theme.BodyFont;
            return button;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint dpi = 96;
            try { dpi = GetDpiForWindow(Handle); } catch { }
            if (dpi < 72) dpi = 96;
            uiScale = dpi / 96f;
            ClientSize = new Size(S(660), S(560));
            MinimumSize = new Size(S(520), S(400));
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { }
            LayoutViewer();
        }

        int S(int v)
        {
            return (int)Math.Round(v * uiScale);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutViewer();
        }

        void LayoutViewer()
        {
            if (titleLabel == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int pad = S(18);

            titleLabel.Bounds = new Rectangle(pad, S(12), w - pad * 2, S(28));
            subtitleLabel.Bounds = new Rectangle(pad, S(44), w - pad * 2, S(18));

            int navY = S(68);
            prevButton.Bounds = new Rectangle(pad, navY, S(36), S(32));
            dateLabel.Bounds = new Rectangle(pad + S(42), navY, S(150), S(32));
            nextButton.Bounds = new Rectangle(pad + S(198), navY, S(36), S(32));
            todayButton.Bounds = new Rectangle(pad + S(242), navY, S(64), S(32));

            int bottom = h - S(58);
            viewPanel.Bounds = new Rectangle(pad, S(106), w - pad * 2, bottom - S(106));
            view.Bounds = new Rectangle(S(12), S(12), viewPanel.Width - S(24), viewPanel.Height - S(24));

            int btnY = h - S(48);
            copyButton.Bounds = new Rectangle(pad, btnY, S(100), S(34));
            exportButton.Bounds = new Rectangle(pad + S(108), btnY, S(100), S(34));
            refreshButton.Bounds = new Rectangle(pad + S(216), btnY, S(80), S(34));
            closeButton.Bounds = new Rectangle(w - pad - S(80), btnY, S(80), S(34));
            statusLabel.Bounds = new Rectangle(pad + S(304), btnY + S(3), Math.Max(S(80), w - pad * 2 - S(384)), S(26));
        }

        void LoadDays()
        {
            days.Clear();
            try { days.AddRange(log.GetAvailableDays()); }
            catch { }
        }

        void Navigate(int direction)
        {
            if (days.Count == 0) return;
            DateTime target = currentDay;
            if (direction < 0)
            {
                for (int i = days.Count - 1; i >= 0; i--)
                {
                    if (days[i] < currentDay) { target = days[i]; break; }
                }
            }
            else
            {
                for (int i = 0; i < days.Count; i++)
                {
                    if (days[i] > currentDay) { target = days[i]; break; }
                }
            }
            if (target != currentDay) LoadDay(target);
        }

        void LoadDay(DateTime day)
        {
            currentDay = day.Date;
            dateLabel.Text = currentDay.ToString("yyyy-MM-dd");
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.Text = "";

            view.Text = log.BuildDayText(currentDay);
            view.SelectionStart = view.TextLength;
            view.ScrollToCaret();
            view.Focus();

            bool hasPrev = false;
            bool hasNext = false;
            foreach (DateTime candidate in days)
            {
                if (candidate < currentDay) hasPrev = true;
                if (candidate > currentDay) hasNext = true;
            }
            prevButton.Enabled = hasPrev;
            nextButton.Enabled = hasNext;
        }

        void ExportTxt()
        {
            string dataDir = Path.GetDirectoryName(log.Dir);
            if (string.IsNullOrEmpty(dataDir)) dataDir = log.Dir;

            string suggested = Path.Combine(dataDir, "按键记录_" + currentDay.ToString("yyyy-MM-dd") + ".txt");
            string chosen;
            if (!SaveAsForm.Ask(this, "导出按键记录 TXT", suggested, out chosen)) return;

            string text = log.BuildDayText(currentDay);
            string error;
            if (FileExport.WriteTextFile(chosen, text, log.Dir, out error))
            {
                statusLabel.ForeColor = Theme.Green;
                statusLabel.Text = "已导出：" + chosen;
                return;
            }

            ExportFailureAction action = ExportErrorForm.Ask(this, chosen, error == null ? "未知错误" : error);
            if (action == ExportFailureAction.SaveToDataDir)
            {
                string fallback = Path.Combine(dataDir, Path.GetFileName(chosen));
                if (FileExport.WriteTextFile(fallback, text, log.Dir, out error))
                {
                    statusLabel.ForeColor = Theme.Green;
                    statusLabel.Text = "已导出到数据目录：" + fallback;
                }
                else
                {
                    MessageBox.Show(this, "仍然无法保存：\n" + error, "按键记录",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else if (action == ExportFailureAction.RetryAdmin)
            {
                if (RunElevatedRecordExport(chosen))
                {
                    statusLabel.ForeColor = Theme.Green;
                    statusLabel.Text = "已以管理员身份导出：" + chosen;
                }
                else
                {
                    statusLabel.ForeColor = Theme.Danger;
                    statusLabel.Text = "管理员导出未完成（可能取消了 UAC）";
                }
            }
        }

        bool RunElevatedRecordExport(string path)
        {
            try
            {
                string dataDir = Path.GetDirectoryName(log.Dir);
                if (string.IsNullOrEmpty(dataDir)) dataDir = log.Dir;
                string arguments = "--export-record " + Quote(Path.GetFullPath(path)) +
                    " --day " + currentDay.ToString("yyyy-MM-dd") +
                    " --data-dir " + Quote(Path.GetFullPath(dataDir));

                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, arguments);
                info.UseShellExecute = true;
                info.Verb = "runas";
                using (Process process = Process.Start(info))
                {
                    if (process != null) process.WaitForExit(180000);
                }
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        void CopyAll()
        {
            try
            {
                if (view.TextLength > 0) Clipboard.SetText(view.Text);
                statusLabel.ForeColor = Theme.Green;
                statusLabel.Text = "已复制到剪贴板";
            }
            catch (Exception ex)
            {
                statusLabel.ForeColor = Theme.Danger;
                statusLabel.Text = "复制失败：" + ex.Message;
            }
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
    }
}
