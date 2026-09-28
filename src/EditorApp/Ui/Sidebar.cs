using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What the sidebar shows. The sidebar itself only lays it out and handles its handles.</summary>
public sealed class SidebarContent
{
    /// <summary>Draws the Outliner into the space it is given.</summary>
    public required Action<Vector2> Outliner { get; init; }

    /// <summary>Draws one tab's contents. Called inside a region that scrolls on its own.</summary>
    public required Action<PropertiesTab> Properties { get; init; }

    /// <summary>What a tab's button shows — the Object tab turns into a Light tab while a light is picked.</summary>
    public required Func<PropertiesTab, (Icons.Painter Icon, string Name)> Tab { get; init; }
}

/// <summary>
/// The right-hand column, laid out the way Blender lays out its Outliner and Properties editor: the
/// Outliner always on top, the Properties below it behind a strip of icon tabs, a divider between the
/// two that can be dragged, and a left edge that can be dragged to make the whole column wider.
///
/// One long scroll of sections used to hold all of this, and whatever was open near the top decided
/// what could be seen at the bottom — the palette alone pushed three sections out of sight. Now the
/// Outliner keeps its place and each tab gets the whole height below it.
/// </summary>
public sealed class Sidebar(LayoutSettings layout)
{
    public const float TabButtonSize = 26f;

    /// <summary>The tab strip: a button and a margin either side.</summary>
    public const float TabStripWidth = TabButtonSize + 8f;

    public const float DividerThickness = 6f;

    /// <summary>How wide the grip on the column's left edge is: half over the viewport, half over the column.</summary>
    public const float EdgeGrip = 8f;

    /// <summary>However wide the column is dragged, this much of the window stays viewport.</summary>
    private const float MinViewportWidth = 320f;

    private const ImGuiWindowFlags AreaFlags =
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoNavFocus
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>
    /// For the two handles. Without NoBringToFrontOnFocus: ImGui puts a window with that flag at the
    /// bottom of the pile when it is created, and a handle made after the areas it sits on would then
    /// be underneath them, unable to take the mouse.
    /// </summary>
    private const ImGuiWindowFlags HandleFlags = (AreaFlags & ~ImGuiWindowFlags.NoBringToFrontOnFocus) | ImGuiWindowFlags.NoBackground;

    private static readonly PropertiesTab[] Tabs = Enum.GetValues<PropertiesTab>();

    public LayoutSettings Layout => layout;

    /// <summary>
    /// Draws the column against the right edge of the screen, between <paramref name="top"/> and
    /// <paramref name="bottom"/>, and returns where its left edge ended up.
    /// </summary>
    public float Draw(float top, float bottom, float screenWidth, SidebarContent content)
    {
        float width = MathF.Min(layout.SidebarWidth, MathF.Max(screenWidth - MinViewportWidth, LayoutSettings.MinSidebarWidth));
        float left = screenWidth - width;
        float height = MathF.Max(bottom - top, 1f);

        float outliner = layout.OutlinerHeightWithin(height - DividerThickness);
        float dividerTop = top + outliner;
        float propertiesTop = dividerTop + DividerThickness;

        DrawOutliner(new Vector2(left, top), new Vector2(width, outliner), content);
        DrawDivider(new Vector2(left, dividerTop), width, height);
        DrawProperties(new Vector2(left, propertiesTop), new Vector2(width, MathF.Max(bottom - propertiesTop, 1f)), content);
        DrawEdge(new Vector2(left, top), height);

        return left;
    }

    private static void DrawOutliner(Vector2 position, Vector2 size, SidebarContent content)
    {
        ImGui.SetNextWindowPos(position);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f));
        ImGui.Begin("##outliner-area", AreaFlags);

        content.Outliner(ImGui.GetContentRegionAvail());

        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// The gap between the two areas is the handle, as in Blender: nothing drawn until the cursor
    /// finds it, then a line to show it can be taken.
    /// </summary>
    private void DrawDivider(Vector2 position, float width, float columnHeight)
    {
        ImGui.SetNextWindowPos(position);
        ImGui.SetNextWindowSize(new Vector2(width, DividerThickness));
        PushHandleStyle();
        ImGui.Begin("##sidebar-divider", HandleFlags);

        ImGui.InvisibleButton("##divider", new Vector2(width, DividerThickness));

        if (ImGui.IsItemActive())
        {
            // From the height as drawn, and clamped again after: the stored height is always one
            // that fits this column, so dragging back from past the limit responds at once.
            float available = columnHeight - DividerThickness;
            layout.OutlinerHeight = layout.OutlinerHeightWithin(available) + ImGui.GetIO().MouseDelta.Y;
            layout.OutlinerHeight = layout.OutlinerHeightWithin(available);
        }

        MarkHandle(position + new Vector2(0f, DividerThickness * 0.5f), position + new Vector2(width, DividerThickness * 0.5f), ImGuiMouseCursor.ResizeNS);

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawProperties(Vector2 position, Vector2 size, SidebarContent content)
    {
        ImGui.SetNextWindowPos(position);
        ImGui.SetNextWindowSize(size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##properties-area", AreaFlags);

        DrawTabStrip(size.Y, content);

        ImGui.SameLine(0f, 0f);

        // The tab's contents scroll on their own, so a long tab never moves the strip beside it.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        if (ImGui.BeginChild("##properties-content", new Vector2(-1f, -1f), ImGuiChildFlags.AlwaysUseWindowPadding))
        {
            content.Properties(layout.Tab);
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawTabStrip(float height, SidebarContent content)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Background);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2((TabStripWidth - TabButtonSize) * 0.5f, 6f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 4f));

        if (ImGui.BeginChild("##properties-tabs", new Vector2(TabStripWidth, height), ImGuiChildFlags.AlwaysUseWindowPadding))
        {
            foreach (PropertiesTab tab in Tabs)
            {
                (Icons.Painter icon, string name) = content.Tab(tab);
                if (IconButton.Draw($"tab-{tab}", icon, tab == layout.Tab, name, TabButtonSize))
                {
                    layout.Tab = tab;
                }
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    /// <summary>The column's left edge: drag it to make the column wider or narrower.</summary>
    private void DrawEdge(Vector2 columnTop, float height)
    {
        ImGui.SetNextWindowPos(columnTop - new Vector2(EdgeGrip * 0.5f, 0f));
        ImGui.SetNextWindowSize(new Vector2(EdgeGrip, height));
        PushHandleStyle();
        ImGui.Begin("##sidebar-edge", HandleFlags);

        ImGui.InvisibleButton("##edge", new Vector2(EdgeGrip, height));

        if (ImGui.IsItemActive())
        {
            layout.SidebarWidth -= ImGui.GetIO().MouseDelta.X;
        }

        MarkHandle(columnTop, columnTop + new Vector2(0f, height), ImGuiMouseCursor.ResizeEW);

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// No padding, and no minimum size: ImGui would otherwise widen an eight-pixel grip to its
    /// default minimum of 32, and the grip would sit over the first tab buttons and take their clicks.
    /// </summary>
    private static void PushHandleStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
    }

    /// <summary>A handle shows itself only when the cursor is on it, and changes the cursor to say which way it moves.</summary>
    private static void MarkHandle(Vector2 from, Vector2 to, ImGuiMouseCursor cursor)
    {
        if (!ImGui.IsItemHovered() && !ImGui.IsItemActive())
        {
            return;
        }

        ImGui.SetMouseCursor(cursor);
        ImGui.GetForegroundDrawList().AddLine(from, to, ImGui.GetColorU32(Theme.Accent), 2f);
    }
}
