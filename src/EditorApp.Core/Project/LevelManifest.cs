using System.Globalization;
using System.Text.Json.Serialization;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>
/// The human-readable half of a <c>.vxlevel</c> (EditorApp.md §7). Kept as plain JSON so a file can
/// be inspected and hand-patched, and so a format change can be migrated instead of rejected.
/// </summary>
public sealed class LevelManifest
{
    /// <summary>
    /// Written from the very first release. Being able to open old files is not something that can
    /// be added later.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = VxLevelFile.CurrentVersion;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; } = Chunk.Size;

    /// <summary>256 entries as <c>#RRGGBBAA</c>.</summary>
    [JsonPropertyName("palette")]
    public string[] Palette { get; set; } = [];

    /// <summary>Inclusive world bounds of the solid voxels, or null for an empty level.</summary>
    [JsonPropertyName("boundsMin")]
    public int[]? BoundsMin { get; set; }

    [JsonPropertyName("boundsMax")]
    public int[]? BoundsMax { get; set; }

    /// <summary>
    /// Version 1 only: coordinates of the chunks stored under <c>chunks/</c>. Version 2 moved them
    /// under each object.
    /// </summary>
    [JsonPropertyName("chunks")]
    public int[][] Chunks { get; set; } = [];

    /// <summary>Version 2 and up: the level's objects, each with its own placement.</summary>
    [JsonPropertyName("objects")]
    public ObjectEntry[]? Objects { get; set; }

    /// <summary>One placed object in the manifest.</summary>
    public sealed class ObjectEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("position")]
        public float[] Position { get; set; } = [0f, 0f, 0f];

        /// <summary>Quaternion as [x, y, z, w].</summary>
        [JsonPropertyName("rotation")]
        public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];

        [JsonPropertyName("chunks")]
        public int[][] Chunks { get; set; } = [];
    }

    [JsonPropertyName("savedUtc")]
    public string SavedUtc { get; set; } = string.Empty;

    public static string[] EncodePalette(Palette palette)
    {
        var encoded = new string[Voxels.Palette.Size];
        for (int i = 0; i < encoded.Length; i++)
        {
            Color32 color = palette[i];
            encoded[i] = $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
        }

        return encoded;
    }

    public static Palette DecodePalette(string[]? encoded)
    {
        var palette = new Palette();
        if (encoded is null)
        {
            return palette;
        }

        // Index 0 is the empty marker and is never assignable; start at 1 and ignore extras.
        int count = Math.Min(encoded.Length, Voxels.Palette.Size);
        for (int i = 1; i < count; i++)
        {
            palette[i] = ParseColor(encoded[i], i);
        }

        return palette;
    }

    private static Color32 ParseColor(string text, int index)
    {
        ReadOnlySpan<char> digits = text.AsSpan().TrimStart('#');
        if (digits.Length is not (6 or 8))
        {
            throw new VxLevelFormatException($"Palette entry {index} is not a hex color: '{text}'.");
        }

        static byte Hex(ReadOnlySpan<char> span, int start) =>
            byte.Parse(span.Slice(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        try
        {
            return new Color32(
                Hex(digits, 0),
                Hex(digits, 2),
                Hex(digits, 4),
                digits.Length == 8 ? Hex(digits, 6) : (byte)255);
        }
        catch (FormatException)
        {
            throw new VxLevelFormatException($"Palette entry {index} is not a hex color: '{text}'.");
        }
    }
}
