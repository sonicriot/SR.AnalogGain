#if MACOS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static SR.AnalogGain.UI.Mac.Cocoa;

namespace SR.AnalogGain.UI.Mac
{
    internal sealed class MacAnalogKnobView : IDisposable
    {
        // ---- geometry / behavior matching Windows ----
        const float SweepDeg = 263f;           // total sweep
        const float StartDeg = -SweepDeg / 2;  // start angle relative to "up"
        const int PTR_FRAMES = 73;           // 0..72
        const int PTR_FRAME_SIZE = 300;      // each frame WxH
        // radii (relative to knob radius)
        const double TickOuterR = 0.78;
        const double TickInnerR_Major = 0.68;
        const double TickInnerR_Minor = 0.73;
        const double LabelR = 0.88;

        // assets
        private readonly string _faceRes, _topRes, _pointerRes;
        private IntPtr _face512, _top512, _pointerSheet;

        // parameter wiring
        private readonly Func<float> _getNormalized;
        private readonly Action _beginEdit;
        private readonly Action<double> _performEdit;
        private readonly Action _endEdit;
        private readonly double _minDb, _maxDb;
        private readonly string _paramLabel;

        // view
        public IntPtr Handle { get; private set; }

        // interaction
        private bool _editing;
        private double _normCache;
        private double _lastY;

        // cached CoreText font/color (created lazily)
        private IntPtr _ctFontSmall = IntPtr.Zero;
        private IntPtr _ctFontLabel = IntPtr.Zero;
        private IntPtr _whiteColor = IntPtr.Zero;
        private IntPtr _grayColor = IntPtr.Zero;

        // instance table for bridging draw/mouse callbacks
        private static readonly ConcurrentDictionary<IntPtr, MacAnalogKnobView> _instances = new();

        public MacAnalogKnobView(
            Func<float> getNormalized,
            Action beginEdit,
            Action<double> performEdit,
            Action endEdit,
            double minDb, double maxDb,
            string paramLabel,
            string faceResName,
            string topResName,
            string pointerResName)
        {
            _getNormalized = getNormalized;
            _beginEdit = beginEdit;
            _performEdit = performEdit;
            _endEdit = endEdit;
            _minDb = minDb; _maxDb = maxDb;
            _paramLabel = paramLabel;

            _faceRes = faceResName; _topRes = topResName; _pointerRes = pointerResName;

            Handle = Cocoa.CreateView(0, 0, 300, 300);
            objc_msgSend_void(Handle, SelSetWantsLayer, (IntPtr)1);

            EnsureAssets();
            EnsureClass();
            // replace with subclass that has the draw/mouse methods
            var bounds = objc_msgSend(Handle, SelBounds);
            var newV = objc_msgSend(_cls, SelAlloc);
            objc_msgSend(newV, SelInitWithFrame, bounds);
            objc_msgSend_void(newV, SelSetWantsLayer, (IntPtr)1);
            Handle = newV;

            _instances[Handle] = this;
        }

        public void SetFrame(double x, double y, double w, double h) => Cocoa.SetFrame(Handle, new Cocoa.CGRect(new Cocoa.CGPoint(x, y), new Cocoa.CGSize(w, h)));
        public void Refresh() => Cocoa.SetNeedsDisplay(Handle);

        public void Dispose()
        {
            if (_face512 != IntPtr.Zero) CGImageRelease(_face512); _face512 = IntPtr.Zero;
            if (_top512 != IntPtr.Zero) CGImageRelease(_top512); _top512 = IntPtr.Zero;
            if (_pointerSheet != IntPtr.Zero) CGImageRelease(_pointerSheet); _pointerSheet = IntPtr.Zero;

            if (_ctFontSmall != IntPtr.Zero) CFRelease(_ctFontSmall); _ctFontSmall = IntPtr.Zero;
            if (_ctFontLabel != IntPtr.Zero) CFRelease(_ctFontLabel); _ctFontLabel = IntPtr.Zero;
            if (_whiteColor != IntPtr.Zero) CGColorRelease(_whiteColor); _whiteColor = IntPtr.Zero;
            if (_grayColor != IntPtr.Zero) CGColorRelease(_grayColor); _grayColor = IntPtr.Zero;

            _instances.TryRemove(Handle, out _);
        }

