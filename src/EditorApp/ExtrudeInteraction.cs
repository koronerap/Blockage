using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>
/// Extrude's gesture state machine (EditorApp.md, "Extrude"): drag on a surface to select it, then
/// drag the arrow that comes out of the selection — or the selection itself. One flow, no separate
/// add and remove tools.
/// </summary>
public sealed class ExtrudeInteraction(EditorSession session)
{
    /// <summary>
    /// How near the arrow a press has to land to grab it, in pixels. Settable because a fingertip
    /// covers several times what a cursor points at — the desktop default would be unhittable on a
    /// phone.
    /// </summary>
    public float ArrowGrabPixels { get; set; } = 14f;

    /// <summary>
    /// Whether the arrow is there to take hold of — off with the header's tool gizmos. A press on the
    /// selection itself still pulls it.
    /// </summary>
    public bool ArrowEnabled { get; set; } = true;

    /// <summary>Arrow length in voxels. Long enough to aim at, short enough not to cover the model.</summary>
    private const float ArrowLength = 3f;

    private Face _dragFace;
    private int _dragPlane;
    private Int3 _dragAnchor;
    private Int3 _dragCurrent;
    private Vector2 _arrowPressPosition;
    private int _arrowBaseSteps;

    /// <summary>The rectangle being dragged out right now, before it is committed to the session.</summary>
    public FaceSelection? PendingSelection { get; private set; }

    /// <summary>
    /// What the in-progress drag will do to the selection. Captured when the drag starts and never
    /// re-read: letting go of Shift halfway through must not turn an add into a replace.
    /// </summary>
    public SelectionOperation PendingOperation { get; private set; } = SelectionOperation.Replace;

    public bool IsSelecting { get; private set; }

    public bool IsDraggingArrow { get; private set; }

    public bool IsBusy => IsSelecting || IsDraggingArrow;

    /// <summary>A new selection is being dragged out that will take the old one's place when it lands.</summary>
    public bool IsReplacing => IsSelecting && PendingOperation == SelectionOperation.Replace;

    /// <summary>
    /// Whether a face is part of the held surface, where it is on screen right now — carried out
    /// with the preview while the arrow is pulled. In the focused object's own space.
    /// </summary>
    public bool IsOnSelection(RaycastHit hit)
    {
        if (session.Selection is not { IsEmpty: false } selection || hit.Face != selection.Direction)
        {
            return false;
        }

        return selection.Contains(hit.Voxel - (FaceInfo.Offset(selection.Direction) * session.ExtrudeSteps));
    }

    /// <summary>Whether a press here would pull the surface: on the arrow, or on the selection itself.</summary>
    public bool WouldPull(ScenePick? pick, Vector2 mouse, Vector2 viewport, FlyCamera camera, bool shift, bool alt)
    {
        if (IsOnArrow(mouse, viewport, camera))
        {
            return true;
        }

        return !shift && !alt && pick is { } target && target.Object.Id == session.Scene.FocusId && IsOnSelection(target.Hit);
    }

    /// <summary>
    /// The arrow segment in <b>world</b> space, or null when there is nothing selected. The
    /// selection lives in the focused object's own space, so the object's transform is folded in
    /// here — otherwise the arrow would be drawn and grabbed in the wrong place for a moved or
    /// rotated object.
    /// </summary>
    public (Vector3 Start, Vector3 End)? Arrow() => ArrowAt(session.ExtrudeSteps);

    /// <summary>The arrow as it stands after a given number of steps: it follows the preview, so it stays attached to the moving surface.</summary>
    private (Vector3 Start, Vector3 End)? ArrowAt(int steps)
    {
        if (session.Selection is not { IsEmpty: false } selection || session.Scene.Focus is not { } focus)
        {
            return null;
        }

        Vector3 localDirection = FaceInfo.Normal(selection.Direction);
        Vector3 localStart = selection.ArrowOrigin() + localDirection * steps;

        Vector3 start = focus.Transform.TransformPoint(localStart);
        Vector3 direction = focus.Transform.TransformDirection(localDirection);
        return (start, start + direction * ArrowLength);
    }

    /// <param name="pick">
    /// Whatever the cursor is over, which may belong to an object that does not hold focus. Hovering
    /// one of those never takes focus while a selection is held; pressing on one does, because a
    /// click is a decision and passing the cursor over something is not.
    /// </param>
    public void OnPress(ScenePick? pick, Vector2 mouse, Vector2 viewport, FlyCamera camera, bool shift, bool alt)
    {
        // Tried first: the arrow is drawn in front of everything, so grabbing it must never be read
        // as a click on whatever happens to be behind it.
        if (TryGrabArrow(mouse, viewport, camera))
        {
            return;
        }

        if (pick is not { } target)
        {
            return;
        }

        // Pressing on the held surface pulls it, as the arrow does: the arrow is small and the
        // surface is not. Shift and Alt still mean add to it and take from it.
        if (!shift && !alt && target.Object.Id == session.Scene.FocusId && IsOnSelection(target.Hit))
        {
            StartPull(mouse);
            return;
        }

        if (target.Object.Id != session.Scene.FocusId)
        {
            // Clearing first is what lets the focus change through — a held selection pins it.
            session.ClearSelection();
            if (!session.TryFocus(target.Object.Id))
            {
                return;
            }
        }

        RaycastHit hit = target.Hit;

        // Starting a new selection while a drag is pending keeps the pending one rather than
        // silently throwing the user's work away.
        session.ConfirmExtrude();

        PendingOperation = shift
            ? SelectionOperation.Add
            : alt ? SelectionOperation.Subtract : SelectionOperation.Replace;

        if (session.ExtrudeSelectionMode == ExtrudeSelectionMode.Face)
        {
            session.SelectPatch(hit, PendingOperation);
            return;
        }

        _dragFace = hit.Face;
        _dragPlane = VoxelBox.Component(hit.Voxel, FaceInfo.Axis(hit.Face));
        _dragAnchor = hit.Voxel;
        _dragCurrent = hit.Voxel;
        IsSelecting = true;
        UpdatePending();
    }

