using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>
/// Extrude's gesture state machine (EditorApp.md, "Extrude"): drag on a surface to select it, then
/// drag the arrow that comes out of the selection. One flow, no separate add and remove tools.
/// </summary>
public sealed class ExtrudeInteraction(EditorSession session)
{
    /// <summary>How close in pixels the cursor has to be to the arrow to grab it.</summary>
    private const float ArrowGrabPixels = 14f;

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

    /// <summary>
    /// The arrow segment in <b>world</b> space, or null when there is nothing selected. The
    /// selection lives in the focused object's own space, so the object's transform is folded in
    /// here — otherwise the arrow would be drawn and grabbed in the wrong place for a moved or
    /// rotated object.
    /// </summary>
    public (Vector3 Start, Vector3 End)? Arrow()
    {
        if (session.Selection is not { IsEmpty: false } selection || session.Scene.Focus is not { } focus)
        {
            return null;
        }

        Vector3 localDirection = FaceInfo.Normal(selection.Direction);

        // While dragging, the arrow follows the preview so it stays attached to the moving surface.
        Vector3 localStart = selection.ArrowOrigin() + localDirection * session.ExtrudeSteps;

        Vector3 start = focus.Transform.TransformPoint(localStart);
        Vector3 direction = focus.Transform.TransformDirection(localDirection);
        return (start, start + direction * ArrowLength);
    }

    public void OnPress(RaycastHit? hover, Vector2 mouse, Vector2 viewport, FlyCamera camera, bool shift, bool alt)
    {
        if (TryGrabArrow(mouse, viewport, camera))
        {
            return;
        }

        if (hover is not { } hit)
        {
            return;
        }

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
        if (Arrow() is not { } arrow
            || !camera.TryProjectToScreen(arrow.Start, viewport, out Vector2 start)
            || !camera.TryProjectToScreen(arrow.End, viewport, out Vector2 end))
        {
            return false;
        }

        if (DistanceToSegment(mouse, start, end) > ArrowGrabPixels)
        {
            return false;
        }

        IsDraggingArrow = true;
        _arrowPressPosition = mouse;
        _arrowBaseSteps = session.ExtrudeSteps;
        return true;
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
    /// </summary>
    private int StepsFromDrag(Vector2 mouse, Vector2 viewport, FlyCamera camera)
    {
        if (session.Selection is not { } selection
            || session.Scene.Focus is not { } focus
            || Arrow() is not { } arrow)
        {
            return 0;
        }

        Vector3 axis = focus.Transform.TransformDirection(FaceInfo.Normal(selection.Direction));
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
        PendingSelection = FaceSelection.Box(session.World, _dragFace, _dragPlane, _dragAnchor, _dragCurrent);

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

        // The arrow drag deliberately survives the release: the spec confirms with Enter and
        // cancels with Esc, so letting go is a chance to look at the result, not a commit.
        IsDraggingArrow = false;
    }

    public void Confirm()
    {
        session.ConfirmExtrude();
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
