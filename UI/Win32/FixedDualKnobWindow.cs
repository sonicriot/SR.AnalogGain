using System;
using System.Runtime.InteropServices;

namespace NPlug.SimpleGain.UI.Win32
{
    public enum KnobColor { Red, LightGray }

    /// <summary>
    /// Win32 host container for TWO knobs side by side (left = knob1, right = knob2).
    /// No margins, each knob is centered and sized to the largest square inside its half.
    /// Subclasses the host window to avoid flicker and relayouts children on resize.
    /// </summary>
    internal sealed class FixedDualKnobWindow
    {
        // ---------------- Win32 constants ----------------
        private const string WC_STATIC = "STATIC";
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;

        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_SIZE = 0x0005;

        private const int GWL_WNDPROC = -4;

        // ---------------- Handles / state ----------------
        private IntPtr _parent;
        private IntPtr _hwnd;                 // container window
        private IntPtr _origWndProc = IntPtr.Zero;

        private int _width;
        private int _height;

        private readonly AnalogKnobWindow _knobLeft;
        private readonly AnalogKnobWindow _knobRight;

        private readonly WndProc _wndProcDelegate;

        // ---------------- Public API ----------------

        public readonly struct KnobSpec
        {
            public readonly string Label;
            public readonly double MinDb, MaxDb;
            public readonly KnobColor Color;
            public readonly Func<float> GetNormalized;
            public readonly Action BeginEdit;
            public readonly Action<double> PerformEdit;
            public readonly Action EndEdit;

            public KnobSpec(
                string label,
                double minDb, double maxDb,
                KnobColor color,
                Func<float> getNormalized,
                Action beginEdit,
                Action<double> performEdit,
                Action endEdit)
            {
                Label = label;
                MinDb = minDb;
                MaxDb = maxDb;
                Color = color;
                GetNormalized = getNormalized;
                BeginEdit = beginEdit;
                PerformEdit = performEdit;
                EndEdit = endEdit;
            }
        }

        /// <summary>
        /// Creates the dual container and internally constructs two AnalogKnobWindow children.
        /// </summary>
        public FixedDualKnobWindow(
            int width, int height,
            KnobSpec knob1,     // left (e.g., GAIN)
            KnobSpec knob2)     // right (e.g., OUTPUT)
        {
            _width = width;
            _height = height;

            _wndProcDelegate = CustomWndProc;

            // Create left knob (label/color aware)
            _knobLeft = new AnalogKnobWindow(
                getNormalized: knob1.GetNormalized,
                beginEdit: () => knob1.BeginEdit(),
                performEdit: v => { knob1.PerformEdit(v); _knobLeft.Refresh(); },
                endEdit: () => knob1.EndEdit(),
                minDb: knob1.MinDb,
                maxDb: knob1.MaxDb
                //label: knob1.Label,
                //color: knob1.Color
            );

            // Create right knob (label/color aware)
            _knobRight = new AnalogKnobWindow(
                getNormalized: knob2.GetNormalized,
                beginEdit: () => knob2.BeginEdit(),
                performEdit: v => { knob2.PerformEdit(v); _knobRight.Refresh(); },
                endEdit: () => knob2.EndEdit(),
                minDb: knob2.MinDb,
                maxDb: knob2.MaxDb
                //label: knob2.Label,
                //color: knob2.Color
            );
        }

        /// <summary>
        /// Creates the container as a child of <paramref name="parentHwnd"/> and attaches both knobs.
        /// </summary>
        public bool AttachToParent(IntPtr parentHwnd)
        {
            if (parentHwnd == IntPtr.Zero) return false;
            _parent = parentHwnd;

            _hwnd = CreateWindowEx(
                0, WC_STATIC, null,
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
                0, 0, _width, _height,
                _parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero) return false;

            // Subclass
            _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            // Create both knob children and load assets
            _knobLeft.Create(_hwnd, 0, 0, _width / 2, _height);
            _knobRight.Create(_hwnd, _width / 2, 0, _width / 2, _height);

            try
            {
                _knobLeft.LoadAssetsEmbedded();
                _knobRight.LoadAssetsEmbedded();
            }
            catch
            {
                // swallow to avoid crashing host (assets optional)
            }

            LayoutKnobs();
            return true;
        }

        /// <summary>
        /// Destroys child windows and releases resources.
        /// </summary>
        public void Destroy()
        {
            _knobLeft.Destroy();
            _knobLeft.DisposeAssets();

            _knobRight.Destroy();
            _knobRight.DisposeAssets();

            if (_hwnd != IntPtr.Zero)
            {
                if (_origWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtr(_hwnd, GWL_WNDPROC, _origWndProc);
                    _origWndProc = IntPtr.Zero;
                }
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            _parent = IntPtr.Zero;
        }

        /// <summary>
        /// Sets container bounds; relayouts both knobs in two columns.
        /// </summary>
        public void SetBounds(int x, int y, int width, int height)
        {
            _width = width;
            _height = height;

            if (_hwnd != IntPtr.Zero)
            {
                MoveWindow(_hwnd, x, y, _width, _height, true);
                LayoutKnobs();
            }
        }

        /// <summary>
        /// Requests a repaint for both knobs.
        /// </summary>
        public void RefreshUI()
        {
            _knobLeft.Refresh();
            _knobRight.Refresh();
        }

        // ---------------- Layout ----------------

        /// <summary>
        /// Splits client area in two equal columns and centers a square knob in each.
        /// </summary>
        private void LayoutKnobs()
        {
            if (_hwnd == IntPtr.Zero) return;

            int colW = Math.Max(1, _width / 2);
            int side = Math.Min(colW, _height);
            int offY = (_height - side) / 2;

            // Left knob rectangle (column 1)
            int leftX = (colW - side) / 2;
            _knobLeft.SetBounds(leftX, offY, side, side);

            // Right knob rectangle (column 2)
            int rightX = colW + (colW - side) / 2;
            _knobRight.SetBounds(rightX, offY, side, side);
        }

        // ---------------- WndProc ----------------

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    // Child draws everything; avoid flicker.
                    return (IntPtr)1;

                case WM_SIZE:
                    {
                        int w = (int)(lParam.ToInt64() & 0xFFFF);
                        int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                        if (w > 0 && h > 0)
                        {
                            _width = w;
                            _height = h;
                            LayoutKnobs();
                        }
                        break;
                    }
            }

            return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
        }

        // ---------------- P/Invoke ----------------

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
    }
}
