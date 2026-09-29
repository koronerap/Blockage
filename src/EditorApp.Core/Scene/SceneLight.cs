using System.Numerics;

namespace EditorApp.Core.Scene;

/// <summary>What kind of light: the sun, a bulb, or a torch.</summary>
public enum LightKind
{
    /// <summary>Parallel rays from infinitely far away. Where it stands does not matter, only where it points.</summary>
    Directional,

    /// <summary>Shines every way from one point, fading out by its range.</summary>
    Point,

    /// <summary>A point light narrowed to a cone.</summary>
    Spot,
}

/// <summary>
/// Anything the Transform tool can move and turn: a voxel object or a light. What the gizmo needs
/// is a placement and a point to put itself on; everything else about the two is different.
/// </summary>
public interface IPlaceable
{
    int Id { get; }

    string Name { get; }

    ObjectTransform Transform { get; set; }

    /// <summary>What it is a child of — an object's id — or 0. It moves with its parent.</summary>
    int ParentId { get; }

    /// <summary>Where the gizmo sits and what the rings turn about.</summary>
    Vector3 WorldCentre();
}

/// <summary>Every setting of a light at one moment, so an edit can be undone as a whole.</summary>
public readonly record struct LightState(
    string Name,
    LightKind Kind,
    ObjectTransform Transform,
    Vector3 Colour,
    float Intensity,
    float Range,
    float SpotAngle,
    float SpotBlend,
    bool Visible);

/// <summary>
/// A light placed in the level. Saved with it and drawn with it, and never exported: a level
/// becomes a mesh and a texture, and the engine it goes to lights it its own way. These are for
/// seeing, while building, how the shapes will catch light — the sun a scene will have, a torch on
/// a wall, the glow over a doorway.
///
/// A light points along its own -Y, so one that has not been turned shines straight down, and the
/// Transform tool's rings aim it.
/// </summary>
public sealed class SceneLight(int id, LightKind kind, string name) : IPlaceable
{
    public const float MaxIntensity = 20f;
    public const float MinRange = 0.1f;
    public const float MaxRange = 1000f;
    public const float MinSpotAngle = 1f;
    public const float MaxSpotAngle = 179f;

    /// <summary>
    /// A key light over the viewer's left shoulder from the opening camera — the light the viewport
    /// had before lights were things in the level, kept so a level looks the same as it did.
    /// </summary>
    public const float SunAzimuth = 200f;

    public const float SunElevation = 50f;
    public const float SunIntensity = 0.70f;

    public int Id { get; } = id;

    public string Name { get; set; } = name;

    public LightKind Kind { get; set; } = kind;

    private ObjectTransform _transform = ObjectTransform.Identity;

    /// <summary>
    /// Where it is in the world. Set by anything that moves it; a parent's move sets it through
    /// <see cref="Follow"/> instead, which is how children go with their parent without every place
    /// that moves an object having to know about children.
    /// </summary>
    public ObjectTransform Transform
    {
        get => _transform;
        set
        {
            _transform = value;
            Moved?.Invoke(this, false);
        }
    }

    /// <summary>The object this is a child of; 0 for none. An id with no object behind it — a deleted parent — is no parent.</summary>
    public int ParentId { get; internal set; }

    /// <summary>Where this sits in its parent's frame, kept while it has one.</summary>
    public ObjectTransform ParentOffset { get; internal set; } = ObjectTransform.Identity;

    /// <summary>Told when this moves, so its children follow and its own offset stays true. Set by the scene.</summary>
    internal Action<IPlaceable, bool>? Moved { get; set; }

    /// <summary>Moved by its parent: the world placement changes, the offset it is held at does not.</summary>
    internal void Follow(ObjectTransform world)
    {
        _transform = world;
        Moved?.Invoke(this, true);
    }

    /// <summary>Red, green and blue, each 0 to 1.</summary>
    public Vector3 Colour { get; set; } = Vector3.One;

    public float Intensity { get; set; } = 1f;

    /// <summary>Point and spot: how far the light reaches, in world units. It fades to nothing there.</summary>
    public float Range { get; set; } = 15f;

