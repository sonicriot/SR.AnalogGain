using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;

static class Embedded
{
    public static Bitmap LoadBitmap(Assembly asm, string fileName)
    {
        // Busca por sufijo para evitar depender del namespace exacto
        string? resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));

        if (resName == null)
            throw new FileNotFoundException($"Embedded resource not found: {fileName}\n" +
                "Available: " + string.Join(", ", asm.GetManifestResourceNames()));

        using Stream s = asm.GetManifestResourceStream(resName)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        // New Bitmap sobre un MemoryStream propio -> puedes cerrar el stream
        return new Bitmap(ms);
    }
}
