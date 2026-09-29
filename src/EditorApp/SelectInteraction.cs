using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>
/// Clicking and box-dragging to choose what the tools work on: the whole of the Select tool, and
/// what the Transform tool does wherever a press misses its gizmo. Shift adds — and Shift-clicking
/// the active one lets it go — Ctrl takes away, and a plain click on nothing lets go of everything.
///
/// Only a click or a box changes the selection. Passing the pointer over things never does: that
/// was what made the gizmo jump to whatever the object being moved slid across.
/// </summary>
public sealed class SelectInteraction(EditorSession session)
{
    /// <summary>How far the pointer travels before a press is a box rather than a click, in pixels.</summary>
    public const float BoxPixels = 4f;

    private Vector2 _press;
    private Vector2 _current;
    private bool _shift;
    private bool _control;

    public bool IsPressed { get; private set; }

    /// <summary>The box being drawn, in viewport pixels, once the press has become a drag; null before.</summary>
    public (Vector2 Min, Vector2 Max)? Box =>
        IsPressed && Vector2.Distance(_press, _current) > BoxPixels
            ? (Vector2.Min(_press, _current), Vector2.Max(_press, _current))
            : null;

    /// <summary>What the box will do to the selection, by the keys held when it began.</summary>
    public SelectionOperation Operation =>
        _shift ? SelectionOperation.Add : _control ? SelectionOperation.Subtract : SelectionOperation.Replace;

    public void OnPress(Vector2 mouse, bool shift, bool control)
    {
        IsPressed = true;
        _press = mouse;
        _current = mouse;
        _shift = shift;
        _control = control;
    }

    public void OnDrag(Vector2 mouse)
    {
        if (IsPressed)
        {
            _current = mouse;
        }
    }

    /// <summary>Ends the press: a click picks what was under it, a box what is inside it.</summary>
    /// <param name="under">What a click at a point in the viewport lands on — a light's icon, else an object — or 0.</param>
    /// <param name="inBox">Everything inside a box on screen that can be selected.</param>
    public void OnRelease(Func<Vector2, int> under, Func<Vector2, Vector2, IEnumerable<int>> inBox)
    {
        if (!IsPressed)
        {
            return;
        }

        (Vector2 Min, Vector2 Max)? box = Box;
        IsPressed = false;

        if (box is { } drawn)
        {
            session.SelectMany(inBox(drawn.Min, drawn.Max), Operation);
            return;
        }

        int id = under(_press);
        if (id == 0)
        {
            // Nothing there: a plain click lets go of everything, a modified one changes nothing.
            if (!_shift && !_control)
            {
                session.DeselectAll();
            }

            return;
        }

        if (_control)
        {
            session.Deselect(id);
        }
        else
        {
            session.ClickSelect(id, extend: _shift);
        }
    }

    /// <summary>The same, inside an object in Edit Mode: a click picks voxels as the mode says, a box the voxels in it.</summary>
    /// <param name="under">The voxel a click at a point lands on, or null.</param>
    /// <param name="inBox">The voxels inside a box on screen.</param>
    public void OnReleaseVoxels(Func<Vector2, Int3?> under, Func<Vector2, Vector2, IEnumerable<Int3>> inBox)
    {
        if (!IsPressed)
        {
            return;
        }

        (Vector2 Min, Vector2 Max)? box = Box;
        IsPressed = false;

        if (box is { } drawn)
        {
            session.SelectVoxels(inBox(drawn.Min, drawn.Max), Operation);
            return;
        }

        if (under(_press) is { } cell)
        {
            session.ClickVoxel(cell, _shift, _control);
        }
        else if (!_shift && !_control)
        {
            session.DeselectAllVoxels();
        }
    }

    public void Cancel() => IsPressed = false;

    /// <summary>
    /// The voxels of an object whose middles fall inside a box on screen. Seen ones only — the first
    /// voxel along the line of sight — unless <paramref name="throughWalls"/>, X-Ray's way.
    /// </summary>
    public static List<Int3> VoxelsInBox(VoxelObject o, FlyCamera camera, Vector2 viewport, Vector2 min, Vector2 max, bool throughWalls)
    {
        var found = new List<Int3>();

        foreach (Int3 cell in ClipboardOperations.Everything(o.Grid))
        {
            // Inside the model nothing is ever seen: only a voxel with an open face can be.
            if (!throughWalls && !VoxelSelecting.IsExposed(o.Grid, cell))
            {
                continue;
            }

            Vector3 centre = o.Transform.TransformPoint(cell.ToVector3() + new Vector3(0.5f));
            if (!camera.TryProjectToScreen(centre, viewport, out Vector2 at)
                || at.X < min.X || at.X > max.X || at.Y < min.Y || at.Y > max.Y)
            {
                continue;
            }

            if (!throughWalls)
            {
                Core.Raycast.Ray local = o.Transform.InverseTransformRay(camera.ScreenPointToRay(at, viewport));
                if (!Core.Raycast.VoxelRaycaster.TryCast(o.Grid, local, out Core.Raycast.RaycastHit hit) || hit.Voxel != cell)
                {
                    continue;
                }
            }

            found.Add(cell);
        }

        return found;
    }

    /// <summary>
    /// What a box on screen holds: every object that can be selected whose own box, seen from the
    /// camera, overlaps it, and every light whose icon is inside it.
    /// </summary>
    public static List<int> InBox(
        VoxelScene scene,
        FlyCamera camera,
        Vector2 viewport,
        Vector2 min,
        Vector2 max,
        Func<SceneLight, bool> lightShown)
    {
        var found = new List<int>();

        foreach (VoxelObject o in scene.Objects)
        {
            if (scene.CanSelect(o.Id) && ScreenBounds(o, camera, viewport) is { } bounds
                && bounds.Min.X <= max.X && bounds.Max.X >= min.X
                && bounds.Min.Y <= max.Y && bounds.Max.Y >= min.Y)
            {
                found.Add(o.Id);
            }
        }

        foreach (SceneLight light in scene.Lights)
        {
            if (scene.CanSelect(light.Id) && lightShown(light)
                && camera.TryProjectToScreen(light.Position, viewport, out Vector2 at)
                && at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y)
            {
                found.Add(light.Id);
            }
        }

        return found;
    }

    /// <summary>Where an object's box falls on screen, from those of its corners in front of the camera; null for none.</summary>
    public static (Vector2 Min, Vector2 Max)? ScreenBounds(VoxelObject o, FlyCamera camera, Vector2 viewport)
    {
        if (!o.TryGetLocalBounds(out Vector3 low, out Vector3 high))
        {
            return null;
        }

        Vector2? min = null;
        Vector2? max = null;

        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? low.X : high.X,
                (corner & 2) == 0 ? low.Y : high.Y,
                (corner & 4) == 0 ? low.Z : high.Z);

            if (camera.TryProjectToScreen(o.Transform.TransformPoint(local), viewport, out Vector2 screen))
            {
                min = min is { } a ? Vector2.Min(a, screen) : screen;
                max = max is { } b ? Vector2.Max(b, screen) : screen;
            }
        }

        return min is { } lo && max is { } hi ? (lo, hi) : null;
    }
}
