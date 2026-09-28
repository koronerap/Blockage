using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The level as a whole: how big it is in the world, and what the ground grid is measuring. The
/// voxel size used to live here, when a level had one; each object has its own now, so it moved to
/// the focused object's properties under the outliner.
/// </summary>
public static class LevelPanel
{
    public static void DrawContent(EditorSession session)
    {
        VoxelScene scene = session.Scene;

        if (!scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            ImGui.TextDisabled("Empty level.");
            return;
        }

        Vector3 extent = max - min;
        ImGui.TextDisabled($"Level  {extent.X:0.###} x {extent.Y:0.###} x {extent.Z:0.###} units");
        ImGui.TextDisabled($"{scene.Objects.Count} object(s)  ·  {scene.SolidCount:N0} voxels");

        // What the ground grid is measuring, so the cells the model sits on can be read as a number.
        float cell = Rendering.GroundGrid.WorldUnitsPerCell(scene.Focus?.VoxelSize ?? 1f);
        ImGui.TextDisabled($"Grid cell  {cell:0.###} {(MathF.Abs(cell - 1f) < 1e-6f ? "unit" : "units")}");

        if (scene.SharedVoxelSize is null && scene.Objects.Count(o => o.Visible && !o.IsEmpty) > 1)
        {
            ImGui.TextDisabled("Voxel sizes differ by object.");
        }
    }
}

/// <summary>
/// The focused object's own settings, under the outliner: for now, how big its voxels are.
///
/// Dragging the value resizes the object live and lands as one undo step when let go; typing one in
/// with Ctrl+click does the same. The object grows about its own origin.
/// </summary>
public static class ObjectPropertiesPanel
{
    private static ObjectTransform? _before;

    public static void DrawContent(EditorSession session)
    {
        // A picked light has settings of its own, and they are what this space is for while it is.
        if (session.SelectedLight is { } light)
        {
            LightPropertiesPanel.DrawContent(session, light);
            return;
        }

        LightPropertiesPanel.Flush(session);

        if (session.Scene.Focus is not { } focus)
        {
            return;
        }

        float size = focus.VoxelSize;

        ImGui.SetNextItemWidth(-1f);
        bool changed = ImGui.DragFloat(
            "##voxel-size",
            ref size,
            0.005f,
            ObjectTransform.MinVoxelSize,
            ObjectTransform.MaxVoxelSize,
            "Voxel size  %.4g");

        if (ImGui.IsItemActivated())
        {
            _before = focus.Transform;
        }

        if (changed && ObjectTransform.ValidVoxelSize(size) is { } valid)
        {
            session.ApplyTransform(focus, focus.Transform with { VoxelSize = valid });
        }

        if (ImGui.IsItemDeactivated() && _before is { } before)
        {
            session.PushTransformEdit(focus, before, "Voxel size");
            _before = null;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                $"World units one voxel of {focus.Name} measures.\n"
                + "Ctrl+click to type an exact value.\n\n"
                + "Each object has its own. Copies, cuts and extruded\n"
                + "pieces start at the size of the object they came from.");
        }

        if (MathF.Abs(focus.VoxelSize - 1f) > 1e-6f && ImGui.SmallButton("Back to 1"))
        {
            session.SetObjectVoxelSize(focus.Id, 1f);
        }

        if (focus.Grid.TryGetBounds(out Core.Voxels.Int3 min, out Core.Voxels.Int3 max))
        {
            Core.Voxels.Int3 cells = max - min + Core.Voxels.Int3.One;
            Vector3 world = new Vector3(cells.X, cells.Y, cells.Z) * focus.VoxelSize;

            ImGui.TextDisabled($"{cells.X} x {cells.Y} x {cells.Z} vx  =  {world.X:0.###} x {world.Y:0.###} x {world.Z:0.###} units");
        }
    }
}
