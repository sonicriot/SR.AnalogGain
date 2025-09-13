using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace NPlug.SimpleGain.UI.Win32;

internal sealed class AnalogKnobWindow
{
    // --- Assets ---
    private Bitmap? _bg512;
    private Bitmap? _pointerSheet; // 73 frames horizontales (300x300 c/u)
    private Bitmap? _top512;

    private Bitmap? _backBuffer;
    private int _bufW, _bufH;

    private readonly double _minDb;
    private readonly double _maxDb;

    // --- Constantes de assets originales ---
    private const int BG_PX = 512;
    private const int TOP_PX = 512;
    private const int PTR_FRAME_PX = 300;
    private const int PTR_FRAMES = 73; // -60..+12 → 73 estados
    private const int PTR_STEPS = PTR_FRAMES - 1; // 72

    private int _dpi = 96;
    private float DpiScale => _dpi / 96f;

    // Estilos/Win32
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
    private const int WM_DPICHANGED = 0x02E0;

    private const int WHEEL_DELTA = 120;

    // Geometría básica
    private int _x, _y, _w, _h;
    private int CX => _w / 2;
    private int CY => _h / 2;
    private int Radius => Math.Max(1, Math.Min(_w, _h) / 2 - 4);

    // Ángulos (radianes): -135° a +135°
    private const double Deg2Rad = Math.PI / 180.0;
    private const double AngleMin = -135 * Deg2Rad;
    private const double AngleMax = +135 * Deg2Rad;

    // Interacción
    private bool _editing;
    private int _dragStartY;
    private double _dragStartNorm;

    // Callbacks
    private readonly Func<float> _getNormalized;
    private readonly Action _beginEdit;
    private readonly Action<double> _performEdit;
    private readonly Action _endEdit;

    // Win32
    private IntPtr _parent;
    private IntPtr _hwnd;
    private IntPtr _origWndProc = IntPtr.Zero;
    private readonly WndProc _proc;

    public AnalogKnobWindow(Func<float> getNormalized, Action beginEdit, Action<double> performEdit, Action endEdit,
                        double minDb, double maxDb)
    {
        _getNormalized = getNormalized;
        _beginEdit = beginEdit;
        _performEdit = performEdit;
        _endEdit = endEdit;
        _minDb = minDb;
        _maxDb = maxDb;

        _proc = WndProcImpl;
    }

    // Para liberar
    public void DisposeAssets()
    {
        _bg512?.Dispose(); _bg512 = null;
        _pointerSheet?.Dispose(); _pointerSheet = null;
        _top512?.Dispose(); _top512 = null;
    }

    public void LoadAssetsEmbedded()
    {
        DisposeAssets();
        var asm = typeof(AnalogKnobWindow).Assembly;

        _bg512 = Embedded.LoadBitmap(asm, "KnobBg512.png");
        _pointerSheet = Embedded.LoadBitmap(asm, "knob_pointer_sprite_300x300_x73_clockwise263.png");
        _top512 = Embedded.LoadBitmap(asm, "KnobTop512.png");
    }

