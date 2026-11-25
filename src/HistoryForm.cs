using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyRank
{
    internal sealed class HistoryForm : Form
    {
        static readonly string[] Weekdays = new string[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        readonly StatsStore store;
        readonly Label titleLabel;
        readonly Label subtitleLabel;
        readonly Label dateLabel;
        readonly Label statusLabel;
        readonly PillButton prevButton;
        readonly PillButton nextButton;
        readonly PillButton todayButton;
        readonly PillButton totalButton;
        readonly PillButton exportButton;
        readonly PillButton closeButton;
        readonly RankList list;
        readonly Timer timer;
        readonly List<DateTime> days = new List<DateTime>();

        DateTime currentDay;
        bool showingTotal;
        long lastTotal = -1;
        float uiScale = 1f;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public HistoryForm(StatsStore statsStore, DateTime day)
        {
            store = statsStore;
            currentDay = day.Date;

            Text = "历史排行";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 560);
            MinimumSize = new Size(380, 420);
            KeyPreview = true;

            titleLabel = NewLabel("历史排行", Theme.H1Font, Theme.Text);
            subtitleLabel = NewLabel("按天留存的排行快照 · 全部 = 键盘 + 鼠标左右键", Theme.SmallFont, Theme.Muted);
            dateLabel = NewLabel("", Theme.BoldFont, Theme.Text);
            statusLabel = NewLabel("", Theme.SmallFont, Theme.Muted);

            prevButton = NewButton("◀", "上一天");
            prevButton.Click += delegate { Navigate(-1); };
            nextButton = NewButton("▶", "下一天");
            nextButton.Click += delegate { Navigate(1); };
            todayButton = NewButton("今天", "回到今天");
            todayButton.Click += delegate { LoadDay(DateTime.Today); };
            totalButton = NewButton("累计", "查看全部时间的排行");
            totalButton.Click += delegate { ShowTotal(); };
            exportButton = NewButton("导出 CSV", "导出这一天的排行");
            exportButton.Click += delegate { ExportDay(); };
            closeButton = NewButton("关闭", "关闭窗口");
            closeButton.Click += delegate { Close(); };

            list = new RankList();
            list.SetEmptyMessage("这一天没有记录", "用 ◀ ▶ 换个日期看看");

            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(prevButton);
            Controls.Add(nextButton);
            Controls.Add(dateLabel);
            Controls.Add(todayButton);
            Controls.Add(totalButton);
            Controls.Add(exportButton);
            Controls.Add(list);
            Controls.Add(statusLabel);
            Controls.Add(closeButton);

            LoadDays();
            LoadDay(currentDay);

            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                if (showingTotal) return;
                if (currentDay.Date != DateTime.Today) return;
                if (store.GetDayTotal(currentDay) == lastTotal) return;
                LoadDay(currentDay);
            };
            timer.Start();
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
            button.Font = Theme.SmallFont;
            return button;
        }

        int S(int v)
        {
            return (int)Math.Round(v * uiScale);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint dpi = 96;
            try { dpi = GetDpiForWindow(Handle); } catch { }
            if (dpi < 72) dpi = 96;
            uiScale = dpi / 96f;
            if (uiScale < 0.75f) uiScale = 0.75f;
            if (uiScale > 2.5f) uiScale = 2.5f;

            ClientSize = new Size(S(440), S(560));
            MinimumSize = new Size(S(380), S(420));

            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
                int corner = 2;
                DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));
            }
            catch
            {
            }

            list.SetScale(uiScale);
            LayoutUi();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
        }

        void LayoutUi()
        {
            if (titleLabel == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int pad = S(14);
            titleLabel.Bounds = new Rectangle(pad, S(8), Math.Max(S(120), w - pad * 2), S(24));
            subtitleLabel.Bounds = new Rectangle(pad, S(32), Math.Max(S(120), w - pad * 2), S(16));

            int navY = S(56);
            int navH = S(28);

            int exportW = S(76);
            int exportX = Math.Max(pad + S(150), w - pad - exportW);
            int totalW = S(48);
            int totalX = exportX - S(6) - totalW;
            int todayW = S(48);
            int todayX = totalX - S(6) - todayW;
            int nextX = todayX - S(8) - S(30);
            int dateX = pad + S(38);
            int dateW = Math.Max(S(80), nextX - S(6) - dateX);

            prevButton.Bounds = new Rectangle(pad, navY, S(30), navH);
            dateLabel.Bounds = new Rectangle(dateX, navY, dateW, navH);
            nextButton.Bounds = new Rectangle(nextX, navY, S(30), navH);
            todayButton.Bounds = new Rectangle(todayX, navY, todayW, navH);
            totalButton.Bounds = new Rectangle(totalX, navY, totalW, navH);
            exportButton.Bounds = new Rectangle(exportX, navY, exportW, navH);

            int listTop = S(94);
            int listBottom = h - S(46);
            list.Bounds = new Rectangle(pad, listTop, Math.Max(S(160), w - pad * 2), Math.Max(S(80), listBottom - listTop));

            statusLabel.Bounds = new Rectangle(pad, h - S(34), Math.Max(S(100), w - pad * 2 - S(76)), S(24));
            closeButton.Bounds = new Rectangle(w - pad - S(64), h - S(36), S(64), S(28));
        }

        void LoadDays()
        {
            days.Clear();
            try { days.AddRange(store.GetHistoryDays()); }
            catch { }
        }

        void Navigate(int direction)
        {
            if (showingTotal)
            {
                LoadDay(currentDay);
                return;
            }

            if (days.Count == 0)
            {
                LoadDay(currentDay);
                return;
            }

            int index = days.IndexOf(currentDay.Date);
            int next = index < 0 ? days.Count - 1 : index + direction;
            if (next < 0) next = 0;
            if (next >= days.Count) next = days.Count - 1;
            LoadDay(days[next]);
        }

        void LoadDay(DateTime day)
        {
            showingTotal = false;
            currentDay = day.Date;
            List<KeyStat> rows;
            long total;
            try
            {
                rows = store.BuildDayRows(currentDay, true, true, false);
                total = store.GetDayTotal(currentDay);
            }
            catch (Exception ex)
            {
                rows = new List<KeyStat>();
                total = 0;
                Program.TryLog("history load failed: " + ex.Message);
            }

            list.SetItems(rows, total);
            lastTotal = total;
            dateLabel.Text = currentDay.ToString("yyyy-MM-dd") + "  " + Weekdays[(int)currentDay.DayOfWeek];
            if (rows.Count == 0)
                statusLabel.Text = "当天总计 0 次 · 没有记录";
            else
                statusLabel.Text = string.Format("当天总计 {0:N0} 次 · {1} 个按键", total, rows.Count);

            prevButton.Enabled = true;
            nextButton.Enabled = true;
        }

        void ShowTotal()
        {
            showingTotal = true;
            List<KeyStat> rows = store.BuildRows(true, true, false, null);
            long total;
            long today;
            long mouse;
            long wheel;
            long todayWheel;
            store.GetSummary(out total, out today, out mouse, out wheel, out todayWheel);

            list.SetItems(rows, total);
            lastTotal = total;
            dateLabel.Text = "累计 · 全部时间";
            statusLabel.Text = string.Format("总点击 {0:N0} 次 · 滚轮 {1:N0} · {2} 个按键", total, wheel, rows.Count);
        }

        void ExportDay()
        {
            string suggested = Path.Combine(store.DataDir,
                (showingTotal ? "累计排行_" : "历史排行_") + currentDay.ToString("yyyyMMdd") + ".csv");
            string chosen;
            if (!SaveAsForm.Ask(this, showingTotal ? "导出累计排行" : "导出历史排行", suggested, out chosen)) return;

            string error;
            bool ok = showingTotal ? store.ExportCsv(chosen, out error) : store.ExportDayCsv(currentDay, chosen, out error);
            if (ok)
            {
                statusLabel.Text = "已导出：" + chosen;
                return;
            }
            MessageBox.Show(this, "导出失败：\n" + (error == null ? "未知错误" : error),
                "历史排行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Left) { Navigate(-1); return true; }
            if (keyData == Keys.Right) { Navigate(1); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { timer.Stop(); timer.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
