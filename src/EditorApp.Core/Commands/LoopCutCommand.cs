using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// Splits one object into two, reversibly. The original object is kept intact rather than
/// reconstructed on undo, so a cut and an undo return the level to exactly the objects it had —
/// same grid instance, same transform, same name.
///
/// Both halves stay under the original's parent. Its children go to the half that keeps its name,
/// and back to the original on undo.
/// </summary>
public sealed class LoopCutCommand : ICommand
{
    private readonly VoxelScene _scene;
    private readonly VoxelObject _original;
    private readonly VoxelWorld _low;
    private readonly VoxelWorld _high;

    private readonly ParentRecord _children = new();

    private VoxelObject? _lowObject;
    private VoxelObject? _highObject;

    public LoopCutCommand(VoxelScene scene, VoxelObject original, VoxelWorld low, VoxelWorld high)
    {
        _scene = scene;
        _original = original;
        _low = low;
        _high = high;
        RetainedCells = low.SolidCount + high.SolidCount;
    }

    public string Name => "Loop cut";

    public int RetainedCells { get; }

    /// <summary>The two halves, once the cut has been applied.</summary>
    public (VoxelObject Low, VoxelObject High)? Halves =>
        _lowObject is not null && _highObject is not null ? (_lowObject, _highObject) : null;

    public void Redo()
    {
        int parentId = _scene.ParentOf(_original)?.Id ?? 0;
        IPlaceable[] children = [.. _scene.ChildrenOf(_original.Id)];
        _children.Capture(children);

        _scene.Remove(_original.Id);

        // Both halves inherit the original placement, so nothing moves at the moment of the cut.
        _lowObject = _scene.Add(_low, _original.Transform, _original.Name);
        _highObject = _scene.Add(_high, _original.Transform, _original.Name + " (cut)");

        _scene.SetParent(_lowObject.Id, parentId);
        _scene.SetParent(_highObject.Id, parentId);

        foreach (IPlaceable child in children)
        {
            _scene.SetParent(child.Id, _lowObject.Id);
        }

        _scene.SetFocus(_highObject.Id);
    }

    public void Undo()
    {
        if (_lowObject is not null)
        {
            _scene.Remove(_lowObject.Id);
        }

        if (_highObject is not null)
        {
            _scene.Remove(_highObject.Id);
        }

        _scene.Restore(_original);
        _children.Restore();
        _scene.SetFocus(_original.Id);

        _lowObject = null;
        _highObject = null;
    }
}
