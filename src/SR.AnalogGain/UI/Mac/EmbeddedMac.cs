#if MACOS
using System.Reflection;
using System.Runtime.InteropServices;

namespace SR.AnalogGain.UI.Mac
{
    /// <summary>
    /// Loads embedded PNG/JPEG resources into CGImage using CoreFoundation + ImageIO.
    /// Uses framework names (no absolute paths) so it works on any macOS.
    /// Caller must release the returned CGImage with CGImageRelease.
    /// </summary>
    internal static class EmbeddedMac
    {
        // ---- CoreFoundation ----
        [DllImport("CoreFoundation")]
        private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport("CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        // ---- ImageIO ----
        [DllImport("ImageIO")]
        private static extern IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);

        [DllImport("ImageIO")]
        private static extern IntPtr CGImageSourceCreateImageAtIndex(IntPtr isrc, nint index, IntPtr options);

        // ---- CoreGraphics (for releasing CGImage) ----
        [DllImport("CoreGraphics")]
        public static extern void CGImageRelease(IntPtr image);

        /// <summary>
        /// Loads an embedded resource (by file name suffix) from the given assembly into a CGImage.
        /// Example: LoadCGImage(typeof(Foo).Assembly, "KnobTop512.png")
        /// </summary>
        public static IntPtr LoadCGImage(Assembly asm, string fileName)
        {
            if (asm == null) throw new ArgumentNullException(nameof(asm));
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name is required.", nameof(fileName));

            string? resName = asm
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));

            if (resName == null)
                throw new FileNotFoundException($"Embedded resource not found: {fileName}");

            using var s = asm.GetManifestResourceStream(resName)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();

            // Proper cast to nint for NativeAOT/64-bit macOS
            var cfData = CFDataCreate(IntPtr.Zero, bytes, (nint)bytes.LongLength);
            if (cfData == IntPtr.Zero)
                throw new InvalidOperationException("CFDataCreate returned null.");

            try
            {
                var src = CGImageSourceCreateWithData(cfData, IntPtr.Zero);
                if (src == IntPtr.Zero)
                    throw new InvalidOperationException("CGImageSourceCreateWithData returned null.");

                try
                {
                    var img = CGImageSourceCreateImageAtIndex(src, 0, IntPtr.Zero);
                    if (img == IntPtr.Zero)
                        throw new InvalidOperationException("CGImageSourceCreateImageAtIndex returned null.");
                    return img; // caller must CGImageRelease(img)
                }
                finally
                {
                    CFRelease(src);
                }
            }
            finally
            {
                CFRelease(cfData);
            }
        }

        /// <summary>
        /// Convenience overload: loads from the executing assembly.
        /// </summary>
        public static IntPtr LoadCGImage(string fileName) =>
            LoadCGImage(Assembly.GetExecutingAssembly(), fileName);
    }
}
#endif