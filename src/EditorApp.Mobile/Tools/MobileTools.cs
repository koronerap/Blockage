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
/// The state machines are the desktop's — <see cref="ExtrudeInteraction"/> and
/// <see cref="TransformInteraction"/> are compiled into this project unchanged. All this adds is the
/// rule a touchscreen needs and a mouse does not, which is how one finger can be both the camera and
/// the tool:
///
/// **whatever the press landed on owns the drag.** A gizmo first, then the model, then the sky.
/// Touch a face and you are painting it or selecting it; touch the space around the model and you
/// are turning the view. Two fingers are always the camera, so there is a way to reframe without
/// letting go of anything.
///
/// The alternative — every drag belonging to the camera — made the two tools that are drags at heart
/// unusable: a brush that paints one face per tap, and an extrude that cannot draw a rectangle.
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

    /// <summary>What the drag in progress is doing, decided when the finger landed.</summary>
    private Holder _holder;

    private enum Holder
    {
        /// <summary>Nobody: the press found empty space, so the camera has it.</summary>
        None,

        Gizmo,
        Painting,
        Selecting,
    }

    public MobileTools(EditorSession session)
    {
        _session = session;
        _extrude = new ExtrudeInteraction(session) { ArrowGrabPixels = TouchGrabPixels };
        _transform = new TransformInteraction(session) { GrabPixels = TouchGrabPixels };
    }

    public ExtrudeInteraction Extrude => _extrude;

    public TransformInteraction Transform => _transform;

    /// <summary>
    /// Whether the next tap takes a colour instead of laying one down.
    ///
    /// The desktop samples with Alt held. A finger holds nothing, so this is a mode — but one that
    /// lasts exactly one tap, because a sampler left armed is indistinguishable from a brush that
    /// has stopped working.
    /// </summary>
    public bool SamplerArmed { get; set; }

    /// <summary>True while the tool has hold of the finger, so the camera must leave it alone.</summary>
    public bool IsCapturing => _holder != Holder.None;

    /// <summary>
    /// A finger has landed. Returns true when the tool took it, in which case every move until the
    /// finger leaves belongs to the tool rather than to the camera.
    /// </summary>
    public bool Press(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        _holder = Decide(point, viewport, camera);
        return IsCapturing;
    }

    private Holder Decide(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        ScenePick? pick = Pick(point, viewport, camera);

        switch (_session.ActiveTool)
        {
            case EditorTool.Transform:
                // The gizmo decides what it is holding from where the cursor is, and on a
                // touchscreen it has never been told. So it is told now, immediately before asking.
                _transform.UpdateHover(point, viewport, camera);
                if (_transform.OnPress(point, viewport, camera))
                {
                    return Holder.Gizmo;
                }

                // Not a handle. Touching an object still chooses it, but the drag is the camera's:
                // there is nothing else for a finger to drag an object by.
                if (pick is { } target)
                {
                    _session.TryFocus(target.Object.Id);
                }

                return Holder.None;

            case EditorTool.Extrude:
                // The arrow is drawn in front of everything, so grabbing it must never be read as a
                // press on whatever happens to be behind it. OnPress tries that first itself.
                _extrude.OnPress(pick, point, viewport, camera, shift: false, alt: false);

                if (_extrude.IsDraggingArrow)
                {
                    return Holder.Gizmo;
                }

                // Box mode leaves a rectangle being dragged out; Face mode has already taken the
                // whole patch and has nothing left to follow the finger.
                return _extrude.IsSelecting ? Holder.Selecting : Holder.None;

            case EditorTool.Paint:
                if (pick is not { } surface || !_session.TryFocus(surface.Object.Id))
                {
                    return Holder.None;
                }

                if (SamplerArmed)
                {
                    _session.SampleColor(surface.Hit);
                    SamplerArmed = false;
                    return Holder.None;
                }

                // One stroke is one undo step, however many faces the finger crosses.
                _session.BeginStroke();
                _session.Paint(surface.Hit);
                return Holder.Painting;

            default:
                return Holder.None;
        }
    }

    public void Drag(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        switch (_holder)
        {
            case Holder.Gizmo when _session.ActiveTool == EditorTool.Transform:
                // freeform: false — a finger cannot hold shift, and snapping to whole voxels is the
                // behaviour worth having by default.
                _transform.OnDrag(point, viewport, camera, freeform: false);
                break;

            case Holder.Gizmo:
            case Holder.Selecting:
                _extrude.OnDrag(Hover(point, viewport, camera), point, viewport, camera);
                break;

            case Holder.Painting:
                if (Hover(point, viewport, camera) is { } hit)
                {
                    _session.Paint(hit);
                }

                break;
        }
    }

    /// <summary>
    /// The finger has left. Returns true when the level changed and the view needs redrawing.
    /// </summary>
    public bool Release(Vector2 point, Vector2 viewport, FlyCamera camera, bool wasTap)
    {
        Holder holder = _holder;
        _holder = Holder.None;

        switch (holder)
        {
            case Holder.Gizmo when _session.ActiveTool == EditorTool.Transform:
                _transform.OnRelease();
                return true;

            case Holder.Gizmo:
            case Holder.Selecting:
                // Letting go commits. Asked for on the desktop and even more true here, where there
                // is no Enter key to reach for.
                _extrude.OnRelease();
                return true;

            case Holder.Painting:
                _session.EndStroke();
                return true;
        }

        // Nothing was held, so this was the camera — unless the finger never moved, in which case it
        // was a tap on something, and the two tools that act on a tap get their turn.
        return wasTap && Tap(point, viewport, camera);
    }

    private bool Tap(Vector2 point, Vector2 viewport, FlyCamera camera)
    {
        if (Pick(point, viewport, camera) is not { } target)
        {
            return false;
        }

        return _session.ActiveTool == EditorTool.LoopCut && TapLoopCut(target);
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

    /// <summary>Takes the colour of whatever is under the point, the way Alt-click does on desktop.</summary>
    public bool Sample(Vector2 point, Vector2 viewport, FlyCamera camera) =>
        Pick(point, viewport, camera) is { } target
            && _session.TryFocus(target.Object.Id)
            && _session.SampleColor(target.Hit);

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
