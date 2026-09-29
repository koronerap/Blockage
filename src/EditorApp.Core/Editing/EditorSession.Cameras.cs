using EditorApp.Core.Commands;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Editing;

/// <summary>
/// The level's cameras (Fullreleaseplan 5.4): made where the view stands, looked through, set to the
/// view again, and one of them the camera renders are seen from. Every change is an undo step.
/// </summary>
public sealed partial class EditorSession
{
    private int _pickedCamera;

    /// <summary>
    /// The camera picked in the outliner, whose settings Properties shows; 0 for none. Picking an
    /// object or a light lets it go, as picking anything else in Blender does.
    /// </summary>
    public int PickedCameraId
    {
        get
        {
            if (Scene.SelectedCount > 0 || Scene.FindCamera(_pickedCamera) is null)
            {
                _pickedCamera = 0;
            }

            return _pickedCamera;
        }
    }

    public SceneCamera? PickedCamera => Scene.FindCamera(PickedCameraId);

    /// <summary>Picks a camera, and nothing else with it.</summary>
    public void PickCamera(int id)
    {
        if (Scene.FindCamera(id) is null)
        {
            return;
        }

        DeselectAll();
        _pickedCamera = id;
    }

    /// <summary>
    /// A camera where <paramref name="view"/> stands, picked, and made the one renders are seen
    /// from when there was none. One undo step.
    /// </summary>
    public SceneCamera AddCamera(SceneCamera view)
    {
        string name = Scene.Cameras.Any(c => c.Name == "Camera")
            ? DuplicateName("Camera", Scene.Cameras.Select(c => c.Name))
            : "Camera";

        SceneCamera camera = view.Clamped() with { Id = Scene.NewCameraId(), Name = name };
        if (camera.Kind == CameraKind.Isometric)
        {
            camera = camera.Isometric();
        }

        Change($"Add {name}", [.. Scene.Cameras, camera], Scene.ActiveCameraId == 0 ? camera.Id : Scene.ActiveCameraId);
        PickCamera(camera.Id);
        return camera;
    }

    /// <summary>A camera moved to where <paramref name="view"/> stands, keeping its name and kind. One undo step.</summary>
    public bool SetCameraToView(int id, SceneCamera view)
    {
        if (Scene.FindCamera(id) is not { } camera)
        {
            return false;
        }

        SceneCamera moved = view.Clamped() with { Id = camera.Id, Name = camera.Name, Kind = camera.Kind };
        if (camera.Kind == CameraKind.Isometric)
        {
            moved = moved.Isometric();
        }
        else if (camera.Kind == CameraKind.Perspective)
        {
            moved = moved with { FieldOfView = camera.FieldOfView };
        }

        if (moved == camera)
        {
            return false;
        }

        Change($"Move {camera.Name} to the View", Replaced(moved), Scene.ActiveCameraId);
        return true;
    }

    /// <summary>The camera renders are seen from; 0 for the view. One undo step.</summary>
    public bool SetActiveCamera(int id)
    {
        if (id != 0 && Scene.FindCamera(id) is null)
        {
            return false;
        }

        if (Scene.ActiveCameraId == id)
        {
            return false;
        }

        string name = Scene.FindCamera(id) is { } camera ? $"Render from {camera.Name}" : "Render from the View";
        Change(name, Scene.Cameras, id);
        return true;
    }

    public bool DeleteCamera(int id)
    {
        if (Scene.FindCamera(id) is not { } camera)
        {
            return false;
        }

        Change($"Delete {camera.Name}", [.. Scene.Cameras.Where(c => c.Id != id)], Scene.ActiveCameraId == id ? 0 : Scene.ActiveCameraId);
        return true;
    }

    public bool RenameCamera(int id, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || Scene.FindCamera(id) is not { } camera)
        {
            return false;
        }

        if (camera.Name == trimmed)
        {
            return true;
        }

        Change($"Rename {camera.Name}", Replaced(camera with { Name = trimmed }), Scene.ActiveCameraId);
        return true;
    }

    /// <summary>
    /// Changes a camera's settings while a slider is dragged, without an undo step each frame;
    /// <see cref="PushCameraEdit"/> records the whole drag once it is let go.
    /// </summary>
    public void SetCameraLive(SceneCamera changed)
    {
        if (Scene.FindCamera(changed.Id) is null)
        {
            return;
        }

        Scene.SetCameras(Replaced(changed.Clamped()), Scene.ActiveCameraId);
        HasUnsavedChanges = true;
    }

    /// <summary>Records a finished edit of a camera, from how it was before, as one undo step.</summary>
    public bool PushCameraEdit(SceneCamera before, string name)
    {
        if (Scene.FindCamera(before.Id) is not { } now || now == before)
        {
            return false;
        }

        SceneCamera[] beforeList = [.. Scene.Cameras.Select(c => c.Id == before.Id ? before : c)];
        History.Push(new CameraCommand(Scene, name, beforeList, Scene.ActiveCameraId, Scene.Cameras, Scene.ActiveCameraId));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>A camera changed to another kind: turned to the isometric angle when it becomes isometric. One undo step.</summary>
    public bool SetCameraKind(int id, CameraKind kind)
    {
        if (Scene.FindCamera(id) is not { } camera || camera.Kind == kind)
        {
            return false;
        }

        SceneCamera changed = kind == CameraKind.Isometric ? camera.Isometric() : camera with { Kind = kind };
        Change($"Make {camera.Name} {kind switch { CameraKind.Perspective => "Perspective", CameraKind.Orthographic => "Orthographic", _ => "Isometric" }}", Replaced(changed), Scene.ActiveCameraId);
        return true;
    }

    private SceneCamera[] Replaced(SceneCamera changed) => [.. Scene.Cameras.Select(c => c.Id == changed.Id ? changed : c)];

    private void Change(string name, IReadOnlyList<SceneCamera> after, int activeAfter)
    {
        var command = new CameraCommand(Scene, name, Scene.Cameras, Scene.ActiveCameraId, after, activeAfter);
        command.Redo();
        History.Push(command);
        HasUnsavedChanges = true;
    }
}
