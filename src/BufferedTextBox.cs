// 肥鱼阅读器 v2 · 抗拖影 + 像素级顺滑滚动 的 RichTextBox
//
// 拖影：RichEdit 不认 WinForms 双缓冲，靠 ReaderForm 的 WS_EX_COMPOSITED 整窗合成解决。
// 顺滑：实测 RichEdit 支持 EM_SETSCROLLPOS（像素坐标），这里自己做缓动动画。
//
// 稳定性要点（踩过的坑）：
//   1) 不能在 WndProc 里同步调 EM_*：RichEdit20 在控件未就绪时会访问违例，
//      且异常穿透原生回调会直接崩进程（0xc000041d）。所以用 BeginInvoke 把滚动
//      投递到消息循环末尾再执行。
//   2) WndProc 全程 try/catch，任何异常都回退到 base.WndProc。
//   3) 只有控件就绪（句柄已建 + 有文本）后才启用平滑滚动。
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TxtReader
{
    internal sealed class BufferedRichTextBox : RichTextBox
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_SETREDRAW = 0x000B;
        private const int WM_VSCROLL = 0x0115;
        private const int EM_GETSCROLLPOS = 0x04DD;
        private const int EM_SETSCROLLPOS = 0x04DE;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly Timer _anim = new Timer();
        private readonly Stopwatch _clock = new Stopwatch();

        private int _animStartY;
        private int _animTargetY;
        private int _animDurationMs = 220;
        private bool _busy;          // 防止重入
        private bool _smoothReady;   // 控件就绪后才启用

        public int LinesPerNotch = 3;

        /// <summary>false 时完全交给 RichEdit 原生滚动（保守模式，绝不会跳）。</summary>
        public bool SmoothEnabled = false;   // 默认关闭：先用原生滚动保证稳定，配置 smoothScroll=1 可开启像素平滑

        public int AnimDurationMs
        {
            get { return _animDurationMs; }
            set { _animDurationMs = Math.Max(40, value); }
        }

        public event EventHandler ScrollChanged;

        public BufferedRichTextBox()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            _anim.Interval = 15;
            _anim.Tick += OnAnimTick;

            HandleCreated += (s, e) => _smoothReady = true;
            TextChanged += (s, e) => { if (IsHandleCreated) _smoothReady = true; _maxY = -1; };
            FontChanged += (s, e) => { _maxY = -1; };
            SizeChanged += (s, e) => { _maxY = -1; };
        }

        /* ---------------- 像素滚动 ---------------- */

        private bool TryGetScrollPos(out int y)
        {
            y = 0;
            if (!IsHandleCreated || IsDisposed) return false;
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(8);
                Marshal.WriteInt32(buf, 0, 0);
                Marshal.WriteInt32(buf, 4, 0);
                SendMessage(Handle, EM_GETSCROLLPOS, IntPtr.Zero, buf);
                y = Marshal.ReadInt32(buf, 4);
                return true;
            }
            catch { return false; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        private bool TrySetScrollPos(int y)
        {
            if (!IsHandleCreated || IsDisposed) return false;
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(8);
                Marshal.WriteInt32(buf, 0, 0);
                Marshal.WriteInt32(buf, 4, y);
                SendMessage(Handle, EM_SETSCROLLPOS, IntPtr.Zero, buf);
                return true;
            }
            catch { return false; }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        /// <summary>
        /// 当前内容可滚动的最大像素位置。
        /// 越界地调用 EM_SETSCROLLPOS 会让 RichEdit 把视图丢到不可预期的位置（表现为"弹回/跳走"），
        /// 所以所有目标值都必须先夹到 [0, maxY]。
        /// </summary>
        // 缓存的最大滚动位置（像素）。文本/字号/窗口尺寸变化时置为 -1 重新测量。
        private int _maxY = -1;

        public void InvalidateMaxScroll()
        {
            _maxY = -1;
        }

        /// <summary>
        /// 实测最大滚动位置：临时把位置设成一个极大值，RichEdit 会夹到真正的底部，
        /// 读回真实值后立刻恢复原位（同一消息内完成，不产生可见跳动）。
        /// </summary>
        /// <summary>
        /// 求真正能滚动到的最大像素位置。
        /// 直接"设个极大值让 RichEdit 夹住"并不可靠（会滚进内容下方的空白区），
        /// 所以用二分：找到"视口底部还能看到正文"的最大位置，结果缓存。
        /// </summary>
        private int MaxScrollY(int currentY)
        {
            if (!IsHandleCreated || IsDisposed || TextLength == 0) return 0;
            if (_maxY >= 0) return _maxY;

            int saved;
            if (!TryGetScrollPos(out saved)) return 0;

            bool redrawOff = false;
            try
            {
                // 关键：测量会多次设置滚动位置，必须关掉重绘，否则画面会被拽动（用户看到的就是"跳/弹"）
                SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                redrawOff = true;

                int lineH = Math.Max(8, Font.Height);
                // 先探一下上界：RichEdit 会接受一个比真实底部更大的值
                TrySetScrollPos(100000000);
                int probe;
                TryGetScrollPos(out probe);
                if (probe <= 0) { TrySetScrollPos(saved); _maxY = 0; return 0; }

                int lo = 0, hi = probe;
                // 二分：找到最大的 y，使视口底部仍有正文（末字符不在视口底部之上）
                for (int i = 0; i < 12 && hi - lo > lineH; i++)
                {
                    int mid = lo + (hi - lo) / 2;
                    TrySetScrollPos(mid);
                    int idx = BottomVisibleCharIndex();
                    if (idx >= TextLength - 1) hi = mid;   // 已经过了内容末尾
                    else lo = mid;
                }

                TrySetScrollPos(saved);
                _maxY = Math.Max(0, lo);
                return _maxY;
            }
            catch
            {
                try { TrySetScrollPos(saved); } catch { }
                _maxY = 0;
                return 0;
            }
            finally
            {
                if (redrawOff)
                {
                    SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    try { Invalidate(); Update(); } catch { }
                }
            }
        }

        private int BottomVisibleCharIndex()
        {
            try
            {
                int x = Math.Max(2, ClientSize.Width / 2);
                int y = Math.Max(2, ClientSize.Height - 6);
                return GetCharIndexFromPosition(new Point(x, y));
            }
            catch { return 0; }
        }        public void SmoothScrollBy(int deltaPixels)
        {
            if (!_smoothReady || !IsHandleCreated || IsDisposed) return;

            int current;
            if (!TryGetScrollPos(out current)) return;

            int maxY = MaxScrollY(current);
            if (maxY <= 0) return;                                 // 内容不足一屏：不滚

            // 连续滚轮时要在"上一次的目标"上累加，否则每次都会丢掉剩余动画距离，
            // 表现为滚得很慢、一顿一顿，甚至回弹。
            int baseTarget = _anim.Enabled ? _animTargetY : current;
            int target = baseTarget + deltaPixels;
            if (target < 0) target = 0;
            if (target > maxY) target = maxY;

            if (target == _animTargetY && _anim.Enabled) return;    // 目标没变，别重启动画
            if (target == current && !_anim.Enabled) return;        // 已经在该位置

            _animStartY = current;
            _animTargetY = target;

            if (Math.Abs(target - current) < 2)
            {
                TrySetScrollPos(target);
                RaiseScrollChanged();
                return;
            }

            _clock.Restart();
            _anim.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (IsDisposed || !IsHandleCreated) { _anim.Stop(); return; }

                double t = _clock.ElapsedMilliseconds / (double)_animDurationMs;
                if (t >= 1.0)
                {
                    TrySetScrollPos(_animTargetY);
                    _anim.Stop();

                    // 校正：如果实际位置和目标不符（被 RichEdit 夹住），把目标改成实际值，
                    // 否则下一次滚动会从一个错误的起点继续
                    int actual;
                    if (TryGetScrollPos(out actual) && Math.Abs(actual - _animTargetY) > 2)
                        _animTargetY = actual;
                    else
                        _animTargetY = actual;

                    RaiseScrollChanged();
                    return;
                }

                // ease-out cubic
                double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
                int y = _animStartY + (int)Math.Round((_animTargetY - _animStartY) * eased);
                if (y < 0) y = 0;
                TrySetScrollPos(y);
                RaiseScrollChanged();
            }
            catch { _anim.Stop(); }
            finally { _busy = false; }
        }

        private void RaiseScrollChanged()
        {
            var handler = ScrollChanged;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); } catch { }
        }

        /* ---------------- 消息处理 ---------------- */

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == WM_MOUSEWHEEL && SmoothEnabled && _smoothReady && IsHandleCreated && TextLength > 0)
                {
                    int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    int notches = delta / 120;
                    if (notches == 0) notches = delta > 0 ? 1 : -1;
                    int pixels = -notches * LinesPerNotch * Math.Max(8, Font.Height);

                    // 关键：投递到消息循环末尾执行，避免在原生回调栈里调 EM_*
                    BeginInvoke((MethodInvoker)(() => SmoothScrollBy(pixels)));
                    return;
                }

                if (m.Msg == WM_VSCROLL)
                {
                    _anim.Stop();
                    base.WndProc(ref m);
                    RaiseScrollChanged();
                    return;
                }

                base.WndProc(ref m);
            }
            catch
            {
                // 绝不把异常抛回原生回调
                try { base.WndProc(ref m); } catch { }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _smoothReady = false;
                _anim.Stop();
                _anim.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
