using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's File › Append (Fullreleaseplan 6.3): objects from another .vxlevel brought into this
/// one where they stood there — chosen from its list, their colours found in this level's palette.
/// </summary>
public static class AppendDialog
{
    private const string PopupId = "Append##append-dialog";

    private static readonly FileBrowserDialog Browser = new();

    private static VoxelScene? _source;
    private static string _from = string.Empty;
    private static readonly HashSet<int> Chosen = [];
    private static bool _openRequested;

    /// <summary>Asks which level to append from.</summary>
    public static void Show() => Browser.Show(FileBrowserMode.Open, "Append from a level", VxLevelFile.Extension, null, null, Load);

    private static void Load(string path)
    {
        try
        {
            _source = VxLevelFile.LoadScene(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            ReportLog.Shared.Post($"Could not read {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
            return;
        }

        _from = Path.GetFileNameWithoutExtension(path);
        Chosen.Clear();
        Chosen.UnionWith(_source.Objects.Select(o => o.Id));
        _openRequested = true;
    }

    public static void Draw(EditorSession session)
    {
        Browser.Draw();

        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        if (_source is not { } source)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 22f, 0f), ImGuiCond.Appearing);
        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open)
            {
                _source = null;
            }

            return;
        }

        ImGui.TextDisabled($"Objects in {_from}: they come in where they stand there.");
        if (ImGui.SmallButton("All"))
        {
            Chosen.UnionWith(source.Objects.Select(o => o.Id));
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("None"))
        {
            Chosen.Clear();
        }

        if (ImGui.BeginChild("##append-list", new Vector2(ImGui.GetFontSize() * 20f, ImGui.GetFontSize() * 14f), ImGuiChildFlags.Border))
        {
            foreach (VoxelObject o in source.Objects)
            {
                bool chosen = Chosen.Contains(o.Id);
                if (ImGui.Checkbox($"{o.Name}  ({o.Grid.SolidCount:N0})##{o.Id}", ref chosen))
                {
                    if (chosen)
                    {
                        Chosen.Add(o.Id);
                    }
                    else
                    {
                        Chosen.Remove(o.Id);
                    }
                }
            }
        }

        ImGui.EndChild();

        ImGui.BeginDisabled(Chosen.Count == 0);
        if (ImGui.Button($"Append {Chosen.Count}", Theme.ModalButton))
        {
            session.AppendObjects(source, Chosen, _from);
            _source = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", Theme.ModalButton))
        {
            _source = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
