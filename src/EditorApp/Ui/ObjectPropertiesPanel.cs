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

        if (Props.Section("Volume", openByDefault: false))
        {
            DrawVolume(session);
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
            Vector3 was = transform.Position;
            Vector3 now = position;
            Change(session, target, transform with { Position = position }, $"Move {target.Name}",
                other => other with { Position = Copied(was, now, other.Position) });
        }

        AltTip(session);

        Vector3 euler = EulerFor(target);
        Vector3 before = euler;
        if (Props.Vector("Rotation", "rotation", ref euler, 0.5f, "%.4g°"))
        {
            Quaternion rotation = Rotations.FromEulerDegrees(euler);
            Vector3 turned = euler;
            Change(session, target, target.Transform with { Rotation = rotation }, $"Rotate {target.Name}",
                other => other with { Rotation = Rotations.FromEulerDegrees(Copied(before, turned, Rotations.ToEulerDegrees(other.Rotation))) });
            _euler = (target.Id, rotation, euler);
        }

        AltTip(session);
    }

    /// <summary>The axes that were changed, from <paramref name="now"/>; the rest as <paramref name="other"/> had them.</summary>
    private static Vector3 Copied(Vector3 was, Vector3 now, Vector3 other) => new(
        now.X != was.X ? now.X : other.X,
        now.Y != was.Y ? now.Y : other.Y,
        now.Z != was.Z ? now.Z : other.Z);

    /// <summary>With several selected, a field says how to set it on all of them: Blender's Alt.</summary>
    private static void AltTip(EditorSession session)
    {
        if (session.SelectedCount > 1 && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hold Alt while changing it to set it on everything selected.");
        }
    }

    private static void DrawVoxels(EditorSession session, VoxelObject focus)
    {
        float size = focus.VoxelSize;
        if (Props.Float("Voxel size", "voxel-size", ref size, 0.005f, ObjectTransform.MinVoxelSize, ObjectTransform.MaxVoxelSize, "%.4g")
            && ObjectTransform.ValidVoxelSize(size) is { } valid)
        {
            Change(session, focus, focus.Transform with { VoxelSize = valid }, $"Voxel size of {focus.Name}",
                other => other with { VoxelSize = valid }, objectsOnly: true);
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

    /// <summary>The volume filters and resampling, with their amounts, for the selected objects.</summary>
    private static void DrawVolume(EditorSession session)
    {
        int thickness = session.HollowThickness;
        if (Props.Int("Wall", "hollow-thickness", ref thickness, 0.1f, 1, 64, "%d voxels"))
        {
            session.HollowThickness = thickness;
        }

        if (Props.Buttons(string.Empty, "hollow", "Hollow") == 0)
        {
            session.HollowSelected();
        }

        Tooltip("Empties the inside, keeping walls this thick from every open face.");

        switch (Props.Buttons(string.Empty, "grow-shrink", "Thicken", "Thin", "Smooth"))
        {
            case 0: session.ThickenSelected(); break;
            case 1: session.ThinSelected(); break;
            case 2: session.SmoothSelected(); break;
        }

        int minimum = session.LooseMinimum;
        if (Props.Int("Loose under", "loose-minimum", ref minimum, 0.2f, 1, 100_000, "%d voxels"))
        {
            session.LooseMinimum = minimum;
        }

        if (Props.Buttons(string.Empty, "loose", "Remove Loose Pieces") == 0)
        {
            session.RemoveLooseSelected();
        }

        float factor = session.ScaleFactor;
        if (Props.Float("Scale", "scale-factor", ref factor, 0.01f, 0.1f, 8f, "x%.2f"))
        {
            session.ScaleFactor = factor;
        }

        switch (Props.Buttons(string.Empty, "resample", "Scale", "Halve Resolution"))
        {
            case 0: session.ScaleSelected(); break;
            case 1: session.HalveSelected(); break;
        }

        Tooltip("On every selected object; each one undo step.");
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

    /// <summary>The rest of the selection an Alt edit is changing too, each with where it stood before.</summary>
    private static readonly List<(IPlaceable Thing, ObjectTransform Before)> _alsoEditing = [];

    /// <param name="forOthers">
    /// What the edit does to everything else selected, while Alt is held — Blender's way of setting
    /// one field on several at once. Only the axes that were changed are copied.
    /// </param>
    private static void Change(
        EditorSession session,
        IPlaceable target,
        ObjectTransform changed,
        string name,
        Func<ObjectTransform, ObjectTransform>? forOthers = null,
        bool objectsOnly = false)
    {
        if (_editing != target)
        {
            Flush(session);
            _editing = target;
            _before = target.Transform;
            _editName = name;
        }

        session.ApplyTransform(target, changed);

        if (forOthers is null || !ImGui.GetIO().KeyAlt)
        {
            return;
        }

        IEnumerable<IPlaceable> others = objectsOnly
            ? session.SelectedObjects
            : session.SelectedObjects.Cast<IPlaceable>().Concat(session.SelectedLights);

        foreach (IPlaceable other in others)
        {
            if (other.Id == target.Id || other is VoxelObject { Locked: true } || other is SceneLight { Locked: true })
            {
                continue;
            }

            if (!_alsoEditing.Exists(e => e.Thing.Id == other.Id))
            {
                _alsoEditing.Add((other, other.Transform));
            }

            session.ApplyTransform(other, forOthers(other.Transform));
        }
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
            if (_alsoEditing.Count == 0)
            {
                session.PushTransformEdit(target, _before, _editName);
            }
            else
            {
                session.PushTransformEdits([(target, _before), .. _alsoEditing], $"{_editName} and {_alsoEditing.Count} more");
            }
        }

        _editing = null;
        _alsoEditing.Clear();
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
