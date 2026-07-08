#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SR.AnalogGain.UI.Win32
{
    internal sealed class FixedDualKnobWindow
    {
        private const string WC_STATIC = "STATIC";
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;

        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_SIZE = 0x0005;
        private const int WM_PAINT = 0x000F;

        private const int GWL_WNDPROC = -4;

        private IntPtr _parent;
        private IntPtr _hwnd;
        private IntPtr _origWndProc = IntPtr.Zero;

        private int _width = 1024;
        private int _height = 512;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        private readonly AnalogKnobWindow _leftKnob;
        private readonly AnalogKnobWindow _rightKnob;
        private readonly SwitchWindow _satSwitch;
        private readonly SwitchWindow _padSwitch;
        private readonly SwitchWindow _phaseSwitch;
        private readonly SwitchWindow _hpfSwitch;

        private Bitmap? _bg1024x512;

        private const int OuterPad = 18;
        private const int MiddleGap = 24;

        // Stable scale & switches geometry
        private const int SwitchSlots = 4;      // reserve 4 vertical slots
        private const int SwitchHGapPx = 12;    // horizontal padding around switch (inside center column)
        private const int BaseLogicalW = 1024;  // reference width for scaling
        private const int SwitchNativeW = 115;  // png width in px
        private const int SwitchNativeH = 60;   // png height in px
        private const int SwitchGapPx = 10;     // vertical gap between switches (logical, scaled)

        private readonly WndProc _wndProcDelegate;

        public FixedDualKnobWindow(
            Func<float> getNormalizedLeft,
            Action beginLeft, Action<double> performLeft, Action endLeft,
            double minDbLeft, double maxDbLeft,
            Func<float> getNormalizedRight,
            Action beginRight, Action<double> performRight, Action endRight,
            double minDbRight, double maxDbRight,
            Func<bool> getSat,
            Action beginSat, Action<bool> performSat, Action endSat,
            Func<bool> getPad,
            Action beginPad, Action<bool> performPad, Action endPad,
            Func<bool> getPhase,
            Action beginPhase, Action<bool> performPhase, Action endPhase,
            Func<bool> getHpf,
            Action beginHpf, Action<bool> performHpf, Action endHpf
        )
        {
            _wndProcDelegate = CustomWndProc;

            _leftKnob = new AnalogKnobWindow(
                getNormalizedLeft, beginLeft, performLeft, endLeft,
                minDbLeft, maxDbLeft, "GAIN",
                faceResName: "KnobFace512.png",
                topResName: "KnobTop512.png",
                pointerResName: "knob_pointer_sprite_300x300_x73_clockwise263.png");

            _rightKnob = new AnalogKnobWindow(
                getNormalizedRight, beginRight, performRight, endRight,
                minDbRight, maxDbRight, "OUTPUT",
                faceResName: "KnobFace512.png",
                topResName: "KnobTop512_gray.png",
                pointerResName: "knob_pointer_sprite_300x300_x73_clockwise263_gray.png");

            _satSwitch = new SwitchWindow(
                resOff: "Switch-off.png",
                resOn: "Switch-on.png",
                label: "SAT",
                get: getSat,
                begin: beginSat,
                perform: performSat,
                end: endSat);

            _padSwitch = new SwitchWindow(
                resOff: "Switch-off.png",
                resOn: "Switch-on.png",
                label: "PAD",
                get: getPad,
                begin: beginPad,
                perform: performPad,
                end: endPad);

            _phaseSwitch = new SwitchWindow(
                resOff: "Switch-off.png",
                resOn: "Switch-on.png",
                label: "PHASE",
                get: getPhase,
                begin: beginPhase,
                perform: performPhase,
                end: endPhase);

            _hpfSwitch = new SwitchWindow(
                resOff: "Switch-off.png",
                resOn: "Switch-on.png",
                label: "HPF",
                get: getHpf,
                begin: beginHpf,
                perform: performHpf,
                end: endHpf);
        }

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

            _origWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            try { _bg1024x512 = Embedded.LoadBitmap(typeof(FixedDualKnobWindow).Assembly, "Bg1024x512.png"); } catch { }

            _leftKnob.Create(_hwnd, 0, 0, _width / 2, _height);
            _rightKnob.Create(_hwnd, _width / 2, 0, _width / 2, _height);
            _satSwitch.Create(_hwnd, 0, 0, 10, 10);
            _padSwitch.Create(_hwnd, 0, 0, 10, 10);
            _phaseSwitch.Create(_hwnd, 0, 0, 10, 10);
            _hpfSwitch.Create(_hwnd, 0, 0, 10, 10);

            PropagateBackgroundRef();

            _lastWidth = _width;
            _lastHeight = _height;
            LayoutChildren(); // initial layout
            return true;
        }

        public void Destroy()
        {
            _leftKnob.Destroy();
            _rightKnob.Destroy();
            _satSwitch.Destroy();
            _padSwitch.Destroy();
            _phaseSwitch.Destroy();
            _hpfSwitch.Destroy();

            _bg1024x512?.Dispose(); _bg1024x512 = null;

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

        public void SetBounds(int x, int y, int width, int height)
        {
            // Guard: only relayout if size changed
            if (width == _lastWidth && height == _lastHeight)
            {
                if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false);
                return;
            }

            _width = width;
            _height = height;
            _lastWidth = width;
            _lastHeight = height;

            if (_hwnd != IntPtr.Zero)
            {
                MoveWindow(_hwnd, x, y, _width, _height, true);
                LayoutChildren();
                PropagateBackgroundRef();
                InvalidateRect(_hwnd, IntPtr.Zero, false);
            }
        }

        public void RefreshUI() => RefreshChildrenOnly();

        public void RefreshGainKnob() => _leftKnob?.Refresh();
        public void RefreshOutputKnob() => _rightKnob?.Refresh();
        public void RefreshSat() => _satSwitch?.Refresh();
        public void RefreshPad() => _padSwitch?.Refresh();
        public void RefreshPhase() => _phaseSwitch?.Refresh();
        public void RefreshHpf() => _hpfSwitch?.Refresh();

        public void RefreshChildrenOnly()
        {
            _leftKnob?.Refresh();
            _rightKnob?.Refresh();
            _satSwitch?.Refresh();
            _padSwitch?.Refresh();
            _phaseSwitch?.Refresh();
            _hpfSwitch?.Refresh();
        }

        public void RepaintBackground()
        {
            if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, IntPtr.Zero, false);
        }

        private void LayoutChildren()
        {
            if (_hwnd == IntPtr.Zero) return;

            int innerW = Math.Max(1, _width - 2 * OuterPad);
            int innerH = Math.Max(1, _height - 2 * OuterPad);

            // --- Center column ---
            float ui = Math.Max(0.1f, (float)_width / BaseLogicalW);
            int centerW = (int)Math.Round((SwitchNativeW + 2 * SwitchHGapPx) * ui);
            int centerX = (_width - centerW) / 2;

            // Subtract center column + gaps so knob HWNDs never overlap the switch column
            int usableW = Math.Max(1, innerW - centerW - 2 * MiddleGap);
            int side = Math.Min(innerH, usableW / 2);

            // Left & Right knob positions
            int leftX = OuterPad;
            int leftY = OuterPad;
            _leftKnob.SetBounds(leftX, leftY, side, side);

            int rightX = _width - OuterPad - side;
            int rightY = OuterPad;
            _rightKnob.SetBounds(rightX, rightY, side, side);
            int centerY = leftY;
            int centerH = side;

            // Switch size at current scale (NO Min/Clamp with centerW that could feed back)
            int swW = (int)Math.Round(SwitchNativeW * ui);
            int swH = (int)Math.Round(SwitchNativeH * ui);
            int gap = (int)Math.Round(SwitchGapPx * ui);

            int slotPitch = swH + gap;
            int groupH = swH * SwitchSlots + gap * (SwitchSlots - 1);

            // Vertically center a 4-slot group within the column (future-proof)
            int groupTopY = centerY + Math.Max(0, (centerH - groupH) / 2);

            // Center each switch horizontally inside the center column
            int swX = centerX + (centerW - swW) / 2;

            // Slots (top to bottom)
            int slot0Y = groupTopY + 0 * slotPitch; // SAT
            int slot1Y = groupTopY + 1 * slotPitch; // PAD
            int slot2Y = groupTopY + 2 * slotPitch; // PHASE
            int slot3Y = groupTopY + 3 * slotPitch; // HPF

            // Place current switches in the TOP HALF
            _satSwitch?.SetBounds(swX, slot0Y, swW, swH);
            _padSwitch?.SetBounds(swX, slot1Y, swW, swH);
            _phaseSwitch?.SetBounds(swX, slot2Y, swW, swH);
            _hpfSwitch?.SetBounds(swX, slot3Y, swW, swH);

            // Ensure switches are always on top of knob HWNDs
            _satSwitch?.BringToFront();
            _padSwitch?.BringToFront();
            _phaseSwitch?.BringToFront();
            _hpfSwitch?.BringToFront();
        }

        private void PropagateBackgroundRef()
        {
            _leftKnob?.SetContainerBackgroundReference(_bg1024x512, _width, _height);
            _rightKnob?.SetContainerBackgroundReference(_bg1024x512, _width, _height);
            _satSwitch?.SetContainerBackgroundReference(_bg1024x512, _width, _height);
            _padSwitch?.SetContainerBackgroundReference(_bg1024x512, _width, _height);
            _phaseSwitch?.SetContainerBackgroundReference(_bg1024x512, _width, _height);
            _hpfSwitch?.SetContainerBackgroundReference(_bg1024x512, _width, _height);
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    return (IntPtr)1;

                case WM_SIZE:
                    {
                        int w = (int)(lParam.ToInt64() & 0xFFFF);
                        int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                        if (w > 0 && h > 0 && (w != _lastWidth || h != _lastHeight))
                        {
                            _width = w;
                            _height = h;
                            _lastWidth = w;
                            _lastHeight = h;
                            LayoutChildren();
                            PropagateBackgroundRef();
                            InvalidateRect(_hwnd, IntPtr.Zero, false);
                        }
                        return (IntPtr)0;
                    }

                case WM_PAINT:
                    {
                        var ps = new PAINTSTRUCT();
                        var hdc = BeginPaint(_hwnd, out ps);
                        try
                        {
                            using var g = Graphics.FromHdc(hdc);
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                            if (_bg1024x512 != null)
                                g.DrawImage(_bg1024x512, new Rectangle(0, 0, _width, _height));
                            else
                                g.Clear(Color.FromArgb(22, 40, 68));
                        }
                        finally { EndPaint(_hwnd, ref ps); }
                        return (IntPtr)0;
                    }
            }

            return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
            int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

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
#endif
