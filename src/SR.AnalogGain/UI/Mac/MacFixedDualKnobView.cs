#if MACOS
using System.Collections.Concurrent;
using static SR.AnalogGain.UI.Mac.Cocoa;

namespace SR.AnalogGain.UI.Mac
{
    internal sealed class MacFixedDualKnobView : IDisposable
    {
        private IntPtr _parent = IntPtr.Zero;
        private IntPtr _host = IntPtr.Zero;   // root NSView subclass with drawRect
        private int _w = 1024, _h = 512;

        private MacAnalogKnobView _left, _right;
        private MacSwitchView _sat, _pad, _phase, _hpf;
        private IntPtr _bgImage;

        private static bool _ready;
        private static IntPtr _cls;
        private static IntPtr SelDrawRect = sel_registerName("drawRect:");
        private static IntPtr SelIsFlipped = sel_registerName("isFlipped");

        private static readonly ConcurrentDictionary<IntPtr, MacFixedDualKnobView> _instances = new();

        public MacFixedDualKnobView(
            // LEFT (GAIN)
            Func<float> getNormalizedLeft, Action beginLeft, Action<double> performLeft, Action endLeft, double minDbLeft, double maxDbLeft,
            // RIGHT (OUTPUT)
            Func<float> getNormalizedRight, Action beginRight, Action<double> performRight, Action endRight, double minDbRight, double maxDbRight,
            // Switches
            Func<bool> getSat, Action beginSat, Action<bool> performSat, Action endSat,
            Func<bool> getPad, Action beginPad, Action<bool> performPad, Action endPad,
            Func<bool> getPhase, Action beginPhase, Action<bool> performPhase, Action endPhase,
            Func<bool> getHpf, Action beginHpf, Action<bool> performHpf, Action endHpf
        )
        {
            // host view
            _host = Cocoa.CreateView(0, 0, _w, _h);
            objc_msgSend_void(_host, SelSetWantsLayer, (IntPtr)1);
            EnsureClass();
            var bounds = objc_msgSend(_host, SelBounds);
            var v = objc_msgSend(_cls, SelAlloc);
            objc_msgSend(v, SelInitWithFrame, bounds);
            objc_msgSend_void(v, SelSetWantsLayer, (IntPtr)1);
            _host = v;

            _instances[_host] = this;

            // children
            _left = new MacAnalogKnobView(getNormalizedLeft, beginLeft, performLeft, endLeft,
                minDbLeft, maxDbLeft, "GAIN",
                "KnobFace512.png", "KnobTop512.png", "knob_pointer_sprite_300x300_x73_clockwise263.png");

            _right = new MacAnalogKnobView(getNormalizedRight, beginRight, performRight, endRight,
                minDbRight, maxDbRight, "OUTPUT",
                "KnobFace512.png", "KnobTop512_gray.png", "knob_pointer_sprite_300x300_x73_clockwise263_gray.png");

            _sat = new MacSwitchView("Switch-off.png", "Switch-on.png", "SAT", getSat, beginSat, performSat, endSat);
            _pad = new MacSwitchView("Switch-off.png", "Switch-on.png", "PAD", getPad, beginPad, performPad, endPad);
            _phase = new MacSwitchView("Switch-off.png", "Switch-on.png", "PHASE", getPhase, beginPhase, performPhase, endPhase);
            _hpf = new MacSwitchView("Switch-off.png", "Switch-on.png", "HPF", getHpf, beginHpf, performHpf, endHpf);

            AddSubview(_host, _left.Handle);
            AddSubview(_host, _right.Handle);
            AddSubview(_host, _sat.Handle);
            AddSubview(_host, _pad.Handle);
            AddSubview(_host, _phase.Handle);
            AddSubview(_host, _hpf.Handle);

            // background
            try { _bgImage = EmbeddedMac.LoadCGImage(typeof(MacFixedDualKnobView).Assembly, "Bg1024x512.png"); } catch { _bgImage = IntPtr.Zero; }

            LayoutChildren();
        }

        public bool AttachToParent(IntPtr parentNSView)
        {
            if (parentNSView == IntPtr.Zero) return false;
            _parent = parentNSView;
            AddSubview(_parent, _host);
            SetBounds(0, 0, _w, _h);
            Refresh();
            return true;
        }

