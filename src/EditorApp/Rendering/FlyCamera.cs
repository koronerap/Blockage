using System.Numerics;
using EditorApp.Core.Raycast;

namespace EditorApp.Rendering;

/// <summary>
/// Free-flying camera (EditorApp.md §1). Yaw/pitch look with the right mouse button, WASD to move,
/// Q/E for vertical, shift to accelerate. All movement is scaled by delta time.
/// </summary>
public sealed class FlyCamera
{
    private const float MaxPitch = 89f * (MathF.PI / 180f);

    public Vector3 Position { get; set; } = new(-24f, 24f, -24f);

    /// <summary>Rotation around +Y, in radians.</summary>
    public float Yaw { get; set; } = 45f * (MathF.PI / 180f);

    /// <summary>Rotation above the horizon, in radians. Clamped short of straight up/down.</summary>
    public float Pitch { get; set; } = -30f * (MathF.PI / 180f);

    public float FieldOfView { get; set; } = 60f * (MathF.PI / 180f);

    public float NearPlane { get; set; } = 0.05f;

    public float FarPlane { get; set; } = 4000f;

    public float MoveSpeed { get; set; } = 16f;

    public float LookSensitivity { get; set; } = 0.0035f;

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);

    public Matrix4x4 ProjectionMatrix(float aspectRatio) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, MathF.Max(aspectRatio, 0.0001f), NearPlane, FarPlane);

    public Matrix4x4 ViewProjection(float aspectRatio) => ViewMatrix * ProjectionMatrix(aspectRatio);

    public void Look(Vector2 mouseDelta)
    {
        Yaw -= mouseDelta.X * LookSensitivity;
        Pitch = Math.Clamp(Pitch - mouseDelta.Y * LookSensitivity, -MaxPitch, MaxPitch);
    }

    /// <param name="movement">X = right, Y = up, Z = forward, each in [-1, 1].</param>
    public void Move(Vector3 movement, float deltaSeconds, float speedMultiplier = 1f)
    {
        if (movement.LengthSquared() < 1e-6f)
        {
            return;
        }

        Vector3 direction = Right * movement.X + Vector3.UnitY * movement.Y + Forward * movement.Z;
        if (direction.LengthSquared() > 1e-6f)
        {
            direction = Vector3.Normalize(direction);
        }

        Position += direction * (MoveSpeed * speedMultiplier * deltaSeconds);
    }

    /// <summary>
    /// Builds a picking ray through a pixel. Uses the inverse view-projection so it stays correct
    /// no matter how the projection is set up.
    /// </summary>
    public Ray ScreenPointToRay(Vector2 pixel, Vector2 viewportSize)
    {
        float ndcX = 2f * pixel.X / MathF.Max(viewportSize.X, 1f) - 1f;
        float ndcY = 1f - 2f * pixel.Y / MathF.Max(viewportSize.Y, 1f);

        if (!Matrix4x4.Invert(ViewProjection(viewportSize.X / MathF.Max(viewportSize.Y, 1f)), out Matrix4x4 inverse))
        {
            return new Ray(Position, Forward);
        }

        Vector4 near = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), inverse);
        Vector4 far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inverse);

        var nearPoint = new Vector3(near.X, near.Y, near.Z) / near.W;
        var farPoint = new Vector3(far.X, far.Y, far.Z) / far.W;

        return Ray.Normalized(nearPoint, farPoint - nearPoint);
    }

    /// <summary>
    /// Projects a world point to a pixel. Returns false when the point is behind the camera, where
    /// the perspective divide would flip it to a nonsense position on screen.
    ///
    /// Gizmo picking works in screen space (EditorApp.md, "Transform"), so this is the primitive the
    /// arrow, ring and edge hit tests are all built on — none of them has a collider.
    /// </summary>
    public bool TryProjectToScreen(Vector3 world, Vector2 viewportSize, out Vector2 screen)
    {
        screen = Vector2.Zero;

        Matrix4x4 viewProjection = ViewProjection(viewportSize.X / MathF.Max(viewportSize.Y, 1f));
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);

        if (clip.W <= 1e-5f)
        {
            return false;
        }

        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        screen = new Vector2(
            (ndc.X * 0.5f + 0.5f) * viewportSize.X,
            (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y);

        return true;
    }

    /// <summary>Places the camera so the given box fills a comfortable part of the view.</summary>
    public void FrameBox(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 1f);
        float distance = radius / MathF.Tan(FieldOfView * 0.5f) * 1.4f;

        Yaw = 45f * (MathF.PI / 180f);
        Pitch = -30f * (MathF.PI / 180f);
        Position = center - Forward * distance;
    }
}
