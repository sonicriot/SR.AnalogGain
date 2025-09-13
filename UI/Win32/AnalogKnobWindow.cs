using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NPlug.SimpleGain.UI.Win32;

/// <summary>
/// Win32 child window that renders a layered knob UI:
/// background (512x512) → pointer sprite (73 frames, 300x300) → top overlay (512x512),
/// with a dB overlay text. Double-buffered, DPI-aware, and quantized to 1 dB steps.
/// </summary>
internal sealed class AnalogKnobWindow
{
    // ---- Embedded assets ----------------------------------------------------

    private Bitmap? _bg512;         // 512x512 background
    private Bitmap? _pointerSheet;  // 73 horizontal frames, each 300x300
    private Bitmap? _top512;        // 512x512 top overlay (ring/highlights)

    // ---- Back buffer --------------------------------------------------------

    private Bitmap? _backBuffer;
    private int _bufW, _bufH;

    // ---- Parameter range ----------------------------------------------------

    private readonly double _minDb;
    private readonly double _maxDb;

    // ---- Design constants ---------------------------------------------------

    private const int BG_PX = 512;
    private const int TOP_PX = 512;
    private const int PTR_FRAME_PX = 300;
    private const int PTR_FRAMES = 73;           // -60..+12 => 73 steps (1 dB per frame)
    private const int PTR_STEPS = PTR_FRAMES - 1; // 72 (index 0..72)

    // ---- DPI ----------------------------------------------------------------

    private int _dpi = 96;
    private float DpiScale => _dpi / 96f;

    // ---- Win32 --------------------------------------------------------------

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

    // ---- Geometry / state ---------------------------------------------------

    private int _x, _y, _w, _h;

    // Interaction (drag)
    private bool _editing;
    private int _dragStartY;
    private double _dragStartNorm;

    // Callbacks
    private readonly Func<float> _getNormalized;
    private readonly Action _beginEdit;
    private readonly Action<double> _performEdit;
    private readonly Action _endEdit;

    // Window handles
    private IntPtr _parent;
    private IntPtr _hwnd;
    private IntPtr _origWndProc = IntPtr.Zero;
    private readonly WndProc _proc;

    // ---- Ctor ---------------------------------------------------------------

    public AnalogKnobWindow(
        Func<float> getNormalized,
        Action beginEdit,
        Action<double> performEdit,
        Action endEdit,
        double minDb,
        double maxDb)
    {
        _getNormalized = getNormalized;
        _beginEdit = beginEdit;
        _performEdit = performEdit;
        _endEdit = endEdit;

        _minDb = minDb;
        _maxDb = maxDb;

        _proc = WndProcImpl;
    }

    // ---- Asset lifetime -----------------------------------------------------

    public void LoadAssetsEmbedded()
    {
        DisposeAssets();

        var asm = typeof(AnalogKnobWindow).Assembly;
        _bg512 = Embedded.LoadBitmap(asm, "KnobBg512.png");
        _pointerSheet = Embedded.LoadBitmap(asm, "knob_pointer_sprite_300x300_x73_clockwise263.png");
        _top512 = Embedded.LoadBitmap(asm, "KnobTop512.png");
    }

    public void DisposeAssets()
    {
        _bg512?.Dispose(); _bg512 = null;
        _pointerSheet?.Dispose(); _pointerSheet = null;
        _top512?.Dispose(); _top512 = null;
    }

    // ---- Window lifetime ----------------------------------------------------

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

        _dpi = GetDpiForWindow(_hwnd); // must be after CreateWindowEx