    private bool TryGrabArrow(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (!IsOnArrow(mouse, viewport, camera))
        {
            return false;
        }

        StartPull(mouse);
        return true;
    }

    private bool IsOnArrow(Vector2 mouse, Vector2 viewport, FlyCamera camera) =>
        ArrowEnabled
        && Arrow() is { } arrow
        && camera.TryProjectToScreen(arrow.Start, viewport, out Vector2 start)
        && camera.TryProjectToScreen(arrow.End, viewport, out Vector2 end)
        && DistanceToSegment(mouse, start, end) <= ArrowGrabPixels;

    /// <summary>The drag along the arrow's axis, from wherever it was taken hold of.</summary>
    private void StartPull(Vector2 mouse)
    {
        IsDraggingArrow = true;
        _arrowPressPosition = mouse;
        _arrowBaseSteps = session.ExtrudeSteps;
    }

    public void OnDrag(RaycastHit? hover, Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (IsDraggingArrow)
        {
            session.PreviewExtrude(_arrowBaseSteps + StepsFromDrag(mouse, viewport, camera));
            return;
        }

        if (!IsSelecting || hover is not { } hit)
        {
            return;
        }

        _dragCurrent = hit.Voxel;
        UpdatePending();
    }

    /// <summary>
    /// Projects the cursor's travel onto the arrow's screen-space direction and rounds it to whole
    /// voxels. Rounding here rather than at apply time is what stops a drag ever sitting between
    /// two units.
    ///
    /// Measured from where the arrow stood when it was grabbed, not where the preview has carried
    /// it. In perspective a pixel is worth a different amount at each distance, so a reading taken
    /// from the moving arrow changed the answer, which moved the arrow, which changed the reading —
    /// a mouse held still flickered between two steps, and near the edge of the view between many.
    /// </summary>
    private int StepsFromDrag(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (session.Selection is not { } selection
            || session.Scene.Focus is not { } focus
            || ArrowAt(_arrowBaseSteps) is not { } arrow)
        {
            return 0;
        }

        // One step is one of this object's voxels, however big those are in the world.
        Vector3 axis = focus.Transform.TransformDirection(FaceInfo.Normal(selection.Direction)) * focus.VoxelSize;
        if (!camera.TryProjectToScreen(arrow.Start, viewport, out Vector2 origin)
            || !camera.TryProjectToScreen(arrow.Start + axis, viewport, out Vector2 oneUnit))
        {
            return 0;
        }

        Vector2 screenAxis = oneUnit - origin;
        float pixelsPerUnit = screenAxis.Length();

        // Edge on, one voxel is worth almost no pixels and the drag would be uncontrollable.
        if (pixelsPerUnit < 1.5f)
        {
            return 0;
        }

        float units = Vector2.Dot(mouse - _arrowPressPosition, screenAxis / pixelsPerUnit) / pixelsPerUnit;
        return (int)MathF.Round(units);
    }

    private void UpdatePending() =>
        PendingSelection = session.ExtrudeSelectionMode switch
        {
            ExtrudeSelectionMode.Ellipse => FaceSelection.Ellipse(session.World, _dragFace, _dragPlane, _dragAnchor, _dragCurrent),
            ExtrudeSelectionMode.Line => FaceSelection.Line(session.World, _dragFace, _dragPlane, _dragAnchor, _dragCurrent),
            _ => FaceSelection.Box(session.World, _dragFace, _dragPlane, _dragAnchor, _dragCurrent),
        };

    public void OnRelease()
    {
        if (IsSelecting)
        {
            if (PendingSelection is { } pending)
            {
                session.SetSelection(pending, PendingOperation);
            }

            PendingSelection = null;
            IsSelecting = false;
        }

        if (IsDraggingArrow)
        {
            // Letting go commits. The spec confirmed with Enter, on the reasoning that the release
            // was a chance to look at the result — but the result is already on screen during the
            // drag, and a gesture that needs a keystroke to stick is one you have to remember to
            // finish. Ctrl+Z is the way back, as it is for every other edit.
            //
            // A grab released without moving is a no-op: there is no preview to commit, and the
            // surface stays held to be pulled after all.
            //
            // A pull that did something is the job done: the surface is let go of with its highlight
            // and arrow, so the next press on what was just pulled out — or anywhere — starts a
            // selection afresh rather than pulling the old one again.
            if (session.ConfirmExtrude())
            {
                session.ClearSelection();
            }

            IsDraggingArrow = false;
        }
    }

    public void Confirm()
    {
        if (session.ConfirmExtrude())
        {
            session.ClearSelection();
        }

        IsDraggingArrow = false;
    }

    public void Cancel()
    {
        session.CancelExtrude();
        PendingSelection = null;
        IsSelecting = false;
        IsDraggingArrow = false;
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
        return Vector2.Distance(point, a + ab * t);
    }
}
