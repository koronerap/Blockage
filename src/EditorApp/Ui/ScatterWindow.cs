using System.Numerics;
using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The scatter's window (Fullreleaseplan 6.7): how many, how far apart, how turned, linked or not,
/// and the seed. Changed straight after a scatter, it scatters again in its place.
/// </summary>
public static class ScatterWindow
{
    private static bool _open;
    private static ScatterSettings _settings = new();

    /// <summary>Settings changed while the last scatter can be done again, not yet done again.</summary>
    private static bool _pending;

    public static bool IsOpen => _open;

    public static void Open() => _open = true;

    public static void Draw(EditorSession session)
    {
        if (!_open)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 20f, 0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Scatter", ref _open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }

        bool adjusting = session.CanAdjustScatter;
        string? problem = session.ScatterProblem();
        if (adjusting)
        {
            ImGui.TextDisabled("Changing these scatters again, in place of the last.");
        }
        else if (problem is not null)
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 19f);
            ImGui.TextColored(Theme.Highlight, problem);
            ImGui.PopTextWrapPos();
        }
        else if (session.Scene.Focus is { } ground)
        {
            int count = session.SelectedObjects.Count(o => o.Id != ground.Id);
            ImGui.TextDisabled($"{count} object{(count == 1 ? string.Empty : "s")} to scatter on {ground.Name}.");
        }

        ScatterSettings changed = _settings;

        int copies = changed.Count;
        if (Props.Int("Copies", "scatter-count", ref copies, 0.2f, 1, ScatterSettings.MaxCount, "%d"))
        {
            changed = changed with { Count = copies };
        }

        float spacing = changed.Spacing;
        if (Props.Float("Apart", "scatter-spacing", ref spacing, 0.05f, 0f, 1000f, "%.1f"))
        {
            changed = changed with { Spacing = spacing };
        }

        Tooltip("How close two copies may stand, in world units, at the least.");

        int turn = Props.Choice("Turned", "scatter-turn", [(null, "No"), (null, "Quarters"), (null, "Freely")], (int)changed.Turn);
        if (turn != (int)changed.Turn)
        {
            changed = changed with { Turn = (ScatterTurn)turn };
        }

        bool linked = changed.Linked;
        if (Props.Check(string.Empty, "scatter-linked", "Linked copies", ref linked))
        {
            changed = changed with { Linked = linked };
        }

        Tooltip("Copies that share the originals' voxels: an edit to one is an edit to all, and the level stays small.");

        int seed = changed.Seed;
        if (Props.Int("Seed", "scatter-seed", ref seed, 0.2f, 0, 1_000_000, "%d"))
        {
            changed = changed with { Seed = seed };
        }

        if (Props.Buttons(string.Empty, "scatter-reseed", "Another Seed") == 0)
        {
            changed = changed with { Seed = Random.Shared.Next(1_000_000) };
        }

        if (changed != _settings)
        {
            _settings = changed.Clamped();
            _pending = adjusting;
        }

        // A drag adjusts once it is let go, rather than every frame of it.
        if (_pending && !ImGui.IsAnyItemActive())
        {
            _pending = false;
            session.AdjustScatter(_settings);
        }

        ImGui.Separator();
        ImGui.BeginDisabled(problem is not null);
        if (ImGui.Button("Scatter", new Vector2(-1f, 0f)))
        {
            session.Scatter(_settings);
        }

        ImGui.EndDisabled();
        ImGui.End();
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
