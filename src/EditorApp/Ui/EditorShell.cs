using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>Where the 3D view is allowed to draw, in logical window pixels.</summary>
public readonly record struct ViewportRect(Vector2 Position, Vector2 Size)
{
    public float AspectRatio => Size.X / MathF.Max(Size.Y, 1f);

    public bool Contains(Vector2 point) =>
        point.X >= Position.X && point.X < Position.X + Size.X
        && point.Y >= Position.Y && point.Y < Position.Y + Size.Y;

    /// <summary>The cursor relative to the viewport's own top-left corner.</summary>
    public Vector2 ToLocal(Vector2 windowPoint) => windowPoint - Position;
}

/// <summary>
/// The application shell: a fixed frame of panels around a 3D view, rather than free-floating
/// windows over it. Panels here cannot be dragged, resized or closed — an editor's layout is part
/// of the product, and windows scattered across the viewport are the thing that makes a tool look
/// like a debug overlay.
///
/// The layout is computed by hand rather than docked. ImGui.NET exposes <c>DockSpace</c> but not
/// <c>DockBuilder</c>, so a docked default layout could only come from a shipped ini file — a fixed
/// shell is both simpler and more predictable.
/// </summary>
public sealed class EditorShell(LayoutSettings layout)
{

    /// <summary>How far the floating tool column sits in from the viewport's corner.</summary>
    private const float ToolInset = 8f;

    private readonly NavigationGizmo _navigation = new();
    private readonly Sidebar _sidebar = new(layout);

    private const ImGuiWindowFlags PanelFlags =
        ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoNavFocus;

    /// <summary>Draws the whole frame and returns the space left for the 3D view.</summary>
    public ViewportRect Draw(ShellContext context)
    {
        float menuHeight = MainMenu.Draw(
            context.Session, context.Project, context.Export, context.Mimicraft, context.View, context.OnExit);

        Vector2 screen = ImGui.GetIO().DisplaySize;
        float optionsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2f;
        float statusHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y;

        float top = menuHeight;
        float bottom = screen.Y - statusHeight;

        DrawToolOptions(context, new Vector2(0f, top), new Vector2(screen.X, optionsHeight));
        top += optionsHeight;

        // A rename has to be seen to be typed, so asking for one brings the sidebar back.
        if (ObjectListPanel.RenamePending)
        {
            layout.SidebarVisible = true;
        }

        float sidebarLeft = layout.SidebarVisible
            ? _sidebar.Draw(top, bottom, screen.X, SidebarContentFor(context))
            : screen.X;

        DrawStatusBar(context, new Vector2(0f, bottom), new Vector2(screen.X, statusHeight));

        // The viewport runs all the way to the left edge now; the tools float over it rather than
        // taking a strip of their own.
        DrawToolColumn(context, new Vector2(ToolInset, top + ToolInset));

        var viewport = new ViewportRect(
            new Vector2(0f, top),
            new Vector2(MathF.Max(sidebarLeft, 1f), MathF.Max(bottom - top, 1f)));

        _navigation.Draw(context.Camera, viewport, context.View.FrameLevel);
        DrawStatistics(context, viewport);

        return viewport;
    }

    private static void BeginPanel(string id, Vector2 position, Vector2 size, ImGuiWindowFlags extra = ImGuiWindowFlags.None)
    {
        ImGui.SetNextWindowPos(position);
        ImGui.SetNextWindowSize(size);
        ImGui.Begin(id, PanelFlags | extra);
    }

    /// <summary>
    /// The header strip: settings for the active tool and nothing else. A control that does not
    /// apply right now is not dimmed here, it is absent.
    /// </summary>
    private static void DrawToolOptions(ShellContext context, Vector2 position, Vector2 size)
    {
        BeginPanel("##tool-options", position, size, ImGuiWindowFlags.NoScrollbar);

        EditorSession session = context.Session;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(ToolColumn.Describe(session.ActiveTool).Name);
        ImGui.SameLine(0f, 14f);

        ToolOptions.Draw(session);
        DrawOverlayToggles(context, size);

        ImGui.End();
    }

    /// <summary>
    /// The right-hand end of the header: what can be done to the object as a whole, then — as Blender
    /// puts them — what is drawn over the scene and how the scene is shaded. They belong to the
    /// viewport rather than to any tool, so they keep the same place whichever tool is active.
    /// </summary>
    private static void DrawOverlayToggles(ShellContext context, Vector2 size)
    {
        float button = ImGui.GetFrameHeight();

        // The wider gap is what says the first edits the level and the other two only change how it
        // is looked at.
        const float GroupGap = 14f;
        const float Pair = 4f;
        float width = (button * 3f) + GroupGap + Pair;

        ImGui.SameLine(size.X - width - ImGui.GetStyle().WindowPadding.X);

        DrawObjectMenuButton(context, button);

        ImGui.SameLine(0f, GroupGap);
        OverlaysMenu.DrawButton(context.View, button);

        ImGui.SameLine(0f, Pair);

        SceneLighting lighting = context.View.Lighting;
        if (IconButton.Toggle(
                "shading",
                (Icons.Lit, "Lit  -  by the level's own lights"),
                (Icons.Unlit, "Unlit  -  flat per-face shade, as exported"),
                !lighting.IsLit,
                string.Empty,
                button))
        {
            lighting.Mode = lighting.IsLit ? ShadingMode.Unlit : ShadingMode.Lit;
        }
    }

