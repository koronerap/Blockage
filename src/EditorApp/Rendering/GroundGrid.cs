namespace EditorApp.Rendering;

/// <summary>
/// How coarse the ground grid is drawn.
///
/// The grid measures **world units**, not voxels. It is the only thing in the viewport standing in
/// for the world the level will end up in, so it is what has to change when a voxel stops being one
/// unit — otherwise setting a voxel size has no visible effect at all and the model floats at a
/// scale nothing on screen agrees with.
///
/// Everything else stays at one unit per voxel. Only the spacing of these lines moves.
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