        public void SetBounds(int x, int y, int w, int h)
        {
            _w = Math.Max(1, w);
            _h = Math.Max(1, h);
            Cocoa.SetFrame(_host, new Cocoa.CGRect(new Cocoa.CGPoint(x, y), new Cocoa.CGSize(_w, _h)));
            LayoutChildren();
            Refresh();
        }

        public void Refresh() => Cocoa.SetNeedsDisplay(_host);
        public void Destroy() { /* noop; host owned by parent */ }

        public void Dispose()
        {
            _left?.Dispose(); _right?.Dispose();
            _sat?.Dispose(); _pad?.Dispose(); _phase?.Dispose(); _hpf?.Dispose();
            if (_bgImage != IntPtr.Zero) CGImageRelease(_bgImage); _bgImage = IntPtr.Zero;
            _instances.TryRemove(_host, out _);
        }

        private static void EnsureClass()
        {
            if (_ready) return;
            _cls = objc_allocateClassPair(NSViewClass, "SRMacFixedDualKnobView", IntPtr.Zero);
            class_addMethod(_cls, SelDrawRect, (DrawRectD)DrawRectImpl, "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
            class_addMethod(_cls, SelIsFlipped, (Func<IntPtr, IntPtr, byte>)((_, __) => (byte)1), "c@:");
            objc_registerClassPair(_cls);
            _ready = true;
        }

        private delegate void DrawRectD(IntPtr self, IntPtr cmd, Cocoa.CGRect dirty);
        private static void DrawRectImpl(IntPtr self, IntPtr _cmd, Cocoa.CGRect dirty)
        {
            var ctx = Cocoa.CurrentCGContext();
            // fallback bg
            Cocoa.CGContextSetRGBFillColor(ctx, 22 / 255.0, 40 / 255.0, 68 / 255.0, 1);
            Cocoa.CGContextFillRect(ctx, dirty);

            // draw background image scaled to fill (keep aspect ~2:1)
            if (_instances.TryGetValue(self, out var owner) && owner._bgImage != IntPtr.Zero)
            {
                // cover mode
                double arImg = 1024.0 / 512.0;
                double arDst = dirty.Size.W / dirty.Size.H;

                double w, h, x, y;
                if (arDst > arImg)
                {
                    w = dirty.Size.W; h = w / arImg;
                }
                else
                {
                    h = dirty.Size.H; w = h * arImg;
                }
                x = dirty.Origin.X + (dirty.Size.W - w) * 0.5;
                y = dirty.Origin.Y + (dirty.Size.H - h) * 0.5;

                Cocoa.CGContextDrawImage(ctx, new Cocoa.CGRect(new Cocoa.CGPoint(x, y), new Cocoa.CGSize(w, h)), owner._bgImage);
            }
        }

        private void LayoutChildren()
        {
            // Mirrors Windows layout: [knob] [switch column] [knob]
            const int OuterPad = 18;
            const int MiddleGap = 24;

            double ui = Math.Max(0.1, _w / 1024.0);

            int leftW = (int)Math.Round((_w - (OuterPad * 2 + MiddleGap * 2)) * 0.4);
            int rightW = leftW;
            int centerW = _w - (OuterPad * 2 + leftW + rightW + MiddleGap * 2);

            int leftX = OuterPad;
            int rightX = _w - OuterPad - rightW;
            int centerX = leftX + leftW + MiddleGap;

            int h = _h;
            _left.SetFrame(leftX, 0, leftW, h);
            _right.SetFrame(rightX, 0, rightW, h);

            // switches column
            int nativeW = 115, nativeH = 60;
            int swW = Math.Min((int)Math.Round(nativeW * ui), centerW - 12);
            int swH = Math.Min((int)Math.Round(nativeH * ui), (int)(h * 0.22));
            int pitch = (int)Math.Round(swH + 10 * ui);
            int groupH = pitch * 4 - (int)Math.Round(10 * ui);
            int topY = Math.Max(0, (h - groupH) / 2);
            int swX = centerX + (centerW - swW) / 2;

            _sat.SetFrame(swX, topY + pitch * 0, swW, swH);
            _pad.SetFrame(swX, topY + pitch * 1, swW, swH);
            _phase.SetFrame(swX, topY + pitch * 2, swW, swH);
            _hpf.SetFrame(swX, topY + pitch * 3, swW, swH);
        }
    }
}
#endif