        private void EnsureAssets()
        {
            var asm = typeof(MacAnalogKnobView).Assembly;
            _face512 = _face512 == IntPtr.Zero ? EmbeddedMac.LoadCGImage(asm, _faceRes) : _face512;
            _top512 = _top512 == IntPtr.Zero ? EmbeddedMac.LoadCGImage(asm, _topRes) : _top512;
            _pointerSheet = _pointerSheet == IntPtr.Zero ? EmbeddedMac.LoadCGImage(asm, _pointerRes) : _pointerSheet;
        }

        // ---- drawing helpers ----
        private static int FrameFromNorm(double n) => (int)Math.Round(Math.Clamp(n, 0.0, 1.0) * (PTR_FRAMES - 1));
        private static double QuantizeTo1dB(double n) => Math.Round(Math.Clamp(n, 0.0, 1.0) * (PTR_FRAMES - 1)) / (PTR_FRAMES - 1);
        private static double DegToRad(double d) => d * Math.PI / 180.0;

        private static (double cx, double cy, double r) ComputeCircle(CGRect dirty)
        {
            double w = dirty.Size.W, h = dirty.Size.H;
            double s = Math.Min(w, h);
            double cx = dirty.Origin.X + w * 0.5;
            double cy = dirty.Origin.Y + h * 0.52;    // slight bias down like Windows
            double r = s * 0.42;
            return (cx, cy, r);
        }

        // CoreText utilities
        private void EnsureTextResources()
        {
            if (_whiteColor == IntPtr.Zero) _whiteColor = CGColorCreateGenericRGB(1, 1, 1, 1);
            if (_grayColor == IntPtr.Zero) _grayColor = CGColorCreateGenericRGB(0.8, 0.8, 0.8, 1);

            if (_ctFontSmall == IntPtr.Zero)
            {
                var name = CFStringCreateWithCString(IntPtr.Zero, "Menlo", Cocoa.kCFStringEncodingUTF8);
                _ctFontSmall = CTFontCreateWithName(name, 11, IntPtr.Zero);
                CFRelease(name);
            }
            if (_ctFontLabel == IntPtr.Zero)
            {
                var name = CFStringCreateWithCString(IntPtr.Zero, "Menlo-Bold", Cocoa.kCFStringEncodingUTF8);
                _ctFontLabel = CTFontCreateWithName(name, 13, IntPtr.Zero);
                CFRelease(name);
            }
        }

        private static IntPtr MakeAttrString(string text, IntPtr font, IntPtr color)
        {
            var s = CFStringCreateWithCString(IntPtr.Zero, text, Cocoa.kCFStringEncodingUTF8);
            var dict = CFDictionaryCreateMutable(IntPtr.Zero, 2, IntPtr.Zero, IntPtr.Zero);
            var kFont = CFStringCreateWithCString(IntPtr.Zero, "NSFont", Cocoa.kCFStringEncodingUTF8);
            var kColor = CFStringCreateWithCString(IntPtr.Zero, "NSColor", Cocoa.kCFStringEncodingUTF8);
            CFDictionaryAddValue(dict, kFont, font);
            CFDictionaryAddValue(dict, kColor, color);
            var attr = CFAttributedStringCreate(IntPtr.Zero, s, dict);
            CFRelease(kFont); CFRelease(kColor); CFRelease(s); CFRelease(dict);
            return attr;
        }

        private static void DrawTextCentered(IntPtr ctx, string text, double x, double y, IntPtr font, IntPtr color)
        {
            var attr = MakeAttrString(text, font, color);
            var line = CTLineCreateWithAttributedString(attr);
            // Position baseline
            Cocoa.CGContextSaveGState(ctx);
            Cocoa.CGContextTranslateCTM(ctx, (nfloat)x, (nfloat)y);
            CTLineDraw(line, ctx);
            Cocoa.CGContextRestoreGState(ctx);
            CFRelease(line);
            CFRelease(attr);
        }

