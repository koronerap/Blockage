using System.Globalization;
using System.Text;
using EditorApp.Core.Export;
using EditorApp.Core.Import;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>
/// Palettes in the files other tools and Lospec share them in (Fullreleaseplan 4.4): GIMP's .gpl,
/// a plain .hex list, and a .png whose pixels are the colours in reading order. Opaque colours only —
/// a palette entry is a colour, not a shade of see-through.
/// </summary>
public static class PaletteFiles
{
    /// <summary>The extensions it reads and writes.</summary>
    public static readonly string[] Extensions = [".gpl", ".hex", ".png"];

    /// <summary>More than this and it is not a palette the level has room for.</summary>
    public const int MaxColours = Palette.Size - 1;

    public static List<Color32> Read(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".gpl" => ReadGpl(File.ReadAllText(path)),
        ".hex" => ReadHex(File.ReadAllText(path)),
        ".png" => ReadPng(File.ReadAllBytes(path)),
        string other => throw new InvalidDataException($"Palettes are .gpl, .hex or .png, not {other}."),
    };

    public static void Write(string path, IReadOnlyList<Color32> colours, string name)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".gpl":
                File.WriteAllText(path, WriteGpl(colours, name), Encoding.UTF8);
                break;

            case ".hex":
                File.WriteAllText(path, WriteHex(colours), Encoding.UTF8);
                break;

            case ".png":
                File.WriteAllBytes(path, WritePng(colours));
                break;

            default:
                throw new InvalidDataException($"Palettes are written as .gpl, .hex or .png, not {Path.GetExtension(path)}.");
        }
    }

    /// <summary>GIMP's palette: a header, then a colour a line as three numbers and a name, # for comments.</summary>
    public static List<Color32> ReadGpl(string text)
    {
        var colours = new List<Color32>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("GIMP", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Columns:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3
                && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte r)
                && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g)
                && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
            {
                colours.Add(new Color32(r, g, b));
            }
        }

        return Capped(colours);
    }

    public static string WriteGpl(IReadOnlyList<Color32> colours, string name)
    {
        var text = new StringBuilder();
        text.Append("GIMP Palette\n");
        text.Append("Name: ").Append(name).Append('\n');
        text.Append("Columns: 16\n#\n");
        foreach (Color32 colour in colours)
        {
            text.Append(CultureInfo.InvariantCulture, $"{colour.R,3} {colour.G,3} {colour.B,3}\t#{colour.R:X2}{colour.G:X2}{colour.B:X2}\n");
        }

        return text.ToString();
    }

    /// <summary>A colour a line, as six hex digits, with or without the #.</summary>
    public static List<Color32> ReadHex(string text)
    {
        var colours = new List<Color32>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('#');
            if (line.Length >= 6 && int.TryParse(line.AsSpan(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                colours.Add(new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            }
        }

        return Capped(colours);
    }

    public static string WriteHex(IReadOnlyList<Color32> colours) =>
        string.Concat(colours.Select(c => $"{c.R:x2}{c.G:x2}{c.B:x2}\n"));

    /// <summary>
    /// The colours of an image in reading order, each once, the see-through pixels left out — as
    /// Lospec's one-pixel-per-colour strips are, and as a swatch sheet is read.
    /// </summary>
    public static List<Color32> ReadPng(byte[] png)
    {
        DecodedImage image = PngReader.Decode(png);
        var seen = new HashSet<Color32>();
        var colours = new List<Color32>();

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Color32 pixel = image[x, y];
                if (pixel.A < 128)
                {
                    continue;
                }

                Color32 colour = pixel with { A = 255 };
                if (seen.Add(colour))
                {
                    colours.Add(colour);
                }
            }
        }

        return Capped(colours);
    }

    /// <summary>One pixel a colour, in a row: Lospec's own form.</summary>
    public static byte[] WritePng(IReadOnlyList<Color32> colours)
    {
        int width = Math.Max(colours.Count, 1);
        var rgba = new byte[width * 4];
        for (int i = 0; i < colours.Count; i++)
        {
            rgba[(i * 4) + 0] = colours[i].R;
            rgba[(i * 4) + 1] = colours[i].G;
            rgba[(i * 4) + 2] = colours[i].B;
            rgba[(i * 4) + 3] = 255;
        }

        return PngWriter.EncodeRgba(rgba, width, 1);
    }

    private static List<Color32> Capped(List<Color32> colours) =>
        colours.Count > MaxColours ? colours[..MaxColours] : colours;

    /// <summary>Where palettes kept for use in any level live: a folder of .gpl files in the user's settings.</summary>
    public static string LibraryDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blockage", "Palettes");
}
