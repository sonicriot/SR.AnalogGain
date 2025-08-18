using System;
using System.Runtime.InteropServices;

namespace NPlug.SimpleGain.UI.Win32;

/// <summary>
/// Minimal Win32 child window with a label and a custom vertical fader (AOT-friendly).
/// </summary>
internal sealed class FixedGainWindow
{
    // ---------- Layout ----------
    private const string WC_STATIC = "STATIC";
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int SS_NOTIFY = 0x0100;

    private const int LabelPadding = 8;
    private const int LabelHeight = 24;
    private const int GapBelowLabel = 8;
    private const int KnobHalf = 6; // pixels

    // Fader geometry (relative to container)
    private const int FaderLeft = 40;
    private const int FaderWidth = 40;
    private const int FaderHeight = 100;
    private int FaderTopDyn => LabelPadding + LabelHeight + GapBelowLabel;

    // ---------- Handles / state ----------
    private IntPtr _parent;
    private IntPtr _hwnd;   // container
    private IntPtr _label;  // "Gain: X dB"
    private IntPtr _fader;  // custom painted control

    private int _width;
    private int _height;

    private readonly double _minDb;
    private readonly double _maxDb;

    private readonly Func<float> _getNormalized;
    private readonly Action _beginEdit;
    private readonly Action<double> _performEdit; // normalized [0..1]
    private readonly Action _endEdit;

    private bool _editing;

    // ---------- Constructor ----------
    public FixedGainWindow(
        int width, int height,
        double minDb, double maxDb,
        Func<float> getNormalized,
        Action beginEdit, Action<double> performEdit, Action endEdit)
    {
        _width = width; _height = height;
        _minDb = minDb; _maxDb = maxDb;
        _getNormalized = getNormalized;
        _beginEdit = beginEdit; _performEdit = performEdit; _endEdit = endEdit;

        _wndProcDelegate = CustomWndProc;
        _faderWndProcDelegate = CustomFaderWndProc;
    }

    // ---------- Public API ----------
    public bool AttachToParent(IntPtr parentHwnd)
    {
        _parent = parentHwnd;
        if (_parent == IntPtr.Zero) return false;

        // Container
        _hwnd = CreateWindowEx(0, WC_STATIC, null, WS_CHILD | WS_VISIBLE,
                               0, 0, _width, _height,
                               _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return false;

        // Label
        _label = CreateWindowEx(0, WC_STATIC, "",
                                WS_CHILD | WS_VISIBLE,
                                LabelPadding, LabelPadding, _width - 2 * LabelPadding, LabelHeight,
                                _hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        // Fader (custom)
        _fader = CreateWindowEx(0, WC_STATIC, null,
                                WS_CHILD | WS_VISIBLE | SS_NOTIFY,
                                FaderLeft, FaderTopDyn, FaderWidth, FaderHeight,
                                _hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        // Subclass container + fader
        _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        _origFaderWndProc = SetWindowLongPtr(_fader, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_faderWndProcDelegate));

        // First paint
        RefreshLabel();
        MoveWindow(_fader, FaderLeft, FaderTopDyn, FaderWidth, FaderHeight, true);
        return true;
    }

    public void Destroy()
    {
        if (_fader != IntPtr.Zero) DestroyWindow(_fader);
        if (_label != IntPtr.Zero) DestroyWindow(_label);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _fader = _label = _hwnd = IntPtr.Zero;
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        _width = width; _height = height;
        MoveWindow(_hwnd, x, y, _width, _height, true);
        MoveWindow(_label, LabelPadding, LabelPadding, _width - 2 * LabelPadding, LabelHeight, true);
        MoveWindow(_fader, FaderLeft, FaderTopDyn, FaderWidth, FaderHeight, true);
    }

    public void RefreshLabel()
    {
        UpdateLabelTextOnly();
        if (_fader != IntPtr.Zero) InvalidateRect(_fader, IntPtr.Zero, true);
    }

    // ---------- Math helpers ----------
    private void UpdateLabelTextOnly()
    {
        var norm = _getNormalized();
        var db = _minDb + norm * (_maxDb - _minDb);
        SetWindowText(_label, $"Gain: {db:0.0} dB");
    }

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
    private double NormToY(double norm)
    {
        // norm=1 -> top (KnobHalf), norm=0 -> bottom (FaderHeight - KnobHalf)
        double travel = FaderHeight - 2 * KnobHalf;
        return KnobHalf + (1.0 - norm) * travel;
    }

    private double YToNorm(int y)
    {
        // clamp to keep knob fully inside
        int yy = Math.Clamp(y, KnobHalf, FaderHeight - KnobHalf);
        double travel = FaderHeight - 2 * KnobHalf;
        double t = (yy - KnobHalf) / travel;  // 0 at top, 1 at bottom
        return 1.0 - t;                       // convert to norm (1 top, 0 bottom)
    }

    // ---------- WndProcs ----------
    private IntPtr _origWndProc = IntPtr.Zero;
    private readonly WndProc _wndProcDelegate;

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Container has nothing special to handle for now.
        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    private IntPtr _origFaderWndProc = IntPtr.Zero;
    private readonly WndProc _faderWndProcDelegate;

    private IntPtr CustomFaderWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_PAINT:
                    {
                        var ps = new PAINTSTRUCT();
                        var hdc = BeginPaint(_fader, out ps);
                        try
                        {
                            // Background
                            var bg = CreateSolidBrush(0x00F0F0F0); var oldBrush = SelectObject(hdc, bg);
                            Rectangle(hdc, 0, 0, FaderWidth, FaderHeight);

                            // Slot line
                            var pen = CreatePen(0, 2, 0x00404040); var oldPen = SelectObject(hdc, pen);
                            MoveToEx(hdc, FaderWidth / 2, 2, IntPtr.Zero);
                            LineTo(hdc, FaderWidth / 2, FaderHeight - 2);

                            // Knob (8 px tall)
                            UpdateLabelTextOnly();
                            var norm = _getNormalized();
                            int y = (int)Math.Round(NormToY(norm));
                            y = Math.Clamp(y, KnobHalf, FaderHeight - KnobHalf); // extra safety
                            var knob = CreateSolidBrush(0x002060A0);
                            SelectObject(hdc, knob);
                            Rectangle(hdc, 4, y - KnobHalf, FaderWidth - 4, y + KnobHalf);

                            // Cleanup
                            SelectObject(hdc, oldPen); DeleteObject(pen);
                            SelectObject(hdc, oldBrush); DeleteObject(bg);
                            DeleteObject(knob);
                        }
                        finally { EndPaint(_fader, ref ps); }
                        return IntPtr.Zero;
                    }

                case WM_LBUTTONDOWN:
                    {
                        SetCapture(_fader);
                        _editing = true;
                        _beginEdit();

                        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        _performEdit(Clamp01(YToNorm(y)));
                        RefreshLabel();
                        return IntPtr.Zero;
                    }

                case WM_MOUSEMOVE:
                    {
                        if (_editing)
                        {
                            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                            _performEdit(Clamp01(YToNorm(y)));
                            RefreshLabel();
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
            }
        }
        catch
        {
            // swallow to protect the host
        }

        return CallWindowProc(_origFaderWndProc, hWnd, msg, wParam, lParam);
    }

    // ---------- P/Invoke ----------
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWL_WNDPROC = -4;
    private const int WM_PAINT = 0x000F;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

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
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(IntPtr hdc, int X, int Y, IntPtr lppt);
    [DllImport("gdi32.dll")] private static extern bool LineTo(IntPtr hdc, int nXEnd, int nYEnd);
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
