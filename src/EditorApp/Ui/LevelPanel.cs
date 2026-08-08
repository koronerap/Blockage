using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Properties of the level as a whole rather than of anything in it. Right now that is one number:
/// how big a voxel is.
/// </summary>
public static class LevelPanel
{
    public static void DrawContent(EditorSession session)
    {
        VoxelScene scene = session.Scene;
        float size = scene.VoxelSize;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.DragFloat(
                "##voxel-size",
                ref size,
                0.005f,
                VoxelScene.MinVoxelSize,
                VoxelScene.MaxVoxelSize,
                "Voxel size  %.4g"))
        {
            session.SetVoxelSize(size);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "World units one voxel measures in the exported mesh.\n"
                + "Ctrl+click to type an exact value.\n\n"
                + "Fixed for the whole level. Editing stays at one unit\n"
                + "per voxel; the size is applied when exporting.");
        }

        if (scene.HasCustomVoxelSize && ImGui.SmallButton("Back to 1"))
        {
            session.SetVoxelSize(1f);
        }

        DrawDimensions(scene);
    }

    /// <summary>
    /// What the level currently measures, in voxels and in world units. The point of setting a voxel
    /// size is a model that comes out the right size, so the resulting number is worth showing next
    /// to the control that decides it rather than only at export.
    /// </summary>
    private static void DrawDimensions(VoxelScene scene)
    {
        ImGui.Spacing();

        if (!scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            ImGui.TextDisabled("Empty level.");
            return;
        }

        Vector3 extent = max - min;

        ImGui.TextDisabled($"Level  {extent.X:0.##} x {extent.Y:0.##} x {extent.Z:0.##} vx");

        if (!scene.HasCustomVoxelSize)
        {
            return;
        }

        Vector3 world = extent * scene.VoxelSize;
        ImGui.TextColored(
            Theme.Highlight,
            $"Exports  {world.X:0.###} x {world.Y:0.###} x {world.Z:0.###}");

        // What the ground grid is measuring, so the cells the model sits on can be read as a number.
        float spacing = Rendering.GroundGrid.Spacing(scene.VoxelSize);
        ImGui.TextDisabled(
            $"Grid cell  {spacing:0.###} vx  =  {Rendering.GroundGrid.WorldUnitsPerCell(scene.VoxelSize):0.###}");
    }
}
