using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Mobile.Tools;

/// <summary>
/// What a finger does to the level.
///
/// The state machines themselves are the desktop's — <see cref="ExtrudeInteraction"/> and
/// <see cref="TransformInteraction"/> are compiled into this project unchanged. All this adds is the
/// one rule a touchscreen needs and a mouse does not: **a drag belongs to the camera unless it
/// started on a gizmo.**
///
/// A mouse can hover, so the desktop always knows what a press will hit before it happens. A finger
/// cannot, and there is only one of it, so every drag would otherwise have to be either a camera
/// move or an edit and never both. Splitting it by what the press landed on gives both without a
/// mode to remember: grab the arrow and you are extruding, touch anywhere else and you are turning
/// the model. Tapping — a touch that goes nowhere — is what uses the tool.
/// </summary>
public sealed class MobileTools
{
    /// <summary>
    /// A fingertip covers several times what a cursor points at, and a gizmo that cannot be grabbed
    /// is worse than no gizmo. Roughly a 9 mm target on a typical phone.
    /// </summary>
    private const float TouchGrabPixels = 48f;

    private readonly EditorSession _session;
    private readonly ExtrudeInteraction _extrude;
    private readonly TransformInteraction _transform;

    public MobileTools(EditorSession session)
    {
        _session = session;
        _extrude = new ExtrudeInteraction(session) { ArrowGrabPixels = TouchGrabPixels };
        _transform = new TransformInteraction(session) { GrabPixels = TouchGrabPixels };

        // Box selection is a drag, and a drag is the camera's. Face mode asks for exactly one tap,
        // which is the gesture a phone actually has to give.
        session.ExtrudeSelectionMode = ExtrudeSelectionMode.Face;
    }

    public ExtrudeInteraction Extrude => _extrude;

    public TransformInteraction Transform => _transform;

    /// <summary>True while a gizmo has hold of the finger, so the camera must leave it alone.</summary>
    public bool IsCapturing { get; private set; }

    /// <summary>
    /// A finger has landed. Returns true when a gizmo took it, in which case every move until the
    /// finger leaves belongs to the tool rather than to the camera.
    /// </summary>
    public bool Press(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        IsCapturing = _session.ActiveTool switch
        {
            EditorTool.Transform => PressTransform(point, viewport, camera),
            EditorTool.Extrude => PressExtrude(point, viewport, camera),
            _ => false,
        };

        return IsCapturing;
    }

    private bool PressTransform(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        // The gizmo decides what it is holding from where the cursor is, and on a touchscreen it has
        // never been told. So it is told now, immediately before being asked.
        _transform.UpdateHover(point, viewport, camera);
        return _transform.OnPress(point, viewport, camera);
    }

    private bool PressExtrude(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        if (_session.Selection is not { IsEmpty: false })
        {
            return false;   // nothing selected, so there is no arrow to grab
        }

        _extrude.OnPress(Pick(point, viewport, camera), point, viewport, camera, shift: false, alt: false);

        if (_extrude.IsDraggingArrow)
        {
            return true;
        }

        // Not the arrow: the press has re-selected whatever was under it, which is what a tap on a
        // face means anyway. Let go of it so the drag can reach the camera.
        _extrude.OnRelease();
        return false;
    }

    public void Drag(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        if (!IsCapturing)
        {
            return;
        }

        switch (_session.ActiveTool)
        {
            case EditorTool.Transform:
                // freeform: false — a finger cannot hold shift, and snapping to whole voxels is the
                // behaviour worth having by default.
                _transform.OnDrag(point, viewport, camera, freeform: false);
                break;

            case EditorTool.Extrude:
                _extrude.OnDrag(Hover(point, viewport, camera), point, viewport, camera);
                break;
        }
    }

    /// <summary>
    /// The finger has left. A tap that never travelled is the tool being used; anything else was
    /// the camera, and only a captured gizmo has anything to finish.
    /// </summary>
    public bool Release(Vector2 point, Vector2 viewport, FlyCamera camera, bool wasTap)
    {
        if (IsCapturing)
        {
            IsCapturing = false;

            switch (_session.ActiveTool)
            {
                case EditorTool.Transform:
                    _transform.OnRelease();
                    return true;

                case EditorTool.Extrude:
                    // Letting go commits. Asked for on the desktop and even more true here, where
                    // there is no Enter key to reach for.
                    _extrude.OnRelease();
                    return true;
            }

            return false;
        }

        return wasTap && Tap(point, viewport, camera);
    }

    /// <summary>Returns true when the level changed and the view needs redrawing.</summary>
    private bool Tap(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        ScenePick? pick = Pick(point, viewport, camera);
        if (pick is not { } target)
        {
            return false;
        }

        return _session.ActiveTool switch
        {
            EditorTool.Paint => TapPaint(target),
            EditorTool.Extrude => TapExtrude(target),
            EditorTool.LoopCut => TapLoopCut(target),
            EditorTool.Transform => _session.TryFocus(target.Object.Id),
            _ => false,
        };
    }

    private bool TapPaint(ScenePick target)
    {
        if (!_session.TryFocus(target.Object.Id))
        {
            return false;
        }

        // One tap is one undo step. Without the stroke around it, a bucket fill and a single face
        // would both land as bare edits and undo would take them apart differently.
        _session.BeginStroke();
        bool painted = _session.Paint(target.Hit);
        _session.EndStroke();

        return painted;
    }

    private bool TapExtrude(ScenePick target)
    {
        if (target.Object.Id != _session.Scene.FocusId)
        {
            _session.ClearSelection();
            if (!_session.TryFocus(target.Object.Id))
            {
                return false;
            }
        }

        _session.SelectPatch(target.Hit);
        return true;
    }

    /// <summary>
    /// Loop Cut takes two taps: the first shows the plane, the second commits it.
    ///
    /// The desktop can cut on a single click because the plane has been following the cursor the
    /// whole time — you have already seen exactly where it will land. A finger gives no such warning,
    /// and a cut that splits an object in two is not something to discover after the fact. Tapping
    /// somewhere else moves the preview instead of cutting.
    /// </summary>
    private bool TapLoopCut(ScenePick target)
    {
        if (!_session.TryFocus(target.Object.Id) || _session.Scene.Focus is not { } focus)
        {
            return false;
        }

        RaycastHit hit = target.Hit;
        Vector3 surfacePoint = hit.Voxel.ToVector3() + new Vector3(0.5f) + (FaceInfo.Normal(hit.Face) * 0.5f);

        if (LoopCut.FindNearestPlane(focus, surfacePoint) is not { } plane
            || !LoopCut.Divides(focus.Grid, plane))
        {
            _session.PreviewCutPlane = null;
            return true;
        }

        if (_session.PreviewCutPlane != plane)
        {
            _session.PreviewCutPlane = plane;
            return true;
        }

        _session.PreviewCutPlane = null;
        return _session.ApplyLoopCut(plane);
    }

    /// <summary>Whatever is under the finger, in any object.</summary>
    public ScenePick? Pick(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        Ray ray = camera.ScreenPointToRay(point, viewport);
        return _session.Scene.TryPick(ray, out ScenePick pick) ? pick : null;
    }

    /// <summary>
    /// What is under the finger <b>in the focused object</b>. The tools edit in the focused object's
    /// own space, so a hit anywhere else is not something they can act on.
    /// </summary>
    private RaycastHit? Hover(Vector2 point, Vector2 viewport, FlyCamera camera) =>
        Pick(point, viewport, camera) is { } pick && pick.Object.Id == _session.Scene.FocusId
            ? pick.Hit
            : null;
}
