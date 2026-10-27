using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;

namespace LoyGoldPrice
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Length == 2 && args[0] == "--probe")
            {
                try
                {
                    var q = Quotes.Fetch().GetAwaiter().GetResult();
                    File.WriteAllText(args[1], q.Summary);
                    Environment.ExitCode = 0;
                }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\LoyGoldPrice.Minimal" +
                (args.Length == 2 && args[0] == "--smoke" ? ".Smoke" : ""), out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PriceBar(args.Length == 2 && args[0] == "--smoke" ? args[1] : null));
            }
        }
    }

    internal sealed class Quote
    {
        public decimal Buy, Sell;
        // The endpoint has one change value, not separate buy/sell previous closes.
        // Live samples consistently give Buy - zDvlCur = a fixed daily reference.
        // This is inferred from observations; it is not a documented API contract.
        public decimal? DailyChange;
        public DateTime Time;
        public bool TradingDay, Quoting;
        public string Summary { get { return string.Format(CultureInfo.InvariantCulture,
            "招行黄金（元/克）\r\n客户买入：{0:F2}\r\n客户卖出：{1:F2}\r\n接口时间：{2:yyyy-MM-dd HH:mm:ss}（北京时间）\r\n交易日：{3}；报价开启：{4}", Buy, Sell, Time, TradingDay, Quoting) +
            (DailyChange.HasValue ? string.Format(CultureInfo.InvariantCulture,
                "\r\n接口涨跌：{0:+0.00;-0.00;0.00}\r\n昨收参考（按接口涨跌反推，待App核对）：{1:F2}\r\n入、出共用接口涨跌方向", DailyChange.Value, Buy - DailyChange.Value) :
                "\r\n昨收涨跌基准不可用：显示中性色"); } }
    }

    internal static class Quotes
    {
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        internal static async Task<Quote> Fetch()
        {
            using (var body = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("params", "[{\"prdType\":\"H\",\"prdCode\":\"\"}]") }))
            using (var response = await Client.PostAsync("https://mbmodule-openapi.paas.cmbchina.com/product/v1/func/market-center", body).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        internal static Quote Parse(string json)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (!root.ContainsKey("success") || !object.Equals(root["success"], true) || !object.Equals(root["code"], "SUC0000"))
                throw new InvalidDataException("招行接口未返回成功状态");
            var data = (Dictionary<string, object>)root["data"];
            var prices = (Dictionary<string, object>)data["FQAMBPRCZ1"];
            var q = new Quote {
                Buy = decimal.Parse(Convert.ToString(prices["zBuyPrc"]), CultureInfo.InvariantCulture),
                Sell = decimal.Parse(Convert.ToString(prices["zSelPrc"]), CultureInfo.InvariantCulture),
                Time = DateTime.ParseExact((string)data["NowTime"], "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
                TradingDay = (bool)data["MarketDay"], Quoting = (bool)data["QuotedPrice"]
            };
            if (q.Buy <= 0 || q.Sell <= 0) throw new InvalidDataException("接口价格无效");
            object change;
            decimal parsedChange;
            if (prices.TryGetValue("zDvlCur", out change) && change != null &&
                decimal.TryParse(Convert.ToString(change, CultureInfo.InvariantCulture), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out parsedChange) && q.Buy - parsedChange > 0)
                q.DailyChange = parsedChange;
            return q;
        }
    }

    internal static class PriceColors
    {
        internal static Color ForChange(decimal? change, bool stale)
        {
            if (stale) return Color.FromArgb(155, 160, 169);
            if (change > 0) return Color.FromArgb(255, 103, 112);
            if (change < 0) return Color.FromArgb(80, 208, 145);
            return Color.FromArgb(242, 207, 125);
        }
    }

    internal sealed class PriceBar : Form
    {
        readonly NotifyIcon tray;
        readonly ToolTip tooltip = new ToolTip();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 10000 };
        readonly System.Windows.Forms.Timer layout = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly Font priceFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        readonly string smokePath;
        Quote quote;
        string error = "正在读取招行报价";
        bool fetching, positioning, stopping;
        bool floating;
        Point dragOffset;
        bool dragging;
        DateTime received;

        public PriceBar(string smoke)
        {
            smokePath = smoke;
            Text = "Loy 招行金价";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(29, 32, 38);
            using (var g = CreateGraphics())
            {
                Width = (int)Math.Ceiling(g.MeasureString("入 0000.00", priceFont).Width) + 24;
                Height = (int)Math.Ceiling(priceFont.GetHeight(g)) * 2 + 8;
            }
            var menu = new ContextMenuStrip();
            menu.Items.Add("立即刷新", null, async delegate { await RefreshQuote(); });
            menu.Items.Add("自动停靠任务栏", null, delegate { floating = false; PositionBar(); });
            menu.Items.Add("悬浮（可拖动）", null, delegate { floating = true; Location = new Point(Screen.FromControl(this).WorkingArea.Right - Width - 12, Screen.FromControl(this).WorkingArea.Bottom - Height - 12); Show(); });
            var topmostItem = new ToolStripMenuItem("始终置顶") { CheckOnClick = true, Checked = TopMost };
            topmostItem.CheckedChanged += delegate
            {
                TopMost = topmostItem.Checked;
                Native.SetWindowPos(Handle, new IntPtr(TopMost ? -1 : -2), 0, 0, 0, 0, 0x13);
            };
            menu.Items.Add(topmostItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            ContextMenuStrip = menu;
            tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Loy 招行金价", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Show(); PositionBar(); } };
            refresh.Tick += async delegate { await RefreshQuote(); };
            layout.Tick += delegate { PositionBar(); UpdateStatus(); Invalidate(); };
            Shown += async delegate { PositionBar(); refresh.Start(); layout.Start(); await RefreshQuote(); };
            MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && floating) { dragging = true; dragOffset = e.Location; Capture = true; } };
            MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); };
            MouseUp += delegate { dragging = false; Capture = false; };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }

        bool Stale
        {
            get
            {
                if (quote == null || error != null || !quote.Quoting || !quote.TradingDay) return true;
                var chinaNow = DateTime.UtcNow.AddHours(8);
                return DateTime.UtcNow - received > TimeSpan.FromSeconds(45) ||
                    chinaNow - quote.Time > TimeSpan.FromMinutes(2) || quote.Time - chinaNow > TimeSpan.FromMinutes(2);
            }
        }

        async Task RefreshQuote()
        {
            if (fetching || stopping) return;
            fetching = true;
            try { quote = await Quotes.Fetch(); received = DateTime.UtcNow; error = null; }
            catch (Exception ex) { error = "读取失败：" + ex.Message; }
            finally { fetching = false; }
            if (stopping) return;
            UpdateStatus();
            Invalidate();
            if (smokePath != null)
            {
                using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(smokePath + ".png"); }
                File.WriteAllText(smokePath + ".txt", (quote == null ? error : quote.Summary) + "\r\n窗口：" + Bounds + "\r\n灰色：" + Stale + "\r\n错误：" + error);
                Environment.ExitCode = quote == null ? 1 : 0;
                Close();
            }
        }

        void UpdateStatus()
        {
            string text = quote == null ? error : quote.Summary;
            if (quote != null && Stale) text += "\r\n状态：暂停报价、数据过期或读取失败；保留上次价格";
            if (quote != null && error != null) text += "\r\n" + error;
            tooltip.SetToolTip(this, text + "\r\n每10秒刷新；右键设置。成交价以招行App为准。");
            tray.Text = quote == null ? "招行金价：读取失败或等待中" : string.Format("招行 买 {0:F2} / 卖 {1:F2}{2}", quote.Buy, quote.Sell, Stale ? "（旧）" : "");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var color = PriceColors.ForChange(quote == null ? null : quote.DailyChange, Stale);
            using (var brush = new SolidBrush(color))
            {
                string buy = quote == null ? "--.--" : quote.Buy.ToString("F2");
                string sell = quote == null ? "--.--" : quote.Sell.ToString("F2");
                e.Graphics.DrawString("入 " + buy, priceFont, brush, 8, 3);
                e.Graphics.DrawString("出 " + sell, priceFont, brush, 8, 3 + priceFont.GetHeight(e.Graphics));
            }
        }

        async void PositionBar()
        {
            if (positioning || floating || stopping) return;
            positioning = true;
            try
            {
                // UI Automation must run on a worker to avoid cross-process calls blocking the UI.
                int width = Width, height = Height;
                var position = await Task.Run(() => LoyGoldPrice.Dock.Find(width, height));
                if (!stopping && !floating)
                {
                    Location = position;
                    bool hide = Native.Fullscreen(Handle);
                    if (hide) Hide(); else { if (!Visible) Show(); Native.SetWindowPos(Handle, new IntPtr(TopMost ? -1 : -2), 0, 0, 0, 0, 0x13); }
                }
            }
            finally { positioning = false; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            stopping = true;
            refresh.Dispose(); layout.Dispose(); tooltip.Dispose();
            tray.Visible = false; tray.Dispose(); priceFont.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal static class Dock
    {
        internal static Point Find(int width, int height)
        {
            var tb = Native.FindWindow("Shell_TrayWnd", null);
            Native.Rect r;
            var screen = tb == IntPtr.Zero ? Screen.PrimaryScreen : Screen.FromHandle(tb);
            var work = screen.WorkingArea;
            var fallback = new Point(work.Right - width - 12, work.Bottom - height - 8);
            if (tb == IntPtr.Zero || !Native.GetWindowRect(tb, out r) || r.Right - r.Left < r.Bottom - r.Top) return fallback;
            var appbar = new Native.AppBarData(); appbar.Size = Marshal.SizeOf(appbar);
            if ((Native.SHAppBarMessage(4, ref appbar).ToUInt64() & 1) != 0) return fallback;
            try
            {
                var root = AutomationElement.FromHandle(tb);
                var start = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
                var widgets = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton"));
                if (start == null) return fallback;
                var startRect = start.Current.BoundingRectangle;
                int left = r.Left + 8;
                if (widgets != null)
                {
                    var w = widgets.Current.BoundingRectangle;
                    if (w.Width > 0 && w.Right <= startRect.Left) left = (int)w.Right + 8;
                }
                if (startRect.Left - left >= width + 8 && r.Bottom - r.Top >= height)
                    return new Point(left, r.Top + (r.Bottom - r.Top - height) / 2);
            }
            catch (Exception) { /* Explorer may restart or UIA may be unavailable: use the safe fallback. */ }
            return fallback;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct AppBarData { public int Size; public IntPtr Window; public uint Callback, Edge; public Rect Rect; public IntPtr Param; }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr h, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        internal static bool Fullscreen(IntPtr own)
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == own) return false;
            var name = new System.Text.StringBuilder(256); GetClassName(fg, name, name.Capacity);
            var cls = name.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls.Contains("TrayWnd") || cls == "Windows.UI.Core.CoreWindow" || cls == "XamlExplorerHostIslandWindow") return false;
            Rect r; if (!GetWindowRect(fg, out r)) return false;
            var bounds = Screen.FromHandle(fg).Bounds;
            return r.Left <= bounds.Left && r.Top <= bounds.Top && r.Right >= bounds.Right && r.Bottom >= bounds.Bottom;
        }
    }
}
