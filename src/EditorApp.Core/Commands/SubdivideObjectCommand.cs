using EditorApp.Core.Editing;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>
/// One object's voxels cut into smaller ones, and its voxel size divided to match so it keeps its
/// size in the world.
///
/// Holds two transforms and nothing of the grid, however large: the undo is the exact reverse of the
/// cut, so there is no copy to keep. The transforms are held rather than recomputed so that halving
/// and doubling a size cannot drift it by a rounding error over many undos.
/// </summary>
public sealed class SubdivideObjectCommand(VoxelObject target, Symmetry symmetry, int factor = Subdivide.Factor) : ICommand
{
    private readonly ObjectTransform _before = target.Transform;
    private readonly ObjectTransform _after = target.Transform with { VoxelSize = target.VoxelSize / factor };

    public string Name => "Subdivide";

    /// <summary>Nothing retained: the undo stack trims by memory held, and this holds two transforms.</summary>
    public int RetainedCells => 0;

    public void Redo()
    {
        Subdivide.Apply(target.Grid, factor);
        target.Transform = _after;
        symmetry.Subdivided(target, factor);
    }

    public void Undo()
    {
        Subdivide.Revert(target.Grid, factor);
        target.Transform = _before;
        symmetry.Unsubdivided(target, factor);
    }
}
