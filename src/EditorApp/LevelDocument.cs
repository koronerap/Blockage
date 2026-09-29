using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>
/// One level open in a tab (Fullreleaseplan 7.8): its session — the scene, its undo history, what is
/// selected — the tools and dialogs working on it, its own autosave, and where the view was the last
/// time it was in front, to go back to.
/// </summary>
public sealed class LevelDocument
{
    public LevelDocument(EditorSession session, int slot)
    {
        Session = session;
        Slot = slot;
        Export = new ExportController(session);
        Mimicraft = new MimicraftController(session);
        Extrude = new ExtrudeInteraction(session);
        Transform = new TransformInteraction(session);
        Aim = new LightAimInteraction(session);
        Select = new SelectInteraction(session);
    }

    /// <summary>Which of the levels opened since the editor started it is: names its tab and its autosave.</summary>
    public int Slot { get; }

    public EditorSession Session { get; }

    public ExportController Export { get; }

    public MimicraftController Mimicraft { get; }

    public ExtrudeInteraction Extrude { get; }

    public TransformInteraction Transform { get; }

    public LightAimInteraction Aim { get; }

    public SelectInteraction Select { get; }

    /// <summary>Keeps its unsaved work in the recovery folder; null for smoke and screenshot runs.</summary>
    public AutosaveController? Autosave { get; set; }

    /// <summary>Where the view was when another level came in front; null until then.</summary>
    public ViewPose? View { get; set; }

    /// <summary>Its section box, when one was on.</summary>
    public ClipBox? Clip { get; set; }

    /// <summary>The outliner's folded rows: an id means something only in its own level.</summary>
    public HashSet<int> Folded { get; } = [];

    /// <summary>The revision its unsaved work was answered for at, on the way out of the editor.</summary>
    public long? AnsweredAt { get; set; }

    /// <summary>
    /// Untitled, and nothing done in it yet: a level opened goes in its place rather than beside it,
    /// since the cube the editor starts on is not worth a tab of its own.
    /// </summary>
    public bool IsUntouched =>
        Session.ProjectPath is null && !Session.HasUnsavedChanges && !Session.History.CanUndo && !Session.History.CanRedo;
}

/// <summary>Where a view was: enough to put it back.</summary>
public readonly record struct ViewPose(Vector3 Position, float Yaw, float Pitch, float PivotDistance, bool Orthographic)
{
    public static ViewPose Of(FlyCamera camera) =>
        new(camera.Position, camera.Yaw, camera.Pitch, camera.PivotDistance, camera.Orthographic);

    public void ApplyTo(FlyCamera camera)
    {
        camera.Position = Position;
        camera.Yaw = Yaw;
        camera.Pitch = Pitch;
        camera.PivotDistance = PivotDistance;
        camera.Orthographic = Orthographic;
    }
}
