using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// A mirror of one object's voxels along one of its own axes.
///
/// Holds the axis and nothing else. A mirror across the middle of the object's bounds keeps those
/// bounds exactly, so doing it a second time is the undo — there is no shift to remember and no
/// copy of the grid to keep, however large the object.
/// </summary>
public sealed class FlipObjectCommand(VoxelWorld grid, Axis axis) : ICommand
{
    public string Name { get; } = $"Flip {axis}";

    /// <summary>Nothing retained: the undo stack trims by memory held, and this holds one byte.</summary>
    public int RetainedCells => 0;

    public void Redo() => RotateOperations.Flip(grid, axis);

    public void Undo() => RotateOperations.Flip(grid, axis);
}
