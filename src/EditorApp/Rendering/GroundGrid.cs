namespace EditorApp.Rendering;

/// <summary>
/// How coarse the ground grid is drawn.
///
/// The viewport is in **world units**, and so is the grid: a cell is always a whole power of ten of
/// them. Which power follows the focused object's voxel size, so the cells stay a few voxels across
/// — at one unit per voxel, one cell is one voxel, which is where the grid started. Objects each
/// have their own voxel size, and the grid cannot be every one of them at once; the one being worked
/// on is the one it measures for.
/// </summary>
public static class GroundGrid
{
    /// <summary>
    /// How far the grid reaches from the origin, counted in **cells** rather than in voxels. Fixing
    /// the cell count is what makes the change legible: the grid keeps the same density on screen at
    /// every voxel size, so the only thing that moves is how many cells the model covers — which is
    /// the ratio that was staying stubbornly the same before.
    /// </summary>
    public const int HalfExtentCells = 64;

    /// <summary>Never draw more lines than this in one direction, whatever the arithmetic says.</summary>
    public const int MaxLinesPerAxis = 512;

    // A cell narrower than half a voxel is a smear; one wider than a few voxels stops giving the eye
    // anything to measure against. Between those, step by tens.
    private const float NarrowestCell = 0.5f;
    private const float WidestCell = 5f;

    /// <summary>
    /// Distance between minor lines, in voxels. Always a power of ten of world units, picked so the
    /// cells stay legible at any voxel size — at one unit per voxel it comes out as exactly one
    /// voxel, which is where the grid started.
    /// </summary>
    public static float Spacing(float voxelSize)
    {
        if (!float.IsFinite(voxelSize) || voxelSize <= 0f)
        {
            return 1f;
        }

        float spacing = 1f / voxelSize;

        while (spacing < NarrowestCell)
        {
            spacing *= 10f;
        }

        while (spacing > WidestCell)
        {
            spacing /= 10f;
        }

        return spacing;
    }

    /// <summary>What one cell is worth in world units — a power of ten, by construction.</summary>
    public static float WorldUnitsPerCell(float voxelSize) => Spacing(voxelSize) * voxelSize;
}
