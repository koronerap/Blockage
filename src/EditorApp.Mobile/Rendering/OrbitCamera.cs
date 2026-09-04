using System.Numerics;
using EditorApp.Rendering;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// A camera that turns around a point rather than flying through the scene.
///
/// The desktop's <see cref="FlyCamera"/> is the wrong shape for a phone: WASD has nowhere to live,
/// and a finger has no way to say "move forward while looking sideways". What a finger is good at is
/// spinning something round to look at it. So the fly camera is kept — every ray, projection and
/// framing calculation in the editor already goes through it — and this drives it, holding a target
/// and a distance and placing the camera on the far end of them.
/// </summary>
public sealed class OrbitCamera
{
    private const float MaxPitch = 89f * (MathF.PI / 180f);

    /// <summary>
    /// Radians per pixel of finger travel. The desktop's mouse sensitivity unchanged: a finger and a
    /// mouse both move in screen pixels, and this is the number that was already comfortable.
    /// </summary>
    public float OrbitSensitivity { get; set; } = 0.0035f;

    public float MinDistance { get; set; } = 0.5f;

    public float MaxDistance { get; set; } = 2000f;

    /// <summary>The point the camera looks at and turns around.</summary>
    public Vector3 Target { get; set; }

    public float Distance { get; set; } = 40f;

    /// <summary>
    /// The camera itself. Its position is derived — writing to it directly would be overwritten on
    /// the next gesture — but everything else about it is the editor's own camera.
    /// </summary>
    public FlyCamera Camera { get; } = new();

    public OrbitCamera() => Apply();

    /// <summary>
    /// One finger dragging: swing around the target.
    ///
    /// What the finger is holding is the model, not the camera, so the scene has to travel with it:
    /// drag right and the model spins right, drag down and it tips towards you. Both come out as
    /// the camera going the other way around the target, which is why the signs here read as though
    /// they were backwards. They are the same signs the desktop's mouse look uses, and they mean
    /// the opposite thing there — a head turning right sends the scene left. Tests project a fixed
    /// world point before and after, because reading the arithmetic is exactly how this gets
    /// believed wrong.
    /// </summary>
    public void Orbit(Vector2 delta)
    {
        Camera.Yaw -= delta.X * OrbitSensitivity;
        Camera.Pitch = Math.Clamp(Camera.Pitch - delta.Y * OrbitSensitivity, -MaxPitch, MaxPitch);
        Apply();
    }

    /// <summary>
    /// Two fingers dragging: slide the target sideways and vertically. The conversion is exact rather
    /// than a tuned constant — at this distance the view covers <c>2 * distance * tan(fov / 2)</c> of
    /// world height, so the scene stays glued to the fingers instead of drifting away from them.
    /// </summary>
    public void Pan(Vector2 delta, Vector2 viewportSize)
    {
        float worldPerPixel = 2f * MathF.Max(Distance, 0.1f) * MathF.Tan(Camera.FieldOfView * 0.5f)
            / MathF.Max(viewportSize.Y, 1f);

        // Content follows the fingers, so the target moves against them on X, and screen Y runs down
        // while world up runs the other way.
        Target += Camera.Right * (-delta.X * worldPerPixel) + Camera.Up * (delta.Y * worldPerPixel);
        Apply();
    }

    /// <summary>Pinching: <paramref name="scale"/> above 1 is fingers spreading, which comes closer.</summary>
    public void Zoom(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return;
        }

        Distance = Math.Clamp(Distance / scale, MinDistance, MaxDistance);
        Apply();
    }

    /// <summary>Puts a box comfortably in view, keeping the angle the camera is already at.</summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        Target = (min + max) * 0.5f;

        float radius = MathF.Max((max - min).Length() * 0.5f, 1f);
        Distance = Math.Clamp(
            radius / MathF.Tan(Camera.FieldOfView * 0.5f) * 1.4f,
            MinDistance,
            MaxDistance);

        Apply();
    }

    private void Apply() => Camera.Position = Target - Camera.Forward * Distance;
}
