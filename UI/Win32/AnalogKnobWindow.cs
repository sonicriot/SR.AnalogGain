using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SR.AnalogGain.UI.Win32
{
    internal sealed class AnalogKnobWindow
    {
        // ---------- Recursos (nombres embebidos) ----------
        private readonly string _faceResName;
        private readonly string _topResName;
        private readonly string _pointerResName;

        private Bitmap? _face512;        // aro metálico / cara
        private Bitmap? _top512;         // tapa superior del color
        private Bitmap? _pointerSheet;   // sprite puntero 300x300 x 73

        // Fondo del contenedor (para recortar debajo del knob)
        private Bitmap? _containerBg;
        private int _containerW = 1, _containerH = 1;

        // ---------- Geometría / sprite ----------
        private const float SweepDeg = 263f;        // barrido total del puntero
        private const float StartDeg = -SweepDeg + 39f; // primer frame
        private const int PTR_FRAMES = 73;
        private const int PTR_STEPS = PTR_FRAMES - 1;    // 72
        private const int PTR_FRAME_PX = 300;

        // Radios (fracción del radio del knob)
        private const float TickOuterR = 0.70f;
        private const float TickInnerR = 0.64f;
        private const float LabelRadiusR = 0.80f;

        // ---------- Back buffer ----------
        private Bitmap? _backBuffer;
        private int _bufW, _bufH;

        // ---------- Rango / etiqueta ----------
        private readonly double _minDb, _maxDb;
        private readonly string _label;

        // ---------- Win32 ----------
        private const string WC_STATIC = "STATIC";
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int SS_NOTIFY = 0x0100;

        private const int GWL_WNDPROC = -4;
        private const int WM_PAINT = 0x000F;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WHEEL_DELTA = 120;

        // ---------- Estado / interacción ----------
        private int _x, _y, _w, _h;
        private bool _editing;
        private int _dragStartY;
        private double _dragStartNorm;

        private readonly Func<float> _getNormalized;
        private readonly Action _beginEdit;
        private readonly Action<double> _performEdit;
        private readonly Action _endEdit;

        private IntPtr _parent;
        private IntPtr _hwnd;
        private IntPtr _origWndProc = IntPtr.Zero;
        private readonly WndProc _proc;

        public AnalogKnobWindow(
            Func<float> getNormalized,
            Action beginEdit,
            Action<double> performEdit,
            Action endEdit,
            double minDb, double maxDb,
            string label,
            string faceResName,
            string topResName,
            string pointerResName)
        {
            _getNormalized = getNormalized;
            _beginEdit = beginEdit;
            _performEdit = performEdit;
            _endEdit = endEdit;

            _minDb = minDb;
            _maxDb = maxDb;
            _label = label;

            _faceResName = faceResName;
            _topResName = topResName;
            _pointerResName = pointerResName;

            _proc = WndProcImpl;
        }

        // El contenedor nos pasa su fondo (para “recortar” exacto bajo el knob)
        public void SetContainerBackgroundReference(Bitmap? bg, int containerW, int containerH)
        {
            _containerBg = bg;
            _containerW = Math.Max(1, containerW);
            _containerH = Math.Max(1, containerH);
            Refresh();
        }

        // ---------- Recursos ----------
        private void EnsureAssetsLoaded()
        {
            if (_face512 == null) { try { _face512 = Embedded.LoadBitmap(typeof(AnalogKnobWindow).Assembly, _faceResName); } catch { } }
            if (_top512 == null) { try { _top512 = Embedded.LoadBitmap(typeof(AnalogKnobWindow).Assembly, _topResName); } catch { } }
            if (_pointerSheet == null) { try { _pointerSheet = Embedded.LoadBitmap(typeof(AnalogKnobWindow).Assembly, _pointerResName); } catch { } }
        }

        public void DisposeAssets()
        {
            _face512?.Dispose(); _face512 = null;
            _top512?.Dispose(); _top512 = null;
            _pointerSheet?.Dispose(); _pointerSheet = null;
        }

        // ---------- Vida de la ventana ----------
        public bool Create(IntPtr parent, int x, int y, int w, int h)
        {
            _parent = parent;
            _x = x; _y = y; _w = w; _h = h;

            _hwnd = CreateWindowEx(
                0, WC_STATIC, null,
                WS_CHILD | WS_VISIBLE | SS_NOTIFY,
                _x, _y, _w, _h,
                _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero) return false;

            _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
            EnsureAssetsLoaded();
            InvalidateRect(_hwnd, IntPtr.Zero, true); // erase
            return true;
        }

        public void Destroy()
        {
            if (_hwnd != IntPtr.Zero && _origWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWL_WNDPROC, _origWndProc);
                _origWndProc = IntPtr.Zero;
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            _backBuffer?.Dispose(); _backBuffer = null;
            _bufW = _bufH = 0;
            DisposeAssets();
        }

        public void SetBounds(int x, int y, int w, int h)
        {
            _x = x; _y = y; _w = w; _h = h;
            if (_hwnd != IntPtr.Zero) MoveWindow(_hwnd, _x, _y, _w, _h, true);
            EnsureBackBuffer(_w, _h);
        }

        public void Refresh()
        {
            if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, true);
        }

        // ---------- WndProc ----------
        private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (msg)
                {
                    case WM_PAINT:
                        {
                            var ps = new PAINTSTRUCT();
                            var hdc = BeginPaint(_hwnd, out ps);
                            try
                            {
                                EnsureBackBuffer(_w, _h);
                                if (_backBuffer == null) break;

                                using (var gb = Graphics.FromImage(_backBuffer))
                                {
                                    gb.SmoothingMode = SmoothingMode.HighQuality;
                                    gb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                    gb.CompositingQuality = CompositingQuality.HighQuality;
                                    gb.CompositingMode = CompositingMode.SourceOver;

                                    // 1) slice del fondo (sin rastro)
                                    PaintBackgroundSlice(gb);

                                    // 2) knob completo
                                    RenderKnob(gb);
                                }

                                using (var g = Graphics.FromHdc(hdc))
                                {
                                    g.CompositingMode = CompositingMode.SourceOver;
                                    g.DrawImageUnscaled(_backBuffer, 0, 0);
                                }
                            }
                            finally { EndPaint(_hwnd, ref ps); }
                            return (IntPtr)0;
                        }

                    case WM_LBUTTONDOWN:
                        SetCapture(_hwnd);
                        _editing = true;
                        _beginEdit();
                        _dragStartY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        _dragStartNorm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
                        return IntPtr.Zero;

                    case WM_MOUSEMOVE:
                        if (_editing)
                        {
                            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                            int dy = y - _dragStartY;
                            const double pixelsForFullRange = 200.0;
                            double newNorm = Math.Clamp(_dragStartNorm - dy / pixelsForFullRange, 0.0, 1.0);
                            newNorm = QuantizeTo1dB(newNorm);
                            _performEdit(newNorm);
                            Refresh();
                        }
                        return IntPtr.Zero;

                    case WM_LBUTTONUP:
                        if (_editing)
                        {
                            _editing = false;
                            _endEdit();
                            ReleaseCapture();
                        }
                        return IntPtr.Zero;

                    case WM_MOUSEWHEEL:
                        {
                            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                            int ticks = delta / WHEEL_DELTA;
                            double n = _getNormalized() + ticks * (1.0 / PTR_STEPS);
                            n = QuantizeTo1dB(n);
                            _beginEdit(); _performEdit(n); _endEdit();
                            Refresh();
                            return (IntPtr)0;
                        }

                    case WM_LBUTTONDBLCLK:
                        {
                            double norm0 = (0.0 - _minDb) / (_maxDb - _minDb);
                            norm0 = QuantizeTo1dB(norm0);
                            _beginEdit(); _performEdit(norm0); _endEdit();
                            Refresh();
                            return (IntPtr)0;
                        }

                    case WM_ERASEBKGND:
                        return (IntPtr)1; // todo se pinta en WM_PAINT
                }
            }
            catch { /* no romper host */ }

            return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
        }

        // ---------- Pintado ----------
        private void PaintBackgroundSlice(Graphics g)
        {
            if (_containerBg == null || _containerW <= 0 || _containerH <= 0)
            {
                g.Clear(Color.FromArgb(22, 40, 68)); // fallback
                return;
            }

            int srcX = (int)Math.Round((double)_x / _containerW * _containerBg.Width);
            int srcY = (int)Math.Round((double)_y / _containerH * _containerBg.Height);
            int srcW = (int)Math.Round((double)_w / _containerW * _containerBg.Width);
            int srcH = (int)Math.Round((double)_h / _containerH * _containerBg.Height);

            srcX = Math.Clamp(srcX, 0, Math.Max(0, _containerBg.Width - 1));
            srcY = Math.Clamp(srcY, 0, Math.Max(0, _containerBg.Height - 1));
            srcW = Math.Max(1, Math.Min(srcW, _containerBg.Width - srcX));
            srcH = Math.Max(1, Math.Min(srcH, _containerBg.Height - srcY));

            g.DrawImage(_containerBg, new Rectangle(0, 0, _w, _h),
                        new Rectangle(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);
        }

        private void RenderKnob(Graphics g)
        {
            EnsureAssetsLoaded();

            int side = Math.Min(_w, _h);
            int offX = (_w - side) / 2;
            int offY = (_h - side) / 2;

            // margen interior para que entren ticks/labels dentro del aro
            float inset = Math.Max(12f, side * 0.06f);
            var content = new RectangleF(offX + inset, offY + inset, side - 2 * inset, side - 2 * inset);

            // 1) Ticks y números (debajo del aro)
            DrawTicksAndNumbers(g, content);

            // 2) Cara / aro
            if (_face512 != null) g.DrawImage(_face512, content);

            // 3) Puntero (sprite)
            DrawPointer(g, content);

            // 4) Tapa color
            if (_top512 != null) g.DrawImage(_top512, content);

            // 5) Textos (valor y etiqueta)
            DrawTexts(g, content);
        }

        private void DrawTicksAndNumbers(Graphics g, RectangleF content)
        {
            float cx = content.Left + content.Width * 0.5f;
            float cy = content.Top + content.Height * 0.5f;
            float R = Math.Min(content.Width, content.Height) * 0.5f;

            using var penTick = new Pen(Color.FromArgb(190, 220, 220, 220), 1f);
            using var f = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brushTxt = new SolidBrush(Color.FromArgb(235, 235, 235));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Use custom tick positions for gain knob (-60 to +12), standard for others
            var tickPositions = GetTickPositions();

            foreach (double db in tickPositions)
            {
                DrawOne(db);
            }

            void DrawOne(double db)
            {
                float ang = AngleDegFromDb(db, _minDb, _maxDb);
                double rad = ang * Math.PI / 180.0;

                float x0 = cx + (float)(Math.Cos(rad) * (R * TickInnerR));
                float y0 = cy + (float)(Math.Sin(rad) * (R * TickInnerR));
                float x1 = cx + (float)(Math.Cos(rad) * (R * TickOuterR));
                float y1 = cy + (float)(Math.Sin(rad) * (R * TickOuterR));
                g.DrawLine(penTick, x0, y0, x1, y1);

                string label = DbToLabel(db);
                float lr = R * LabelRadiusR;
                float tx = cx + (float)(Math.Cos(rad) * lr);
                float ty = cy + (float)(Math.Sin(rad) * lr);
                var sz = g.MeasureString(label, f);
                g.DrawString(label, f, brushTxt, tx - sz.Width / 2f, ty - sz.Height / 2f);
            }
        }

        private void DrawPointer(Graphics g, RectangleF content)
        {
            if (_pointerSheet == null) return;
            if (_pointerSheet.Height != PTR_FRAME_PX ||
                _pointerSheet.Width != PTR_FRAME_PX * PTR_FRAMES) return;

            double norm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
            int frame = FrameFromNorm(norm, _minDb, _maxDb); // ← mapeo exacto min→0, max→72

            int srcX = frame * PTR_FRAME_PX;
            var src = new Rectangle(srcX, 0, PTR_FRAME_PX, PTR_FRAME_PX);

            float scale = content.Width / 512f; // el sprite está pensado para encajar sobre 512
            int dstSide = (int)Math.Round(PTR_FRAME_PX * scale);
            int cx = (int)Math.Round(content.Left + content.Width * 0.5f);
            int cy = (int)Math.Round(content.Top + content.Height * 0.5f);

            var dst = new Rectangle(cx - dstSide / 2, cy - dstSide / 2, dstSide, dstSide);
            g.DrawImage(_pointerSheet, dst, src, GraphicsUnit.Pixel);
        }

        private void DrawTexts(Graphics g, RectangleF content)
        {
            double norm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
            double db = _minDb + norm * (_maxDb - _minDb);

            string val = $"{db:+0.0;-0.0;0.0} dB";
            using var fVal = new Font(FontFamily.GenericSansSerif, Math.Max(10f, content.Width * 0.08f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var fLbl = new Font(FontFamily.GenericSansSerif, Math.Max(12f, content.Width * 0.10f), FontStyle.Bold, GraphicsUnit.Pixel);

            using var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            using var white = new SolidBrush(Color.White);

            var valPt = new PointF(content.Left - 10, content.Top -10);
            g.DrawString(val, fVal, shadow, valPt.X + 1, valPt.Y + 1);
            g.DrawString(val, fVal, white, valPt);

            var size = g.MeasureString(_label, fLbl);
            var lblPt = new PointF(content.Left + (content.Width - size.Width) * 0.5f, content.Bottom - size.Height + 10);
            g.DrawString(_label, fLbl, shadow, lblPt.X + 1, lblPt.Y + 1);
            g.DrawString(_label, fLbl, white, lblPt);
        }

        // ---------- Utilidades ----------
        private double[] GetTickPositions()
        {
            // Custom tick positions for gain knob (-60 to +12 dB)
            if (Math.Abs(_minDb - (-60.0)) < 1e-6 && Math.Abs(_maxDb - 12.0) < 1e-6)
            {
                return new double[] { -60, -50, -40, -30, -20, -10, 0, 6, 12 };
            }
            
            // For other ranges (like output knob), use the original algorithm
            double range = _maxDb - _minDb;
            double step = ChooseNiceStep(range);
            
            var positions = new List<double>();
            
            // Add min
            positions.Add(_minDb);
            
            // Add intermediate steps
            double first = Math.Ceiling(_minDb / step) * step;
            if (Math.Abs(first - _minDb) < 1e-6) first = _minDb;
            for (double v = first; v < _maxDb - 1e-6; v += step)
            {
                if (Math.Abs(v - _minDb) > 1e-6) // Don't duplicate min
                    positions.Add(v);
            }
            
            // Add max
            if (Math.Abs(_maxDb - _minDb) > 1e-6) // Don't duplicate if min == max
                positions.Add(_maxDb);
            
            return positions.ToArray();
        }

        private static double ChooseNiceStep(double range)
        {
            if (range >= 48) return 10;
            if (range >= 30) return 6;
            if (range >= 24) return 5;
            if (range >= 18) return 3;
            if (range >= 12) return 2;
            return 1;
        }

        private static string DbToLabel(double db)
            => Math.Abs(db) < 0.5 ? "0" : (db > 0 ? $"+{Math.Round(db)}" : $"{Math.Round(db)}");

        private static int FrameFromNorm(double norm, double minDb, double maxDb)
        {
            double db = minDb + norm * (maxDb - minDb);
            return FrameFromDb(db, minDb, maxDb);
        }

        private static int FrameFromDb(double db, double minDb, double maxDb)
        {
            double t = (db - minDb) / (maxDb - minDb);
            t = Math.Clamp(t, 0.0, 1.0);
            int frame = (int)Math.Round(t * PTR_STEPS); // 0..72
            return Math.Max(0, Math.Min(PTR_STEPS, frame));
        }

        private static float AngleDegFromDb(double db, double minDb, double maxDb)
        {
            double t = (db - minDb) / (maxDb - minDb);
            t = Math.Clamp(t, 0.0, 1.0);
            return StartDeg + (float)(t * SweepDeg);
        }

        private static double QuantizeTo1dB(double n)
            => Math.Round(Math.Clamp(n, 0.0, 1.0) * PTR_STEPS) / PTR_STEPS;

        private void EnsureBackBuffer(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            if (_backBuffer == null || _bufW != w || _bufH != h)
            {
                _backBuffer?.Dispose();
                _backBuffer = new Bitmap(Math.Max(1, w), Math.Max(1, h),
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                _bufW = w; _bufH = h;
            }
        }

        // ---------- P/Invoke ----------
        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
            int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();

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
