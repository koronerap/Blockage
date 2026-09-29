using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Says, the next time the editor starts, that it crashed the time before, and offers the report:
/// GitHub's bug form filled in from the crash log (see <see cref="CrashReport"/>), the log itself,
/// or to let it be. Drawn at the foot of the viewport until one of the three is chosen.
/// </summary>
public static class CrashNotice
{
    private static string? _crash;

    public static bool IsShown => _crash is not null;

    public static void Show(string? crash) => _crash = crash;

    public static void Draw(Vector2 viewportPosition, Vector2 viewportSize)
    {
        if (_crash is not { } crash)
        {
            return;
        }

        float unit = ImGui.GetFontSize();
        float width = MathF.Min(unit * 30f, viewportSize.X - (unit * 2f));
        ImGui.SetNextWindowPos(new Vector2(viewportPosition.X + (viewportSize.X * 0.5f), viewportPosition.Y + viewportSize.Y - (unit * 0.8f)), ImGuiCond.Always, new Vector2(0.5f, 1f));
        ImGui.SetNextWindowSize(new Vector2(width, 0f));
        ImGui.SetNextWindowBgAlpha(0.95f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
        if (ImGui.Begin("##crash-notice", flags))
        {
            ImGui.Text("Blockage closed unexpectedly the last time it ran.");
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - (unit * 1.2f));
            ImGui.TextDisabled("A report helps it get fixed. GitHub's bug form opens with the version and what the crash log says already filled in, for you to read over before you send it.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            if (ImGui.Button("Report on GitHub"))
            {
                Links.Open(CrashReport.IssueUrl(crash, AppVersion.Number, CrashReport.Platform));
                _crash = null;
            }

            ImGui.SameLine();
            if (ImGui.Button("Show the Log"))
            {
                Links.Open(CrashLog.Path);
            }

            ImGui.SameLine();
            if (ImGui.Button("Dismiss"))
            {
                _crash = null;
            }
        }

        ImGui.End();
    }
}
