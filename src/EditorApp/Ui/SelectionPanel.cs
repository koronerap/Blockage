using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Everything that acts on a box selection at once: fill, paint, delete, move, mirror, extrude and
/// the clipboard. Each button is one undo step regardless of how many voxels it touched.
/// </summary>
public static class SelectionPanel
{
    private static int _extrudeLayers = 1;
    private static int _moveStep = 1;

    public static void Draw(EditorSession session)
    {
        ImGui.SetNextWindowPos(new Vector2(430f, 170f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420f, 400f), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Selection"))
        {
            ImGui.End();
            return;
        }

        if (session.Selection is not { } box)
        {
            ImGui.TextDisabled("No selection.");
            ImGui.TextDisabled("Pick the Select tool (6) and drag, or press Ctrl+A.");
            DrawClipboardRow(session);
            ImGui.End();
            return;
        }

        Int3 size = box.Size;
        ImGui.Text($"{size.X} x {size.Y} x {size.Z}   ({box.Volume:N0} cells)");
        ImGui.TextDisabled($"{box.Min}  ..  {box.Max}");
        ImGui.Separator();

        DrawRegionEdits(session);
        ImGui.Separator();
        DrawMoveAndMirror(session);
        ImGui.Separator();
        DrawExtrude(session);
        ImGui.Separator();
        DrawClipboardRow(session);

        ImGui.End();
    }

    private static void DrawRegionEdits(EditorSession session)
    {
        if (ImGui.Button("Fill"))
        {
            session.FillSelection();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Sets every cell in the box to the active color, empty ones included.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Paint"))
        {
            session.PaintSelection();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Recolors only the cells that already hold a voxel.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            session.DeleteSelection();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear selection"))
        {
            session.ClearSelection();
        }
    }

    private static void DrawMoveAndMirror(EditorSession session)
    {
        ImGui.SetNextItemWidth(120f);
        ImGui.SliderInt("Move step", ref _moveStep, 1, 32);

        DrawAxisButtons("Move", axis =>
        {
            ImGui.SameLine();
            if (ImGui.Button($"-##move{axis}"))
            {
                session.MoveSelection(Offset(axis, -_moveStep));
            }

            ImGui.SameLine();
            if (ImGui.Button($"+##move{axis}"))
            {
                session.MoveSelection(Offset(axis, _moveStep));
            }
        });

        ImGui.Text("Mirror");
        foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            ImGui.SameLine();
            if (ImGui.Button($"{axis}##mirror"))
            {
                session.MirrorSelection(axis);
            }
        }
    }

    private static void DrawExtrude(EditorSession session)
    {
        ImGui.SetNextItemWidth(120f);
        ImGui.SliderInt("Layers", ref _extrudeLayers, 1, 32);

        ImGui.Text("Extrude / intrude");
        DrawFaceButton(session, "+X", Face.PosX);
        DrawFaceButton(session, "-X", Face.NegX);
        DrawFaceButton(session, "+Y", Face.PosY);
        DrawFaceButton(session, "-Y", Face.NegY);
        DrawFaceButton(session, "+Z", Face.PosZ);
        DrawFaceButton(session, "-Z", Face.NegZ);

        ImGui.TextDisabled("Left button extrudes, right button intrudes.");
    }

    private static void DrawFaceButton(EditorSession session, string label, Face face)
    {
        if (label != "+X")
        {
            ImGui.SameLine();
        }

        ImGui.Button(label);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            session.ExtrudeSelection(face, _extrudeLayers);
        }
        else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            session.ExtrudeSelection(face, -_extrudeLayers);
        }
    }

    private static void DrawClipboardRow(EditorSession session)
    {
        ImGui.BeginDisabled(!session.HasSelection);
        if (ImGui.Button("Copy (Ctrl+C)"))
        {
            session.CopySelection();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cut (Ctrl+X)"))
        {
            session.CutSelection();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (session.Clipboard is { } clip)
        {
            ImGui.TextDisabled($"Clipboard: {clip.Size.X}x{clip.Size.Y}x{clip.Size.Z}, {clip.SolidCount:N0} voxels");
        }
        else
        {
            ImGui.TextDisabled("Clipboard empty");
        }

        if (session.HasClipboard)
        {
            ImGui.TextDisabled("Ctrl+V places it under the cursor; click to commit, Esc to cancel.");
        }
    }

    private static void DrawAxisButtons(string label, Action<Axis> perAxis)
    {
        ImGui.Text(label);
        foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            ImGui.SameLine();
            ImGui.TextDisabled(axis.ToString());
            perAxis(axis);
        }
    }

    private static Int3 Offset(Axis axis, int amount) => axis switch
    {
        Axis.X => new Int3(amount, 0, 0),
        Axis.Y => new Int3(0, amount, 0),
        _ => new Int3(0, 0, amount),
    };
}
