using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyRank
{
    internal sealed class MainForm : Form
    {
        const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string RunValueName = "KeyRank";

        readonly bool startHidden;
        readonly StatsStore store;
        readonly InputLog inputLog;
        readonly InputHook hook;
        readonly Timer tick;

        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ContextMenuStrip settingsMenu;
        ToolStripMenuItem trayPauseItem;

        Label lblTitle;
        Label lblSubtitle;
        Label lblStatus;
        StatusPill pill;
        StatsCard card;
        Segmented seg;
        SearchBox search;
        RankList list;
        PillButton btnPause;
        PillButton btnReset;
        PillButton btnLog;
        PillButton btnExport;
        PillButton btnSettings;
        PillButton btnHistory;
        HistoryForm historyForm;
        ToolTip tips;

        bool paused;
        bool exiting;
        bool balloonShown;
        bool trayAvailable = true;
        bool trayWarningShown;
        bool elevatePrompted;
        bool hookWarningPending;
        int saveTicks;
        long lastTotal = -1;
        long lastMouse = -1;
        long lastWheel = -1;
        List<KeyStat> lastRows = new List<KeyStat>();
        float uiScale = 1f;
        Icon appIcon;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public MainForm(bool startHidden)
        {
            this.startHidden = startHidden;

            store = new StatsStore(StatsStore.DefaultDataDir());
            Program.TryLog("data dir=" + store.DataDir);

            try
            {
                inputLog = new InputLog(store.DataDir);
            }
            catch (Exception ex)
            {
                inputLog = null;
                Program.TryLog("log unavailable: " + ex.Message);
            }

            hook = new InputHook();
            hook.Hit += delegate(KeyHit hit)
            {
                store.Record(hit.Id);
                if (inputLog != null) inputLog.Record(hit.Id, DateTime.Now);
            };

            Text = "按键排行榜";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(480, 620);
            KeyPreview = true;

            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { appIcon = null; }
            if (appIcon != null) Icon = appIcon;

            BuildUi();

            bool started = hook.Start();
            Program.TryLog("hook=" + started + (started ? "" : " err=" + hook.LastError));
            if (!started)
            {
                paused = true;
                hookWarningPending = true;
            }

            RefreshRows();
            UpdatePauseState();

            tick = new Timer();
            tick.Interval = 400;
            tick.Tick += OnTick;
        }

        int S(int v)
        {
            return (int)Math.Round(v * uiScale);
        }

        void BuildUi()
        {
            lblTitle = MakeLabel("按键排行榜", Theme.TitleFont, Theme.Text, ContentAlignment.MiddleLeft);
            lblSubtitle = MakeLabel("键盘 · 鼠标左/右键 点击统计", Theme.SmallFont, Theme.Muted, ContentAlignment.MiddleLeft);

            pill = new StatusPill();
            card = new StatsCard();

            seg = new Segmented();
            seg.Items = new string[] { "全部", "键盘", "鼠标", "滚轮" };
            seg.SelectedIndexChanged += delegate { RefreshRows(); };

            search = new SearchBox();
            search.QueryChanged += delegate { RefreshRows(); };

            list = new RankList();

            tips = new ToolTip();

            btnPause = MakeButton("\uE769", "暂停统计", true);
            btnPause.Click += delegate { TogglePause(); };
            btnReset = MakeButton("\uE72C", "重置统计", false);
            btnReset.Click += delegate { ResetCounts(); };
            btnLog = MakeButton("\uE81C", "按键记录（5 分钟一段）", false);
            btnLog.Click += delegate { OpenKeyLog(); };
            btnExport = MakeButton("\uE896", "导出排行 CSV", false);
            btnExport.Click += delegate { ExportCsv(); };
            btnSettings = MakeButton("\uE713", "设置", false);
            btnSettings.Click += delegate { ShowSettingsMenu(); };

            btnHistory = new PillButton();
            btnHistory.Text = "历史";
            btnHistory.Font = Theme.SmallFont;
            tips.SetToolTip(btnHistory, "历史排行（按天查看，Ctrl+H）");
            btnHistory.Click += delegate { OpenHistory(); };

            lblStatus = MakeLabel("", Theme.SmallFont, Theme.Muted, ContentAlignment.MiddleLeft);
            lblStatus.AutoEllipsis = true;

            Controls.Add(lblTitle);
            Controls.Add(lblSubtitle);
            Controls.Add(pill);
            Controls.Add(card);
            Controls.Add(seg);
            Controls.Add(search);
            Controls.Add(list);
            Controls.Add(lblStatus);
            Controls.Add(btnPause);
            Controls.Add(btnReset);
            Controls.Add(btnLog);
            Controls.Add(btnExport);
            Controls.Add(btnSettings);
            Controls.Add(btnHistory);
        }

        Label MakeLabel(string text, Font font, Color color, ContentAlignment align)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.TextAlign = align;
            label.AutoSize = false;
            return label;
        }

        PillButton MakeButton(string glyph, string tip, bool accent)
        {
            PillButton button = new PillButton();
            button.Glyph = glyph;
            button.Accent = accent;
            tips.SetToolTip(button, tip);
            return button;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint dpi = 96;
            try { dpi = GetDpiForWindow(Handle); }
            catch { }
            if (dpi < 72) dpi = 96;
            uiScale = dpi / 96f;
            if (uiScale < 0.75f) uiScale = 0.75f;
            if (uiScale > 2.5f) uiScale = 2.5f;

            ClientSize = new Size(S(440), S(610));
            MinimumSize = new Size(S(400), S(500));

            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            int maxHeight = wa.Height - S(16);
            if (Height > maxHeight) Height = Math.Max(S(560), maxHeight);

            list.SetScale(uiScale);
            ApplyWindowChrome();
            LayoutUi();
            UiLog("handle-created");
        }

        void UiLog(string tag)
        {
            if (Environment.GetEnvironmentVariable("KEYRANK_DEBUG") != "1") return;
            try
            {
                string line = string.Format("{0} {1} dpi={2} scale={3} client={4}x{5} size={6}x{7} screen={8}",
                    DateTime.Now.ToString("HH:mm:ss.fff"), tag, DeviceDpi, uiScale, ClientSize.Width, ClientSize.Height,
                    Width, Height, Screen.FromHandle(Handle).WorkingArea);
                File.AppendAllText(Path.Combine(store.DataDir, "ui.log"), line + Environment.NewLine);
            }
            catch
            {
            }
        }

        void CenterOnScreen()
        {
            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            int x = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
            int y = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
            Location = new Point(x, y);
        }

        void ApplyWindowChrome()
        {
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
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
            if (IsHandleCreated) UiLog("resize");
        }

        void LayoutUi()
        {
            if (lblTitle == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int pad = S(14);
            lblTitle.Bounds = new Rectangle(pad, S(6), Math.Max(S(120), w - pad - S(100)), S(26));
            lblSubtitle.Bounds = new Rectangle(pad + S(2), S(34), Math.Max(S(120), w - pad * 2), S(16));
            pill.Bounds = new Rectangle(w - pad - S(80), S(10), S(80), S(22));
            btnHistory.Bounds = new Rectangle(w - pad - S(80) - S(8) - S(56), S(8), S(56), S(26));

            card.Bounds = new Rectangle(pad, S(54), Math.Max(S(200), w - pad * 2), S(56));

            int segW = S(240);
            int gap = S(8);
            int searchW = Math.Max(S(120), w - pad * 2 - segW - gap);
            seg.Bounds = new Rectangle(pad, S(118), segW, S(30));
            search.Bounds = new Rectangle(w - pad - searchW, S(118), searchW, S(30));

            int footerY = h - S(38);
            int listTop = S(156);
            int listBottom = h - S(50);
            list.Bounds = new Rectangle(pad, listTop, Math.Max(S(200), w - pad * 2), Math.Max(S(100), listBottom - listTop));

            int btn = S(30);
            int btnGap = S(5);
            int bx = w - pad - btn;
            btnSettings.Bounds = new Rectangle(bx, footerY, btn, btn);
            bx -= btn + btnGap;
            btnExport.Bounds = new Rectangle(bx, footerY, btn, btn);
            bx -= btn + btnGap;
            btnLog.Bounds = new Rectangle(bx, footerY, btn, btn);
            bx -= btn + btnGap;
            btnReset.Bounds = new Rectangle(bx, footerY, btn, btn);
            bx -= btn + btnGap;
            btnPause.Bounds = new Rectangle(bx, footerY, btn, btn);

            lblStatus.Bounds = new Rectangle(pad, footerY + S(2), Math.Max(S(80), bx - pad - S(6)), S(24));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UiLog("shown");
            CenterOnScreen();
            SetUpTray();
            tick.Start();
            Application.AddMessageFilter(new WheelForwarder(list, this));

            if (!trayAvailable && !startHidden) BeginInvoke(new Action(OfferElevatedRestart));
            if (hookWarningPending) BeginInvoke(new Action(ShowHookWarning));
            if (startHidden) BeginInvoke(new Action(delegate { HideToTray(true); }));
        }

        void SetUpTray()
        {
            tray = new NotifyIcon();
            tray.Icon = appIcon != null ? appIcon : SystemIcons.Application;
            tray.Text = "按键排行榜 · 记录中";
            tray.Visible = true;

            trayMenu = new ContextMenuStrip();
            trayMenu.Renderer = new DarkMenuRenderer();
            trayMenu.BackColor = Color.FromArgb(26, 31, 40);
            trayMenu.ForeColor = Theme.Text;
            trayMenu.ShowImageMargin = false;
            trayMenu.Font = Theme.BodyFont;

            ToolStripMenuItem open = new ToolStripMenuItem("打开面板");
            open.Click += delegate { RestoreFromTray(); };
            trayMenu.Items.Add(open);

            trayPauseItem = new ToolStripMenuItem("暂停统计");
            trayPauseItem.Click += delegate { TogglePause(); };
            trayMenu.Items.Add(trayPauseItem);

            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { ExitApp(); };
            trayMenu.Items.Add(quit);

            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { RestoreFromTray(); };
            tray.Visible = true;

            try
            {
                trayAvailable = TraySupport.Probe(Handle, tray.Icon);
            }
            catch
            {
                trayAvailable = false;
            }
            Program.TryLog("tray available=" + trayAvailable);

            long total;
            long today;
            long mouse;
            long wheel;
            long todayWheel;
            store.GetSummary(out total, out today, out mouse, out wheel, out todayWheel);
            UpdateTrayText(total, today, mouse);
        }

        void OnTick(object sender, EventArgs e)
        {
            long total;
            long today;
            long mouse;
            long wheel;
            long todayWheel;
            store.GetSummary(out total, out today, out mouse, out wheel, out todayWheel);

            if (total != lastTotal || mouse != lastMouse || wheel != lastWheel) RefreshRows();

            if (store.IsDirty)
            {
                saveTicks++;
                if (saveTicks >= 25)
                {
                    saveTicks = 0;
                    if (!store.Save() && store.LastError != null)
                        SetStatus("保存失败：" + store.LastError, true);
                    if (inputLog != null) inputLog.Flush();
                }
            }
        }

        void RefreshRows()
        {
            int filter = seg.SelectedIndex;
            bool showKeyboard = filter == 0 || filter == 1;
            bool showMouse = filter == 0 || filter == 2;
            bool showWheel = filter == 3;

            long total;
            long today;
            long mouse;
            long wheel;
            long todayWheel;
            store.GetSummary(out total, out today, out mouse, out wheel, out todayWheel);

            List<KeyStat> rows = store.BuildRows(showKeyboard, showMouse, showWheel, search.Query);
            if (rows.Count == 0)
            {
                if (total == 0) list.SetEmptyMessage("还没有记录", "敲键盘、点鼠标，这里会实时排名");
                else if (search.Query.Length > 0) list.SetEmptyMessage("没有匹配的按键", "换个关键词试试");
                else list.SetEmptyMessage("这个分类还没有记录", "切换筛选看看其他按键");
            }

            list.SetItems(rows, showWheel ? wheel : total);
            card.Total = total;
            card.Today = today;
            card.MousePercent = total > 0 ? mouse * 100.0 / total : 0.0;
            card.Wheel = wheel;
            card.TodayWheel = todayWheel;
            card.Invalidate();

            lastTotal = total;
            lastMouse = mouse;
            lastWheel = wheel;
            lastRows = rows;
            UpdateFooter(total, today, mouse, wheel);
            UpdateTrayText(total, today, mouse);
        }

        void UpdateFooter(long total, long today, long mouse, long wheel)
        {
            if (paused)
            {
                SetStatus("已暂停 · 已记录 " + total.ToString("N0") + " 次", false);
                return;
            }
            string text = "已记录 " + total.ToString("N0") + " · 今日 " + today.ToString("N0") +
                " · 鼠标 " + mouse.ToString("N0") + " · 滚轮 " + wheel.ToString("N0") + "（独立）";
            SetStatus(text, false);
        }

        void UpdateTrayText(long total, long today, long mouse)
        {
            if (tray == null) return;

            string text = (paused ? "已暂停" : "记录中") + " · 总计 " + total.ToString("N0") + " · 今日 " + today.ToString("N0");
            if (lastRows != null && lastRows.Count > 0)
            {
                int percent = total > 0 ? (int)Math.Round(mouse * 100.0 / total) : 0;
                text += "\n" + lastRows[0].Label + " " + lastRows[0].Count.ToString("N0") + " · 鼠标 " + percent + "%";
            }
            if (text.Length > 63) text = text.Substring(0, 63);
            try { tray.Text = text; } catch { }
        }

        void SetStatus(string text, bool danger)
        {
            lblStatus.Text = text == null ? "" : text;
            lblStatus.ForeColor = danger ? Theme.Danger : Theme.Muted;
        }

        void TogglePause()
        {
            if (!paused)
            {
                hook.Stop();
                paused = true;
            }
            else
            {
                if (!hook.Start())
                {
                    MessageBox.Show(this, "无法恢复统计：\n" + (hook.LastError == null ? "未知错误" : hook.LastError),
                        "按键排行榜", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                paused = false;
            }
            UpdatePauseState();
            RefreshRows();
        }

        void UpdatePauseState()
        {
            pill.Paused = paused;
            pill.Invalidate();
            btnPause.Glyph = paused ? "\uE768" : "\uE769";
            btnPause.Invalidate();
            tips.SetToolTip(btnPause, paused ? "继续统计" : "暂停统计");
            if (trayPauseItem != null) trayPauseItem.Text = paused ? "继续统计" : "暂停统计";
            if (paused) SetStatus("已暂停 · 点击播放按钮继续统计", false);
        }

        void ResetCounts()
        {
            if (!ConfirmResetForm.Ask(this)) return;

            store.Reset();
            store.Save();
            lastTotal = -1;
            lastMouse = -1;
            RefreshRows();
            SetStatus("统计已清空", false);
        }

        void RestartElevated()
        {
            try
            {
                string dataDir = Path.GetFullPath(store.DataDir);
                string arguments = "--elevated --data-dir " + Quote(dataDir);
                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, arguments);
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                ExitApp();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "以管理员身份重启失败：" + ex.Message,
                    "按键排行榜", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ExportCsv()
        {
            string suggested = Path.Combine(store.DataDir,
                "按键统计_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv");

            string chosen;
            if (!SaveAsForm.Ask(this, "导出按键统计", suggested, out chosen)) return;
            ExportTo(chosen);
        }

        void OpenKeyLog()
        {
            if (inputLog == null)
            {
                MessageBox.Show(this, "按键记录不可用：无法创建日志目录。\n排行榜统计仍然正常。",
                    "按键排行榜", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (KeyLogForm form = new KeyLogForm(inputLog, DateTime.Today))
            {
                form.ShowDialog(this);
            }
        }

        void OpenHistory()
        {
            try
            {
                if (historyForm != null && !historyForm.IsDisposed)
                {
                    if (historyForm.WindowState == FormWindowState.Minimized)
                        historyForm.WindowState = FormWindowState.Normal;
                    historyForm.Activate();
                    return;
                }

                historyForm = new HistoryForm(store, DateTime.Today);
                historyForm.FormClosed += delegate { historyForm = null; };
                historyForm.Show(this);
            }
            catch (Exception ex)
            {
                SetStatus("无法打开历史排行：" + ex.Message, true);
            }
        }

        void ExportTo(string path)
        {
            string error;
            bool ok = store.ExportCsv(path, out error);
            if (ok)
            {
                SetStatus("已导出：" + path, false);
                return;
            }

            ExportFailureAction action = ExportErrorForm.Ask(this, path, error == null ? "未知错误" : error);
            if (action == ExportFailureAction.SaveToDataDir)
            {
                string fallback = Path.Combine(store.DataDir, Path.GetFileName(path));
                if (store.ExportCsv(fallback, out error)) SetStatus("已导出到数据目录：" + fallback, false);
                else MessageBox.Show(this, "仍然无法保存：\n" + error, "按键排行榜", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (action == ExportFailureAction.RetryAdmin)
            {
                if (RunElevatedExport(path)) SetStatus("已以管理员身份导出：" + path, false);
                else SetStatus("管理员导出未完成（可能取消了 UAC）", true);
            }
        }

        bool RunElevatedExport(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string dataDir = Path.GetFullPath(store.DataDir);
                string arguments = "--export-csv " + Quote(fullPath) + " --data-dir " + Quote(dataDir);

                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, arguments);
                info.UseShellExecute = true;
                info.Verb = "runas";
                using (Process process = Process.Start(info))
                {
                    if (process != null) process.WaitForExit(180000);
                }
                return File.Exists(fullPath);
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

        void ClearLog()
        {
            if (inputLog == null) return;

            DialogResult answer = MessageBox.Show(this,
                "确定要清空按键记录吗？排行榜统计不受影响。",
                "清空记录", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            inputLog.Clear();
            SetStatus("按键记录已清空", false);
        }

        void OpenLogFolder()
        {
            if (inputLog == null) return;
            try { Process.Start("explorer.exe", "\"" + inputLog.Dir + "\""); }
            catch (Exception ex) { SetStatus("无法打开日志文件夹：" + ex.Message, true); }
        }

        void ShowSettingsMenu()
        {
            if (settingsMenu != null)
            {
                settingsMenu.Dispose();
                settingsMenu = null;
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            settingsMenu = menu;
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Color.FromArgb(26, 31, 40);
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Font = Theme.BodyFont;

            ToolStripMenuItem elevate = new ToolStripMenuItem("以管理员身份重新启动");
            elevate.Click += delegate { RestartElevated(); };
            menu.Items.Add(elevate);

            ToolStripMenuItem topMost = new ToolStripMenuItem("窗口置顶");
            topMost.Checked = TopMost;
            topMost.Click += delegate { TopMost = !TopMost; };
            menu.Items.Add(topMost);

            ToolStripMenuItem adminAlways = new ToolStripMenuItem("默认以管理员身份启动");
            adminAlways.Checked = Program.IsRunAsAdminLayer();
            adminAlways.Click += delegate
            {
                Program.SetRunAsAdminLayer(!Program.IsRunAsAdminLayer());
                SetStatus(Program.IsRunAsAdminLayer() ? "已开启：下次启动会请求管理员权限" : "已关闭默认管理员启动", false);
            };
            menu.Items.Add(adminAlways);

            ToolStripMenuItem autoStart = new ToolStripMenuItem("开机自动启动");
            autoStart.Checked = IsAutoStart();
            autoStart.Click += delegate
            {
                SetAutoStart(!IsAutoStart());
                SetStatus(IsAutoStart() ? "已开启开机自启" : "已关闭开机自启", false);
            };
            menu.Items.Add(autoStart);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem keyLog = new ToolStripMenuItem("按键记录（5 分钟一段）…");
            keyLog.Click += delegate { OpenKeyLog(); };
            menu.Items.Add(keyLog);

            ToolStripMenuItem history = new ToolStripMenuItem("历史排行（按天）…");
            history.Click += delegate { OpenHistory(); };
            menu.Items.Add(history);

            ToolStripMenuItem logFolder = new ToolStripMenuItem("打开日志文件夹");
            logFolder.Click += delegate { OpenLogFolder(); };
            menu.Items.Add(logFolder);

            ToolStripMenuItem folder = new ToolStripMenuItem("打开数据文件夹");
            folder.Click += delegate { OpenDataFolder(); };
            menu.Items.Add(folder);

            ToolStripMenuItem clearLog = new ToolStripMenuItem("清空按键记录…");
            clearLog.Click += delegate { ClearLog(); };
            menu.Items.Add(clearLog);

            ToolStripMenuItem about = new ToolStripMenuItem("关于 KeyRank");
            about.Click += delegate { ShowAbout(); };
            menu.Items.Add(about);

            menu.Show(btnSettings, new Point(0, btnSettings.Height + 6));
        }

        void OpenDataFolder()
        {
            try { Process.Start("explorer.exe", "\"" + store.DataDir + "\""); }
            catch (Exception ex) { SetStatus("无法打开文件夹：" + ex.Message, true); }
        }

        void ShowAbout()
        {
            string text = "KeyRank 1.1.0\n\n" +
                "统计每个按键与鼠标左/右键的点击次数；同时在本机记录每次按键的时间与顺序，" +
                "可在“按键记录”里按 5 分钟一段查看。不记录输入的文字内容。\n\n" +
                "每天自动留存一份排行快照，可在主界面「历史」（Ctrl+H）里按天查看。\n\n" +
                "数据文件：\n" + store.FilePath + "\n日志目录：\n" + (inputLog == null ? "（不可用）" : inputLog.Dir) + "\n\n" +
                "普通权限下无法统计以管理员身份运行的窗口；右键以管理员身份运行本程序即可完整统计。\n\n" +
                "关闭窗口会最小化到托盘并继续计数，右键托盘图标可退出。";
            MessageBox.Show(this, text, "关于 KeyRank", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    return key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (on) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --tray");
                    else key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("修改开机自启失败：" + ex.Message + "\n\n" +
                    "如果当前是受限启动（例如从聊天窗口打开），请先用设置里的“以管理员身份重新启动”，再开启开机自启。",
                    "按键排行榜", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void HideToTray(bool silent)
        {
            if (!trayAvailable)
            {
                ShowInTaskbar = true;
                WindowState = FormWindowState.Minimized;
                if (!silent && !trayWarningShown)
                {
                    trayWarningShown = true;
                    MessageBox.Show(this,
                        "当前运行环境不允许注册系统托盘图标（常见于从聊天窗口/受限入口启动）。\n\n" +
                        "已改为最小化到任务栏，程序仍在后台计数。\n" +
                        "如需右下角托盘常驻，请关闭本程序后，在资源管理器里双击 ClickCounter.exe，\n" +
                        "或右键“以管理员身份运行”。",
                        "托盘图标不可用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            Hide();
            ShowInTaskbar = false;
            if (!silent && !balloonShown && tray != null)
            {
                balloonShown = true;
                try
                {
                    tray.ShowBalloonTip(3000, "仍在统计",
                        "窗口已最小化到托盘，按键和鼠标点击会继续记录。右键托盘图标可退出。",
                        ToolTipIcon.Info);
                }
                catch { }
            }
        }

        void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void OfferElevatedRestart()
        {
            if (elevatePrompted || trayAvailable) return;
            elevatePrompted = true;

            string marker = Path.Combine(store.DataDir, "skip-elevate-prompt");
            if (File.Exists(marker)) return;

            DialogResult answer = MessageBox.Show(this,
                "当前启动方式限制了系统功能：Windows 拒绝注册托盘图标，开机自启也无法写入注册表。\n\n" +
                "是否以管理员身份重新启动？在 UAC 弹窗点“是”后，托盘图标和开机自启即可正常使用。\n\n" +
                "点“否”将以受限模式继续运行（关闭窗口会最小化到任务栏），之后可在设置里再次重启。",
                "需要更高权限", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

            if (answer == DialogResult.Yes)
            {
                RestartElevated();
                return;
            }

            try { File.WriteAllText(marker, "1"); } catch { }
            SetStatus("受限模式：托盘/自启不可用，设置 → 以管理员身份重新启动", true);
        }

        void ShowHookWarning()
        {
            string reason = hook.LastError == null ? "未知错误" : hook.LastError;
            MessageBox.Show(this,
                "全局键鼠钩子安装失败：" + reason + "\n\n" +
                "可能是安全软件拦截。可尝试以管理员身份重新运行本程序，或点击左下角播放按钮重试。",
                "无法开始统计", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.P)) { TogglePause(); return true; }
            if (keyData == (Keys.Control | Keys.R)) { ResetCounts(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { OpenKeyLog(); return true; }
            if (keyData == (Keys.Control | Keys.H)) { OpenHistory(); return true; }
            if (!search.ContainsFocus)
            {
                if (keyData == Keys.Down) { list.ScrollBy(S(28)); return true; }
                if (keyData == Keys.Up) { list.ScrollBy(-S(28)); return true; }
                if (keyData == Keys.PageDown) { list.ScrollPage(true); return true; }
                if (keyData == Keys.PageUp) { list.ScrollPage(false); return true; }
                if (keyData == Keys.Home) { list.ScrollToTop(); return true; }
                if (keyData == Keys.End) { list.ScrollToBottom(); return true; }
            }
            if (keyData == (Keys.Control | Keys.E)) { ExportCsv(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { search.FocusInput(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowMessage)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(false);
                return;
            }

            Shutdown();
            base.OnFormClosing(e);
        }

        void Shutdown()
        {
            try { tick.Stop(); } catch { }
            try { store.Save(); } catch { }
            try { if (inputLog != null) inputLog.Close(); } catch { }
            try { hook.Dispose(); } catch { }
            if (settingsMenu != null)
            {
                settingsMenu.Dispose();
                settingsMenu = null;
            }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
        }
    }
}
