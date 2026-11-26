// 肥鱼阅读器 · 图片查看层
// ImageLoader：加载图片（GDI+ 失败时用 WIC 兜底，可读 webp/部分 tiff）
// ImageCanvas：支持适应窗口、1:1、滚轮缩放、拖动平移的画布控件
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace TxtReader
{
    internal static class ImageLoader
    {
        public static readonly string[] Exts = new string[]
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".bmp", ".gif", ".tif", ".tiff",
            ".ico", ".webp", ".wmf", ".emf"
        };

        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Exts, ext) >= 0;
        }

        /// <summary>供 OpenFileDialog 用的图片筛选片段。</summary>
        public const string DialogFilter =
            "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.ico";

        public static Bitmap Load(string path)
        {
            try
            {
                // 用流打开，避免文件被锁住
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var tmp = Image.FromStream(fs, true, true))
                {
                    return new Bitmap(tmp);
                }
            }
            catch (OutOfMemoryException)
            {
                // GDI+ 对未知格式会抛 OutOfMemory，转而用 WIC
            }
            catch (ArgumentException)
            {
            }

            return LoadWithWic(path);
        }

        /// <summary>WIC 解码（Windows Imaging Component），可覆盖 webp 等 GDI+ 不支持的格式。</summary>
        private static Bitmap LoadWithWic(string path)
        {
            try
            {
                var uri = new Uri(path, UriKind.Absolute);
                BitmapDecoder decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) throw new NotSupportedException("图片没有可用的帧。");

                BitmapFrame frame = decoder.Frames[0];
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));

                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    using (var tmp = Image.FromStream(ms, true, true))
                    {
                        return new Bitmap(tmp);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new NotSupportedException("这张图片解不开（可能是 webp/avif 等系统缺少解码器的格式）：" + ex.Message);
            }
        }
    }

    /// <summary>图片画布：适应窗口 / 1:1 / 滚轮缩放 / 拖动平移 / 双击切换。</summary>
    internal class ImageCanvas : Control
    {
        private Bitmap _image;
        private float _zoom = 1f;
        private bool _fit = true;
        private bool _dragging;
        private Point _dragStart;
        private PointF _origin = new PointF(0, 0);   // 内容左上角相对控件的位置
        private Color _back = Color.FromArgb(0xF6, 0xF7, 0xF9);

        public ImageCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = _back;
            TabStop = true;
        }

        public Bitmap Image
        {
            get { return _image; }
            set
            {
                if (_image != null && !ReferenceEquals(_image, value)) _image.Dispose();
                _image = value;
                _fit = true;
                _zoom = 1f;
                _origin = new PointF(0, 0);
                FitToWindow();
                Invalidate();
            }
        }

        public void SetBackColor(Color c) { _back = c; BackColor = c; Invalidate(); }

        public bool IsFit { get { return _fit; } }

        public float Zoom { get { return _zoom; } }

        public void FitToWindow()
        {
            if (_image == null || Width <= 0 || Height <= 0) return;
            float sx = (float)(Width - 24) / _image.Width;
            float sy = (float)(Height - 24) / _image.Height;
            _zoom = Math.Min(sx, sy);
            if (_zoom > 1f) _zoom = 1f;          // 小图不放大，居中显示
            _fit = true;
            Center();
            Invalidate();
        }

        public void ActualSize()
        {
            if (_image == null) return;
            _zoom = 1f;
            _fit = false;
            Center();
            Invalidate();
        }

        public void ZoomBy(float factor, Point? anchor)
        {
            if (_image == null) return;
            float old = _zoom;
            float next = Math.Max(0.05f, Math.Min(16f, old * factor));
            if (Math.Abs(next - old) < 0.0001f) return;

            PointF pivot = anchor.HasValue ? (PointF)anchor.Value : new PointF(Width / 2f, Height / 2f);
            // 保持光标下的图像点不动
            float ix = (pivot.X - _origin.X) / old;
            float iy = (pivot.Y - _origin.Y) / old;
            _zoom = next;
            _fit = false;
            _origin = new PointF(pivot.X - ix * next, pivot.Y - iy * next);
            Clamp();
            Invalidate();
        }

        private void Center()
        {
            if (_image == null || Width <= 0 || Height <= 0) return;
            float w = _image.Width * _zoom;
            float h = _image.Height * _zoom;
            _origin = new PointF((Width - w) / 2f, (Height - h) / 2f);
        }

        private void Clamp()
        {
            if (_image == null) return;
            float w = _image.Width * _zoom;
            float h = _image.Height * _zoom;
            // 图比控件小就居中；比控件大就保证不拖出空白
            if (w <= Width) _origin.X = (Width - w) / 2f;
            else _origin.X = Math.Min(0, Math.Max(Width - w, _origin.X));
            if (h <= Height) _origin.Y = (Height - h) / 2f;
            else _origin.Y = Math.Min(0, Math.Max(Height - h, _origin.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_back);
            if (_image == null) return;

            if (_fit) FitToWindow();

            g.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var rect = new RectangleF(_origin.X, _origin.Y, _image.Width * _zoom, _image.Height * _zoom);
            g.DrawImage(_image, rect);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_image == null) return;
            if (_fit) FitToWindow(); else Clamp();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            _origin.X += e.X - _dragStart.X;
            _origin.Y += e.Y - _dragStart.Y;
            _dragStart = e.Location;
            _fit = false;
            Clamp();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_fit) ActualSize(); else FitToWindow();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) == Keys.Control || true)
                ZoomBy(e.Delta > 0 ? 1.2f : 1f / 1.2f, e.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }
    }
}
