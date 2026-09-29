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

    /// <summary>
    /// Versions 4 and 5 only: the world size of one voxel for the whole level, applied at export.
    /// Version 6 moved it onto each object and stopped writing this; absent before version 4, where
    /// one voxel was always one unit.
    /// </summary>
    [JsonPropertyName("voxelSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? VoxelSize { get; set; }

    /// <summary>256 entries as <c>#RRGGBBAA</c>.</summary>
    [JsonPropertyName("palette")]
    public string[] Palette { get; set; } = [];

    /// <summary>
    /// Custom slots the user chose to keep, as indices. Absent in files written before saved
    /// swatches existed, where every occupied custom slot had been picked deliberately — so those
    /// are all treated as saved rather than silently disappearing from the Custom row.
    /// </summary>
    [JsonPropertyName("savedCustomSlots")]
    public int[]? SavedCustomSlots { get; set; }

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

        /// <summary>Absent before version 5, where every object was visible.</summary>
        [JsonPropertyName("visible")]
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Version 6 and up: world units one of this object's voxels measures. The position is in
        /// world units from version 6 on, where before it was in voxels of the level's one size.
        /// </summary>
        [JsonPropertyName("voxelSize")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float? VoxelSize { get; set; }

        /// <summary>Written only when true; absent everywhere before objects could be locked.</summary>
        [JsonPropertyName("locked")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Locked { get; set; }

        /// <summary>The <see cref="Id"/> of the object this is a child of; absent for none.</summary>
        [JsonPropertyName("parent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Parent { get; set; }

        /// <summary>The <see cref="CollectionEntry.Id"/> it is in; absent at the top.</summary>
        [JsonPropertyName("collection")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Collection { get; set; }

        /// <summary>Written only when true; absent in files from before there was a selection.</summary>
        [JsonPropertyName("selected")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Selected { get; set; }

        /// <summary>Its modifiers, in order; absent for none.</summary>
        [JsonPropertyName("modifiers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ModifierEntry[]? Modifiers { get; set; }
    }

    /// <summary>One modifier of an object.</summary>
    public sealed class ModifierEntry
    {
        /// <summary>"mirror" or "array".</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "mirror";

        /// <summary>"x", "y" or "z".</summary>
        [JsonPropertyName("axis")]
        public string Axis { get; set; } = "x";

        [JsonPropertyName("plane")]
        public int Plane { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; } = 3;

        [JsonPropertyName("step")]
        public int Step { get; set; } = 8;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>How the level is rendered; absent in files from before there was a renderer.</summary>
    [JsonPropertyName("render")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RenderEntry? Render { get; set; }

    public sealed class RenderEntry
    {
        /// <summary>"cpu" or "gpu".</summary>
        [JsonPropertyName("engine")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Engine { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; } = 1280;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 720;

        [JsonPropertyName("samples")]
        public int Samples { get; set; } = 64;

        [JsonPropertyName("bounces")]
        public int Bounces { get; set; } = 4;

        [JsonPropertyName("seed")]
        public int Seed { get; set; } = 1;

        [JsonPropertyName("skyStrength")]
        public float SkyStrength { get; set; } = 1f;

        [JsonPropertyName("skyTop")]
        public float[] SkyTop { get; set; } = [0.55f, 0.68f, 0.9f];

        [JsonPropertyName("skyHorizon")]
        public float[] SkyHorizon { get; set; } = [0.86f, 0.88f, 0.9f];

        [JsonPropertyName("transparent")]
        public bool TransparentBackground { get; set; }

        /// <summary>A plain backdrop in place of the sky, in sRGB; absent for the sky.</summary>
        [JsonPropertyName("backgroundColour")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float[]? BackgroundColour { get; set; }

        [JsonPropertyName("exposure")]
        public float Exposure { get; set; }

        [JsonPropertyName("emission")]
        public float EmissionStrength { get; set; } = 3f;

        [JsonPropertyName("fog")]
        public float Fog { get; set; }

        [JsonPropertyName("aperture")]
        public float Aperture { get; set; }

        [JsonPropertyName("focus")]
        public float FocusDistance { get; set; } = 20f;

        [JsonPropertyName("bloom")]
        public float Bloom { get; set; }

        [JsonPropertyName("sunSize")]
        public float SunSize { get; set; } = 0.5f;
    }

    /// <summary>The palette entries that are not plain, with what they are made of; absent for none.</summary>
    [JsonPropertyName("materials")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterialEntry[]? Materials { get; set; }

    public sealed class MaterialEntry
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("emission")]
        public float Emission { get; set; }

        [JsonPropertyName("metallic")]
        public float Metallic { get; set; }

        [JsonPropertyName("roughness")]
        public float Roughness { get; set; } = 1f;

        [JsonPropertyName("opacity")]
        public float Opacity { get; set; } = 1f;
    }

    /// <summary>The <see cref="ObjectEntry.Id"/> of the active object; absent when there was none.</summary>
    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Active { get; set; }

    /// <summary>
    /// The level's lights. Absent in files written before lights were saved, which get the sun the
    /// viewport always had; an empty list is a level someone chose to leave unlit.
    /// </summary>
    [JsonPropertyName("lights")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LightEntry[]? Lights { get; set; }

    /// <summary>The level's collections, each after the one it is inside; absent for none.</summary>
    [JsonPropertyName("collections")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CollectionEntry[]? Collections { get; set; }

    /// <summary>One collection and its three switches.</summary>
    public sealed class CollectionEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Collection";

        /// <summary>The <see cref="Id"/> of the collection it is inside; absent at the top.</summary>
        [JsonPropertyName("parent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Parent { get; set; }

        [JsonPropertyName("visible")]
        public bool Visible { get; set; } = true;

        [JsonPropertyName("locked")]
        public bool Locked { get; set; }

        [JsonPropertyName("export")]
        public bool Export { get; set; } = true;
    }

    /// <summary>The level's cameras; absent for none.</summary>
    [JsonPropertyName("cameras")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CameraEntry[]? Cameras { get; set; }

    /// <summary>The <see cref="CameraEntry.Id"/> a render is seen from; absent for the view.</summary>
    [JsonPropertyName("activeCamera")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ActiveCamera { get; set; }

    /// <summary>One camera. Angles in degrees, as a person reading the file would expect.</summary>
    public sealed class CameraEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Camera";

        [JsonPropertyName("position")]
        public float[] Position { get; set; } = [0f, 0f, 0f];

        [JsonPropertyName("yaw")]
        public float Yaw { get; set; }

        [JsonPropertyName("pitch")]
        public float Pitch { get; set; }

        /// <summary>"perspective", "orthographic" or "isometric".</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "perspective";

        [JsonPropertyName("fov")]
        public float FieldOfView { get; set; } = 60f;

        [JsonPropertyName("orthographicHeight")]
        public float OrthographicHeight { get; set; } = 20f;

        [JsonPropertyName("pivotDistance")]
        public float PivotDistance { get; set; } = 20f;
    }

    /// <summary>The ambient floor, 0 to 1. Absent where lights are.</summary>
    [JsonPropertyName("ambient")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Ambient { get; set; }

    /// <summary>One light in the manifest. Never exported, only kept.</summary>
    public sealed class LightEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Light";

        /// <summary>"directional", "point" or "spot".</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "point";

        [JsonPropertyName("position")]
        public float[] Position { get; set; } = [0f, 0f, 0f];

        /// <summary>Quaternion as [x, y, z, w]. The light shines along its own -Y.</summary>
        [JsonPropertyName("rotation")]
        public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];

        /// <summary><c>#RRGGBB</c>.</summary>
        [JsonPropertyName("colour")]
        public string Colour { get; set; } = "#FFFFFF";

        [JsonPropertyName("intensity")]
        public float Intensity { get; set; } = 1f;

        [JsonPropertyName("range")]
        public float Range { get; set; } = 15f;

        [JsonPropertyName("spotAngle")]
        public float SpotAngle { get; set; } = 45f;

        [JsonPropertyName("spotBlend")]
        public float SpotBlend { get; set; } = 0.15f;

        [JsonPropertyName("visible")]
        public bool Visible { get; set; } = true;

        [JsonPropertyName("locked")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Locked { get; set; }

        /// <summary>The id of the object this light is a child of; absent for none.</summary>
        [JsonPropertyName("parent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Parent { get; set; }

        /// <summary>The <see cref="CollectionEntry.Id"/> it is in; absent at the top.</summary>
        [JsonPropertyName("collection")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Collection { get; set; }

        [JsonPropertyName("selected")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Selected { get; set; }
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

    public static Palette DecodePalette(string[]? encoded, int[]? savedCustomSlots = null)
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

        if (savedCustomSlots is null)
        {
            // An older file: every occupied custom slot was a deliberate pick back then.
            for (int i = Voxels.Palette.CustomStart; i < Voxels.Palette.Size; i++)
            {
                palette.SetCustomSaved(i, !palette.IsCustomSlotFree(i));
            }
        }
        else
        {
            foreach (int index in savedCustomSlots)
            {
                palette.SetCustomSaved(index, true);
            }
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
