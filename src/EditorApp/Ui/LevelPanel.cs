using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;

namespace EditorApp.Ui;

/// <summary>
/// The level as a whole, in the World tab: how big it is in the world, what is in it, and what the
/// ground grid is measuring. The voxel size used to live here, when a level had one; each object has
/// its own now, in the Object tab.
/// </summary>
public static class LevelPanel
{
    public static void DrawContent(EditorSession session)
    {
        VoxelScene scene = session.Scene;

        if (!scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            Props.Value("Size", "empty");
            return;
        }

        Vector3 extent = max - min;
        Props.Value("Size", $"{extent.X:0.###} × {extent.Y:0.###} × {extent.Z:0.###}");
        Props.Value("Contents", $"{scene.Objects.Count} object(s), {scene.SolidCount:N0} voxels");

        // What the ground grid is measuring, so the cells the model sits on can be read as a number.
        float cell = Rendering.GroundGrid.WorldUnitsPerCell(scene.Focus?.VoxelSize ?? 1f);
        Props.Value("Grid cell", $"{cell:0.###} {(MathF.Abs(cell - 1f) < 1e-6f ? "unit" : "units")}");

        if (scene.SharedVoxelSize is null && scene.Objects.Count(o => o.Visible && !o.IsEmpty) > 1)
        {
            Props.Value("Voxel sizes", "differ by object");
        }
    }
}
