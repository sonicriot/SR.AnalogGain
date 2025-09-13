using System;
using System.Runtime.InteropServices;

namespace NPlug.SimpleGain.UI.Win32;

/// <summary>
/// Contenedor Win32 con una etiqueta ("Gain: X dB") y un knob analógico (AOT-friendly).
/// </summary>
internal sealed class FixedGainWindow
{
    // ---- Layout ----
    private const string WC_STATIC = "STATIC";
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;

    private const int LabelPadding = 8;
    private const int LabelHeight = 24;
    private const int GapBelowLabel = 8;

    // Coloca el knob bajo la etiqueta
    private const int KnobX = 40;
    private const int KnobSize = 512;
    private const int MinWidth = KnobX + KnobSize + KnobX; // 40 + 512 + 40 = 592
    private const int MinHeight = LabelPadding + LabelHeight + GapBelowLabel + KnobSize + LabelPadding;
    // 8 + 24 + 8 + 512 + 8 = 560

    private const int WM_ERASEBKGND = 0x0014;

    private int KnobY => LabelPadding + LabelHeight + GapBelowLabel;

    // ---- Handles/estado ----
    private IntPtr _parent;
    private IntPtr _hwnd;   // contenedor
    private IntPtr _label;  // "Gain: X dB"

    private int _width;
    private int _height;

    private readonly double _minDb;
    private readonly double _maxDb;

    private readonly Func<float> _getNormalized;
    private readonly Action _beginEdit;
    private readonly Action<double> _performEdit;
    private readonly Action _endEdit;

    private readonly AnalogKnobWindow _knob;

    // ---- Ctor ----
    public FixedGainWindow(
        int width, int height,
        double minDb, double maxDb,
        Func<float> getNormalized,
        Action beginEdit, Action<double> performEdit, Action endEdit)
    {
        _width = width; _height = height;
        _minDb = minDb; _maxDb = maxDb;
        _getNormalized = getNormalized;
        _beginEdit = beginEdit;
        _performEdit = performEdit;
        _endEdit = endEdit;

        _wndProcDelegate = CustomWndProc;

        _knob = new AnalogKnobWindow(
            getNormalized: () => _getNormalized(),
            beginEdit: () => _beginEdit(),
            performEdit: v => { _performEdit(v); UpdateLabelTextOnly(); },
            endEdit: () => _endEdit()
        );
    }

    string AssetsPath(params string[] parts)
    => Path.Combine(AppContext.BaseDirectory, "Assets", Path.Combine(parts));

    // ---- API pública ----
    public bool AttachToParent(IntPtr parentHwnd)
    {
        _parent = parentHwnd;
        if (_parent == IntPtr.Zero) return false;

        // Contenedor
        _hwnd = CreateWindowEx(0, WC_STATIC, null, WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                               0, 0, _width, _height,
                               _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return false;

        // Etiqueta
        _label = CreateWindowEx(0, WC_STATIC, "",
                                WS_CHILD | WS_VISIBLE,
                                LabelPadding, LabelPadding, _width - 2 * LabelPadding, LabelHeight,
                                _hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        // Knob
        _knob.Create(_hwnd, KnobX, KnobY, KnobSize, KnobSize);
        try
        {
            _knob.LoadAssetsEmbedded();
        }
        catch (Exception ex)
        {
            SetWindowText(_label, "Assets error: " + ex.Message);
        }
        // Subclase del contenedor (por si luego quieres manejar mensajes)
        _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        RefreshLabel();
        return true;
    }

    public void Destroy()
    {
        _knob.Destroy();
        _knob.DisposeAssets();
        if (_label != IntPtr.Zero) DestroyWindow(_label);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _label = _hwnd = IntPtr.Zero;
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        _width = Math.Max(width, MinWidth);
        _height = Math.Max(height, MinHeight);
        MoveWindow(_hwnd, x, y, _width, _height, true);
        MoveWindow(_label, LabelPadding, LabelPadding, _width - 2 * LabelPadding, LabelHeight, true);
        _knob.SetBounds(KnobX, KnobY, KnobSize, KnobSize);
    }

    public void RefreshLabel()
    {
        var norm = _getNormalized();
        var db = _minDb + norm * (_maxDb - _minDb);
        SetWindowText(_label, $"Gain: {db:0.0} dB");
        _knob.Refresh();
    }

    // ---- WndProc contenedor ----
    private IntPtr _origWndProc = IntPtr.Zero;
    private readonly WndProc _wndProcDelegate;
    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_ERASEBKGND) return (IntPtr)1;
        // De momento, no manejamos nada especial en el contenedor
        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    private void UpdateLabelTextOnly()
    {
        var norm = _getNormalized();
        var db = _minDb + norm * (_maxDb - _minDb);
        SetWindowText(_label, $"Gain: {db:0.0} dB");
    }

    // ---- P/Invoke mínima ----
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWL_WNDPROC = -4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);
}
