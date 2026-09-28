using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// The editor's visual language: a dark, neutral palette in the vein of Blender 4.x, with one blue
/// accent for selection and one orange for the active object.
///
/// It lives in one place on purpose. The default ImGui look reads as a debug tool for three
/// reasons — a 13px bitmap font, hard corners with grey hairline borders, and no spacing rhythm —
/// and all three are style state, not a limitation of the library.
/// </summary>
public static class Theme
{
    // ---- Palette -----------------------------------------------------------------------------
    // Neutral greys, never blue-tinted: a coloured chrome fights whatever colour the level is.

    /// <summary>Behind everything — the darkest surface.</summary>
    public static readonly Vector4 Background = Rgb(0x1D1D1D);

    /// <summary>Panels and menus.</summary>
    public static readonly Vector4 Surface = Rgb(0x282828);

    /// <summary>Raised surfaces: headers, tabs, the menu bar.</summary>
    public static readonly Vector4 SurfaceRaised = Rgb(0x303030);

    /// <summary>Inputs and wells, which sit *below* the panel rather than above it.</summary>
    public static readonly Vector4 Sunken = Rgb(0x1E1E1E);

    /// <summary>Buttons and slider troughs at rest.</summary>
    public static readonly Vector4 Control = Rgb(0x4B4B4B);

    public static readonly Vector4 ControlHovered = Rgb(0x5A5A5A);
    public static readonly Vector4 ControlActive = Rgb(0x656565);

    /// <summary>Selection and anything currently switched on.</summary>
    public static readonly Vector4 Accent = Rgb(0x4772B3);

    public static readonly Vector4 AccentHovered = Rgb(0x5480C4);
    public static readonly Vector4 AccentActive = Rgb(0x3B62A0);

    /// <summary>The focused object, and drag readouts — the one warm colour in the interface.</summary>
    public static readonly Vector4 Highlight = Rgb(0xED9E5C);

    public static readonly Vector4 Text = Rgb(0xE5E5E5);
    public static readonly Vector4 TextDim = Rgb(0x9A9A9A);
    public static readonly Vector4 TextDisabled = Rgb(0x6B6B6B);

    /// <summary>Borders are nearly invisible: surfaces are separated by tone, not by lines.</summary>
    public static readonly Vector4 Border = Rgb(0x161616);

    public static readonly Vector4 Danger = Rgb(0xD9584A);
    public static readonly Vector4 Success = Rgb(0x7FBF6A);

    // The three axes, shared by the gizmo, the axis indicator and the dimension labels, so a colour
    // means the same axis everywhere in the interface.
    // Taken from the overlay table rather than repeated, so an axis is the same colour in the
    // gizmo, the corner indicator and the dimension labels.
    public static readonly Vector4 AxisX = Rendering.EditorOverlays.AxisX.ToVector4();
    public static readonly Vector4 AxisY = Rendering.EditorOverlays.AxisY.ToVector4();
    public static readonly Vector4 AxisZ = Rendering.EditorOverlays.AxisZ.ToVector4();

    /// <summary>The 3D viewport's gradient, lighter at the top so the scene reads as having a horizon.</summary>
    public static readonly Vector4 Viewport = Rgb(0x323232);

    public static readonly Vector4 ViewportTop = Rgb(0x434547);

    /// <summary>Every modal's action buttons are this wide, so they line up across dialogs.</summary>
    public static readonly Vector2 ModalButton = new(128f, 0f);

    // ---- Metrics -----------------------------------------------------------------------------

    /// <summary>A size down from where it started: at 16 the panels spent their height on air.</summary>
    public const int FontSizePixels = 15;

    /// <summary>Small but present — Blender's widgets are rounded just enough to read as soft.</summary>
    private const float Rounding = 4f;

