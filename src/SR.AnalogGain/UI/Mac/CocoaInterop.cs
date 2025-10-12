#if MACOS
using System.Runtime.InteropServices;

namespace SR.AnalogGain.UI.Mac
{
    /// <summary>
    /// Minimal interop layer for Cocoa/AppKit, CoreGraphics (Quartz 2D), CoreText, CoreFoundation, ImageIO, and Objective-C.
    /// Imports use library names (not absolute paths) so dyld resolves them on any macOS.
    /// </summary>
    internal static class Cocoa
    {
        // ========= Objective-C runtime =========
        // Use library name (not a path). This resolves on all macOS versions/architectures.
        [DllImport("libobjc.A.dylib")] public static extern IntPtr objc_getClass(string name);
        [DllImport("libobjc.A.dylib")] public static extern IntPtr sel_registerName(string name);
        [DllImport("libobjc.A.dylib")] public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
        [DllImport("libobjc.A.dylib")] public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);
        [DllImport("libobjc.A.dylib")] public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, double arg1);
        [DllImport("libobjc.A.dylib")] public static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector, IntPtr arg1);
        [DllImport("libobjc.A.dylib")] public static extern IntPtr objc_allocateClassPair(IntPtr superClass, string name, IntPtr extraBytes);
        [DllImport("libobjc.A.dylib")] public static extern void objc_registerClassPair(IntPtr cls);
        [DllImport("libobjc.A.dylib")] public static extern bool class_addMethod(IntPtr cls, IntPtr name, Delegate imp, string types);

        // ========= CoreGraphics (Quartz 2D) =========
        [DllImport("CoreGraphics")] public static extern void CGContextSetRGBFillColor(IntPtr c, nfloat r, nfloat g, nfloat b, nfloat a);
        [DllImport("CoreGraphics")] public static extern void CGContextFillRect(IntPtr c, CGRect rect);
        [DllImport("CoreGraphics")] public static extern void CGContextSetRGBStrokeColor(IntPtr c, nfloat r, nfloat g, nfloat b, nfloat a);
        [DllImport("CoreGraphics")] public static extern void CGContextSetLineWidth(IntPtr c, nfloat w);
        [DllImport("CoreGraphics")] public static extern void CGContextAddEllipseInRect(IntPtr c, CGRect rect);
        [DllImport("CoreGraphics")] public static extern void CGContextStrokePath(IntPtr c);
        [DllImport("CoreGraphics")] public static extern void CGContextMoveToPoint(IntPtr c, nfloat x, nfloat y);
        [DllImport("CoreGraphics")] public static extern void CGContextAddLineToPoint(IntPtr c, nfloat x, nfloat y);
        [DllImport("CoreGraphics")] public static extern void CGContextDrawImage(IntPtr c, CGRect rect, IntPtr image);
        [DllImport("CoreGraphics")] public static extern void CGContextSaveGState(IntPtr c);
        [DllImport("CoreGraphics")] public static extern void CGContextRestoreGState(IntPtr c);
        [DllImport("CoreGraphics")] public static extern void CGContextTranslateCTM(IntPtr c, nfloat tx, nfloat ty);
        [DllImport("CoreGraphics")] public static extern void CGContextRotateCTM(IntPtr c, nfloat angleRad);

        [DllImport("CoreGraphics")] public static extern IntPtr CGColorCreateGenericRGB(nfloat r, nfloat g, nfloat b, nfloat a);
        [DllImport("CoreGraphics")] public static extern void CGColorRelease(IntPtr color);

        [DllImport("CoreGraphics")] public static extern IntPtr CGImageCreateWithImageInRect(IntPtr image, CGRect rect);
        [DllImport("CoreGraphics")] public static extern void CGImageRelease(IntPtr image);

        [DllImport("CoreGraphics")] public static extern IntPtr CGDataProviderCreateWithCFData(IntPtr data);
        [DllImport("CoreGraphics")] public static extern IntPtr CGFontCreateWithDataProvider(IntPtr provider);
        [DllImport("CoreGraphics")] public static extern void CGFontRelease(IntPtr font);

        // ========= CoreText =========
        [DllImport("CoreText")] public static extern IntPtr CTFontCreateWithName(IntPtr name, nfloat size, IntPtr matrix);
        [DllImport("CoreText")] public static extern IntPtr CTLineCreateWithAttributedString(IntPtr attrString);
        [DllImport("CoreText")] public static extern void CTLineDraw(IntPtr line, IntPtr context);
        [DllImport("CoreText")] public static extern IntPtr CTRunGetAttributes(IntPtr run);

        // ========= CoreFoundation =========
        public const uint kCFStringEncodingUTF8 = 0x08000100; // UTF-8
        [DllImport("CoreFoundation")] public static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);
        [DllImport("CoreFoundation")] public static extern IntPtr CFDictionaryCreateMutable(IntPtr alloc, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);
        [DllImport("CoreFoundation")] public static extern void CFDictionaryAddValue(IntPtr dict, IntPtr key, IntPtr value);
        [DllImport("CoreFoundation")] public static extern IntPtr CFAttributedStringCreate(IntPtr alloc, IntPtr str, IntPtr attributes);
        [DllImport("CoreFoundation")] public static extern void CFRelease(IntPtr cf);

        // ========= ImageIO =========
        [DllImport("ImageIO")] public static extern IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);
        [DllImport("ImageIO")] public static extern IntPtr CGImageSourceCreateImageAtIndex(IntPtr isrc, nint index, IntPtr options);

        // ========= AppKit (Cocoa) =========
        // NSGraphicsContext → CGContext
        [DllImport("AppKit")] static extern IntPtr NSGraphicsContext_currentContext();
        [DllImport("AppKit")] static extern IntPtr NSGraphicsContext_CGContext(IntPtr nsGraphicsContext);
        public static IntPtr CurrentCGContext()
        {
            var gc = NSGraphicsContext_currentContext();
            return NSGraphicsContext_CGContext(gc);
        }

        // Common classes/selectors for NSView
        public static readonly IntPtr NSViewClass = objc_getClass("NSView");

        public static readonly IntPtr SelAlloc = sel_registerName("alloc");
        public static readonly IntPtr SelInit = sel_registerName("init");
        public static readonly IntPtr SelInitWithFrame = sel_registerName("initWithFrame:");
        public static readonly IntPtr SelSetWantsLayer = sel_registerName("setWantsLayer:");
        public static readonly IntPtr SelAddSubview = sel_registerName("addSubview:");
        public static readonly IntPtr SelBounds = sel_registerName("bounds");
        public static readonly IntPtr SelSetFrame = sel_registerName("setFrame:");
        public static readonly IntPtr SelIsFlipped = sel_registerName("isFlipped");
        public static readonly IntPtr SelConvertPointFromView = sel_registerName("convertPoint:fromView:");
        public static readonly IntPtr SelLocationInWindow = sel_registerName("locationInWindow");
        public static readonly IntPtr SelScrollingDeltaY = sel_registerName("scrollingDeltaY");
        public static readonly IntPtr SelSetNeedsDisplay = sel_registerName("setNeedsDisplay:");

        // ========= Core geometry types =========
        [StructLayout(LayoutKind.Sequential)]
        public struct CGPoint { public double X, Y; public CGPoint(double x, double y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct CGSize { public double W, H; public CGSize(double w, double h) { W = w; H = h; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct CGRect
        {
            public CGPoint Origin;
            public CGSize Size;
            public CGRect(CGPoint origin, CGSize size) { Origin = origin; Size = size; }
        }

        // ========= Convenience helpers for NSView =========
        public static IntPtr CreateView(double x, double y, double w, double h)
        {
            var view = objc_msgSend(NSViewClass, SelAlloc);
            var framePtr = Marshal.AllocHGlobal(Marshal.SizeOf<CGRect>());
            Marshal.StructureToPtr(new CGRect(new CGPoint(x, y), new CGSize(w, h)), framePtr, false);
            try { return objc_msgSend(view, SelInitWithFrame, framePtr); }
            finally { Marshal.FreeHGlobal(framePtr); }
        }

        public static void SetFrame(IntPtr view, CGRect r)
        {
            var p = Marshal.AllocHGlobal(Marshal.SizeOf<CGRect>());
            Marshal.StructureToPtr(r, p, false);
            try { objc_msgSend_void(view, SelSetFrame, p); }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static void AddSubview(IntPtr parent, IntPtr child) => objc_msgSend_void(parent, SelAddSubview, child);
        public static void SetNeedsDisplay(IntPtr view) => objc_msgSend_void(view, SelSetNeedsDisplay, (IntPtr)1);
    }

    /// <summary>
    /// nfloat shim matching macOS native CGFloat (double on 64-bit).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct nfloat
    {
        private double _v;
        public nfloat(double v) => _v = v;
        public static implicit operator nfloat(double v) => new nfloat(v);
        public static implicit operator double(nfloat v) => v._v;
    }
}
#endif