    /// <summary>Spot: the full width of the cone, in degrees.</summary>
    public float SpotAngle { get; set; } = 45f;

    /// <summary>Spot: how soft the cone's edge is, from a hard edge at 0 to fading from the centre at 1.</summary>
    public float SpotBlend { get; set; } = 0.15f;

    /// <summary>Off lights stay in the level but light nothing.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>A locked light still shines, but its icon cannot be picked, moved or aimed.</summary>
    public bool Locked { get; set; }

    public Vector3 Position => Transform.Position;

    /// <summary>Which way the light shines — the direction its rays travel.</summary>
    public Vector3 Direction => Vector3.Normalize(Vector3.Transform(-Vector3.UnitY, Transform.Rotation));

    public Vector3 WorldCentre() => Transform.Position;

    public LightState State => new(Name, Kind, Transform, Colour, Intensity, Range, SpotAngle, SpotBlend, Visible);

    /// <summary>Sets everything at once, clamped to what a light can be.</summary>
    public void Apply(LightState state)
    {
        Name = state.Name;
        Kind = state.Kind;
        Transform = state.Transform with { VoxelSize = 1f };
        Colour = Vector3.Clamp(state.Colour, Vector3.Zero, Vector3.One);
        Intensity = Math.Clamp(Finite(state.Intensity, Intensity), 0f, MaxIntensity);
        Range = Math.Clamp(Finite(state.Range, Range), MinRange, MaxRange);
        SpotAngle = Math.Clamp(Finite(state.SpotAngle, SpotAngle), MinSpotAngle, MaxSpotAngle);
        SpotBlend = Math.Clamp(Finite(state.SpotBlend, SpotBlend), 0f, 1f);
        Visible = state.Visible;
    }

    /// <summary>A copy with a new id: every setting, and the same parent at the same offset.</summary>
    public SceneLight Copy(int id) => new(id, Kind, Name)
    {
        Transform = Transform,
        Colour = Colour,
        Intensity = Intensity,
        Range = Range,
        SpotAngle = SpotAngle,
        SpotBlend = SpotBlend,
        Visible = Visible,
        Locked = Locked,
        ParentId = ParentId,
        ParentOffset = ParentOffset,
    };

    /// <summary>The turn that makes a light shine along <paramref name="direction"/>.</summary>
    public static Quaternion Aiming(Vector3 direction)
    {
        if (direction.LengthSquared() < 1e-12f)
        {
            return Quaternion.Identity;
        }

        Vector3 from = -Vector3.UnitY;
        Vector3 to = Vector3.Normalize(direction);
        float dot = Vector3.Dot(from, to);

        // Straight up is the one direction the shortest-arc formula cannot find an axis for.
        if (dot < -0.999999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        }

        Vector3 axis = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(axis, 1f + dot));
    }

    /// <summary>
    /// Compass bearing and height, in degrees, of where the light comes <i>from</i> — the way a
    /// person describes a sun. The opposite of the way it shines.
    /// </summary>
    public static Vector3 ShiningFrom(float azimuthDegrees, float elevationDegrees)
    {
        float azimuth = azimuthDegrees * (MathF.PI / 180f);
        float elevation = elevationDegrees * (MathF.PI / 180f);
        float horizontal = MathF.Cos(elevation);

        Vector3 towardsLight = new(
            horizontal * MathF.Sin(azimuth),
            MathF.Sin(elevation),
            horizontal * MathF.Cos(azimuth));

        return -Vector3.Normalize(towardsLight);
    }

    /// <summary>The bearing and height a direction of travel comes from — the inverse of <see cref="ShiningFrom"/>.</summary>
    public static (float Azimuth, float Elevation) AnglesOf(Vector3 direction)
    {
        Vector3 towardsLight = -Vector3.Normalize(direction);
        float elevation = MathF.Asin(Math.Clamp(towardsLight.Y, -1f, 1f)) * (180f / MathF.PI);
        float azimuth = MathF.Atan2(towardsLight.X, towardsLight.Z) * (180f / MathF.PI);

        return (azimuth < 0f ? azimuth + 360f : azimuth, elevation);
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
