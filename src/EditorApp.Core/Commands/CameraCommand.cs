using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// Any change to the level's cameras — one added, moved, renamed, taken away, or made the one a
/// render is seen from — as the whole list before and after. Cameras are few and small, so the
/// whole list is the simplest thing that is always right.
/// </summary>
public sealed class CameraCommand(VoxelScene scene, string name, IReadOnlyList<SceneCamera> before, int activeBefore, IReadOnlyList<SceneCamera> after, int activeAfter) : ICommand
{
    private readonly SceneCamera[] _before = [.. before];
    private readonly SceneCamera[] _after = [.. after];

    public string Name { get; } = name;

    public int RetainedCells => 0;

    public void Redo() => scene.SetCameras(_after, activeAfter);

    public void Undo() => scene.SetCameras(_before, activeBefore);
}
