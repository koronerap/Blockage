using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>
/// Pointing a sun or a spot by its aim line: take hold of the line, or the ring at its end, and drop
/// it on the model, and the light turns to shine at the spot under the cursor. The rotate rings aim
/// a light as well, but by angle, and what is wanted is nearly always "at that".
/// </summary>
public sealed class LightAimInteraction(EditorSession session)
{
    /// <summary>
    /// The share of the line nearest the light that does not take hold: the icon is there, and the
    /// move gizmo on it, and a press on those means them.
    /// </summary>
    private const float DeadStart = 0.3f;

    private ObjectTransform _before;

    /// <summary>How near the line a press has to land to take hold of it, in pixels.</summary>
    public float GrabPixels { get; set; } = 8f;

    /// <summary>The light being aimed, while it is.</summary>
    public SceneLight? Light { get; private set; }

    /// <summary>Where it is being pointed, in world space — the end of the line being dragged.</summary>
    public Vector3? Target { get; private set; }

    public bool IsAiming => Light is not null;

    /// <summary>Which way the light now comes from, in the Properties' own terms, for the cursor.</summary>
    public string Readout
    {
        get
        {
            if (Light is not { } light || Target is null)
            {
                return string.Empty;
            }

            (float azimuth, float elevation) = SceneLight.AnglesOf(light.Direction);
            return $"From {azimuth:0}°  Height {elevation:0}°";
        }
    }

    /// <summary>The light whose aim line a press here would take hold of, if any.</summary>
    /// <param name="shown">Whether a light's icon is on screen: a line that is not drawn cannot be grabbed.</param>
    public SceneLight? LineUnder(Vector2 mouse, Vector2 viewport, FlyCamera camera, Func<SceneLight, bool> shown)
    {
        SceneLight? found = null;
        float nearest = GrabPixels;

        foreach (SceneLight light in session.Scene.Lights)
        {
            if (!shown(light)
                || EditorOverlays.AimHandle(light, camera) is not { } handle
                || !camera.TryProjectToScreen(Vector3.Lerp(light.Position, handle, DeadStart), viewport, out Vector2 from)
                || !camera.TryProjectToScreen(handle, viewport, out Vector2 to))
            {
                continue;
            }

            float distance = DistanceToSegment(mouse, from, to);
            if (distance <= nearest)
            {
                nearest = distance;
                found = light;
            }
        }

        return found;
    }

    /// <summary>Takes hold of an aim line under the cursor. False when there is none, so the press goes on to the gizmo.</summary>
    public bool OnPress(Vector2 mouse, Vector2 viewport, FlyCamera camera, Func<SceneLight, bool> shown)
    {
        if (LineUnder(mouse, viewport, camera, shown) is not { } light)
        {
            return false;
        }

        Light = light;
        Target = null;
        _before = light.Transform;

        // The light in hand is the one the gizmo and the Properties show.
        session.SelectLight(light.Id);
        return true;
    }

    public void OnDrag(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (Light is not { } light)
        {
            return;
        }

        Vector3 target = PointUnder(camera.ScreenPointToRay(mouse, viewport), light, camera);
        Vector3 towards = target - light.Position;
        if (towards.LengthSquared() < 1e-8f)
        {
            return;
        }

        Target = target;
        session.ApplyTransform(light, light.Transform with { Rotation = SceneLight.Aiming(Vector3.Normalize(towards)) });
    }

    /// <summary>
    /// The model under the cursor; failing that the ground; failing that a point as far out as the
    /// light itself — so the line keeps following the cursor off the edge of the model.
    /// </summary>
    private Vector3 PointUnder(Ray ray, SceneLight light, FlyCamera camera)
    {
        if (session.Scene.TryPick(ray, out ScenePick pick))
        {
            return ray.Origin + (ray.Direction * pick.Distance);
        }

        if (MathF.Abs(ray.Direction.Y) > 1e-4f)
        {
            float along = -ray.Origin.Y / ray.Direction.Y;
            if (along > 0f)
            {
                return ray.Origin + (ray.Direction * along);
            }
        }

        return ray.Origin + (ray.Direction * Vector3.Distance(camera.Position, light.Position));
    }

    /// <summary>Letting go keeps the aim, as one step to undo.</summary>
    public void OnRelease()
    {
        if (Light is { } light)
        {
            session.PushTransformEdit(light, _before, $"Aim {light.Name}");
        }

        Light = null;
        Target = null;
    }

    public void Cancel()
    {
        if (Light is { } light)
        {
            session.ApplyTransform(light, _before);
        }

        Light = null;
        Target = null;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-6f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + (ab * t));
    }
}
