// 肥鱼阅读器（Windows 桌面版）
// 界面风格对齐 DeepSeek Harness：浅色/深色双主题、卡片、圆角、蓝色主按钮、克制的分隔线。
// 编译：csc /nologo /target:winexe /win32icon:icon.ico /out:肥鱼阅读器.exe ^
//         /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\TxtReader.cs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TxtReader
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogAndShow(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogAndShow(e.ExceptionObject as Exception);
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string initial = null;
                if (args != null && args.Length >= 3 && args[0] == "--demo-switch")
                    initial = "--demo-switch:" + args[1] + "|" + args[2];
                else if (args != null && args.Length >= 2 && args[0] == "--demo-search")
                    initial = "--demo-search:" + args[1] + (args.Length >= 3 ? "|" + args[2] : "");
                else if (args != null && args.Length > 0) initial = args[0];
                Application.Run(new ReaderForm(initial));
            }
            catch (Exception ex)
            {
                LogAndShow(ex);
            }
        }

        private static void LogAndShow(Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "肥鱼阅读器");
                Directory.CreateDirectory(dir);
                var text = DateTime.Now.ToString("s") + " " + (ex == null ? "null" : ex.GetType().FullName) + "\r\n" +
                           (ex == null ? "" : ex.Message) + "\r\n" + (ex == null ? "" : ex.StackTrace) + "\r\n\r\n";
                File.AppendAllText(Path.Combine(dir, "error.log"), text, new UTF8Encoding(true));
            }
            catch { }
            try { MessageBox.Show(ex == null ? "未知错误" : ex.Message, "肥鱼阅读器 出错了", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch { }
        }
    }

    /* ======================= 主题 ======================= */

    internal sealed class Theme
    {
        public string Name;
        public Color Bg;
        public Color Card;
        public Color Border;
        public Color Text;
        public Color Muted;
        public Color Accent;
        public Color AccentText;
        public Color Hover;
        public Color Selection;

        public static Theme Light()
        {
            return new Theme
            {
                Name = "浅色",
                Bg = Color.FromArgb(0xF6, 0xF7, 0xF9),
                Card = Color.White,
                Border = Color.FromArgb(0xE6, 0xE8, 0xEC),
                Text = Color.FromArgb(0x1B, 0x1F, 0x27),
                Muted = Color.FromArgb(0x6B, 0x72, 0x80),
                Accent = Color.FromArgb(0x4D, 0x6B, 0xFE),
                AccentText = Color.White,
                Hover = Color.FromArgb(0xEE, 0xF1, 0xF6),
                Selection = Color.FromArgb(0xE8, 0xEE, 0xFF)
            };
        }

        public static Theme Dark()
        {
            return new Theme
            {
                Name = "深色",
                Bg = Color.FromArgb(0x16, 0x18, 0x1C),
                Card = Color.FromArgb(0x1D, 0x21, 0x29),
                Border = Color.FromArgb(0x2B, 0x30, 0x38),
                Text = Color.FromArgb(0xE8, 0xEA, 0xED),
                Muted = Color.FromArgb(0x9B, 0xA1, 0xAB),
                Accent = Color.FromArgb(0x6B, 0x86, 0xFF),
                AccentText = Color.White,
                Hover = Color.FromArgb(0x25, 0x2A, 0x33),
                Selection = Color.FromArgb(0x25, 0x32, 0x5A)
            };
        }
    }

    /* ======================= 通用控件 ======================= */

    internal static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (radius <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>
    /// 卡片容器：外层 1px 描边色，内层卡片色。内容是加在 <see cref="Inner"/> 上的。
    /// 不要在这个类里重写 OnPaint 或启用 AllPaintingInWmPaint，也不要设 Region——
    /// 实测这几种做法都会让子控件画到错误位置（标题被列表盖住、列表整体上移）。
    /// </summary>
    internal class CardPanel : Panel
    {
        public Theme Theme;
        public Panel Inner;

        public CardPanel()
        {
            DoubleBuffered = true;
            Padding = new Padding(1);
            BackColor = Color.White;
            Inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            Controls.Add(Inner);
        }

        public void SetTheme(Theme t)
        {
            Theme = t;
            BackColor = t.Border;
            Inner.BackColor = t.Card;
        }
    }

    /// <summary>克制的扁平按钮：主按钮实心蓝，次按钮描边幽灵。</summary>
    internal class FlatButton : Button
    {
        public Theme Theme;
        public bool Primary;
        public int Radius = 8;
        private float _scale = 1f;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { _scale = DeviceDpi / 96f; } catch { _scale = 1f; }
            Radius = Math.Max(2, (int)Math.Round(8 * _scale));
        }
        private bool _hover;
        private bool _down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            AutoSize = false;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Theme == null) { base.OnPaint(e); return; }
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 先用背景色涂满整个客户区：否则基类画过的边框会残留在圆角外，形成"角上黑/蓝小方块"
            Color bg = Theme != null ? Theme.Bg : (Parent != null ? Parent.BackColor : SystemColors.Control);
            using (var bgBrush = new SolidBrush(bg)) g.FillRectangle(bgBrush, ClientRectangle);

            var r = new Rectangle(0, 0, Width, Height);

            Color fill, text, border;
            if (Primary)
            {
                fill = _down ? ControlPaint.Dark(Theme.Accent, 0.08f) : (_hover ? ControlPaint.Light(Theme.Accent, 0.08f) : Theme.Accent);
                text = Theme.AccentText;
                border = fill;
            }
            else
            {
                fill = _down ? Theme.Border : (_hover ? Theme.Hover : Theme.Card);
                text = Theme.Text;
                border = _hover ? Theme.Muted : Theme.Border;
            }

            using (var path = Draw.Round(r, Radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>圆角高亮的目录列表。</summary>
    internal class TocList : ListBox
    {
        public Theme Theme;
        public int ItemHeightCustom = 34;

        public TocList()
        {
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            ItemHeight = 34;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            // 双缓冲 + 不擦背景：消除悬停重绘时的闪烁
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (Theme == null || e.Index < 0 || e.Index >= Items.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 自己铺背景：上面屏蔽了 WM_ERASEBKGND，不铺就会残留上一次的内容
            using (var bg = new SolidBrush(Theme.Card)) g.FillRectangle(bg, e.Bounds);

            var row = new Rectangle(6, e.Bounds.Y + 2, Width - 14, ItemHeight - 4);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (selected)
            {
                using (var path = Draw.Round(row, 8))
                using (var b = new SolidBrush(Theme.Selection)) g.FillPath(b, path);
            }
            else if (e.Index == _hot)
            {
                using (var path = Draw.Round(row, 8))
                using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, path);
            }

            var textRect = new Rectangle(row.X + 10, row.Y, row.Width - 20, row.Height);
            TextRenderer.DrawText(g, Items[e.Index].ToString(), Font, textRect,
                selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexFromPoint(e.Location);
            if (idx != _hot)
            {
                InvalidateItem(_hot);
                _hot = idx;
                InvalidateItem(_hot);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            InvalidateItem(_hot);
            _hot = -1;
            base.OnMouseLeave(e);
        }


        /// <summary>批量替换条目：一次重绘，避免逐条添加引起的闪烁。</summary>
        public void SetItems(System.Collections.Generic.IEnumerable<string> items)
        {
            BeginUpdate();
            try
            {
                Items.Clear();
                if (items != null)
                    foreach (var s in items) Items.Add(s);
            }
            finally
            {
                EndUpdate();
            }
        }

        /// <summary>只重绘单个条目，避免整块刷新导致闪烁。</summary>
        private void InvalidateItem(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            var rect = GetItemRectangle(index);
            if (rect.IsEmpty) return;
            Invalidate(rect);
        }

        private int _hot = -1;
    }

    /* ======================= 主窗体 ======================= */

    internal sealed class ReaderForm : Form
    {
        private static readonly Regex ChapterRe = new Regex(
            @"^\s*(第[零一二三四五六七八九十百千0-9０-９]{1,12}[章节回卷部篇集]|序章|楔子|引子|尾声|后记|番外|Chapter\s+[0-9IVXLC]+|CHAPTER\s+[0-9IVXLC]+)",
            RegexOptions.Compiled);

        private Theme _theme = Theme.Light();

        // 顶部
        private readonly Panel _header = new Panel();
        private readonly FlowLayoutPanel _actions = new FlowLayoutPanel();
        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly Panel _accentBar = new Panel();
        private readonly Panel _progressTrack = new Panel();
        private readonly Panel _progressFill = new Panel();

        // 操作区
        private readonly FlatButton _openBtn = new FlatButton();
        private readonly FlatButton _tocBtn = new FlatButton();
        private readonly FlatButton _minusBtn = new FlatButton();
        private readonly FlatButton _plusBtn = new FlatButton();
        private readonly FlatButton _themeBtn = new FlatButton();
        private readonly ComboBox _encBox = new ComboBox();

        // 正文区
        private readonly TableLayoutPanel _body = new TableLayoutPanel();
        private readonly CardPanel _tocCard = new CardPanel();
        private readonly Label _tocHead = new Label();
        private readonly TocList _toc = new TocList();
        private readonly CardPanel _textCard = new CardPanel();
        private readonly BufferedRichTextBox _text = new BufferedRichTextBox();
        private readonly Panel _textHost = new Panel();
        private readonly Panel _imageHost = new Panel();
        private readonly ImageCanvas _canvas = new ImageCanvas();
        private readonly FlatButton _fitBtn = new FlatButton();
        private readonly FlatButton _actualBtn = new FlatButton();
        private readonly FlatButton _prevBtn = new FlatButton();
        private readonly FlatButton _nextBtn = new FlatButton();
        private string[] _imageFiles = new string[0];
        private int _imageIndex = -1;
        private bool _imageMode;

        private readonly Panel _searchBar = new Panel();
        private readonly TextBox _searchBox = new TextBox();
        private readonly FlatButton _searchBtn = new FlatButton();
        private readonly FlatButton _browserBtn = new FlatButton();
        private readonly Label _searchHint = new Label();
        private readonly ContextMenuStrip _textMenu = new ContextMenuStrip();
        private readonly FlatButton _backBtn = new FlatButton();
        private readonly FlatButton _recentBtn = new FlatButton();
        private List<HistoryEntry> _history = new List<HistoryEntry>();
        private List<SearchHit> _hits = new List<SearchHit>();
        private ListMode _listMode = ListMode.Toc;
        private string _proxy = "";
        private string _serperKey = "";
        private bool _searching;
        private long _pendingPosition = -1;
        private bool _hintPlaceholder = true;

        private enum ListMode { Toc, Images, Search, History }

        // 底栏
        private readonly Panel _footer = new Panel();
        private readonly Label _info = new Label();
        private readonly Label _progress = new Label();

        private readonly List<Chapter> _chapters = new List<Chapter>();
        private readonly string _initialPath;
        private string _filePath = "";
        private long _fileSize;
        private string _encodingUsed = "";
        private int _fontSize = 18;
        private bool _dark;
        private bool _syncing;
        private int _lastFirstChar;

        private static readonly string AppDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "肥鱼阅读器");
        private static readonly string ConfigPath = Path.Combine(AppDir, "config.ini");

        private sealed class Chapter
        {
            public string Title;
            public int Offset;
        }

        private const int WS_EX_COMPOSITED = 0x02000000;

        // 整窗合成：RichTextBox 这类系统控件不吃 SetStyle 双缓冲，靠这个消除滚动残影
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_COMPOSITED;
                return cp;
            }
        }
        public ReaderForm(string initialPath)
        {
            _initialPath = initialPath;
            Text = "肥鱼阅读器";
            // 让标题栏和任务栏使用 exe 内嵌的图标（WinForms 默认用系统通用图标）
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Width = 1080;
            Height = 780;
            MinimumSize = new Size(680, 480);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;

            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;

            BuildHeader();
            BuildBody();
            BuildFooter();
            BuildSearchBar();

            // 用表格式根容器固定行顺序，不再依赖 Dock 叠加顺序
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
            root.ColumnStyles.Clear();
            root.RowStyles.Clear();
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 3f));    // 进度条
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));   // 标题栏
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));   // 搜索条
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 正文
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));   // 页脚

            Controls.Clear();
            Controls.Add(root);
            root.Controls.Add(_progressTrack, 0, 0);
            root.Controls.Add(_header, 0, 1);
            root.Controls.Add(_searchBar, 0, 2);
            root.Controls.Add(_body, 0, 3);
            root.Controls.Add(_footer, 0, 4);

            _progressTrack.Dock = DockStyle.Fill;
            _header.Dock = DockStyle.Fill;
            _searchBar.Dock = DockStyle.Fill;
            _body.Dock = DockStyle.Fill;
            _footer.Dock = DockStyle.Fill;

            DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (s, e) =>
            {
                var files = e.Data != null ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
                if (files != null && files.Length > 0) OpenFile(files[0]);
            };
            KeyDown += OnKeyDown;
            Load += (s, e) =>
            {
                LoadConfig();
                ApplyTheme();
                _history = HistoryStore.Load();
                if (!string.IsNullOrEmpty(_initialPath) && File.Exists(_initialPath)) OpenFile(_initialPath);
                else if (_history.Count > 0) ShowHistory();

                // 调试/截图用：--demo-search "关键词" 启动即搜索
                // 调试用：--demo-switch <文本> <图片>，先开文本再切图片，用于复现模式切换闪烁
                if (_initialPath != null && _initialPath.StartsWith("--demo-switch:"))
                {
                    string[] two = _initialPath.Substring("--demo-switch:".Length).Split('|');
                    if (two.Length > 0 && File.Exists(two[0])) OpenFile(two[0]);
                    if (two.Length > 1)
                    {
                        var t = new Timer { Interval = 1200 };
                        t.Tick += (s2, e2) =>
                        {
                            t.Stop();
                            if (File.Exists(two[1])) OpenFile(two[1]);
                        };
                        t.Start();
                    }
                }

                if (_initialPath != null && _initialPath.StartsWith("--demo-search:"))
                {
                    string spec = _initialPath.Substring("--demo-search:".Length);
                    string[] parts = spec.Split('|');
                    if (parts.Length > 1 && File.Exists(parts[1])) OpenFile(parts[1]);
                    SetPlaceholder(false);
                    _searchBox.Text = parts[0];
                    StartSearch();
                }
            };
            FormClosing += (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
                    {
                        string kind = _imageMode ? "image" : "text";
                        HistoryStore.Touch(_history, _filePath, _fileSize, kind, _imageMode ? 0 : _lastFirstChar);
                        HistoryStore.Save(_history);
                    }
                }
                catch { }
                SaveConfig();
            };

            ShowEmptyState();
        }

        /* ---------------- 顶部 ---------------- */

        private void BuildHeader()
        {
            _header.Dock = DockStyle.Top;
            _header.Height = 84;
            _header.Padding = new Padding(24, 16, 24, 12);

            _title.AutoSize = true;
            _title.Text = "肥鱼阅读器";
            _title.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold, GraphicsUnit.Point);
            _title.Location = new Point(0, 0);

            _subtitle.AutoSize = true;
            _subtitle.Text = "本地 TXT 阅读 · 拖入文件即可";
            _subtitle.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _subtitle.Location = new Point(2, 32);

            _openBtn.Text = "打开文件";
            _openBtn.Primary = true;
            _openBtn.Size = new Size(96, 34);
            _openBtn.Click += (s, e) => ChooseFile();

            _tocBtn.Text = "目录";
            _tocBtn.Size = new Size(62, 34);
            _tocBtn.Click += (s, e) =>
            {
                if (_listMode == ListMode.Search || _listMode == ListMode.History) { RestoreListMode(); return; }
                ToggleToc();
            };

            _minusBtn.Text = "A-";
            _minusBtn.Size = new Size(44, 34);
            _minusBtn.Click += (s, e) => SetFontSize(_fontSize - 1);

            _plusBtn.Text = "A+";
            _plusBtn.Size = new Size(44, 34);
            _plusBtn.Click += (s, e) => SetFontSize(_fontSize + 1);

            _themeBtn.Text = "深色";
            _themeBtn.Size = new Size(62, 34);
            _themeBtn.Click += (s, e) => ToggleTheme();

            _fitBtn.Text = "适应";
            _fitBtn.Size = new Size(56, 34);
            _fitBtn.Click += (s, e) => { _canvas.FitToWindow(); UpdateImageInfo(); };

            _actualBtn.Text = "1:1";
            _actualBtn.Size = new Size(50, 34);
            _actualBtn.Click += (s, e) => { _canvas.ActualSize(); UpdateImageInfo(); };

            _prevBtn.Text = "‹ 上一张";
            _prevBtn.Size = new Size(76, 34);
            _prevBtn.Click += (s, e) => StepImage(-1);

            _nextBtn.Text = "下一张 ›";
            _nextBtn.Size = new Size(76, 34);
            _nextBtn.Click += (s, e) => StepImage(1);

            _encBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _encBox.FlatStyle = FlatStyle.Flat;
            _encBox.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _encBox.Items.AddRange(new object[] { "自动识别", "UTF-8", "GBK", "Big5", "UTF-16" });
            _encBox.SelectedIndex = 0;
            _encBox.Size = new Size(110, 30);
            _encBox.SelectedIndexChanged += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_filePath)) _info.Text = Path.GetFileName(_filePath) + "　重新打开文件以应用编码";
            };

            // 用 TableLayoutPanel 分两列：标题占满剩余，操作区按内容宽度靠右。
            // 不用 Dock=Right + AutoSize，那组合在某些启动路径下会把面板拉成整行宽。
            _actions.Dock = DockStyle.Fill;
            _actions.AutoSize = true;
            _actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _actions.WrapContents = false;
            _actions.FlowDirection = FlowDirection.LeftToRight;
            _actions.Padding = new Padding(0, 9, 0, 0);
            _actions.Margin = new Padding(0);
            _openBtn.Margin = new Padding(0, 0, 8, 0);
            _themeBtn.Margin = new Padding(8, 0, 8, 0);
            _tocBtn.Margin = new Padding(0, 0, 8, 0);
            _minusBtn.Margin = new Padding(0, 0, 8, 0);
            _plusBtn.Margin = new Padding(0, 0, 8, 0);
            _encBox.Margin = new Padding(0, 2, 0, 0);
            _fitBtn.Margin = new Padding(0, 0, 8, 0);
            _actualBtn.Margin = new Padding(0, 0, 8, 0);
            _prevBtn.Margin = new Padding(0, 0, 8, 0);
            _nextBtn.Margin = new Padding(0, 0, 0, 0);

            _actions.Controls.Add(_openBtn);
            _actions.Controls.Add(_themeBtn);
            _actions.Controls.Add(_tocBtn);
            _actions.Controls.Add(_minusBtn);
            _actions.Controls.Add(_plusBtn);
            _actions.Controls.Add(_encBox);
            _actions.Controls.Add(_fitBtn);
            _actions.Controls.Add(_actualBtn);
            _actions.Controls.Add(_prevBtn);
            _actions.Controls.Add(_nextBtn);

            var titleBox = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            titleBox.Controls.Add(_title);
            titleBox.Controls.Add(_subtitle);

            var headerGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            headerGrid.ColumnStyles.Clear();
            headerGrid.RowStyles.Clear();
            headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            headerGrid.Controls.Add(titleBox, 0, 0);
            headerGrid.Controls.Add(_actions, 1, 0);

            _header.Controls.Add(headerGrid);

            _progressTrack.Dock = DockStyle.Top;
            _progressTrack.Height = 3;
            _progressFill.Dock = DockStyle.Left;
            _progressFill.Width = 0;
            _progressTrack.Controls.Add(_progressFill);

            Controls.Add(_progressTrack);
            Controls.Add(_header);
            LayoutHeaderControls();
            _header.Resize += (s, e) => LayoutHeaderControls();
            _progressTrack.Resize += (s, e) => UpdateProgress();
        }

        private void LayoutHeaderControls()
        {
            // 操作区由 FlowLayoutPanel 自动排布，这里只保证标题不被挤压
            if (_actions.Visible && _subtitle.Right > _actions.Left - 12)
                _subtitle.MaximumSize = new Size(Math.Max(80, _actions.Left - _subtitle.Left - 16), 0);
            else
                _subtitle.MaximumSize = Size.Empty;
        }

        /* ---------------- 主体 ---------------- */

        private void BuildBody()
        {
            _body.Dock = DockStyle.Fill;
            _body.Padding = new Padding(20, 4, 20, 8);
            _body.ColumnCount = 2;
            _body.RowCount = 1;
            _body.ColumnStyles.Clear();
            _body.RowStyles.Clear();
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 288f));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // 目录卡片
            _tocCard.Dock = DockStyle.Fill;
            _tocCard.Margin = new Padding(0, 0, 12, 0);
            _tocCard.SetTheme(_theme);

            _tocHead.Dock = DockStyle.Top;
            _tocHead.Height = 44;
            _tocHead.Padding = new Padding(16, 0, 16, 0);
            _tocHead.TextAlign = ContentAlignment.MiddleLeft;
            _tocHead.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            _tocHead.Text = "目录";

            _toc.Dock = DockStyle.Fill;
            _toc.Theme = _theme;
            _toc.DoubleClick += (s, e) => JumpToSelected();
            _toc.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { JumpToSelected(); e.Handled = true; } };

            // 标题用绝对定位，列表占剩余空间：嵌套 TableLayoutPanel 在圆角 Region 容器里会错位
            _tocHead.Dock = DockStyle.None;
            _tocHead.AutoSize = false;
            _tocHead.Location = new Point(18, 12);
            _tocHead.Size = new Size(240, 24);
            _tocHead.TextAlign = ContentAlignment.MiddleLeft;

            var tocHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 44, 8, 10) };
            tocHost.Controls.Add(_toc);

            _backBtn.Text = "← 返回目录";
            _backBtn.Size = new Size(96, 26);
            _backBtn.Visible = false;
            _backBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _backBtn.Click += (s, e) => RestoreListMode();
            _tocCard.Inner.Controls.Add(_backBtn);
            _tocCard.Inner.Resize += (s, e) => PositionBackButton();
            PositionBackButton();

            _tocCard.Inner.Controls.Add(tocHost);
            _tocCard.Inner.Controls.Add(_tocHead);
            _tocHead.BringToFront();
            _recentBtn.Text = "最近阅读";
            _recentBtn.Size = new Size(84, 26);
            _recentBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _recentBtn.Click += (s, e) => ShowHistory();
            _tocCard.Inner.Controls.Add(_recentBtn);
            _recentBtn.BringToFront();

            _backBtn.BringToFront();   // 必须在填充容器之后提到最前，否则会被盖住

            // 正文卡片
            _textCard.Dock = DockStyle.Fill;
            _textCard.Margin = new Padding(0);
            _textCard.SetTheme(_theme);

            _text.Dock = DockStyle.Fill;
            _text.BorderStyle = BorderStyle.None;
            _text.ReadOnly = true;
            _text.WordWrap = true;
            _text.ScrollBars = RichTextBoxScrollBars.Vertical;
            _text.DetectUrls = false;
            _text.Font = BodyFont();
            _text.ScrollChanged += (s, e) => { if (!_syncing) UpdateProgress(); };

            _textHost.Dock = DockStyle.Fill;
            _textHost.Padding = new Padding(30, 22, 16, 12);
            _textHost.Controls.Add(_text);

            _canvas.Dock = DockStyle.Fill;
            _canvas.Visible = false;
            _imageHost.Dock = DockStyle.Fill;
            _imageHost.Padding = new Padding(1);
            _imageHost.Visible = false;
            _imageHost.Controls.Add(_canvas);

            _fitBtn.Visible = false;
            _actualBtn.Visible = false;
            _prevBtn.Visible = false;
            _nextBtn.Visible = false;

            _textCard.Inner.Controls.Add(_textHost);
            _textCard.Inner.Controls.Add(_imageHost);

            _body.Controls.Add(_tocCard, 0, 0);
            _body.Controls.Add(_textCard, 1, 0);
            Controls.Add(_body);
        }

        private Font BodyFont()
        {
            return new Font("Microsoft YaHei UI", _fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void BuildSearchBar()
        {
            _searchBar.Dock = DockStyle.Top;
            _searchBar.Height = 54;
            _searchBar.Padding = new Padding(20, 9, 20, 9);

            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            _searchBox.Dock = DockStyle.Fill;
            SetPlaceholder(true);

            _searchBox.GotFocus += (s, e) => { if (_hintPlaceholder) SetPlaceholder(false); };
            _searchBox.LostFocus += (s, e) => { if (_searchBox.Text.Trim().Length == 0) SetPlaceholder(true); };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartSearch(); }
                else if (e.KeyCode == Keys.Escape) { _searchBox.Clear(); SetPlaceholder(true); }
            };

            _searchBtn.Text = "搜索";
            _searchBtn.Primary = true;
            _searchBtn.Size = new Size(72, 32);
            _searchBtn.Margin = new Padding(10, 0, 8, 0);
            _searchBtn.Click += (s, e) => StartSearch();

            _browserBtn.Text = "浏览器";
            _browserBtn.Size = new Size(72, 32);
            _browserBtn.Margin = new Padding(0, 0, 10, 0);
            _browserBtn.Click += (s, e) => OpenUrl(WebSearch.BrowserUrl(_searchBox.Text));

            _searchHint.AutoSize = false;
            _searchHint.Size = new Size(190, 32);
            _searchHint.TextAlign = ContentAlignment.MiddleLeft;
            _searchHint.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _searchHint.Text = "联网搜索 · 百度百科";
            _searchHint.Margin = new Padding(0);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var right = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
            right.Controls.Add(_searchHint);
            right.Controls.Add(_searchBtn);
            right.Controls.Add(_browserBtn);

            grid.Controls.Add(_searchBox, 0, 0);
            grid.Controls.Add(right, 1, 0);
            _searchBar.Controls.Add(grid);

            // 正文右键菜单
            _textMenu.Items.Add("用所选内容联网搜索", null, (s, e) => SearchSelection());
            _textMenu.Items.Add("在浏览器里搜索所选内容", null, (s, e) => OpenUrl(WebSearch.BrowserUrl(SelectedQuery())));
            _textMenu.Items.Add(new ToolStripSeparator());
            _textMenu.Items.Add("复制", null, (s, e) => { if (_text.SelectedText.Length > 0) Clipboard.SetText(_text.SelectedText); });
            _text.ContextMenuStrip = _textMenu;
        }

        private void SetPlaceholder(bool on)
        {
            _hintPlaceholder = on;
            if (on)
            {
                _searchBox.ForeColor = _theme != null ? _theme.Muted : Color.Gray;
                _searchBox.Text = "输入关键词，回车联网搜索…";
            }
            else
            {
                _searchBox.ForeColor = _theme != null ? _theme.Text : Color.Black;
                if (_searchBox.Text.StartsWith("输入关键词")) _searchBox.Text = "";
            }
        }

        private string SelectedQuery()
        {
            string sel = _text.SelectedText;
            if (string.IsNullOrEmpty(sel)) return _searchBox.Text.Trim();
            sel = System.Text.RegularExpressions.Regex.Replace(sel, @"\s+", " ").Trim();
            if (sel.Length > 200) sel = sel.Substring(0, 200);
            return sel;
        }

        private void SearchSelection()
        {
            string q = SelectedQuery();
            if (string.IsNullOrEmpty(q)) return;
            SetPlaceholder(false);
            _searchBox.Text = q;
            StartSearch();
        }

        private void StartSearch()
        {
            string q = _searchBox.Text.Trim();
            if (q.Length == 0 || _hintPlaceholder || _searching) return;

            _searching = true;
            _searchBtn.Enabled = false;
            _searchHint.Text = "搜索中…";

            string proxy = _proxy, key = _serperKey;
            var worker = new System.Threading.Thread(new System.Threading.ThreadStart(() =>
            {
                SearchResult r;
                try { r = WebSearch.Query(q, proxy, key); }
                catch (Exception ex) { r = new SearchResult { Error = ex.Message }; }
                try { BeginInvoke((MethodInvoker)(() => ShowSearchResult(q, r))); } catch { }
            }));
            worker.IsBackground = true;
            worker.Start();
        }

        private void ShowSearchResult(string query, SearchResult r)
        {
            _searching = false;
            _searchBtn.Enabled = true;

            _hits = r.Hits != null ? r.Hits : new List<SearchHit>();
            _listMode = ListMode.Search;

            var searchRows = new List<string>();
            foreach (var h in _hits)
            {
                string text = string.IsNullOrEmpty(h.Title) ? h.Url : h.Title;
                if (text.Length > 80) text = text.Substring(0, 80) + "…";
                searchRows.Add(text);
            }
            if (searchRows.Count == 0) searchRows.Add("（没有结果，可点「浏览器」）");
            _toc.SetItems(searchRows);

            _tocHead.Text = "结果 " + _hits.Count + " 条";
            _toc.Invalidate();
            PositionBackButton();
            _backBtn.Visible = true;
            _searchHint.Text = string.IsNullOrEmpty(r.Error) ? ("找到 " + _hits.Count + " 条") : r.Error;
            if (!string.IsNullOrEmpty(r.Answer)) _info.Text = "摘要：" + (r.Answer.Length > 140 ? r.Answer.Substring(0, 140) + "…" : r.Answer);
            else _info.Text = "搜索：" + query + "　" + _hits.Count + " 条结果";
            _toc.SelectedIndex = _hits.Count > 0 ? 0 : -1;
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { MessageBox.Show(this, "打开链接失败：" + ex.Message, "肥鱼阅读器", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void BuildFooter()
        {
            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 40;
            _footer.Padding = new Padding(24, 0, 24, 0);

            _info.AutoSize = false;
            _info.Dock = DockStyle.Fill;
            _info.TextAlign = ContentAlignment.MiddleLeft;
            _info.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _info.Text = "还没有打开文件";

            _progress.AutoSize = false;
            _progress.Dock = DockStyle.Right;
            _progress.Width = 96;
            _progress.TextAlign = ContentAlignment.MiddleRight;
            _progress.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _progress.Text = "0%";

            _footer.Controls.Add(_info);
            _footer.Controls.Add(_progress);
            Controls.Add(_footer);
        }

        /* ---------------- 主题 ---------------- */

        private void ToggleTheme()
        {
            _dark = !_dark;
            _theme = _dark ? Theme.Dark() : Theme.Light();
            ApplyTheme();
            try { File.WriteAllText(ConfigPath + ".theme.tmp", _dark ? "dark" : "light"); } catch { }
        }

        private void ApplyTheme()
        {
            SuspendLayout();
            BackColor = _theme.Bg;
            ForeColor = _theme.Text;

            _header.BackColor = _theme.Bg;
            _title.ForeColor = _theme.Text;
            _subtitle.ForeColor = _theme.Muted;

            foreach (var b in new[] { _openBtn, _tocBtn, _minusBtn, _plusBtn, _themeBtn })
            {
                b.Theme = _theme;
                b.BackColor = _theme.Card;
                b.Invalidate();
            }
            _themeBtn.Text = _dark ? "浅色" : "深色";

            _encBox.BackColor = _theme.Card;
            _encBox.ForeColor = _theme.Text;

            _progressTrack.BackColor = _theme.Border;
            _progressFill.BackColor = _theme.Accent;

            _body.BackColor = _theme.Bg;
            _tocCard.SetTheme(_theme);
            _textCard.SetTheme(_theme);

            _tocHead.ForeColor = _theme.Text;
            _toc.BackColor = _theme.Card;
            _toc.ForeColor = _theme.Text;
            _toc.Theme = _theme;
            _toc.Invalidate();

            _text.BackColor = _theme.Card;
            _text.ForeColor = _theme.Text;

            _footer.BackColor = _theme.Bg;
            _info.ForeColor = _theme.Muted;
            _progress.ForeColor = _theme.Muted;

            _tocCard.Inner.BackColor = _theme.Card;
            _textCard.Inner.BackColor = _theme.Card;
            _imageHost.BackColor = _theme.Border;
            _canvas.SetBackColor(_theme.Card);

            _searchBar.BackColor = _theme.Bg;
            _searchBox.BackColor = _theme.Card;
            _searchBox.ForeColor = _hintPlaceholder ? _theme.Muted : _theme.Text;
            _searchHint.ForeColor = _theme.Muted;
            _recentBtn.Theme = _theme;
            _recentBtn.BackColor = _theme.Card;
            _recentBtn.Invalidate();
            _backBtn.Theme = _theme;
            _backBtn.BackColor = _theme.Card;
            _backBtn.Invalidate();
            _searchBtn.Theme = _theme;
            _browserBtn.Theme = _theme;
            _searchBtn.BackColor = _theme.Card;
            _browserBtn.BackColor = _theme.Card;
            _searchBtn.Invalidate();
            _browserBtn.Invalidate();
            ResumeLayout(true);
            Invalidate(true);
        }

        /* ---------------- 内容 ---------------- */

        private void ShowEmptyState()
        {
            _syncing = true;
            _text.Text = "把 .txt 文件拖进来，或点右上角「打开文件」。\r\n\r\n快捷键：←/→ 或 PageUp/PageDown 翻屏，+/- 调字号，T 切换主题，C 开关目录。";
            _text.SelectionStart = 0;
            _text.SelectionLength = 0;
            _text.ScrollToCaret();
            _syncing = false;
            _toc.Items.Clear();
            _tocHead.Text = "目录";
            _info.Text = "还没有打开文件";
            _progress.Text = "0%";
            _progressFill.Width = 0;
        }

        private const string CombinedFilter =
            "所有支持的格式|*.txt;*.md;*.log;*.rtf;*.docx;*.odt;*.epub;*.html;*.htm;*.csv;*.json;*.xml;*.yml;*.yaml;*.ini;*.conf;*.srt;*.ass;*.ssa;*.vtt;*.lrc;*.py;*.js;*.ts;*.cs;*.cpp;*.java;*.go;*.rs;*.sql;*.sh;*.ps1;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
            "|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.ico;*.wmf;*.emf" +
            "|文本与代码|*.txt;*.md;*.log;*.csv;*.tsv;*.json;*.xml;*.yml;*.yaml;*.ini;*.conf;*.srt;*.ass;*.vtt;*.lrc;*.py;*.js;*.ts;*.cs;*.cpp;*.java;*.go;*.rs;*.sql;*.sh;*.ps1" +
            "|网页|*.html;*.htm;*.xhtml" +
            "|Word / 电子书|*.docx;*.odt;*.epub;*.rtf" +
            "|所有文件 (*.*)|*.*";

        private void ChooseFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = CombinedFilter;
                dlg.Title = "打开文件";
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
            }
        }

        private string EncodingLabel()
        {
            switch (_encBox.SelectedIndex)
            {
                case 1: return "utf-8";
                case 2: return "gbk";
                case 3: return "big5";
                case 4: return "utf-16le";
                default: return "auto";
            }
        }

        private void OpenFile(string path)
        {
            if (ImageLoader.IsImage(path))
            {
                OpenImage(path);
                return;
            }

            try
            {
                TextExtract.Result result = TextExtract.FromFile(path, EncodingLabel());

                _filePath = path;
                _fileSize = new FileInfo(path).Length;
                _encodingUsed = result.Encoding;

                SuspendLayout();
                _tocCard.Inner.SuspendLayout();
                _imageMode = false;
                _imageHost.Visible = false;
                _textHost.Visible = true;
                _backBtn.Visible = false;
                UpdateModeUi();
                _tocCard.Inner.ResumeLayout(false);
                ResumeLayout(true);

                _syncing = true;
                _text.Text = result.Text;
                BuildChapters(result.Text);
                _syncing = false;

                Text = Path.GetFileName(path) + " - 肥鱼阅读器";
                _info.Text = Path.GetFileName(path) + "　" + result.Format + "　" + result.Encoding + "　"
                           + (_fileSize / 1024.0).ToString("0.0") + " KB　" + _chapters.Count + " 章";
                RestorePosition();
                UpdateProgress();
                _recentBtn.Visible = true;
                HistoryStore.Touch(_history, path, _fileSize, "text", _lastFirstChar);
                HistoryStore.Save(_history);

            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开失败：" + ex.Message, "肥鱼阅读器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /* ---------------- 图片 ---------------- */

        private void OpenImage(string path)
        {
            try
            {
                Bitmap bmp = ImageLoader.Load(path);
                _canvas.Image = bmp;            // 画布接管所有权
                SuspendLayout();
                _tocCard.Inner.SuspendLayout();
                _imageMode = true;
                _backBtn.Visible = false;
                _canvas.Visible = true;
                _imageHost.Visible = true;
                _textHost.Visible = false;
                _tocCard.Inner.ResumeLayout(false);
                ResumeLayout(false);

                _filePath = path;
                _fileSize = new FileInfo(path).Length;
                _encodingUsed = "";

                BuildImageList(path);
                UpdateModeUi();
                Text = Path.GetFileName(path) + " - 肥鱼阅读器";
                UpdateImageInfo();
                _recentBtn.Visible = true;
                HistoryStore.Touch(_history, path, _fileSize, "image", 0);
                HistoryStore.Save(_history);
                _progress.Text = "100%";
                _progressFill.Width = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开图片失败：" + ex.Message, "肥鱼阅读器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BuildImageList(string path)
        {
            _imageFiles = new string[0];
            _imageIndex = -1;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) dir = ".";
                var list = new List<string>();
                foreach (var f in Directory.GetFiles(dir))
                    if (ImageLoader.IsImage(f)) list.Add(f);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                _imageFiles = list.ToArray();

                for (int i = 0; i < _imageFiles.Length; i++)
                    if (string.Equals(_imageFiles[i], path, StringComparison.OrdinalIgnoreCase)) { _imageIndex = i; break; }

                _listMode = ListMode.Images;
                var imageNames = new List<string>();
                for (int i = 0; i < _imageFiles.Length; i++) imageNames.Add(Path.GetFileName(_imageFiles[i]));
                _toc.SetItems(imageNames);
                _tocHead.Text = "同目录图片 · " + _imageFiles.Length + " 张";
                if (_imageIndex >= 0)
                {
                    _toc.SelectedIndex = _imageIndex;
                    _toc.TopIndex = Math.Max(0, _imageIndex - 3);
                }
            }
            catch { }
        }

        private void StepImage(int delta)
        {
            if (_imageFiles.Length == 0) return;
            int next = _imageIndex + delta;
            if (next < 0) next = _imageFiles.Length - 1;
            if (next >= _imageFiles.Length) next = 0;
            OpenImage(_imageFiles[next]);
        }

        private void OpenHistoryEntry(HistoryEntry entry)
        {
            if (entry == null || !File.Exists(entry.Path)) return;
            _pendingPosition = entry.Position;
            OpenFile(entry.Path);
            _pendingPosition = -1;
        }

        private void UpdateImageInfo()
        {
            if (_canvas.Image == null) return;
            int zoom = (int)Math.Round(_canvas.Zoom * 100);
            string pos = (_imageIndex >= 0 && _imageFiles.Length > 0) ? (_imageIndex + 1) + "/" + _imageFiles.Length : "-";
            _info.Text = Path.GetFileName(_filePath) + "　图片　" + _canvas.Image.Width + "×" + _canvas.Image.Height
                       + "　" + pos + "　" + (_fileSize / 1024.0).ToString("0.0") + " KB　显示 " + zoom + "%";
        }

        private void PositionBackButton()
        {
            if (_tocCard.Inner == null) return;
            int right = Math.Max(8, _tocCard.Inner.Width - 12);
            _backBtn.Location = new Point(right - _backBtn.Width, 10);
            _recentBtn.Location = new Point(right - _recentBtn.Width, 10);

            int reserve = (_backBtn.Visible ? _backBtn.Width : (_recentBtn.Visible ? _recentBtn.Width : 0)) + 24;
            _tocHead.Width = Math.Max(60, _tocCard.Inner.Width - reserve - 18);
        }

        /// <summary>从搜索结果/最近阅读回到目录（文本）或同目录图片。</summary>
        private void RestoreListMode()
        {
            if (_listMode != ListMode.Search && _listMode != ListMode.History) return;
            _backBtn.Visible = false;
            _recentBtn.Visible = _listMode != ListMode.History ? false : true;

            if (_imageMode && _imageFiles.Length > 0)
            {
                // 回到同目录图片列表
                BuildImageList(_filePath);
                return;
            }

            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath) && !ImageLoader.IsImage(_filePath))
            {
                // 重新解析章节（不重新读文件）
                BuildChapters(_text.Text);
                return;
            }

            // 没打开文件：回到空目录
            _listMode = ListMode.Toc;
            _toc.Items.Clear();
            _tocHead.Text = "目录";
            _info.Text = "还没有打开文件";
        }

        private void ShowHistory()
        {
            _history = HistoryStore.Load();
            _listMode = ListMode.History;
            _backBtn.Visible = true;
            _backBtn.Text = "← 返回目录";
            _recentBtn.Visible = false;
            PositionBackButton();

            var historyRows = new List<string>();
            foreach (var e in _history)
            {
                string kind = e.Kind == "image" ? "图片" : "文本";
                string when = e.OpenedAt.ToString("MM-dd HH:mm");
                string label = e.DisplayName + "　·　" + kind + "　·　" + when;
                if (label.Length > 90) label = label.Substring(0, 90) + "…";
                historyRows.Add(label);
            }
            if (historyRows.Count == 0) historyRows.Add("（还没有阅读记录）");
            _toc.SetItems(historyRows);

            _tocHead.Text = "最近阅读 · " + _history.Count + " 个";
            _toc.Invalidate();
            _info.Text = "最近阅读：" + _history.Count + " 个文件，双击继续读";
            _toc.SelectedIndex = -1;
        }

        private void UpdateModeUi()
        {
            _tocBtn.Visible = !_imageMode;
            _minusBtn.Visible = !_imageMode;
            _plusBtn.Visible = !_imageMode;
            _encBox.Visible = !_imageMode;
            _fitBtn.Visible = _imageMode;
            _actualBtn.Visible = _imageMode;
            _prevBtn.Visible = _imageMode;
            _nextBtn.Visible = _imageMode;
            LayoutHeaderControls();
        }

        private bool HandleImageKeys(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.PageUp:
                    StepImage(-1); return true;
                case Keys.Right:
                case Keys.PageDown:
                case Keys.Space:
                    StepImage(1); return true;
                case Keys.Oemplus:
                case Keys.Add:
                    _canvas.ZoomBy(1.2f, null); UpdateImageInfo(); return true;
                case Keys.OemMinus:
                case Keys.Subtract:
                    _canvas.ZoomBy(1f / 1.2f, null); UpdateImageInfo(); return true;
                case Keys.D0:
                    _canvas.FitToWindow(); UpdateImageInfo(); return true;
                case Keys.D1:
                    _canvas.ActualSize(); UpdateImageInfo(); return true;
                case Keys.T:
                    if (!e.Control) { ToggleTheme(); return true; }
                    return false;
                case Keys.C:
                    if (!e.Control) { ToggleToc(); return true; }
                    return false;
                case Keys.O:
                    if (e.Control) { ChooseFile(); return true; }
                    return false;
            }
            return false;
        }
        private void BuildChapters(string text)
        {
            _chapters.Clear();

            var chapterTitles = new List<string>();
            int lineStart = 0;
            int len = text.Length;
            while (true)
            {
                int nl = text.IndexOf('\n', lineStart);
                int lineEnd = nl < 0 ? len : nl;
                string line = text.Substring(lineStart, lineEnd - lineStart).Trim();
                if (line.Length > 0 && line.Length <= 60 && ChapterRe.IsMatch(line))
                {
                    _chapters.Add(new Chapter { Title = line, Offset = lineStart });
                    chapterTitles.Add(line);
                }
                if (nl < 0) break;
                lineStart = nl + 1;
            }

            if (chapterTitles.Count == 0) chapterTitles.Add("未识别到章节标题");
            _toc.SetItems(chapterTitles);
            _toc.Enabled = _chapters.Count > 0;
            _listMode = ListMode.Toc;
            _tocHead.Text = "目录 · " + _chapters.Count + " 章";
            _toc.ClearSelected();
            if (_toc.Items.Count > 0) _toc.TopIndex = 0;
        }

        private void JumpToSelected()
        {
            int idx = _toc.SelectedIndex;

            if (_listMode == ListMode.Search)
            {
                if (idx >= 0 && idx < _hits.Count) OpenUrl(_hits[idx].Url);
                return;
            }
            if (_listMode == ListMode.History)
            {
                if (idx >= 0 && idx < _history.Count) OpenHistoryEntry(_history[idx]);
                return;
            }
            if (_imageMode)
            {
                if (idx >= 0 && idx < _imageFiles.Length) OpenImage(_imageFiles[idx]);
                return;
            }
            if (idx < 0 || idx >= _chapters.Count) return;
            _text.SelectionStart = _chapters[idx].Offset;
            _text.SelectionLength = 0;
            _text.ScrollToCaret();
            UpdateProgress();
        }

        private void ToggleToc()
        {
            _tocCard.Visible = !_tocCard.Visible;
            _body.ColumnStyles[0].Width = _tocCard.Visible ? 288f : 0f;
        }

        private void SetFontSize(int px)
        {
            _fontSize = Math.Max(12, Math.Min(48, px));
            _text.Font = BodyFont();
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            if (_text.TextLength == 0)
            {
                _progress.Text = "0%";
                _progressFill.Width = 0;
                return;
            }
            int first = _text.GetCharIndexFromPosition(new Point(2, 2));
            if (first < 0) first = 0;
            double pct = Math.Min(100.0, first * 100.0 / _text.TextLength);
            _progress.Text = ((int)pct) + "%";
            _lastFirstChar = first;
            int track = _progressTrack.ClientSize.Width;
            _progressFill.Width = (int)(track * pct / 100.0);
        }

        private void FocusSearch()
        {
            SetPlaceholder(false);
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { FocusSearch(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Escape && (_listMode == ListMode.Search || _listMode == ListMode.History))
            { RestoreListMode(); e.Handled = true; return; }
            if (_imageMode && HandleImageKeys(e)) { e.Handled = true; return; }

            int page = Math.Max(120, _text.ClientSize.Height - 60);
            switch (e.KeyCode)
            {
                case Keys.Right:
                case Keys.PageDown:
                case Keys.Space:
                    ScrollBy(page); e.Handled = true; break;
                case Keys.Left:
                case Keys.PageUp:
                    ScrollBy(-page); e.Handled = true; break;
                case Keys.Home:
                    _text.SelectionStart = 0; _text.ScrollToCaret(); UpdateProgress(); e.Handled = true; break;
                case Keys.End:
                    _text.SelectionStart = _text.TextLength; _text.ScrollToCaret(); UpdateProgress(); e.Handled = true; break;
                case Keys.Oemplus:
                case Keys.Add:
                    if (!e.Control) { SetFontSize(_fontSize + 1); e.Handled = true; } break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    if (!e.Control) { SetFontSize(_fontSize - 1); e.Handled = true; } break;
                case Keys.T:
                    if (!e.Control) { ToggleTheme(); e.Handled = true; } break;
                case Keys.C:
                    if (!e.Control) { ToggleToc(); e.Handled = true; } break;
                case Keys.O:
                    if (e.Control) { ChooseFile(); e.Handled = true; } break;
            }
        }

        private void ScrollBy(int delta)
        {
            _text.SmoothScrollBy(delta);   // delta 为像素
            UpdateProgress();
        }

        /* ---------------- 配置 ---------------- */

        private string PositionKey()
        {
            return (_filePath ?? "") + "|" + _fileSize;
        }

        private void RestorePosition()
        {
            if (_pendingPosition >= 0 && _pendingPosition < _text.TextLength)
            {
                _text.SelectionStart = (int)_pendingPosition;
                _text.SelectionLength = 0;
                _text.ScrollToCaret();
                _lastFirstChar = (int)_pendingPosition;
                return;
            }

            string saved = ConfigGet("pos:" + PositionKey());
            int idx;
            if (int.TryParse(saved, out idx) && idx > 0 && idx < _text.TextLength)
            {
                _text.SelectionStart = idx;
                _text.SelectionLength = 0;
                _text.ScrollToCaret();
                _lastFirstChar = idx;
            }
            else
            {
                _text.SelectionStart = 0;
                _text.ScrollToCaret();
                _lastFirstChar = 0;
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq);
                    string val = line.Substring(eq + 1);
                    if (key == "fontSize") int.TryParse(val, out _fontSize);
                    else if (key == "proxy") _proxy = val;
                    else if (key == "serperKey") _serperKey = val;
                    else if (key == "smoothScroll") _text.SmoothEnabled = val.Trim() != "0";
                    else if (key == "theme") _dark = val == "dark";
                    else if (key == "width") { int w; if (int.TryParse(val, out w)) Width = Math.Max(680, w); }
                    else if (key == "height") { int h; if (int.TryParse(val, out h)) Height = Math.Max(480, h); }
                }
                _fontSize = Math.Max(12, Math.Min(48, _fontSize));
                _text.Font = BodyFont();
                _theme = _dark ? Theme.Dark() : Theme.Light();
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                var lines = new List<string>
                {
                    "fontSize=" + _fontSize,
                    "proxy=" + (_proxy ?? ""),
                    "serperKey=" + (_serperKey ?? ""),
                    "smoothScroll=" + (_text.SmoothEnabled ? "1" : "0"),
                    "theme=" + (_dark ? "dark" : "light"),
                    "width=" + Width,
                    "height=" + Height,
                    "pos:" + PositionKey() + "=" + _lastFirstChar
                };
                File.WriteAllLines(ConfigPath, lines);
            }
            catch { }
        }

        private string ConfigGet(string key)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line = raw.Trim();
                    if (line.StartsWith(key + "=")) return line.Substring(key.Length + 1);
                }
            }
            catch { }
            return null;
        }
    }
}
