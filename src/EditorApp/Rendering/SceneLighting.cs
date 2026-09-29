using System.Numerics;

namespace EditorApp.Rendering;

/// <summary>How the voxel shader colours a face.</summary>
public enum ShadingMode
{
    /// <summary>One directional light, so the level can be judged the way an engine would light it.</summary>
    Lit = 0,

    /// <summary>The flat per-face shade of EditorApp.md §5. No light source at all. "Solid" in the header.</summary>
    Unlit = 1,

    /// <summary>Only the voxel lattice, drawn as lines — the desktop's; the phone offers the other two.</summary>
    Wireframe = 2,

    /// <summary>The path tracer's picture, clearing while the view is still — the desktop's, Blender's Rendered.</summary>
    Rendered = 3,
}

/// <summary>
/// The single directional light the viewport shades with, and the mode switch beside it.
///
/// Deliberately not part of the level: it is a way of looking at the model, not a property of it, so
/// it is never saved into a <c>.vxlevel</c> and never reaches an export. Nothing here changes what
/// is built — only what is seen while building it.
/// </summary>
public sealed class SceneLighting
{
    /// <summary>
    /// A key light over the viewer's left shoulder from the opening camera. Chosen so the three
    /// faces of a cube at that angle land well apart from each other and none of them falls all the
    /// way to the ambient floor — a face at pure ambient reads as a hole rather than as a side.
    /// </summary>
    public const float DefaultAzimuth = 200f;

    public const float DefaultElevation = 50f;
    public const float DefaultIntensity = 0.70f;
    public const float DefaultAmbient = 0.32f;

    public ShadingMode Mode { get; set; } = ShadingMode.Lit;

    /// <summary>Compass bearing of the light in degrees, measured around the vertical axis.</summary>
    public float Azimuth { get; set; } = DefaultAzimuth;

    /// <summary>Height of the light in degrees: 90 is straight overhead, 0 is on the horizon.</summary>
    public float Elevation { get; set; } = DefaultElevation;

    /// <summary>How much the facing direction contributes on top of the ambient floor.</summary>
    public float Intensity { get; set; } = DefaultIntensity;

    /// <summary>
    /// The floor a face gets regardless of where it points. Without one, faces turned away from the
    /// light go to pure black and their colour cannot be judged at all.
    /// </summary>
    public float Ambient { get; set; } = DefaultAmbient;

    public bool IsLit => Mode == ShadingMode.Lit;

    /// <summary>Unit vector pointing from the scene towards the light — the L of a lambert term.</summary>
    public Vector3 Direction
    {
        get
        {
            float azimuth = Azimuth * (MathF.PI / 180f);
            float elevation = Elevation * (MathF.PI / 180f);
            float horizontal = MathF.Cos(elevation);

            return Vector3.Normalize(new Vector3(
                horizontal * MathF.Sin(azimuth),
                MathF.Sin(elevation),
                horizontal * MathF.Cos(azimuth)));
        }
    }

    public void ResetAngles()
    {
        Azimuth = DefaultAzimuth;
        Elevation = DefaultElevation;
        Intensity = DefaultIntensity;
        Ambient = DefaultAmbient;
    }
}
