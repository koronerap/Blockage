using System.Numerics;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What the tab strip is given: the levels open, the one in front, and what to do when one is chosen or closed.</summary>
public sealed class LevelTabsContext
{
    public required IReadOnlyList<LevelDocument> Levels { get; init; }

    public required LevelDocument Active { get; init; }

    public required Action<LevelDocument> Show { get; init; }

    public required Action<LevelDocument> Close { get; init; }

    public required Action New { get; init; }
}

/// <summary>
/// The levels open, a tab each (Fullreleaseplan 7.8), in a strip under the menus while there is more
/// than one. A tab shows its level's name, a dot while it has unsaved work, and where it is saved
/// when the pointer rests on it; a middle click or its cross closes it, and the + after them starts a new level.
/// </summary>
public static class LevelTabs
{
    private static readonly Vector2 Padding = new(6f, 3f);

    private const ImGuiWindowFlags Flags =
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoNavFocus
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>The level in front when the strip was last drawn, to tell a tab clicked from a level brought forward some other way.</summary>
    private static LevelDocument? _drawnActive;

    /// <summary>A level brought forward by a shortcut or by opening it rather than by its tab: its tab is made the chosen one.</summary>
    private static LevelDocument? _bringForward;

    private static readonly Dictionary<int, (Vector2 Min, Vector2 Max)> TabRects = [];

    /// <summary>Where a level's tab was drawn last frame, for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? TabRect(LevelDocument level) =>
        TabRects.TryGetValue(level.Slot, out var rect) ? rect : null;

    /// <summary>Where the + was drawn last frame.</summary>
    public static (Vector2 Min, Vector2 Max)? NewRect { get; private set; }

    public static float Height => ImGui.GetFrameHeight() + (Padding.Y * 2f);

    /// <summary>Draws the strip across the window at <paramref name="position"/> and returns its height.</summary>
    public static float Draw(Vector2 position, float width, LevelTabsContext context)
    {
        float height = Height;
        ImGui.SetNextWindowPos(position);
        ImGui.SetNextWindowSize(new Vector2(width, height));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Padding);
        ImGui.Begin("##level-tabs", Flags);
        ImGui.PopStyleVar();

        if (!ReferenceEquals(context.Active, _drawnActive))
        {
            _bringForward = context.Active;
            _drawnActive = context.Active;
        }

        if (_bringForward is not null && !context.Levels.Contains(_bringForward))
        {
            _bringForward = null;
        }

        LevelDocument? chosen = null;
        LevelDocument? closing = null;
        if (ImGui.BeginTabBar("##levels", ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.NoTooltip))
        {
            foreach (LevelDocument level in context.Levels)
            {
                bool open = true;
                ImGuiTabItemFlags flags = ImGuiTabItemFlags.NoTooltip;
                if (level.Session.HasUnsavedChanges)
                {
                    flags |= ImGuiTabItemFlags.UnsavedDocument;
                }

                if (ReferenceEquals(level, _bringForward))
                {
                    flags |= ImGuiTabItemFlags.SetSelected;
                }

                bool selected = ImGui.BeginTabItem($"{level.Session.ProjectName}###level-{level.Slot}", ref open, flags);
                (Vector2 min, Vector2 max) = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                TabRects[level.Slot] = (min, max);

                // The one in front marked along its top, in the accent, as Blender marks what is active.
                if (selected && ReferenceEquals(level, context.Active))
                {
                    ImGui.GetWindowDrawList().AddLine(min + new Vector2(1f, 1f), new Vector2(max.X - 1f, min.Y + 1f), ImGui.GetColorU32(Theme.Accent), 2f);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(level.Session.ProjectPath ?? "Never saved.");
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
                    {
                        closing = level;
                    }
                }

                if (selected)
                {
                    ImGui.EndTabItem();

                    // Chosen by the strip only once a level brought forward otherwise has its tab.
                    if (ReferenceEquals(level, _bringForward))
                    {
                        _bringForward = null;
                    }
                    else if (_bringForward is null && !ReferenceEquals(level, context.Active))
                    {
                        chosen = level;
                    }
                }

                if (!open)
                {
                    closing = level;
                }
            }

            bool newLevel = ImGui.TabItemButton("+", ImGuiTabItemFlags.NoTooltip);
            NewRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            if (newLevel)
            {
                context.New();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"New level{Shortcut.Hint(EditorAction.NewLevel)}");
            }

            ImGui.EndTabBar();
        }

        ImGui.End();

        if (closing is not null)
        {
            context.Close(closing);
        }
        else if (chosen is not null)
        {
            _drawnActive = chosen;
            context.Show(chosen);
        }

        return height;
    }
}
