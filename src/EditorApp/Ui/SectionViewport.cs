using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The section box's settings (Fullreleaseplan 7.2), in the viewport's shading popover: on or off,
/// and how far it reaches along each axis — the top brought down to take a ceiling away, a side in
/// to take away the wall in front.
/// </summary>
public static class SectionViewport
{
    /// <summary>The level whose bounds the box is fitted to: the application's, set once.</summary>
    public static Func<VoxelScene?> SceneOf { get; set; } = () => null;

    /// <summary>Round everything shown, with a voxel to spare, and its top two fifths cut away.</summary>
    public static ClipBox Default(VoxelScene scene)
    {
        (Vector3 min, Vector3 max) = Bounds(scene);
        float top = min.Y + ((max.Y - min.Y) * 0.6f);
        return new ClipBox(min, max with { Y = MathF.Max(top, min.Y + 1f) });
    }

    /// <summary>Everything shown, a voxel wider all round; a box round the origin for an empty level.</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(VoxelScene scene)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (VoxelObject o in scene.Objects)
        {
            if (o.Visible && o.TryGetWorldBounds(out Vector3 low, out Vector3 high))
            {
                min = Vector3.Min(min, low);
                max = Vector3.Max(max, high);
            }
        }

        return min.X > max.X ? (new Vector3(-16f), new Vector3(16f)) : (min - Vector3.One, max + Vector3.One);
    }

    public static void Draw(ViewportSettings v)
    {
        if (SceneOf() is not { } scene)
        {
            return;
        }

        bool on = v.Clip is not null;
        if (ImGui.Checkbox("Section box", ref on))
        {
            v.Clip = on ? Default(scene) : null;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Nothing outside the box is drawn or picked: the ceiling and the wall in front taken away.{Shortcut.Hint(Input.EditorAction.ToggleSection)}");
        }

        if (v.Clip is not { } clip)
        {
            return;
        }

        (Vector3 low, Vector3 high) = Bounds(scene);
        Vector3 min = clip.Min;
        Vector3 max = clip.Max;
        bool changed = false;
        foreach ((string axis, int index) in new[] { ("X", 0), ("Y", 1), ("Z", 2) })
        {
            float from = min[index];
            float to = max[index];
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14f);
            if (ImGui.DragFloatRange2($"{axis}##section-{axis}", ref from, ref to, 0.25f, low[index], high[index], "%.0f", "%.0f"))
            {
                min[index] = from;
                max[index] = MathF.Max(to, from);
                changed = true;
            }
        }

        if (changed)
        {
            v.Clip = new ClipBox(min, max);
        }

        if (ImGui.SmallButton("Whole level"))
        {
            v.Clip = new ClipBox(low, high);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Ceiling off"))
        {
            v.Clip = Default(scene);
        }
    }
}
