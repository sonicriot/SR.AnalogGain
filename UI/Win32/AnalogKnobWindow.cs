using System;
using System.Runtime.InteropServices;

namespace NPlug.SimpleGain.UI.Win32;

internal sealed class AnalogKnobWindow
{
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

    public AnalogKnobWindow(Func<float> getNormalized, Action beginEdit, Action<double> performEdit, Action endEdit)
    {
        _getNormalized = getNormalized;
        _beginEdit = beginEdit;
        _performEdit = performEdit;
        _endEdit = endEdit;

        _proc = WndProcImpl;
    }

    public bool Create(IntPtr parent, int x, int y, int w, int h)
    {
        _parent = parent;
        _x = x; _y = y; _w = w; _h = h;

        _hwnd = CreateWindowEx(0, WC_STATIC, null, WS_CHILD | WS_VISIBLE | SS_NOTIFY,
                               _x, _y, _w, _h, _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return false;

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
    }

    public void Refresh() => InvalidateRect(_hwnd, IntPtr.Zero, true);

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
                            // Fondo
                            var bg = CreateSolidBrush(0x00F0F0F0); var oldBrush = SelectObject(hdc, bg);
                            Rectangle(hdc, 0, 0, _w, _h);

                            // Cara del knob
                            var face = CreateSolidBrush(0x00E0E0E0);
                            SelectObject(hdc, face);
                            Ellipse(hdc, CX - Radius, CY - Radius, CX + Radius, CY + Radius);

                            // Borde
                            var penBorder = CreatePen(0, 2, 0x00404040);
                            var oldPen = SelectObject(hdc, penBorder);
                            Arc(hdc, CX - Radius, CY - Radius, CX + Radius, CY + Radius, 0, 0, 0, 0);

                            // Marcas (ticks)
                            var penTick = CreatePen(0, 1, 0x00606060);
                            SelectObject(hdc, penTick);
                            const int ticks = 11; // de -135 a +135 (cada 27°)
                            for (int i = 0; i < ticks; i++)
                            {
                                double tt = (double)i / (ticks - 1); // 0..1
                                double ang = AngleMin + tt * (AngleMax - AngleMin);
                                int x1 = CX + (int)Math.Round((Radius - 6) * Math.Cos(ang));
                                int y1 = CY + (int)Math.Round((Radius - 6) * Math.Sin(ang));
                                int x2 = CX + (int)Math.Round((Radius - 1) * Math.Cos(ang));
                                int y2 = CY + (int)Math.Round((Radius - 1) * Math.Sin(ang));
                                MoveToEx(hdc, x1, y1, IntPtr.Zero);
                                LineTo(hdc, x2, y2);
                            }

                            // Indicador (puntero)
                            double norm = Math.Clamp(_getNormalized(), 0.0f, 1.0f);
                            double a = AngleMin + norm * (AngleMax - AngleMin);
                            var penNeedle = CreatePen(0, 3, 0x002060A0);
                            SelectObject(hdc, penNeedle);
                            int xN = CX + (int)Math.Round((Radius - 10) * Math.Cos(a));
                            int yN = CY + (int)Math.Round((Radius - 10) * Math.Sin(a));
                            MoveToEx(hdc, CX, CY, IntPtr.Zero);
                            LineTo(hdc, xN, yN);

                            // Limpieza
                            SelectObject(hdc, oldPen); DeleteObject(penNeedle);
                            DeleteObject(penTick);
                            DeleteObject(penBorder);
                            SelectObject(hdc, oldBrush); DeleteObject(face);
                            DeleteObject(bg);
                        }
                        finally { EndPaint(_hwnd, ref ps); }
                        return IntPtr.Zero;
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
                            _performEdit(newNorm);
                            InvalidateRect(_hwnd, IntPtr.Zero, true);
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
                        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                        double step = (delta / (double)WHEEL_DELTA) * 0.02; // 2% por notch
                        double n = Math.Clamp(_getNormalized() + step, 0.0, 1.0);
                        _beginEdit(); _performEdit(n); _endEdit();
                        InvalidateRect(_hwnd, IntPtr.Zero, true);
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDBLCLK:
                    {
                        // Reset a 0 dB si usas -60..+12 dB
                        const double minDb = -60.0, maxDb = 12.0;
                        double norm0 = (0.0 - minDb) / (maxDb - minDb); // ≈ 0.8333
                        _beginEdit(); _performEdit(norm0); _endEdit();
                        InvalidateRect(_hwnd, IntPtr.Zero, true);
                        return IntPtr.Zero;
                    }
            }
        }
        catch { /* proteger al host */ }

        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
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
