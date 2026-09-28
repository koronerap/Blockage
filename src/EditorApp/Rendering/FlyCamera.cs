using System.Numerics;
using EditorApp.Core.Raycast;

namespace EditorApp.Rendering;

/// <summary>One of the six views straight along an axis, named the way Blender names them.</summary>
public enum AlignedView
{
    /// <summary>From +Z, looking towards -Z.</summary>
    Front,

    /// <summary>From -Z, looking towards +Z.</summary>
    Back,

    /// <summary>From +X, looking towards -X.</summary>
    Right,

    /// <summary>From -X, looking towards +X.</summary>
    Left,

    /// <summary>From +Y, looking straight down.</summary>
    Top,

    /// <summary>From -Y, looking straight up.</summary>
    Bottom,
}

/// <summary>
/// Free-flying camera (EditorApp.md §1). Yaw/pitch look with the right mouse button, WASD to move,
/// Q/E for vertical, shift to accelerate. All movement is scaled by delta time.
///
/// It also carries a pivot — a point a known distance straight ahead — which is what orbiting turns
/// around, what zooming closes in on, and what sets the scale of the orthographic projection. Flying
/// takes the pivot along with it; nothing needs to know where the pivot is to use the camera.
/// </summary>
public sealed class FlyCamera
{
    private const float MaxPitch = 89f * (MathF.PI / 180f);

    /// <summary>
    /// Set when an aligned view switched to orthographic by itself, so the first free turn can switch
    /// back. A projection the user chose on purpose is left alone.
    /// </summary>
    private bool _orthographicByAlignment;

    private bool _orthographic;

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

    /// <summary>Parallel rather than perspective projection: distance no longer shrinks anything.</summary>
    public bool Orthographic
    {
        get => _orthographic;
        set
        {
            _orthographic = value;
            _orthographicByAlignment = false;
        }
    }

    /// <summary>How far ahead the pivot is. Orbiting keeps it; zooming changes it.</summary>
    public float PivotDistance { get; set; } = 40f;

    /// <summary>The point orbiting turns around and zooming closes in on.</summary>
    public Vector3 Pivot => Position + (Forward * PivotDistance);

    /// <summary>
    /// Height of the orthographic view, in world units. Taken from the pivot so that the plane
    /// through the pivot is framed exactly as the perspective view frames it: switching projection
    /// changes how depth reads, not how big the thing being looked at is.
    /// </summary>
    public float OrthographicHeight => 2f * MathF.Max(PivotDistance, 0.01f) * MathF.Tan(FieldOfView * 0.5f);

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    /// <summary>
    /// From the yaw alone. It is what crossing Forward with world up comes to anyway, but that cross
    /// product vanishes looking straight up or down — which is exactly where a top view has to be.
    /// Worked out this way it stays defined at ninety degrees, so an aligned view can be exact rather
    /// than tipped by the one degree that would show every side face as a sliver in orthographic.
    /// </summary>
    public Vector3 Right => new(-MathF.Cos(Yaw), 0f, MathF.Sin(Yaw));

