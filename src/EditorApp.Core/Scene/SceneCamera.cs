using System.Numerics;
using EditorApp.Core.Rendering;

namespace EditorApp.Core.Scene;

/// <summary>How a camera projects: with perspective, flat, or flat at the angle isometric games are drawn at.</summary>
public enum CameraKind
{
    Perspective,

    /// <summary>Parallel rays: distance shrinks nothing.</summary>
    Orthographic,

    /// <summary>
    /// Orthographic, turned a quarter of the way round and tipped 30° down — the 2:1 angle isometric
    /// sprites are drawn at, where a step along the ground is two pixels across for one down.
    /// </summary>
    Isometric,
}

/// <summary>
/// A camera saved with the level (Fullreleaseplan 5.4): a view to come back to, and what a render
/// is seen from. It is made where the viewport stands, and moved the same way — looked through,
/// the view moved, and the camera set to the view again — so it is placed with the navigation the
/// viewport already has. Never exported.
///
/// It turns as the viewport's camera does: <paramref name="Yaw"/> about +Y and <paramref name="Pitch"/>
/// above the horizon, in radians, with no roll.
/// </summary>
/// <param name="FieldOfView">Degrees, top to bottom of the picture; perspective only.</param>
/// <param name="OrthographicHeight">World units the picture spans top to bottom; orthographic and isometric.</param>
/// <param name="PivotDistance">How far ahead it looks at: what the view orbits when it is looked through.</param>
public sealed record SceneCamera(
    int Id,
    string Name,
    Vector3 Position,
    float Yaw,
    float Pitch,
    float FieldOfView,
    CameraKind Kind,
    float OrthographicHeight,
    float PivotDistance)
{
    public const float MinFieldOfView = 1f;
    public const float MaxFieldOfView = 170f;

    /// <summary>How far an isometric camera looks down: the 2:1 pixel-art angle.</summary>
    public const float IsometricPitchDegrees = -30f;

    private const float MaxPitch = 89f * (MathF.PI / 180f);

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    public Vector3 Right => new(-MathF.Cos(Yaw), 0f, MathF.Sin(Yaw));

    public Vector3 Up => Vector3.Cross(Right, Forward);

    /// <summary>The point it looks at.</summary>
    public Vector3 Pivot => Position + (Forward * PivotDistance);

    public bool IsOrthographic => Kind != CameraKind.Perspective;

    public RenderCamera ToRenderCamera() => new(Position, Forward, Up, FieldOfView, IsOrthographic, OrthographicHeight);

    /// <summary>Turned to the nearest isometric angle, still looking at the same point from as far.</summary>
    public SceneCamera Isometric()
    {
        float quarter = MathF.PI / 2f;
        float eighth = MathF.PI / 4f;
        float yaw = (MathF.Round((Yaw - eighth) / quarter) * quarter) + eighth;
        float pitch = IsometricPitchDegrees * (MathF.PI / 180f);
        var turned = this with { Yaw = yaw, Pitch = pitch, Kind = CameraKind.Isometric };
        return turned with { Position = Pivot - (turned.Forward * PivotDistance) };
    }

    /// <summary>Its settings kept to what can be rendered.</summary>
    public SceneCamera Clamped() => this with
    {
        Pitch = Math.Clamp(Pitch, -MaxPitch, MaxPitch),
        FieldOfView = Math.Clamp(FieldOfView, MinFieldOfView, MaxFieldOfView),
        OrthographicHeight = Math.Clamp(OrthographicHeight, 0.01f, 100_000f),
        PivotDistance = Math.Clamp(PivotDistance, 0.01f, 100_000f),
    };
}
