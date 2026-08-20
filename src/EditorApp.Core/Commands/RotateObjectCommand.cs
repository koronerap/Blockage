using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// A quarter turn of one object's voxels.
///
/// Holds a direction and a shift and nothing else — no copy of the grid, however large the object.
/// A quarter turn of a cubic lattice is a permutation, so the opposite turn restores it exactly;
/// keeping the shift is what makes that true of the position as well, since re-deriving it on the
/// way back could disagree by half a voxel on a box whose sides differ in parity.
/// </summary>
public sealed class RotateObjectCommand(VoxelWorld grid, RotateDirection direction) : ICommand
{
    private Int3 _shift;

    public string Name { get; } = $"Rotate {direction.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Nothing retained. The undo stack trims by the memory a command holds, and this one holds four
    /// integers whatever it turned.
    /// </summary>
    public int RetainedCells => 0;

    public void Redo() => _shift = RotateOperations.Rotate(grid, direction);

    public void Undo() =>
        RotateOperations.Rotate(grid, RotateOperations.Opposite(direction), Inverse());

    /// <summary>
    /// Undoing "turn, then shift by s" is "shift by -s, then turn back" — and since the shift is
    /// applied after the turn either way, the one to hand back is the original turned round.
    /// </summary>
    private Int3 Inverse() =>
        RotateOperations.Turn(Int3.Zero - _shift, RotateOperations.Opposite(direction));
}
