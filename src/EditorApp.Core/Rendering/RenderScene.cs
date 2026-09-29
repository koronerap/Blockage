using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Rendering;

/// <summary>What a render is of, and how it looks (Fullreleaseplan 5.2). Saved with the level.</summary>
public sealed record RenderSettings
{
    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    /// <summary>Samples a pixel is averaged over: more is cleaner and slower.</summary>
    public int Samples { get; init; } = 64;

    /// <summary>How many times light may bounce before it is let go.</summary>
    public int Bounces { get; init; } = 4;

    /// <summary>The same seed and settings give the same image, pixel for pixel.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>How bright the sky is, over the level's own ambient.</summary>
    public float SkyStrength { get; init; } = 1f;

    /// <summary>The sky's colour overhead and at the horizon, in sRGB 0 to 1.</summary>
    public Vector3 SkyTop { get; init; } = new(0.55f, 0.68f, 0.9f);

    public Vector3 SkyHorizon { get; init; } = new(0.86f, 0.88f, 0.9f);

    /// <summary>Nothing behind the level: the background comes out see-through, for a PNG to lay over something.</summary>
    public bool TransparentBackground { get; init; }

    /// <summary>Stops of light: 0 as it is, +1 twice as bright.</summary>
    public float Exposure { get; init; }

    /// <summary>How much a glowing colour lights, against a sun of strength 1.</summary>
    public float EmissionStrength { get; init; } = 3f;

    /// <summary>Distance fog: how thick, 0 for none, in the sky's colour.</summary>
    public float Fog { get; init; }

    public RenderSettings Clamped() => this with
    {
        Width = Math.Clamp(Width, 16, 8192),
        Height = Math.Clamp(Height, 16, 8192),
        Samples = Math.Clamp(Samples, 1, 65536),
        Bounces = Math.Clamp(Bounces, 0, 16),
        SkyStrength = Math.Clamp(SkyStrength, 0f, 16f),
        Exposure = Math.Clamp(Exposure, -8f, 8f),
        EmissionStrength = Math.Clamp(EmissionStrength, 0f, 64f),
        Fog = Math.Clamp(Fog, 0f, 1f),
    };
}

/// <summary>Where a render looks from: perspective or orthographic, like the viewport's camera.</summary>
/// <param name="VerticalFov">Degrees, top to bottom of the image; perspective only.</param>
/// <param name="OrthographicHeight">World units the image spans top to bottom; orthographic only.</param>
public readonly record struct RenderCamera(Vector3 Position, Vector3 Forward, Vector3 Up, float VerticalFov, bool Orthographic, float OrthographicHeight)
{
    /// <summary>The ray through a point of the image, <paramref name="u"/> and <paramref name="v"/> from −1 to 1, v up.</summary>
    public Ray Through(float u, float v, float aspect)
    {
        Vector3 forward = Vector3.Normalize(Forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Up));
        Vector3 up = Vector3.Cross(right, forward);

        if (Orthographic)
        {
            float half = OrthographicHeight * 0.5f;
            Vector3 origin = Position + (right * (u * half * aspect)) + (up * (v * half));
            return new Ray(origin, forward);
        }

        float tan = MathF.Tan(VerticalFov * 0.5f * (MathF.PI / 180f));
        Vector3 direction = forward + (right * (u * tan * aspect)) + (up * (v * tan));
        return new Ray(Position, Vector3.Normalize(direction));
    }
}

/// <summary>
/// A level made still for rendering: every shown object's voxels, the palette's colours and
/// materials, the lights. A copy, so editing can go on while it renders, and nothing it reads can
/// change halfway through.
/// </summary>
public sealed class RenderScene
{
    internal sealed record Model(VoxelWorld Grid, ObjectTransform Transform, Vector3 WorldMin, Vector3 WorldMax, Int3 CellMin, Int3 CellMax);

    internal readonly record struct Light(LightKind Kind, Vector3 Position, Vector3 Direction, Vector3 Colour, float Range, float ConeOuter, float ConeInner);

    internal Model[] Objects { get; }

    internal Light[] Lights { get; }

    /// <summary>Each palette entry's colour, linear.</summary>
    internal Vector3[] Colours { get; } = new Vector3[Palette.Size];

    internal VoxelMaterial[] Materials { get; } = new VoxelMaterial[Palette.Size];

    internal float Ambient { get; }

    private RenderScene(Model[] objects, Light[] lights, float ambient)
    {
        Objects = objects;
        Lights = lights;
        Ambient = ambient;
    }

    public static RenderScene Capture(VoxelScene scene)
    {
        var objects = new List<Model>();
        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible || !o.Shown.TryGetBounds(out Int3 min, out Int3 max) || !o.TryGetWorldBounds(out Vector3 worldMin, out Vector3 worldMax))
            {
                continue;
            }

            objects.Add(new Model(o.Shown.Copy(), o.Transform, worldMin, worldMax, min, max));
        }

        var lights = new List<Light>();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Visible)
            {
                continue;
            }

            // The same cone as the viewport's: blend is how much of it is spent fading.
            float half = light.SpotAngle * 0.5f * (MathF.PI / 180f);
            float outer = MathF.Cos(half);
            float inner = MathF.Max(MathF.Cos(half * (1f - light.SpotBlend)), outer + 1e-4f);
            lights.Add(new Light(light.Kind, light.Position, light.Direction, light.Colour * light.Intensity, light.Range, outer, inner));
        }

        var captured = new RenderScene([.. objects], [.. lights], scene.Ambient);
        for (int i = 0; i < Palette.Size; i++)
        {
            Color32 colour = scene.Palette[i];
            captured.Colours[i] = new Vector3(Linear(colour.R), Linear(colour.G), Linear(colour.B));
            captured.Materials[i] = scene.Palette.Material(i);
        }

        return captured;
    }

    internal static float Linear(byte srgb)
    {
        float c = srgb / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
