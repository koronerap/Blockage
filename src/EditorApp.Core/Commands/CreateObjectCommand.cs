using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// Adds a new object to the scene, reversibly. Extrude's Create sub-mode uses it: the voxels it
/// pulled out become a separate object instead of joining the one they came from, which is the
/// "extrude and loop cut in one motion" the spec describes. Duplicating uses it too, placing the
/// copy right after the original in the list. Made from another object, it takes that one's parent.
/// </summary>
public sealed class CreateObjectCommand(
    VoxelScene scene,
    VoxelWorld grid,
    ObjectTransform transform,
    string name,
    string commandName = "Create object",
    int? insertAt = null,
    int parentId = 0,
    int? collectionId = null) : ICommand
{
    private VoxelObject? _created;
    private int _previousFocusId;
    private bool _wasSelected;

    public string Name => commandName;

    public int RetainedCells { get; } = grid.SolidCount;

    public VoxelObject? Created => _created;

    public void Redo()
    {
        _previousFocusId = scene.FocusId;

        if (_created is null)
        {
            _created = scene.Add(grid, transform, name, insertAt);
            scene.SetParent(_created.Id, parentId);
            if (collectionId is { } collection)
            {
                scene.SetCollection(_created.Id, collection);
            }
        }
        else
        {
            scene.Restore(_created, insertAt);
            if (_wasSelected)
            {
                scene.Select(_created.Id);
            }
        }

        scene.SetFocus(_created.Id);
    }

    public void Undo()
    {
        if (_created is null)
        {
            return;
        }

        _wasSelected = scene.IsSelected(_created.Id);
        scene.Remove(_created.Id);
        scene.SetFocus(_previousFocusId);
    }
}