        // Sprite slicing
        private static IntPtr SliceFrame(IntPtr sheet, int frameIndex)
        {
            // Frames are laid horizontally 300x300 each.
            var rect = new Cocoa.CGRect(
                new Cocoa.CGPoint(frameIndex * PTR_FRAME_SIZE, 0),
                new Cocoa.CGSize(PTR_FRAME_SIZE, PTR_FRAME_SIZE));
            return CGImageCreateWithImageInRect(sheet, rect);
        }

        // ---- Dynamic subclass and event plumbing ----
        private static bool _classReady;
        private static IntPtr _cls;
        private static IntPtr SelDrawRect = sel_registerName("drawRect:");
        private static IntPtr SelMouseDown = sel_registerName("mouseDown:");
        private static IntPtr SelMouseDragged = sel_registerName("mouseDragged:");
        private static IntPtr SelMouseUp = sel_registerName("mouseUp:");
        private static IntPtr SelScrollWheel = sel_registerName("scrollWheel:");
        private static IntPtr SelIsFlipped = sel_registerName("isFlipped");

        private delegate void DrawRectD(IntPtr self, IntPtr cmd, Cocoa.CGRect dirty);
        private delegate void EventPtr(IntPtr self, IntPtr cmd, IntPtr evt);
        private static void EnsureClass()
        {
            if (_classReady) return;
            _cls = objc_allocateClassPair(NSViewClass, "SRMacAnalogKnobView", IntPtr.Zero);
            class_addMethod(_cls, SelDrawRect, (DrawRectD)DrawRectImpl, "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
            class_addMethod(_cls, SelMouseDown, (EventPtr)MouseDownImpl, "v@:@");
            class_addMethod(_cls, SelMouseDragged, (EventPtr)MouseDraggedImpl, "v@:@");
            class_addMethod(_cls, SelMouseUp, (EventPtr)MouseUpImpl, "v@:@");
            class_addMethod(_cls, SelScrollWheel, (EventPtr)ScrollWheelImpl, "v@:@");
            class_addMethod(_cls, SelIsFlipped, (Func<IntPtr, IntPtr, byte>)((_, __) => (byte)1), "c@:");
            objc_registerClassPair(_cls);
            _classReady = true;
        }

        private static MacAnalogKnobView Get(IntPtr self) => _instances.TryGetValue(self, out var v) ? v : null;

        private static void DrawRectImpl(IntPtr self, IntPtr _cmd, Cocoa.CGRect dirty)
        {
            var me = Get(self);
            if (me == null) return;

            var ctx = Cocoa.CurrentCGContext();
            me.EnsureAssets();
            me.EnsureTextResources();

            // Background face circle
            Cocoa.CGContextSetRGBFillColor(ctx, 22 / 255.0, 40 / 255.0, 68 / 255.0, 1);
            Cocoa.CGContextFillRect(ctx, dirty);

            var (cx, cy, r) = ComputeCircle(dirty);

            // face ring
            Cocoa.CGContextSetRGBStrokeColor(ctx, 0.12, 0.12, 0.12, 1);
            Cocoa.CGContextSetLineWidth(ctx, 3);
            Cocoa.CGContextAddEllipseInRect(ctx, new Cocoa.CGRect(new Cocoa.CGPoint(cx - r, cy - r), new Cocoa.CGSize(2 * r, 2 * r)));
            Cocoa.CGContextStrokePath(ctx);

            // draw face image
            if (me._face512 != IntPtr.Zero)
            {
                double fr = r * 1.05;
                Cocoa.CGContextDrawImage(ctx, new Cocoa.CGRect(new Cocoa.CGPoint(cx - fr, cy - fr), new Cocoa.CGSize(fr * 2, fr * 2)), me._face512);
            }

            // Ticks & labels
            DrawTicksAndLabels(me, ctx, cx, cy, r);

            // Pointer/Top
            double norm = me._getNormalized();
            int frame = FrameFromNorm(norm);

            // draw pointer frame from sprite (under top so it looks embedded)
            if (me._pointerSheet != IntPtr.Zero)
            {
                using (new CGScoped(ctx))
                {
                    Cocoa.CGContextTranslateCTM(ctx, (nfloat)cx, (nfloat)cy);
                    // angle mapping: frame 0 at StartDeg, frame PTR_FRAMES-1 at StartDeg+Sweep
                    double deg = StartDeg + (frame / (double)(PTR_FRAMES - 1)) * SweepDeg;
                    Cocoa.CGContextRotateCTM(ctx, (nfloat)DegToRad(deg - 90)); // align 0deg to pointing up

                    var img = SliceFrame(me._pointerSheet, frame);
                    double pr = r * 0.8;
                    Cocoa.CGContextDrawImage(ctx, new Cocoa.CGRect(new Cocoa.CGPoint(-pr, -pr), new Cocoa.CGSize(pr * 2, pr * 2)), img);
                    CGImageRelease(img);
                }
            }

            // top chrome (rotates with pointer too, like Win path)
            if (me._top512 != IntPtr.Zero)
            {
                using (new CGScoped(ctx))
                {
                    Cocoa.CGContextTranslateCTM(ctx, (nfloat)cx, (nfloat)cy);
                    double deg = StartDeg + (frame / (double)(PTR_FRAMES - 1)) * SweepDeg;
                    Cocoa.CGContextRotateCTM(ctx, (nfloat)DegToRad(deg - 90));
                    double tr = r * 0.92;
                    Cocoa.CGContextDrawImage(ctx, new Cocoa.CGRect(new Cocoa.CGPoint(-tr, -tr), new Cocoa.CGSize(tr * 2, tr * 2)), me._top512);
                }
            }

            // parameter label
            DrawTextCentered(ctx, me._paramLabel, cx - 18, cy + r + 18, me._ctFontLabel, me._whiteColor);
        }

        private static void DrawTicksAndLabels(MacAnalogKnobView me, IntPtr ctx, double cx, double cy, double r)
        {
            // Major tick every 6 dB (~12 frames), minor every 1 dB (~1 frame)
            // We take labels roughly at [-60, -30, -12, 0, +6, +12] within range, clamped to [min,max].
            double total = (me._maxDb - me._minDb);
            if (total <= 0.1) return;

            // Minor ticks
            Cocoa.CGContextSetRGBStrokeColor(ctx, 0.6, 0.6, 0.6, 1);
            Cocoa.CGContextSetLineWidth(ctx, 1);
            for (int i = 0; i <= PTR_FRAMES - 1; i++)
            {
                double t = i / (double)(PTR_FRAMES - 1);
                double deg = StartDeg + t * SweepDeg;
                double a = DegToRad(deg - 90);
                double ro = r * TickOuterR;
                double ri = r * TickInnerR_Minor;
                double x1 = cx + ro * Math.Cos(a);
                double y1 = cy + ro * Math.Sin(a);
                double x2 = cx + ri * Math.Cos(a);
                double y2 = cy + ri * Math.Sin(a);
                Cocoa.CGContextMoveToPoint(ctx, (nfloat)x1, (nfloat)y1);
                Cocoa.CGContextAddLineToPoint(ctx, (nfloat)x2, (nfloat)y2);
                Cocoa.CGContextStrokePath(ctx);
            }

            // Major ticks and labels every ~6 dB
            Cocoa.CGContextSetRGBStrokeColor(ctx, 1, 1, 1, 1);
            Cocoa.CGContextSetLineWidth(ctx, 2);

            EnsureLabelResources(me);
            int stepFrames = 12; // ~6 dB per detent chunk across 72 steps => label cadence
            for (int i = 0; i <= PTR_FRAMES - 1; i += stepFrames)
            {
                double t = i / (double)(PTR_FRAMES - 1);
                double deg = StartDeg + t * SweepDeg;
                double a = DegToRad(deg - 90);

                // major tick
                double ro = r * TickOuterR;
                double ri = r * TickInnerR_Major;
                double x1 = cx + ro * Math.Cos(a);
                double y1 = cy + ro * Math.Sin(a);
                double x2 = cx + ri * Math.Cos(a);
                double y2 = cy + ri * Math.Sin(a);
                Cocoa.CGContextMoveToPoint(ctx, (nfloat)x1, (nfloat)y1);
                Cocoa.CGContextAddLineToPoint(ctx, (nfloat)x2, (nfloat)y2);
                Cocoa.CGContextStrokePath(ctx);

                // label (db)
                double db = me._minDb + t * (me._maxDb - me._minDb);
                db = Math.Round(db); // whole dB
                string lab = db >= 0 ? $"+{db}" : db.ToString("0");
                double lr = r * LabelR;
                double lx = cx + lr * Math.Cos(a) - 10;
                double ly = cy + lr * Math.Sin(a) + 4;
                DrawTextCentered(ctx, lab, lx, ly, me._ctFontSmall, me._grayColor);
            }
        }

        private static void EnsureLabelResources(MacAnalogKnobView me) => me.EnsureTextResources();

        private static void MouseDownImpl(IntPtr self, IntPtr _cmd, IntPtr evt)
        {
            var me = Get(self);
            if (me == null) return;
            me._editing = true;
            me._normCache = me._getNormalized();
            me._lastY = GetEventYInView(evt, self);
            me._beginEdit();
        }

        private static void MouseDraggedImpl(IntPtr self, IntPtr _cmd, IntPtr evt)
        {
            var me = Get(self);
            if (me == null || !me._editing) return;
            double y = GetEventYInView(evt, self);
            double dy = (me._lastY - y);
            me._lastY = y;

            double delta = dy * 0.005; // sensitivity
            me._normCache = Math.Clamp(me._normCache + delta, 0.0, 1.0);
            me._performEdit(QuantizeTo1dB(me._normCache));
            Cocoa.SetNeedsDisplay(self);
        }

        private static void MouseUpImpl(IntPtr self, IntPtr _cmd, IntPtr evt)
        {
            var me = Get(self);
            if (me == null) return;
            if (me._editing)
            {
                me._editing = false;
                me._endEdit();
            }
        }

        private static void ScrollWheelImpl(IntPtr self, IntPtr _cmd, IntPtr evt)
        {
            var me = Get(self);
            if (me == null) return;
            double dy = GetScrollDeltaY(evt);
            double step = 1.0 / (PTR_FRAMES - 1);
            double n = Math.Clamp(me._getNormalized() + Math.Sign(-dy) * step, 0.0, 1.0);
            me._beginEdit();
            me._performEdit(QuantizeTo1dB(n));
            me._endEdit();
            Cocoa.SetNeedsDisplay(self);
        }

        private static double GetEventYInView(IntPtr evt, IntPtr view)
        {
            var winPt = objc_msgSend(evt, SelLocationInWindow);
            var convSel = SelConvertPointFromView;
            var pInView = objc_msgSend(view, convSel, winPt);
            var pt = Marshal.PtrToStructure<Cocoa.CGPoint>(pInView);
            return pt.Y;
        }
        private static double GetScrollDeltaY(IntPtr evt)
        {
            var ret = objc_msgSend(evt, SelScrollingDeltaY);
            return BitConverter.Int64BitsToDouble(ret.ToInt64());
        }

        private sealed class CGScoped : IDisposable
        {
            private readonly IntPtr _ctx;
            public CGScoped(IntPtr ctx) { _ctx = ctx; Cocoa.CGContextSaveGState(ctx); }
            public void Dispose() { Cocoa.CGContextRestoreGState(_ctx); }
        }
    }
}
#endif