        _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
        InvalidateRect(_hwnd, IntPtr.Zero, true);
        return true;
    }

    public void Destroy()
    {
        // Restore original WndProc before destroy (not strictly required, but good hygiene)
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

        _backBuffer?.Dispose();
        _backBuffer = null;
        _bufW = _bufH = 0;
    }

    public void SetBounds(int x, int y, int w, int h)
    {
        _x = x; _y = y; _w = w; _h = h;

        if (_hwnd != IntPtr.Zero)
            MoveWindow(_hwnd, _x, _y, _w, _h, true);

        EnsureBackBuffer(_w, _h);
    }

    public void Refresh()
    {
        if (_hwnd != IntPtr.Zero)
            InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    // ---- WndProc / rendering / input ---------------------------------------

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
                                // High-quality compositing
                                gb.SmoothingMode = SmoothingMode.HighQuality;
                                gb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                gb.CompositingQuality = CompositingQuality.HighQuality;
                                gb.CompositingMode = CompositingMode.SourceOver;

                                // Clear backbuffer with transparent pixels
                                gb.Clear(Color.Transparent);

                                RenderKnob(gb);
                            }

                            using (var g = Graphics.FromHdc(hdc))
                            {
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

                            // Vertical drag: ~200 px for full range
                            const double pixelsForFullRange = 200.0;
                            double newNorm = Math.Clamp(_dragStartNorm - dy / pixelsForFullRange, 0.0, 1.0);
                            newNorm = QuantizeTo1dB(newNorm);
                            _performEdit(newNorm);
                            Refresh();
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
                        // 1 dB per notch
                        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                        int ticks = delta / WHEEL_DELTA;
                        double n = _getNormalized() + ticks * (1.0 / PTR_STEPS);
                        n = QuantizeTo1dB(n);
                        _beginEdit(); _performEdit(n); _endEdit();
                        Refresh();
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDBLCLK:
                    {
                        // Reset to 0 dB
                        double norm0 = (0.0 - _minDb) / (_maxDb - _minDb); // 60/72 for -60..+12
                        norm0 = QuantizeTo1dB(norm0);
                        _beginEdit(); _performEdit(norm0); _endEdit();
                        Refresh();
                        return IntPtr.Zero;
                    }

                case WM_ERASEBKGND:
                    // Avoid background erase to prevent flicker (we draw the full surface)
                    return (IntPtr)1;

                case WM_DPICHANGED:
                    {
                        // LOWORD contains the new DPI (X); HIWORD is Y (usually equal)
                        _dpi = (int)(wParam.ToInt64() & 0xFFFF);
                        Refresh();
                        return (IntPtr)0;
                    }
            }
        }
        catch
        {
            // Swallow exceptions to keep host stable
        }

        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    // ---- Rendering helpers --------------------------------------------------

    private void RenderKnob(Graphics gb)
    {
        // If any asset is missing, nothing to render
        if (_bg512 == null || _pointerSheet == null || _top512 == null) return;

        // DIP → px using the Graphics DPI (more reliable than cached _dpi in some hosts)
        float scalePxPerDip = gb.DpiX / 96f;

        // Target size for background/top in px (512 DIP → 512 * scale px)
        int targetPx = (int)Math.Round(BG_PX * scalePxPerDip);

        // Final side length must fit the window (square centered)
        int side = Math.Min(targetPx, Math.Min(_w, _h));
        float s = side / (float)BG_PX;

        int offX = (_w - side) / 2;
        int offY = (_h - side) / 2;

        // 1) Background
        gb.DrawImage(_bg512, new Rectangle(offX, offY, side, side));

        // 2) Pointer frame (map normalized value → frame index 0..72)
        double norm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
        int frame = (int)Math.Round(norm * PTR_STEPS);
        frame = Math.Max(0, Math.Min(PTR_STEPS, frame));

        int srcX = frame * PTR_FRAME_PX;
        var src = new Rectangle(srcX, 0, PTR_FRAME_PX, PTR_FRAME_PX);

        int ptrSize = (int)Math.Round(PTR_FRAME_PX * s);
        int cx = offX + side / 2;
        int cy = offY + side / 2;
        var dst = new Rectangle(cx - ptrSize / 2, cy - ptrSize / 2, ptrSize, ptrSize);

        gb.DrawImage(_pointerSheet, dst, src, GraphicsUnit.Pixel);

        // 3) Top overlay
        gb.DrawImage(_top512, new Rectangle(offX, offY, side, side));

        // 4) dB overlay text
        DrawDbOverlay(gb, offX, offY, norm);
    }

    private void DrawDbOverlay(Graphics gb, int offX, int offY, double norm)
    {
        double db = _minDb + norm * (_maxDb - _minDb);
        string txt = $"{db:+0.0;-0.0;0.0} dB";

        gb.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Use pixel font size to avoid double-scaling under different DPI
        float fontPx = 16f * (gb.DpiX / 96f);
        using var f = new Font(FontFamily.GenericSansSerif, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);

        var pt = new PointF(offX + 12, offY + 12);

        using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            gb.DrawString(txt, f, shadow, new PointF(pt.X + 1, pt.Y + 1));

        // Do NOT dispose Brushes.White (it's a shared singleton)
        gb.DrawString(txt, f, Brushes.White, pt);
    }

    // ---- Utilities ----------------------------------------------------------

    private static double QuantizeTo1dB(double n)
    {
        n = Math.Clamp(n, 0.0, 1.0);
        return Math.Round(n * PTR_STEPS) / PTR_STEPS; // 1/72
    }

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

    // ---- P/Invoke -----------------------------------------------------------

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
    [DllImport("user32.dll")] private static extern int GetDpiForWindow(IntPtr hWnd);

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