    /// <summary>
    /// Font files tried in order. A file dropped in <c>assets/fonts</c> wins, so the look can be
    /// pinned across machines later without changing code; otherwise a system UI font is used, and
    /// failing that ImGui's built-in bitmap font still works.
    /// </summary>
    public static string? ResolveFontPath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "ui.ttf"),
            Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "Inter-Regular.ttf"),
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\SegoeUI.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/System/Library/Fonts/SFNS.ttf",
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static void Apply()
    {
        ImGuiStylePtr style = ImGui.GetStyle();

        style.WindowRounding = Rounding + 2f;
        style.ChildRounding = Rounding;
        style.FrameRounding = Rounding;
        style.PopupRounding = Rounding;
        style.GrabRounding = Rounding;
        style.ScrollbarRounding = Rounding + 3f;
        style.TabRounding = Rounding;

        // One hairline, the colour of the background, so panels read as cut out rather than boxed in.
        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;

        // One spacing scale, in fours and eights, near Blender's density: rows close enough that a
        // panel reads as one list, frames just tall enough for the text and a margin. The first pass
        // was roomier, and a panel of ten settings scrolled where Blender's would not.
        style.WindowPadding = new Vector2(10f, 8f);
        style.FramePadding = new Vector2(8f, 4f);
        style.ItemSpacing = new Vector2(8f, 4f);
        style.ItemInnerSpacing = new Vector2(6f, 4f);
        style.CellPadding = new Vector2(6f, 4f);
        style.IndentSpacing = 16f;
        style.ScrollbarSize = 10f;
        style.GrabMinSize = 8f;

        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SeparatorTextBorderSize = 1f;
        style.SeparatorTextPadding = new Vector2(0f, 4f);

        SetColors(style);
    }

    private static void SetColors(ImGuiStylePtr style)
    {
        Set(style, ImGuiCol.Text, Text);
        Set(style, ImGuiCol.TextDisabled, TextDisabled);

        Set(style, ImGuiCol.WindowBg, Surface);
        Set(style, ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        Set(style, ImGuiCol.PopupBg, SurfaceRaised);
        Set(style, ImGuiCol.Border, Border);
        Set(style, ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));

        Set(style, ImGuiCol.FrameBg, Sunken);
        Set(style, ImGuiCol.FrameBgHovered, Rgb(0x2A2A2A));
        Set(style, ImGuiCol.FrameBgActive, Rgb(0x323232));

        Set(style, ImGuiCol.TitleBg, Background);
        Set(style, ImGuiCol.TitleBgActive, SurfaceRaised);
        Set(style, ImGuiCol.TitleBgCollapsed, Background);

        Set(style, ImGuiCol.MenuBarBg, SurfaceRaised);

        Set(style, ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0f));
        Set(style, ImGuiCol.ScrollbarGrab, Control);
        Set(style, ImGuiCol.ScrollbarGrabHovered, ControlHovered);
        Set(style, ImGuiCol.ScrollbarGrabActive, ControlActive);

        Set(style, ImGuiCol.CheckMark, Highlight);
        Set(style, ImGuiCol.SliderGrab, Control);
        Set(style, ImGuiCol.SliderGrabActive, Accent);

        Set(style, ImGuiCol.Button, Control);
        Set(style, ImGuiCol.ButtonHovered, ControlHovered);
        Set(style, ImGuiCol.ButtonActive, Accent);

        // Section headers and list rows share this colour. Accent here would paint every collapsing
        // header a solid blue bar and drown the one thing selection is meant to point at.
        Set(style, ImGuiCol.Header, SurfaceRaised);
        Set(style, ImGuiCol.HeaderHovered, Control);
        Set(style, ImGuiCol.HeaderActive, ControlActive);

        Set(style, ImGuiCol.Separator, Border);
        Set(style, ImGuiCol.SeparatorHovered, Accent);
        Set(style, ImGuiCol.SeparatorActive, AccentActive);

        Set(style, ImGuiCol.ResizeGrip, new Vector4(0f, 0f, 0f, 0f));
        Set(style, ImGuiCol.ResizeGripHovered, Control);
        Set(style, ImGuiCol.ResizeGripActive, Accent);

        Set(style, ImGuiCol.Tab, Surface);
        Set(style, ImGuiCol.TabHovered, ControlHovered);
        Set(style, ImGuiCol.TabActive, SurfaceRaised);

        Set(style, ImGuiCol.PlotLines, TextDim);
        Set(style, ImGuiCol.PlotLinesHovered, Highlight);
        Set(style, ImGuiCol.PlotHistogram, Accent);
        Set(style, ImGuiCol.PlotHistogramHovered, AccentHovered);

        Set(style, ImGuiCol.TableHeaderBg, SurfaceRaised);
        Set(style, ImGuiCol.TableBorderStrong, Border);
        Set(style, ImGuiCol.TableBorderLight, Border);

        Set(style, ImGuiCol.TextSelectedBg, Accent with { W = 0.45f });
        Set(style, ImGuiCol.DragDropTarget, Highlight);
        Set(style, ImGuiCol.NavHighlight, Accent);
        Set(style, ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.55f));
    }

    private static void Set(ImGuiStylePtr style, ImGuiCol target, Vector4 color) =>
        style.Colors[(int)target] = color;

    private static Vector4 Rgb(uint hex) => new(
        ((hex >> 16) & 0xFF) / 255f,
        ((hex >> 8) & 0xFF) / 255f,
        (hex & 0xFF) / 255f,
        1f);
}