    /// <summary>
    /// Turning and mirroring the focused object, behind one button. Four of these used to sit in the
    /// header permanently; three more would have made seven, for something done a few times a session.
    /// </summary>
    private static void DrawObjectMenuButton(ShellContext context, float button)
    {
        if (IconButton.Draw(
                "object-menu",
                Icons.Rotate,
                active: false,
                "Object  -  turn or flip the focused object's voxels",
                button,
                hasAlternatives: true))
        {
            ImGui.OpenPopup("##object-menu");
        }

        if (ImGui.BeginPopup("##object-menu"))
        {
            ObjectMenu.DrawItems(context.Session, context.Camera);
            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// The tools, floating over the top-left of the viewport with no panel behind them — the way
    /// Blender's toolbar sits. A column of its own spent the full height of the window on four buttons.
    ///
    /// Sized to its buttons and no larger, because an ImGui window takes the mouse wherever its
    /// rectangle is, background or not: any slack around the buttons would be a patch of viewport
    /// that stops answering clicks.
    /// </summary>
    private static void DrawToolColumn(ShellContext context, Vector2 position)
    {
        ImGui.SetNextWindowPos(position);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 4f));

        ImGui.Begin(
            "##tools",
            PanelFlags
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar);

        ToolColumn.Draw(context.Session, context.Palette);

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    /// <summary>What each part of the sidebar shows, gathered from this frame's context.</summary>
    private static SidebarContent SidebarContentFor(ShellContext context) => new()
    {
        Outliner = size => ObjectListPanel.Draw(context.Session, context.Camera, size),
        Properties = tab => DrawTab(context, tab),
        Tab = tab => tab switch
        {
            // The tool's own icon, so the strip says which tool the tab is about.
            PropertiesTab.Tool => (ToolColumn.Describe(context.Session.ActiveTool).Icon, $"Tool  -  {ToolColumn.Describe(context.Session.ActiveTool).Name}"),
            // Picking a light turns the Object tab into the light's: the same place, for whatever is picked.
            PropertiesTab.Object => context.Session.SelectedLight is { } light
                ? (Icons.For(light.Kind), $"Light  -  {light.Name}")
                : (Icons.ObjectTab, "Object"),
            PropertiesTab.World => (Icons.WorldTab, "World  -  the level, its grid and its lighting"),
            PropertiesTab.Palette => (Icons.PaletteTab, "Palette"),
            _ => (Icons.ReferenceTab, "Reference model"),
        },
    };

    private static void DrawTab(ShellContext context, PropertiesTab tab)
    {
        switch (tab)
        {
            case PropertiesTab.Tool:
                ToolPanel.DrawContent(context.Session);
                break;

            case PropertiesTab.Object:
                ObjectPropertiesPanel.DrawContent(context.Session);
                break;

            case PropertiesTab.World:
                if (Props.Section("Level"))
                {
                    LevelPanel.DrawContent(context.Session);
                }

                if (Props.Section("Lighting"))
                {
                    LightingPanel.DrawContent(context.Session, context.View.Lighting, context.Renderer.DroppedLights);
                }

                break;

            case PropertiesTab.Palette:
                context.Palette.DrawContent(context.Session);
                break;

            default:
                context.Reference.DrawContent(context.Session, context.ReferenceRenderer);
                break;
        }
    }

    /// <summary>
    /// The frame and mesh counters, over the bottom-left of the viewport where Blender puts its own —
    /// numbers to glance at while working, not a section to scroll to.
    /// </summary>
    private static void DrawStatistics(ShellContext context, ViewportRect viewport)
    {
        if (!context.View.StatisticsVisible())
        {
            return;
        }

        ImGui.SetNextWindowPos(viewport.Position + new Vector2(12f, viewport.Size.Y - 12f), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowBgAlpha(0.72f);
        ImGui.Begin("##statistics", PanelFlags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs);
        context.Stats.DrawContent(context.Renderer, context.Camera, context.Session);
        ImGui.End();
    }

    private static void DrawStatusBar(ShellContext context, Vector2 position, Vector2 size)
    {
        BeginPanel("##status", position, size, ImGuiWindowFlags.NoScrollbar);
        StatusBar.Draw(context);
        ImGui.End();
    }
}

/// <summary>Everything the shell needs to draw a frame, passed as one bundle rather than ten parameters.</summary>
public sealed class ShellContext
{
    public required EditorSession Session { get; init; }

    public required ProjectController Project { get; init; }

    public required ExportController Export { get; init; }

    public required MimicraftController Mimicraft { get; init; }

    public required Rendering.GlRenderer Renderer { get; init; }

    public required Rendering.FlyCamera Camera { get; init; }

    public required PalettePanel Palette { get; init; }

    public required ReferencePanel Reference { get; init; }

    public required Rendering.ReferenceModelRenderer ReferenceRenderer { get; init; }

    public required StatsOverlay Stats { get; init; }

    public required ViewActions View { get; init; }

    public required Action OnExit { get; init; }

    public RaycastHit? Hover { get; set; }

    /// <summary>Whether the right button is held and the mouse is turning the view.</summary>
    public bool Looking { get; set; }

    public string DragReadout { get; set; } = string.Empty;

    public float FrameSeconds { get; set; }
}
