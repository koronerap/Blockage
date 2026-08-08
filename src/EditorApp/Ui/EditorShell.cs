using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
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
public sealed class EditorShell
{
    private const float ToolColumnWidth = ToolColumn.Width;
    private const float PropertiesColumnWidth = 340f;

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
            context.Session, context.Project, context.Export, context.View, context.OnExit);

        Vector2 screen = ImGui.GetIO().DisplaySize;
        float optionsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2f;
        float statusHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y;

        float top = menuHeight;
        float bottom = screen.Y - statusHeight;

        DrawToolOptions(context, new Vector2(0f, top), new Vector2(screen.X, optionsHeight));
        top += optionsHeight;

        DrawToolColumn(context, new Vector2(0f, top), new Vector2(ToolColumnWidth, bottom - top));

        DrawProperties(
            context,
            new Vector2(screen.X - PropertiesColumnWidth, top),
            new Vector2(PropertiesColumnWidth, bottom - top));

        DrawStatusBar(context, new Vector2(0f, bottom), new Vector2(screen.X, statusHeight));

        return new ViewportRect(
            new Vector2(ToolColumnWidth, top),
            new Vector2(
                MathF.Max(screen.X - ToolColumnWidth - PropertiesColumnWidth, 1f),
                MathF.Max(bottom - top, 1f)));
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
        ImGui.TextColored(Theme.Highlight, session.ActiveTool.ToString());
        ImGui.SameLine(0f, 16f);

        ToolOptions.Draw(session);

        ImGui.End();
    }

    private static void DrawToolColumn(ShellContext context, Vector2 position, Vector2 size)
    {
        BeginPanel("##tools", position, size, ImGuiWindowFlags.NoScrollbar);
        ToolColumn.Draw(context.Session);
        ImGui.End();
    }

    private void DrawProperties(ShellContext context, Vector2 position, Vector2 size)
    {
        BeginPanel("##properties", position, size);

        // Collapsing sections rather than separate windows: one column, one scrollbar, one place to
        // look for anything that is not the model itself.
        if (ImGui.CollapsingHeader("Objects", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ObjectListPanel.Draw(context.Session);
        }

        if (ImGui.CollapsingHeader("Palette", ImGuiTreeNodeFlags.DefaultOpen))
        {
            context.Palette.DrawContent(context.Session);
        }

        if (ImGui.CollapsingHeader("Reference model"))
        {
            context.Reference.DrawContent(context.Session, context.ReferenceRenderer);
        }

        if (ImGui.CollapsingHeader("Statistics"))
        {
            context.Stats.DrawContent(context.Renderer, context.Camera, context.Session);
        }

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

    public required Rendering.GlRenderer Renderer { get; init; }

    public required Rendering.FlyCamera Camera { get; init; }

    public required PalettePanel Palette { get; init; }

    public required ReferencePanel Reference { get; init; }

    public required Rendering.ReferenceModelRenderer ReferenceRenderer { get; init; }

    public required StatsOverlay Stats { get; init; }

    public required ViewActions View { get; init; }

    public required Action OnExit { get; init; }

    public RaycastHit? Hover { get; set; }

    public string DragReadout { get; set; } = string.Empty;

    public float FrameSeconds { get; set; }
}