    public bool Create(IntPtr parent, int x, int y, int w, int h)
    {
        _parent = parent;
        _x = x; _y = y; _w = w; _h = h;

        _hwnd = CreateWindowEx(0, WC_STATIC, null, WS_CHILD | WS_VISIBLE | SS_NOTIFY,
                               _x, _y, _w, _h, _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return false;

        _dpi = GetDpiForWindow(_hwnd);

        _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
        InvalidateRect(_hwnd, IntPtr.Zero, true);
        return true;
    }

    public void Destroy()
    {
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    public void SetBounds(int x, int y, int w, int h)
    {
        _x = x; _y = y; _w = w; _h = h;
        MoveWindow(_hwnd, _x, _y, _w, _h, true);

        // recreate buffer si cambia el tamaño
        if (_w != _bufW || _h != _bufH)
        {
            _backBuffer?.Dispose();
            _backBuffer = new Bitmap(Math.Max(1, _w), Math.Max(1, _h), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _bufW = _w; _bufH = _h;
        }
    }

    public void Refresh() => InvalidateRect(_hwnd, IntPtr.Zero, false);

    // ---- Pintado / interacción ----
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
                            if (_backBuffer == null || _backBuffer.Width != _w || _backBuffer.Height != _h)
                            {
                                _backBuffer?.Dispose();
                                _backBuffer = new Bitmap(Math.Max(1, _w), Math.Max(1, _h), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                                _bufW = _w; _bufH = _h;
                            }

                            using (var gb = Graphics.FromImage(_backBuffer))
                            {
                                float scale = gb.DpiX / 96f;
                                int Ddip = 512;
                                int Dpx = (int)Math.Round(Ddip * scale);

                                gb.SmoothingMode = SmoothingMode.HighQuality;
                                gb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                gb.CompositingQuality = CompositingQuality.HighQuality;

                                // Limpia el buffer con transparente (no fondo gris)
                                gb.Clear(Color.Transparent);

                                if (_bg512 != null && _pointerSheet != null && _top512 != null)
                                {
                                    int D = Math.Min(Dpx, Math.Min(_w, _h));
                                    float s = D / 512f;
                                    
                                    int offX = (_w - D) / 2, offY = (_h - D) / 2;

                                    // fondo
                                    gb.DrawImage(_bg512, new Rectangle(offX, offY, D, D));

                                    // puntero
                                    double norm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
                                    int frame = (int)Math.Round(norm * (PTR_STEPS)); // 0..72

                                    var src = new Rectangle(frame * PTR_FRAME_PX, 0, PTR_FRAME_PX, PTR_FRAME_PX);
                                    int ptrSize = (int)Math.Round(PTR_FRAME_PX * s);
                                    int cx = offX + D / 2, cy = offY + D / 2;
                                    var dst = new Rectangle(cx - ptrSize / 2, cy - ptrSize / 2, ptrSize, ptrSize);

                                    gb.DrawImage(_pointerSheet, dst, src, GraphicsUnit.Pixel);

                                    // top
                                    gb.DrawImage(_top512, new Rectangle(offX, offY, D, D));

                                    // --- Overlay de dB (estilo traza) ---
                                    double db = _minDb + norm * (_maxDb - _minDb);

                                    string txt = $"{db:+0.0;-0.0;0.0} dB"; // +0.0 para positivos

                                    gb.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                                    float fontPx = 16f * (gb.DpiX / 96f); // un poco más grande que la traza
                                    using var f = new Font(FontFamily.GenericSansSerif, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);

                                    // sombreado para legibilidad
                                    var pt = new PointF(offX + 12, offY + 12);
                                    using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                                        gb.DrawString(txt, f, shadow, new PointF(pt.X + 1, pt.Y + 1));

                                    gb.DrawString(txt, f, Brushes.White, pt);
                                }
                            }

                            using (var g = Graphics.FromHdc(hdc))
                            {
                                // copia del back buffer a pantalla en un solo blit (sin flicker)
                                g.DrawImageUnscaled(_backBuffer, 0, 0);
                            }
                        }
                        finally { EndPaint(_hwnd, ref ps); }
                        return (IntPtr)0;
                    }


                case WM_LBUTTONDOWN:
                    {
                        SetCapture(_hwnd);
                        _editing = true;
                        _beginEdit();
                        _dragStartY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        _dragStartNorm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
                        return IntPtr.Zero;
                    }

                case WM_MOUSEMOVE:
                    {
                        if (_editing)
                        {
                            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                            int dy = y - _dragStartY;

                            // Arrastre vertical: 200 px de recorrido ≈ todo el rango
                            const double sens = 1.0 / 200.0;
                            double newNorm = Math.Clamp(_dragStartNorm - dy * sens, 0.0, 1.0);
                            newNorm = QuantizeTo1dB(newNorm);
                            _performEdit(newNorm);
                            InvalidateRect(_hwnd, IntPtr.Zero, false);
                        }
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONUP:
                    {
                        if (_editing)
                        {
                            _editing = false;
                            _endEdit();
                            ReleaseCapture();
                        }
                        return IntPtr.Zero;
                    }

                case WM_MOUSEWHEEL:
                    {
                        int ticks = (short)((wParam.ToInt64() >> 16) & 0xFFFF) / WHEEL_DELTA;
                        double n = _getNormalized() + ticks * (1.0 / PTR_STEPS); // 1 dB por notch
                        n = QuantizeTo1dB(n);
                        _beginEdit(); _performEdit(n); _endEdit();
                        InvalidateRect(_hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDBLCLK:
                    {
                        // Reset a 0 dB si usas -60..+12 dB
                        const double minDb = -60.0, maxDb = 12.0;
                        double norm0 = (0.0 - minDb) / (maxDb - minDb); // ≈ 0.8333
                        norm0 = QuantizeTo1dB(norm0);
                        _beginEdit(); _performEdit(norm0); _endEdit();
                        InvalidateRect(_hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                case WM_ERASEBKGND:
                    return (IntPtr)1;
                case WM_DPICHANGED:
                    _dpi = (int)(wParam.ToInt64() & 0xFFFF);
                    // re-layout si hace falta, invalidar sin borrar fondo:
                    InvalidateRect(_hwnd, IntPtr.Zero, false);
                    return (IntPtr)0;
            }
        }
        catch { /* proteger al host */ }

        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    private static double QuantizeTo1dB(double n)
    {
        n = Math.Clamp(n, 0.0, 1.0);
        return Math.Round(n * PTR_STEPS) / PTR_STEPS; // 1/72
    }

    // --- P/Invoke ---
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

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

    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, int crColor);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int crColor);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(IntPtr hdc, int X, int Y, IntPtr lppt);
    [DllImport("gdi32.dll")] private static extern bool LineTo(IntPtr hdc, int nXEnd, int nYEnd);
    [DllImport("gdi32.dll")] private static extern bool Arc(IntPtr hdc, int left, int top, int right, int bottom, int r1, int r2, int r3, int r4);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] static extern int GetDpiForWindow(IntPtr hWnd);

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
