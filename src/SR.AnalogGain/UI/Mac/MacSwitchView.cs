#if MACOS
using System.Collections.Concurrent;
using static SR.AnalogGain.UI.Mac.Cocoa;

namespace SR.AnalogGain.UI.Mac
{
    internal sealed class MacSwitchView : IDisposable
    {
        private readonly string _resOff, _resOn, _label;
        private IntPtr _imgOff, _imgOn;

        // param wiring
        private readonly Func<bool> _get;
        private readonly Action _begin;
        private readonly Action<bool> _perform;
        private readonly Action _end;

        public IntPtr Handle { get; private set; }

        private static readonly ConcurrentDictionary<IntPtr, MacSwitchView> _instances = new();

        public MacSwitchView(string resOff, string resOn, string label,
            Func<bool> get, Action begin, Action<bool> perform, Action end)
        {
            _resOff = resOff; _resOn = resOn; _label = label;
            _get = get; _begin = begin; _perform = perform; _end = end;

            Handle = Cocoa.CreateView(0, 0, 120, 60);
            objc_msgSend_void(Handle, SelSetWantsLayer, (IntPtr)1);
            EnsureAssets();
            EnsureClass();

            // Subclass with draw/mouse
            var bounds = objc_msgSend(Handle, SelBounds);
            var v = objc_msgSend(_cls, SelAlloc);
            objc_msgSend(v, SelInitWithFrame, bounds);
            objc_msgSend_void(v, SelSetWantsLayer, (IntPtr)1);
            Handle = v;

            _instances[Handle] = this;
        }

        public void SetFrame(double x, double y, double w, double h) => Cocoa.SetFrame(Handle, new Cocoa.CGRect(new Cocoa.CGPoint(x, y), new Cocoa.CGSize(w, h)));
        public void Refresh() => Cocoa.SetNeedsDisplay(Handle);

        private void EnsureAssets()
        {
            var asm = typeof(MacSwitchView).Assembly;
            _imgOff = _imgOff == IntPtr.Zero ? EmbeddedMac.LoadCGImage(asm, _resOff) : _imgOff;
            _imgOn = _imgOn == IntPtr.Zero ? EmbeddedMac.LoadCGImage(asm, _resOn) : _imgOn;
        }

        public void Dispose()
        {
            if (_imgOff != IntPtr.Zero) CGImageRelease(_imgOff); _imgOff = IntPtr.Zero;
            if (_imgOn != IntPtr.Zero) CGImageRelease(_imgOn); _imgOn = IntPtr.Zero;
            _instances.TryRemove(Handle, out _);
        }

        private static bool _ready;
        private static IntPtr _cls;
        private static IntPtr SelDrawRect = sel_registerName("drawRect:");
        private static IntPtr SelMouseDown = sel_registerName("mouseDown:");
        private static IntPtr SelIsFlipped = sel_registerName("isFlipped");

        private delegate void DrawRectD(IntPtr self, IntPtr cmd, Cocoa.CGRect dirty);
        private delegate void EventPtr(IntPtr self, IntPtr cmd, IntPtr evt);

        private static void EnsureClass()
        {
            if (_ready) return;
            _cls = objc_allocateClassPair(NSViewClass, "SRMacSwitchView", IntPtr.Zero);
            class_addMethod(_cls, SelDrawRect, (DrawRectD)DrawRectImpl, "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
            class_addMethod(_cls, SelMouseDown, (EventPtr)MouseDownImpl, "v@:@");
            class_addMethod(_cls, SelIsFlipped, (Func<IntPtr, IntPtr, byte>)((_, __) => (byte)1), "c@:");
            objc_registerClassPair(_cls);
            _ready = true;
        }

        private static MacSwitchView Get(IntPtr self) => _instances.TryGetValue(self, out var v) ? v : null;

        private static void DrawRectImpl(IntPtr self, IntPtr _cmd, Cocoa.CGRect dirty)
        {
            var me = Get(self);
            if (me == null) return;
            var ctx = Cocoa.CurrentCGContext();

            // clear
            Cocoa.CGContextSetRGBFillColor(ctx, 0.09, 0.09, 0.09, 1);
            Cocoa.CGContextFillRect(ctx, dirty);

            bool on = me._get();
            var img = on ? me._imgOn : me._imgOff;

            // keep aspect ~115x60
            double dstH = Math.Min(dirty.Size.H, 60);
            double ar = 115.0 / 60.0;
            double dstW = dstH * ar;
            if (dstW > dirty.Size.W) { dstW = dirty.Size.W; dstH = dstW / ar; }
            double x = dirty.Origin.X + (dirty.Size.W - dstW) * 0.5;
            double y = dirty.Origin.Y + (dirty.Size.H - dstH) * 0.5;

            if (img != IntPtr.Zero)
                Cocoa.CGContextDrawImage(ctx, new Cocoa.CGRect(new Cocoa.CGPoint(x, y), new Cocoa.CGSize(dstW, dstH)), img);
        }

        private static void MouseDownImpl(IntPtr self, IntPtr _cmd, IntPtr evt)
        {
            var me = Get(self);
            if (me == null) return;
            bool current = me._get();
            me._begin();
            me._perform(!current);
            me._end();
            Cocoa.SetNeedsDisplay(self);
        }
    }
}
#endif