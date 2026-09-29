namespace EditorApp.Core.Scene;

/// <summary>
/// Whose child some things were and how they were held, taken before a command hands them to
/// another parent, so its undo can put every one of them back exactly.
/// </summary>
internal sealed class ParentRecord
{
    private readonly List<(IPlaceable Child, int ParentId, ObjectTransform Offset)> _held = [];

    public void Capture(IEnumerable<IPlaceable> children)
    {
        foreach (IPlaceable child in children)
        {
            _held.Add((child, child.ParentId, VoxelScene.OffsetOf(child)));
        }
    }

    public void Restore()
    {
        foreach ((IPlaceable child, int parentId, ObjectTransform offset) in _held)
        {
            VoxelScene.PlaceUnder(child, parentId, offset);
        }

        _held.Clear();
    }
}
