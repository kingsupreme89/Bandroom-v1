using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SupremeStadiumSoundSelector;

/// <summary>Loads the embedded Outfit font weights (Regular/Medium/SemiBold/Bold) into a
/// PrivateFontCollection at startup so the design's typeface renders identically on every
/// machine, no system install required. Falls back to Segoe UI if anything goes wrong
/// (corrupt resource, unsupported platform) rather than crashing the app over a font.</summary>
internal static class AppFonts
{
    static readonly PrivateFontCollection Collection = new();
    static readonly List<IntPtr> PinnedBuffers = new(); // kept alive for the collection's lifetime
    static bool _loaded;

    public static string FamilyName { get; private set; } = "Segoe UI";

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            // GDI+ PrivateFontCollection only reliably exposes two weights per family via
            // FontStyle (Regular/Bold) -- registering all 4 weight files under one family
            // name makes Medium/SemiBold unpredictable to select. Only Regular + Bold go into
            // the collection; the Medium/SemiBold .ttf files stay embedded (unused for now)
            // in case of a future WPF/WinUI migration where per-weight families work properly.
            LoadResource("Outfit-Regular.ttf");
            LoadResource("Outfit-Bold.ttf");

            if (Collection.Families.Length > 0)
                FamilyName = Collection.Families[0].Name;
        }
        catch
        {
            FamilyName = "Segoe UI"; // safe fallback -- never let a font problem take down the app
        }
    }

    static void LoadResource(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        string resourceName = asm.GetManifestResourceNames()
            .First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);

        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        PinnedBuffers.Add(buffer);
        Collection.AddMemoryFont(buffer, bytes.Length);
    }

    public static Font Get(float size, FontStyle style = FontStyle.Regular)
    {
        EnsureLoaded();
        return Collection.Families.Length > 0
            ? new Font(Collection.Families[0], size, style)
            : new Font(FamilyName, size, style);
    }
}
