using System.Numerics;
using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Blender's Undo History (Fullreleaseplan 7.6): every step done, oldest first, the one the level is
/// at marked; a click on any goes back or forward to it. What was undone stays listed, faintly, until
/// something new is done.
/// </summary>
public static class HistoryWindow
{
    private static bool _open;

    public static bool IsOpen => _open;

    public static void Toggle() => _open = !_open;

    public static void Draw(EditorSession session)
    {
        if (!_open)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 18f, ImGui.GetFontSize() * 24f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Undo History", ref _open))
        {
            ImGui.End();
            return;
        }

        List<string> done = [.. session.History.DoneNames];
        List<string> undone = [.. session.History.UndoneNames];
        int at = done.Count;
        int target = -1;

        // The step the level is at, in the accent, as a selected row is everywhere else.
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent with { W = 0.55f });

        // The level as it was before any of it.
        if (ImGui.Selectable("Original", at == 0))
        {
            target = 0;
        }

        for (int i = 0; i < done.Count; i++)
        {
            if (ImGui.Selectable($"{done[i]}##done-{i}", at == i + 1))
            {
                target = i + 1;
            }

            if (at == i + 1 && ImGui.IsWindowAppearing())
            {
                ImGui.SetScrollHereY(0.5f);
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDisabled);
        for (int i = 0; i < undone.Count; i++)
        {
            if (ImGui.Selectable($"{undone[i]}##undone-{i}", false))
            {
                target = done.Count + i + 1;
            }
        }

        ImGui.PopStyleColor(2);

        if (target >= 0 && target != at)
        {
            session.GoToHistory(target);
        }

        ImGui.End();
    }
}
