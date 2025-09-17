//using System;
//using System.Runtime.InteropServices;

//namespace NPlug.SimpleGain.UI.Win32;

///// <summary>
///// Win32 host container for the knob view. No label, no margins: the child knob
///// is centered and sized to the largest square that fits the client area.
///// Handles subclassing, flicker-free background, and child layout on resize.
///// </summary>
//internal sealed class FixedGainWindow
//{
//    // ---- Win32 styles / messages -------------------------------------------

//    private const string WC_STATIC = "STATIC";
//    private const int WS_CHILD = 0x40000000;
//    private const int WS_VISIBLE = 0x10000000;
//    private const int WS_CLIPCHILDREN = 0x02000000;

//    private const int WM_ERASEBKGND = 0x0014;
//    private const int WM_SIZE = 0x0005;

//    private const int GWL_WNDPROC = -4;

//    // ---- Handles / state ----------------------------------------------------

//    private IntPtr _parent;
//    private IntPtr _hwnd;     // container window
//    private IntPtr _origWndProc = IntPtr.Zero;

//    private int _width;
//    private int _height;

//    private readonly double _minDb;
//    private readonly double _maxDb;

//    private readonly Func<float> _getNormalized;
//    private readonly Action _beginEdit;
//    private readonly Action<double> _performEdit;
//    private readonly Action _endEdit;

//    private readonly AnalogKnobWindow _knob;
//    private readonly WndProc _wndProcDelegate;

//    // ---- Ctor ---------------------------------------------------------------

//    public FixedGainWindow(
//        int width, int height,
//        double minDb, double maxDb,
//        Func<float> getNormalized,
//        Action beginEdit, Action<double> performEdit, Action endEdit)
//    {
//        _width = width;
//        _height = height;

//        _minDb = minDb;
//        _maxDb = maxDb;

//        _getNormalized = getNormalized;
//        _beginEdit = beginEdit;
//        _performEdit = performEdit;
//        _endEdit = endEdit;

//        _wndProcDelegate = CustomWndProc;

//        _knob = new AnalogKnobWindow(
//            getNormalized: () => _getNormalized(),
//            beginEdit: () => _beginEdit(),
//            performEdit: v => { _performEdit(v); _knob.Refresh(); },
//            endEdit: () => _endEdit(),
//            minDb: _minDb,
//            maxDb: _maxDb
//        );
//    }

//    // ---- Public API ---------------------------------------------------------

//    /// <summary>
//    /// Creates the container as a child of <paramref name="parentHwnd"/> and attaches the knob view.
//    /// </summary>
//    public bool AttachToParent(IntPtr parentHwnd)
//    {
//        if (parentHwnd == IntPtr.Zero) return false;
//        _parent = parentHwnd;

//        _hwnd = CreateWindowEx(
//            0, WC_STATIC, null,
//            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
//            0, 0, _width, _height,
//            _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

//        if (_hwnd == IntPtr.Zero) return false;

//        // Subclass container (for WM_ERASEBKGND/WM_SIZE, etc.)
//        _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

//        // Create knob child and load assets (embedded)
//        _knob.Create(_hwnd, 0, 0, _width, _height);
//        try
//        {
//            _knob.LoadAssetsEmbedded();
//        }
//        catch
//        {
//            // Intentionally swallow to avoid crashing host; the knob will simply paint empty
//            // (You can log here if you have a logging facility.)
//        }

//        LayoutKnob();
//        return true;
//    }

//    /// <summary>
//    /// Destroys the child windows and releases resources.
//    /// </summary>
//    public void Destroy()
//    {
//        _knob.Destroy();
//        _knob.DisposeAssets();

//        if (_hwnd != IntPtr.Zero)
//        {
//            // Restore original WndProc before destroying the window
//            if (_origWndProc != IntPtr.Zero)
//            {
//                SetWindowLongPtr(_hwnd, GWL_WNDPROC, _origWndProc);
//                _origWndProc = IntPtr.Zero;
//            }

//            DestroyWindow(_hwnd);
//            _hwnd = IntPtr.Zero;
//        }

//        _parent = IntPtr.Zero;
//    }

//    /// <summary>
//    /// Sets container bounds; re-lays out the child knob as a centered square.
//    /// </summary>
//    public void SetBounds(int x, int y, int width, int height)
//    {
//        _width = width;
//        _height = height;

//        if (_hwnd != IntPtr.Zero)
//        {
//            MoveWindow(_hwnd, x, y, _width, _height, true);
//            LayoutKnob();
//        }
//    }

//    /// <summary>
//    /// Requests a repaint of the knob (double-buffered inside the knob view).
//    /// </summary>
//    public void RefreshUI() => _knob.Refresh();

//    // ---- Layout -------------------------------------------------------------

//    /// <summary>
//    /// Centers the knob as the largest square that fits inside the container client area.
//    /// </summary>
//    private void LayoutKnob()
//    {
//        if (_hwnd == IntPtr.Zero) return;

//        int D = Math.Min(_width, _height);
//        int offX = (_width - D) / 2;
//        int offY = (_height - D) / 2;

//        _knob.SetBounds(offX, offY, D, D);
//    }

//    // ---- WndProc ------------------------------------------------------------

//    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
//    {
//        switch (msg)
//        {
//            case WM_ERASEBKGND:
//                // Prevent background erase to avoid flicker (child draws everything)
//                return (IntPtr)1;

//            case WM_SIZE:
//                // Keep our cached size in sync even if host resizes us directly
//                int w = (int)(lParam.ToInt64() & 0xFFFF);
//                int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
//                if (w > 0 && h > 0)
//                {
//                    _width = w;
//                    _height = h;
//                    LayoutKnob();
//                }
//                break;
//        }

//        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
//    }

//    // ---- P/Invoke -----------------------------------------------------------

//    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

//    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
//    private static extern IntPtr CreateWindowEx(
//        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
//        int X, int Y, int nWidth, int nHeight,
//        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

//    [DllImport("user32.dll", SetLastError = true)]
//    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

//    [DllImport("user32.dll", SetLastError = true)]
//    private static extern bool DestroyWindow(IntPtr hWnd);

//    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
//    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

//    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
//    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
//}