    public Vector3 Up => Vector3.Cross(Right, Forward);

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    public Matrix4x4 ProjectionMatrix(float aspectRatio)
    {
        aspectRatio = MathF.Max(aspectRatio, 0.0001f);

        if (!Orthographic)
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);
        }

        // The near plane is pulled behind the camera. In a parallel projection the camera's position
        // along the view is arbitrary, and clipping everything behind it would cut away parts of the
        // model that are plainly in view.
        float height = OrthographicHeight;
        return Matrix4x4.CreateOrthographic(height * aspectRatio, height, -FarPlane, FarPlane);
    }

    public Matrix4x4 ViewProjection(float aspectRatio) => ViewMatrix * ProjectionMatrix(aspectRatio);

    public void Look(Vector2 mouseDelta)
    {
        // Holding the button without moving is not a turn, and must not throw away an aligned view.
        if (mouseDelta == Vector2.Zero)
        {
            return;
        }

        LeaveAutomaticOrthographic();
        Yaw -= mouseDelta.X * LookSensitivity;
        Pitch = Math.Clamp(Pitch - mouseDelta.Y * LookSensitivity, -MaxPitch, MaxPitch);
    }

    /// <summary>
    /// Turns around the pivot, the way Blender's middle mouse does. The scene follows the mouse: drag
    /// right and the model turns right, drag down and its top tips towards you.
    /// </summary>
    public void Orbit(Vector2 mouseDelta) =>
        OrbitBy(-mouseDelta.X * LookSensitivity, -mouseDelta.Y * LookSensitivity);

    /// <summary>
    /// Turns around the pivot by angles rather than pixels, for the keys that step the view round.
    ///
    /// Allowed all the way to straight up and down, unlike looking: the sideways axis comes from the
    /// yaw alone, so a top view can be spun about its own centre without first tipping off it.
    /// </summary>
    public void OrbitBy(float yaw, float pitch)
    {
        if (yaw == 0f && pitch == 0f)
        {
            return;
        }

        LeaveAutomaticOrthographic();

        Vector3 pivot = Pivot;
        Yaw += yaw;
        Pitch = Math.Clamp(Pitch + pitch, -MathF.PI / 2f, MathF.PI / 2f);
        Position = pivot - (Forward * PivotDistance);
    }

    /// <summary>
    /// Moves towards or away from the pivot by whole wheel steps, each one the same proportion of the
    /// remaining distance — so zooming feels the same a voxel away and across the level. In
    /// orthographic this is also what changes the scale, since the scale is taken from the distance.
    /// </summary>
    public void Zoom(float steps)
    {
        if (!float.IsFinite(steps) || steps == 0f)
        {
            return;
        }

        Vector3 pivot = Pivot;
        PivotDistance = Math.Clamp(PivotDistance * MathF.Pow(0.85f, steps), 0.1f, FarPlane * 0.5f);
        Position = pivot - (Forward * PivotDistance);
    }

    /// <summary>
    /// Looks straight along an axis, keeping the pivot where it is, and — the first time, from a
    /// perspective view — switches to orthographic, as Blender does. A view along an axis in
    /// perspective still shows the sides of everything off centre, which is most of what an aligned
    /// view is for getting rid of.
    /// </summary>
    public void Align(AlignedView view)
    {
        Vector3 pivot = Pivot;

        (Yaw, Pitch) = view switch
        {
            AlignedView.Front => (MathF.PI, 0f),
            AlignedView.Back => (0f, 0f),
            AlignedView.Right => (-MathF.PI / 2f, 0f),
            AlignedView.Left => (MathF.PI / 2f, 0f),

            // Looking down, with -Z up the screen: the same way round as the front view tipped
            // forward, so going from one to the other does not also spin the picture.
            AlignedView.Top => (MathF.PI, -MathF.PI / 2f),
            _ => (MathF.PI, MathF.PI / 2f),
        };

        Position = pivot - (Forward * PivotDistance);

        if (!Orthographic)
        {
            Orthographic = true;
            _orthographicByAlignment = true;
        }
    }

    /// <summary>Which aligned view the camera is looking along exactly, if any.</summary>
    public AlignedView? CurrentAlignedView()
    {
        const float Tolerance = 1e-4f;
        Vector3 forward = Forward;

        foreach (AlignedView view in Enum.GetValues<AlignedView>())
        {
            if (Vector3.Distance(forward, LookDirection(view)) < Tolerance)
            {
                return view;
            }
        }

        return null;
    }

    /// <summary>The view from the other side: front and back, right and left, top and bottom.</summary>
    public static AlignedView Opposite(AlignedView view) => view switch
    {
        AlignedView.Front => AlignedView.Back,
        AlignedView.Back => AlignedView.Front,
        AlignedView.Right => AlignedView.Left,
        AlignedView.Left => AlignedView.Right,
        AlignedView.Top => AlignedView.Bottom,
        _ => AlignedView.Top,
    };

    /// <summary>The direction an aligned view looks in.</summary>
    public static Vector3 LookDirection(AlignedView view) => view switch
    {
        AlignedView.Front => -Vector3.UnitZ,
        AlignedView.Back => Vector3.UnitZ,
        AlignedView.Right => -Vector3.UnitX,
        AlignedView.Left => Vector3.UnitX,
        AlignedView.Top => -Vector3.UnitY,
        _ => Vector3.UnitY,
    };

    /// <summary>
    /// A free turn after an aligned view goes back to perspective — but only if the aligned view is
    /// what switched to orthographic. One chosen deliberately stays.
    /// </summary>
    private void LeaveAutomaticOrthographic()
    {
        if (_orthographicByAlignment)
        {
            Orthographic = false;
        }
    }

    /// <summary>
    /// World units one pixel of cursor travel covers at a given distance. In perspective that depends
    /// on the distance; in orthographic it does not, and it is the same everywhere on screen.
    /// </summary>
    public float WorldPerPixel(float distance, Vector2 viewportSize) =>
        (Orthographic ? OrthographicHeight : 2f * MathF.Max(distance, 0.1f) * MathF.Tan(FieldOfView * 0.5f))
        / MathF.Max(viewportSize.Y, 1f);

    /// <summary>
    /// Slides the camera sideways and vertically, keeping its direction. The conversion is exact
    /// rather than a tuned constant: at a given distance the view covers
    /// <c>2 * distance * tan(fov / 2)</c> of world height, so a pixel of cursor travel maps to a
    /// definite number of world units and the model stays glued to the cursor while panning.
    /// </summary>
    public void Pan(Vector2 mouseDelta, float distance, Vector2 viewportSize)
    {
        float worldPerPixel = WorldPerPixel(distance, viewportSize);

        // Content follows the cursor, so the camera moves the opposite way on X, and screen Y runs
        // down while world up runs the other way.
        Position += Right * (-mouseDelta.X * worldPerPixel) + Up * (mouseDelta.Y * worldPerPixel);
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

        Vector3 step = direction * (MoveSpeed * speedMultiplier * deltaSeconds);
        Position += step;

        // In orthographic, flying forward would change nothing on screen — distance does not shrink
        // anything. Closing in on the pivot instead is what it would have looked like in perspective.
        if (Orthographic)
        {
            PivotDistance = Math.Clamp(PivotDistance - Vector3.Dot(step, Forward), 0.1f, FarPlane * 0.5f);
        }
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

    /// <summary>Turns to face a point without moving.</summary>
    public void LookAt(Vector3 target)
    {
        Vector3 direction = target - Position;
        if (direction.LengthSquared() < 1e-6f)
        {
            return;
        }

        direction = Vector3.Normalize(direction);
        Pitch = Math.Clamp(MathF.Asin(direction.Y), -MaxPitch, MaxPitch);
        Yaw = MathF.Atan2(direction.X, direction.Z);
        PivotDistance = MathF.Max(Vector3.Distance(Position, target), 0.1f);
    }

    /// <summary>Places the camera so the given box fills a comfortable part of the view.</summary>
    public void FrameBox(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 1f);
        float distance = radius / MathF.Tan(FieldOfView * 0.5f) * 1.4f;

        Yaw = 45f * (MathF.PI / 180f);
        Pitch = -30f * (MathF.PI / 180f);
        PivotDistance = distance;
        Position = center - Forward * distance;
    }
}
