using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SR.AnalogGain.UI.Win32
{
    internal sealed class SwitchWindow
    {
        private const string WC_STATIC = "STATIC";
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;
        private const int WM_SIZE = 0x0005;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSELEAVE = 0x02A3;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        private const int WS_EX_TRANSPARENT = 0x20;
        private const int SS_NOTIFY = 0x0100;

        private IntPtr _parent = IntPtr.Zero;
        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _origWndProc = IntPtr.Zero;
        private readonly WndProc _proc;

        private int _x, _y, _w, _h;
        private Bitmap? _backBuffer; int _bufW, _bufH;

        private readonly string _resOff, _resOn, _label;
        private Bitmap? _bmpOff, _bmpOn;

        // Param binding
        private readonly Func<bool> _get;
        private readonly Action _begin;
        private readonly Action<bool> _perform;
        private readonly Action _end;

        // Container background slice (like the knobs)
        private Bitmap? _containerBg;
        private int _containerW, _containerH;

        // mouse state
        private bool _pressed, _trackingLeave, _editing;

        public SwitchWindow(string resOff, string resOn, string label,
            Func<bool> get, Action begin, Action<bool> perform, Action end)
        {
            _resOff = resOff; _resOn = resOn; _label = label;
            _get = get; _begin = begin; _perform = perform; _end = end;
            _proc = WndProcImpl;
        }

        public void SetContainerBackgroundReference(Bitmap? bg, int containerW, int containerH)
        {
            _containerBg = bg; _containerW = Math.Max(1, containerW); _containerH = Math.Max(1, containerH);
            Refresh();
        }

        private void EnsureAssets()
        {
            if (_bmpOff == null) { try { _bmpOff = Embedded.LoadBitmap(typeof(SwitchWindow).Assembly, _resOff); } catch { } }
            if (_bmpOn == null) { try { _bmpOn = Embedded.LoadBitmap(typeof(SwitchWindow).Assembly, _resOn); } catch { } }
        }

        public bool Create(IntPtr parent, int x, int y, int w, int h)
        {
            _parent = parent; _x = x; _y = y; _w = w; _h = h;

            _hwnd = CreateWindowEx(WS_EX_TRANSPARENT, WC_STATIC, null, WS_CHILD | WS_VISIBLE | SS_NOTIFY, _x, _y, _w, _h, _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero) return false;

            _origWndProc = SetWindowLongPtr(_hwnd, -4 /*GWL_WNDPROC*/, Marshal.GetFunctionPointerForDelegate(_proc));

            SetWindowPos(_hwnd, new IntPtr(-1) /*HWND_TOP*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE

            return true;
        }

        public void Destroy()
        {
            _bmpOff?.Dispose(); _bmpOff = null;
            _bmpOn?.Dispose(); _bmpOn = null;
            _backBuffer?.Dispose(); _backBuffer = null;

            if (_hwnd != IntPtr.Zero)
            {
                if (_origWndProc != IntPtr.Zero) { SetWindowLongPtr(_hwnd, -4, _origWndProc); _origWndProc = IntPtr.Zero; }
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            _parent = IntPtr.Zero;
        }

        public void SetBounds(int x, int y, int w, int h)
        {
            _x = x; _y = y; _w = w; _h = h;
            if (_hwnd != IntPtr.Zero)
            {
                MoveWindow(_hwnd, _x, _y, _w, _h, true);
                InvalidateRect(_hwnd, IntPtr.Zero, false);
            }
        }

        public void Refresh()
        {
            if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false);
        }

        private void EnsureBackBuffer(int w, int h)
        {
            if (_backBuffer == null || _bufW != w || _bufH != h)
            {
                _backBuffer?.Dispose();
                _backBuffer = new Bitmap(Math.Max(1, w), Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                _bufW = w; _bufH = h;
            }
        }

        private float UiScale()
        {
            // derive scale from fixed logical size 1024x512 (same idea as editor)
            // we use width scale; aspect is fixed by the parent anyway
            return (_containerW > 0) ? Math.Max(0.1f, (float)_containerW / 1024f) : 1.0f;
        }

        private void Render(Graphics g)
        {
            bool on = _get();
            EnsureAssets();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingMode = CompositingMode.SourceOver;

            // no background paint here (transparent control)

            float ui = UiScale();
            var bmp = on ? _bmpOn : _bmpOff;
            int sw = bmp?.Width ?? 115, sh = bmp?.Height ?? 60;
            float ar = (float)sw / Math.Max(1, sh);

            // Fit image inside our rect, keep aspect
            int dstH = Math.Min(_h, (int)Math.Round(sh * ui));
            int dstW = Math.Min(_w, (int)Math.Round(dstH * ar));
            int imgX = (_w - dstW) / 2;
            int imgY = (_h - dstH) / 2;

            // draw switch image
            if (bmp != null) g.DrawImage(bmp, new Rectangle(imgX, imgY, dstW, dstH));

            // label overlay (centered on the image)
            using var f = new Font(FontFamily.GenericSansSerif, Math.Max(10f, dstH * 0.26f), FontStyle.Bold, GraphicsUnit.Pixel);
            //using var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            using var black = new SolidBrush(Color.Black);

            var size = g.MeasureString(_label, f);
            float lblX = imgX + (dstW - size.Width) * 0.5f;
            float lblY = imgY + (dstH - size.Height) * 0.5f;

            // offset when ON: (-2, +2) scaled
            if (on) { lblX += -2f * ui; lblY += +2f * ui; }

            //g.DrawString(_label, f, shadow, lblX + 1, lblY + 1);
            g.DrawString(_label, f, black, lblX, lblY);
        }

        private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_ERASEBKGND: return (IntPtr)1;

                case WM_SIZE:
                    {
                        int w = (int)(lParam.ToInt64() & 0xFFFF);
                        int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                        _w = Math.Max(1, w); _h = Math.Max(1, h);
                        InvalidateRect(_hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDOWN:
                    {
                        _pressed = true;
                        _editing = true;              // start a parameter edit
                        SetCapture(_hwnd);
                        _begin();
                        return IntPtr.Zero;
                    }


                case WM_LBUTTONDBLCLK:            // treat as another "down" for the 2nd click
                    {
                        _pressed = true;
                        if (!_editing)
                        {              // be robust if we re-enter
                            _editing = true;
                            _begin();
                        }
                        SetCapture(_hwnd);
                        return IntPtr.Zero;           // handled; don't let default proc meddle
                    }

                case WM_LBUTTONUP:
                    {
                        ReleaseCapture();
                        bool inside = _pressed && HitTest(
                            (short)(lParam.ToInt64() & 0xFFFF),
                            (short)((lParam.ToInt64() >> 16) & 0xFFFF));

                        _pressed = false;

                        if (inside)
                        {
                            bool newVal = !_get();
                            _perform(newVal);
                            InvalidateRect(_hwnd, IntPtr.Zero, false);
                        }

                        if (_editing)
                        {               // only end if we began
                            _editing = false;
                            _end();
                        }
                        return IntPtr.Zero;
                    }

                case WM_MOUSELEAVE:
                    _trackingLeave = false;
                    _pressed = false; // cancel press if pointer left
                    return IntPtr.Zero;

                case WM_PAINT:
                    {
                        var ps = new PAINTSTRUCT();
                        IntPtr hdc = BeginPaint(_hwnd, out ps);
                        try
                        {
                            EnsureBackBuffer(_w, _h);
                            using (var gb = Graphics.FromImage(_backBuffer!))
                            {
                                Render(gb);
                            }
                            using var g = Graphics.FromHdc(hdc);
                            g.CompositingMode = CompositingMode.SourceOver;
                            g.DrawImageUnscaled(_backBuffer!, 0, 0);
                        }
                        finally { EndPaint(_hwnd, ref ps); }
                        return IntPtr.Zero;
                    }
            }

            return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
        }

        private bool HitTest(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

        private void TrackMouseLeave()
        {
            if (_trackingLeave || _hwnd == IntPtr.Zero) return;
            // we don’t need full TRACKMOUSEEVENT P/Invoke; LBUTTONUP logic already cancels,
            // but it’s nice to clear pressed if the pointer leaves.
            _trackingLeave = true;
        }

        // P/Invoke
        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
            int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern bool SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }
    }
}
