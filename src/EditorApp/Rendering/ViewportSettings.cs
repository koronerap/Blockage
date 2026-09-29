using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace EditorApp.Rendering;

/// <summary>How Solid shading lights a face.</summary>
public enum SolidLighting
{
    /// <summary>The fixed per-face shade — the look an export has.</summary>
    Studio,

    /// <summary>No shading at all: every face its bare colour, the palette as it is.</summary>
    Flat,
}

/// <summary>Where a face's colour comes from.</summary>
public enum ColourMode
{
    /// <summary>Its own palette colour.</summary>
    Palette,

    /// <summary>One colour for everything, so the shape is all there is to look at.</summary>
    Single,

    /// <summary>A colour per object, so which voxels belong to which object shows at a glance.</summary>
    Random,
}

/// <summary>What is behind the model.</summary>
public enum BackgroundMode
{
    Theme,
    Custom,
}

/// <summary>
/// Blender's viewport header, as far as it fits a voxel editor: which gizmos are drawn, what is
/// overlaid, whether solid things can be seen through, and how the model is shaded. Kept with the
/// preferences, so the viewport comes back as it was left.
/// </summary>
public sealed class ViewportSettings
{
    // ---- Gizmos ------------------------------------------------------------------------------

    /// <summary>All of them at once: the header's gizmo switch.</summary>
    public bool Gizmos { get; set; } = true;

    /// <summary>The axis ball and the view buttons in the corner.</summary>
    public bool NavigateGizmo { get; set; } = true;

    /// <summary>The Transform gizmo and the extrude arrow — the handles the tools are driven by.</summary>
    public bool ToolGizmos { get; set; } = true;

    /// <summary>A sun's or a spot's aim line, with the ring it is dragged by.</summary>
    public bool LightGizmos { get; set; } = true;

    // ---- Overlays ----------------------------------------------------------------------------

    /// <summary>All of them at once: the header's overlay switch. Tools' own marks stay — they are how the tools are used.</summary>
    public bool Overlays { get; set; } = true;

    public bool Grid { get; set; } = true;

    /// <summary>The world's axes drawn across the floor, in their colours — Blender draws X and Y (here X and Z).</summary>
    public bool AxisX { get; set; } = true;

    public bool AxisY { get; set; }

    public bool AxisZ { get; set; } = true;

    /// <summary>The view's name and what is focused, top left.</summary>
    public bool TextInfo { get; set; } = true;

    public bool Measurements { get; set; } = true;

    /// <summary>Blender's "Extras": the lights' icons.</summary>
    public bool LightIcons { get; set; } = true;

    /// <summary>A dot where each object's origin is.</summary>
    public bool Origins { get; set; }

    /// <summary>A dashed line from each child to its parent.</summary>
    public bool RelationshipLines { get; set; } = true;

    public bool MirrorPlanes { get; set; } = true;

    /// <summary>The focused object lifted, the rest held back — Blender's "Fade Inactive Geometry".</summary>
    public bool FocusHighlight { get; set; } = true;

    /// <summary>The voxel lattice drawn over the faces.</summary>
    public bool Wireframe { get; set; }

    public float WireframeOpacity
    {
        get => _wireframeOpacity;
        set => _wireframeOpacity = float.IsFinite(value) ? Math.Clamp(value, 0.05f, 1f) : _wireframeOpacity;
    }

    private float _wireframeOpacity = 0.5f;

    // ---- X-Ray -------------------------------------------------------------------------------

    public bool XRay { get; set; }

    /// <summary>Corners closed in by voxels drawn darker — what makes a voxel model's shape read (Fullreleaseplan 7.1).</summary>
    public bool AmbientOcclusion { get; set; } = true;

    /// <summary>The sun's shadows, in Lit shading.</summary>
    public bool Shadows { get; set; } = true;

    /// <summary>
    /// The section box (Fullreleaseplan 7.2): nothing outside it is drawn or picked. Null for none.
    /// A way of looking at the level now, not kept from one run to the next.
    /// </summary>
    [JsonIgnore]
    public EditorApp.Core.Scene.ClipBox? Clip { get; set; }

    /// <summary>Blender's quad view (Fullreleaseplan 7.4): top, front and right beside the view itself.</summary>
    public bool Quad { get; set; }

    /// <summary>How solid a face stays with X-Ray on.</summary>
    public float XRayAlpha
    {
        get => _xRayAlpha;
        set => _xRayAlpha = float.IsFinite(value) ? Math.Clamp(value, 0.05f, 0.95f) : _xRayAlpha;
    }

    private float _xRayAlpha = 0.5f;

    // ---- Shading -----------------------------------------------------------------------------

    public ShadingMode Shading { get; set; } = ShadingMode.Lit;

    public SolidLighting SolidLighting { get; set; } = SolidLighting.Studio;

    public ColourMode Colour { get; set; } = ColourMode.Palette;

    /// <summary>Saved as <c>#RRGGBB</c>; <see cref="SingleColour"/> is the value.</summary>
    public string SingleColourHex { get; set; } = "#C8C8C8";

    public BackgroundMode Background { get; set; } = BackgroundMode.Theme;

    public string BackgroundHex { get; set; } = "#3A3C3F";

    [JsonIgnore]
    public Vector3 SingleColour
    {
        get => FromHex(SingleColourHex, new Vector3(0.78f));
        set => SingleColourHex = ToHex(value);
    }

    [JsonIgnore]
    public Vector3 BackgroundColour
    {
        get => FromHex(BackgroundHex, new Vector3(0.23f));
        set => BackgroundHex = ToHex(value);
    }

    /// <summary>A colour for an object in Random colouring: well apart from its neighbours, never garish.</summary>
    public static Vector3 ObjectColour(int id)
    {
        // Golden-ratio steps round the hue circle keep consecutive ids far apart.
        float hue = (id * 0.618034f) % 1f;
        return FromHsv(hue, 0.42f, 0.86f);
    }

    public ViewportSettings Clone() => (ViewportSettings)MemberwiseClone();

    private static Vector3 FromHsv(float h, float s, float v)
    {
        float i = MathF.Floor(h * 6f);
        float f = (h * 6f) - i;
        float p = v * (1f - s);
        float q = v * (1f - (f * s));
        float t = v * (1f - ((1f - f) * s));

        return ((int)i % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    private static string ToHex(Vector3 colour)
    {
        static int Byte(float c) => (int)MathF.Round(Math.Clamp(c, 0f, 1f) * 255f);
        return $"#{Byte(colour.X):X2}{Byte(colour.Y):X2}{Byte(colour.Z):X2}";
    }

    private static Vector3 FromHex(string? hex, Vector3 fallback)
    {
        ReadOnlySpan<char> digits = (hex ?? string.Empty).AsSpan().TrimStart('#');
        if (digits.Length != 6
            || !int.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r)
            || !int.TryParse(digits.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g)
            || !int.TryParse(digits[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
        {
            return fallback;
        }

        return new Vector3(r, g, b) / 255f;
    }
}
