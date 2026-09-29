using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// Changes one palette entry. Cheap to store but wide in effect: every voxel in the level that uses
/// the index is recolored, which is the whole point of storing indices rather than colors.
/// </summary>
/// <summary>A change to what a palette entry is made of.</summary>
public sealed class MaterialEditCommand(Palette palette, int index, VoxelMaterial before, VoxelMaterial after) : ICommand
{
    public string Name => $"Material {index}";

    public int RetainedCells => 1;

    public void Redo() => palette.SetMaterial(index, after);

    public void Undo() => palette.SetMaterial(index, before);
}

public sealed class PaletteEditCommand(VoxelScene scene, int index, Color32 before, Color32 after) : ICommand
{
    public string Name => $"Palette {index}";

    // The stack budgets voxel cells; a palette edit holds a single color, so it barely registers.
    public int RetainedCells => 1;

    public void Redo() => Set(after);

    public void Undo() => Set(before);

    private void Set(Color32 color)
    {
        scene.Palette[index] = color;
        scene.MarkAllDirty();
    }
}
