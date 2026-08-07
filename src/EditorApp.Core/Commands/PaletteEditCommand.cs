using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// Changes one palette entry. Cheap to store but wide in effect: every voxel using the index is
/// recolored, which is the whole point of storing indices rather than colors (§3).
/// </summary>
public sealed class PaletteEditCommand(int index, Color32 before, Color32 after) : ICommand
{
    public string Name => $"Palette {index}";

    // The stack budgets voxel cells; a palette edit holds a single color, so it barely registers.
    public int RetainedCells => 1;

    public void Redo(VoxelWorld world)
    {
        world.Palette[index] = after;
        world.MarkAllDirty();
    }

    public void Undo(VoxelWorld world)
    {
        world.Palette[index] = before;
        world.MarkAllDirty();
    }
}
