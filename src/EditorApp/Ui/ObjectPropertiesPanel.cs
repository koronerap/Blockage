using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The Object tab: the focused object's name, where it is and how it is turned — typed, where the
/// gizmo only drags — and, as a section of its own, its voxels: how big one is and how many there are.
/// A picked light shows its own settings here instead.
///
/// Every edit is live, and each gesture — a drag, a typed value — lands in history as one step.
/// </summary>
public static class ObjectPropertiesPanel
{
    private static IPlaceable? _editing;
    private static ObjectTransform _before;
    private static string _editName = string.Empty;

    /// <summary>
    /// The angles last shown for a rotation. Converting a rotation to angles has more than one answer,
    /// and recomputing it while an angle is dragged would let the other two jump between them; the
    /// angles typed stay as typed until the rotation is changed some other way.
    /// </summary>
    private static (int Id, Quaternion Rotation, Vector3 Euler) _euler;

    private static int _nameFor;
    private static string _nameBuffer = string.Empty;
    private static bool _naming;

    public static void DrawContent(EditorSession session)
    {
        if (session.SelectedLight is { } light)
        {
            Flush(session);
            LightPropertiesPanel.DrawContent(session, light);
            return;
        }

        LightPropertiesPanel.Flush(session);

        if (session.Scene.Focus is not { } focus)
        {
            ImGui.TextDisabled("Nothing is focused.");
            return;
        }

        DrawName(focus.Id, focus.Name, Icons.ObjectTab, name => session.RenameObject(focus.Id, name));

        if (Props.Section("Transform"))
        {
            DrawTransform(session, focus);
        }

        if (Props.Section("Voxels"))
        {
            DrawVoxels(session, focus);
        }

        if (Props.Section("Relations", openByDefault: false))
        {
            DrawRelations(session, focus);
        }

        if (Props.Section("Visibility", openByDefault: false))
        {
            bool visible = focus.Visible;
            if (Props.Check(string.Empty, "visible", "Show in viewport", ref visible))
            {
                session.SetObjectVisible(focus.Id, visible);
            }

            Tooltip($"A hidden object is not drawn, picked or exported.{Shortcut.Hint(EditorAction.Hide)}");
        }

        FinishGesture(session);
    }

    /// <summary>
    /// The name, as a field at the top with the kind of thing beside it — what Blender puts at the head
    /// of the Object tab. Kept as typed until the field is let go, then handed to <paramref name="rename"/>.
    /// </summary>
    public static void DrawName(int id, string name, Icons.Painter icon, Func<string, bool> rename)
    {
        if (_nameFor != id || !_naming)
        {
            _nameFor = id;
            _nameBuffer = name;
        }

        float height = ImGui.GetFrameHeight();
        ImGui.Dummy(new Vector2(height, height));
        Vector2 min = ImGui.GetItemRectMin();
        icon(new ImGuiIconCanvas(ImGui.GetWindowDrawList(), ImGui.GetColorU32(ImGuiCol.Text), 1.4f), min + new Vector2(height * 0.5f), height * 0.32f);

        ImGui.SameLine(0f, 4f);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##name", ref _nameBuffer, 64);
        _naming = ImGui.IsItemActive();

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            rename(_nameBuffer);
        }
    }

    /// <summary>Blender's Relations: the parent, and — for an object — how many it has under it.</summary>
    public static void DrawRelations(EditorSession session, IPlaceable target)
    {
        ParentMenu.DrawField(session, target);

        int children = session.Scene.ChildrenOf(target.Id).Count();
        if (children > 0)
        {
            Props.Label(string.Empty);
            ImGui.TextDisabled(children == 1 ? "1 child moves with it." : $"{children} children move with it.");
        }
    }

    /// <summary>Position and rotation of anything placeable, typed or dragged.</summary>
    public static void DrawTransform(EditorSession session, IPlaceable target)
    {
        ObjectTransform transform = target.Transform;

        Vector3 position = transform.Position;
        float speed = MathF.Max(transform.VoxelSize * 0.05f, 0.005f);
        if (Props.Vector("Location", "location", ref position, speed))
        {
            Change(session, target, transform with { Position = position }, $"Move {target.Name}");
        }

        Vector3 euler = EulerFor(target);
        if (Props.Vector("Rotation", "rotation", ref euler, 0.5f, "%.4g°"))
        {
            Quaternion rotation = Rotations.FromEulerDegrees(euler);
            Change(session, target, target.Transform with { Rotation = rotation }, $"Rotate {target.Name}");
            _euler = (target.Id, rotation, euler);
        }
    }

    private static void DrawVoxels(EditorSession session, VoxelObject focus)
    {
        float size = focus.VoxelSize;
        if (Props.Float("Voxel size", "voxel-size", ref size, 0.005f, ObjectTransform.MinVoxelSize, ObjectTransform.MaxVoxelSize, "%.4g")
            && ObjectTransform.ValidVoxelSize(size) is { } valid)
        {
            Change(session, focus, focus.Transform with { VoxelSize = valid }, $"Voxel size of {focus.Name}");
        }

        Tooltip(
            "World units one voxel measures. Ctrl+click to type.\n\n"
            + "Each object has its own. Copies, cuts and extruded pieces\n"
            + "start at the size of the object they came from.");

        if (MathF.Abs(focus.VoxelSize - 1f) > 1e-6f && Props.Buttons(string.Empty, "voxel-reset", "Back to 1") == 0)
        {
            session.SetObjectVoxelSize(focus.Id, 1f);
        }

        if (!focus.Grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            Props.Value("Voxels", "none");
            return;
        }

        Int3 cells = max - min + Int3.One;
        Vector3 world = new Vector3(cells.X, cells.Y, cells.Z) * focus.VoxelSize;

        Props.Value("Dimensions", $"{cells.X} × {cells.Y} × {cells.Z} vx");
        Props.Value("In the world", $"{world.X:0.###} × {world.Y:0.###} × {world.Z:0.###}");
        Props.Value("Voxels", $"{focus.Grid.SolidCount:N0}");

        // Beside the counts it changes: the dimensions double, the world size stays.
        string? problem = session.SubdivideProblem(focus);
        ImGui.BeginDisabled(problem is not null);
        if (Props.Buttons(string.Empty, "voxel-subdivide", "Subdivide") == 0)
        {
            ObjectMenu.SubdivideFocus(session, ReportLog.Shared);
        }

        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(problem ?? ObjectMenu.SubdivideTip);
        }
    }

    private static Vector3 EulerFor(IPlaceable target)
    {
        Quaternion rotation = target.Transform.Rotation;
        if (_euler.Id == target.Id && _euler.Rotation == rotation)
        {
            return _euler.Euler;
        }

        Vector3 euler = Rotations.ToEulerDegrees(rotation);
        _euler = (target.Id, rotation, euler);
        return euler;
    }

    private static void Change(EditorSession session, IPlaceable target, ObjectTransform changed, string name)
    {
        if (_editing != target)
        {
            Flush(session);
            _editing = target;
            _before = target.Transform;
            _editName = name;
        }

        session.ApplyTransform(target, changed);
    }

    /// <summary>The gesture is over once nothing is held: a drag let go, a typed value entered.</summary>
    private static void FinishGesture(EditorSession session)
    {
        if (_editing is not null && !ImGui.IsAnyItemActive())
        {
            Flush(session);
        }
    }

    /// <summary>Puts an unfinished edit into history.</summary>
    public static void Flush(EditorSession session)
    {
        if (_editing is { } target)
        {
            session.PushTransformEdit(target, _before, _editName);
        }

        _editing = null;
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
