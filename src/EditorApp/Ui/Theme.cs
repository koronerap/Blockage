using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>The interface's colours as a whole.</summary>
public enum ThemeKind
{
    /// <summary>Neutral dark greys, in the vein of Blender 4.x.</summary>
    Dark,

    /// <summary>Nearer black, for a dim room or an OLED screen.</summary>
    Darker,

    /// <summary>Light greys with dark text, for a bright room.</summary>
    Light,
}

/// <summary>The one colour selection and anything switched on is marked in.</summary>
public enum AccentKind
{
    Blue,
    Teal,
    Green,
    Purple,
    Rose,
}

/// <summary>How big the interface's text is.</summary>
public enum TextSize
{
    Small,
    Normal,
    Large,
    Larger,
}

/// <summary>
/// The editor's visual language: a neutral palette in the vein of Blender 4.x, with one accent for
/// selection and one orange for the active object — dark by default, darker or light by choice.
///
/// It lives in one place on purpose. The default ImGui look reads as a debug tool for three
/// reasons — a 13px bitmap font, hard corners with grey hairline borders, and no spacing rhythm —
/// and all three are style state, not a limitation of the library.
///
/// The colours are read wherever something is drawn rather than copied once, so switching theme in
/// Preferences changes the next frame everywhere.
/// </summary>
public static class Theme
{
    // ---- Palette -----------------------------------------------------------------------------
    // Neutral greys, never blue-tinted: a coloured chrome fights whatever colour the level is.

    /// <summary>Behind everything — the darkest surface.</summary>
    public static Vector4 Background { get; private set; } = Rgb(0x1D1D1D);

    /// <summary>Panels and menus.</summary>
    public static Vector4 Surface { get; private set; } = Rgb(0x282828);

    /// <summary>Raised surfaces: headers, tabs, the menu bar.</summary>
    public static Vector4 SurfaceRaised { get; private set; } = Rgb(0x303030);

    /// <summary>Inputs and wells, which sit *below* the panel rather than above it.</summary>
    public static Vector4 Sunken { get; private set; } = Rgb(0x1E1E1E);

    /// <summary>Buttons and slider troughs at rest.</summary>
    public static Vector4 Control { get; private set; } = Rgb(0x4B4B4B);

    public static Vector4 ControlHovered { get; private set; } = Rgb(0x5A5A5A);
    public static Vector4 ControlActive { get; private set; } = Rgb(0x656565);

    /// <summary>Selection and anything currently switched on.</summary>
    public static Vector4 Accent { get; private set; } = Rgb(0x4772B3);

    public static Vector4 AccentHovered { get; private set; } = Rgb(0x5480C4);
    public static Vector4 AccentActive { get; private set; } = Rgb(0x3B62A0);

    /// <summary>The focused object, and drag readouts — the one warm colour in the interface.</summary>
    public static Vector4 Highlight { get; private set; } = Rgb(0xED9E5C);

    public static Vector4 Text { get; private set; } = Rgb(0xE5E5E5);
    public static Vector4 TextDim { get; private set; } = Rgb(0x9A9A9A);
    public static Vector4 TextDisabled { get; private set; } = Rgb(0x6B6B6B);

    /// <summary>Borders are nearly invisible: surfaces are separated by tone, not by lines.</summary>
    public static Vector4 Border { get; private set; } = Rgb(0x161616);

    /// <summary>Text and icons drawn over the accent: light on it whatever the theme, since every accent is a mid tone.</summary>
    public static Vector4 TextOnAccent { get; private set; } = Rgb(0xF2F2F2);

    /// <summary>Behind an input while it is hovered and while it is being edited.</summary>
    private static Vector4 SunkenHovered { get; set; } = Rgb(0x2A2A2A);

    private static Vector4 SunkenActive { get; set; } = Rgb(0x323232);

    public static Vector4 Danger { get; private set; } = Rgb(0xD9584A);
    public static Vector4 Success { get; private set; } = Rgb(0x7FBF6A);

    // The three axes, shared by the gizmo, the axis indicator and the dimension labels, so a colour
    // means the same axis everywhere in the interface.
    // Taken from the overlay table rather than repeated, so an axis is the same colour in the
    // gizmo, the corner indicator and the dimension labels.
    public static Vector4 AxisX => Rendering.EditorOverlays.AxisX.ToVector4();
    public static Vector4 AxisY => Rendering.EditorOverlays.AxisY.ToVector4();
    public static Vector4 AxisZ => Rendering.EditorOverlays.AxisZ.ToVector4();

    /// <summary>
    /// How many window pixels one interface unit is (see Rendering.ImGuiLayer): 1.5 on a 150%
    /// display. Set once as the editor starts; what reads the mouse directly divides by it.
    /// </summary>
    public static float UiScale { get; set; } = 1f;

    /// <summary>The 3D viewport's gradient, lighter at the top so the scene reads as having a horizon.</summary>
    public static Vector4 Viewport { get; private set; } = Rgb(0x323232);

    public static Vector4 ViewportTop { get; private set; } = Rgb(0x434547);

    /// <summary>Every modal's action buttons are this wide, so they line up across dialogs.</summary>
    public static readonly Vector2 ModalButton = new(128f, 0f);

    // ---- Metrics -----------------------------------------------------------------------------

    /// <summary>A size down from where it started: at 16 the panels spent their height on air.</summary>
    public const int FontSizePixels = 15;

    /// <summary>The text sizes on offer, in pixels. All are put in the font atlas at start, so a change is a pointer swap.</summary>
    public static int PixelsFor(TextSize size) => size switch
    {
        TextSize.Small => 13,
        TextSize.Large => 17,
        TextSize.Larger => 19,
        _ => FontSizePixels,
    };

    private static readonly Dictionary<TextSize, ImFontPtr> Fonts = [];

    /// <summary>The welcome screen's name over its picture: far past any text size, so it gets a face of its own.</summary>
    public const int TitlePixels = 34;

    private static ImFontPtr? _title;

    /// <summary>
    /// The face and size for a title. Scaled up from the ordinary face when there is no title face —
    /// ImGui's built-in font, or a test with no atlas built — soft, but in the right place.
    /// </summary>
    public static (ImFontPtr Font, float Size) Title =>
        _title is { } title ? (title, TitlePixels) : (ImGui.GetFont(), ImGui.GetFontSize() * 2.2f);

    /// <summary>The theme in use, as last applied.</summary>
    public static ThemeKind Kind { get; private set; } = ThemeKind.Dark;

    public static AccentKind AccentColour { get; private set; } = AccentKind.Blue;

    /// <summary>
    /// The characters the interface's font carries: Latin with Latin-1 and Latin Extended-A — where
    /// Turkish's İ, Ğ and Ş are, outside ImGui's default, so they showed as question marks in a name —
    /// and the punctuation and arrows the interface's own text uses. Held for the life of the program:
    /// the atlas reads it whenever it is built.
    /// </summary>
    public static IntPtr GlyphRanges
    {
        get
        {
            if (_glyphRanges == IntPtr.Zero)
            {
                ushort[] ranges = [0x0020, 0x017F, 0x2000, 0x206F, 0x2190, 0x21FF, 0x2212, 0x2212, 0];
                _glyphRanges = Marshal.AllocHGlobal(ranges.Length * sizeof(ushort));
                for (int i = 0; i < ranges.Length; i++)
                {
                    Marshal.WriteInt16(_glyphRanges, i * sizeof(ushort), unchecked((short)ranges[i]));
                }
            }

            return _glyphRanges;
        }
    }

    private static IntPtr _glyphRanges;

    /// <summary>Called while the font atlas is being built: one face at every size.</summary>
    /// <param name="pixelsPerUnit">
    /// Framebuffer pixels to an interface unit. Each font is drawn into the atlas that many times its
    /// size and shown at its size (ImGui's FontGlobalScale), so text stays sharp at any scale.
    /// </param>
    public static void AddFonts(ImFontAtlasPtr atlas, string path, ImFontPtr normal, float pixelsPerUnit = 1f)
    {
        Fonts[TextSize.Normal] = normal;

        foreach (TextSize size in Enum.GetValues<TextSize>())
        {
            if (size != TextSize.Normal)
            {
                Fonts[size] = atlas.AddFontFromFileTTF(path, PixelsFor(size) * pixelsPerUnit, new ImFontConfigPtr(IntPtr.Zero), GlyphRanges);
            }
        }

        _title = atlas.AddFontFromFileTTF(path, TitlePixels * pixelsPerUnit, new ImFontConfigPtr(IntPtr.Zero), GlyphRanges);
    }

    /// <summary>Whether the text size can be changed — not with ImGui's built-in bitmap font, which comes in one size.</summary>
    public static bool HasSizes => Fonts.Count > 1;

    /// <summary>Makes a size the default font from the next frame on.</summary>
    public static unsafe void UseTextSize(TextSize size)
    {
        if (Fonts.TryGetValue(size, out ImFontPtr font))
        {
            ImGui.GetIO().NativePtr->FontDefault = font.NativePtr;
        }
    }

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

    /// <summary>Switches palette and accent, and puts them into ImGui's style.</summary>
    public static void Apply(ThemeKind kind, AccentKind accent)
    {
        Kind = kind;
        AccentColour = accent;

        switch (kind)
        {
            case ThemeKind.Light:
                Background = Rgb(0xBDBDBD);
                Surface = Rgb(0xD6D6D6);
                SurfaceRaised = Rgb(0xE3E3E3);
                Sunken = Rgb(0xF2F2F2);
                SunkenHovered = Rgb(0xFAFAFA);
                SunkenActive = Rgb(0xFFFFFF);
                Control = Rgb(0xBFBFBF);
                ControlHovered = Rgb(0xB2B2B2);
                ControlActive = Rgb(0xA6A6A6);
                Text = Rgb(0x1C1C1C);
                TextDim = Rgb(0x585858);
                TextDisabled = Rgb(0x8E8E8E);
                Border = Rgb(0xA8A8A8);
                Highlight = Rgb(0xC0600F);
                Danger = Rgb(0xC0392B);
                Success = Rgb(0x3E8E3E);

                // Mid grey rather than light: a white model on a white page is a model nobody can see.
                Viewport = Rgb(0x8C8E91);
                ViewportTop = Rgb(0xB0B2B5);
                break;

            case ThemeKind.Darker:
                Background = Rgb(0x0D0D0D);
                Surface = Rgb(0x161616);
                SurfaceRaised = Rgb(0x1D1D1D);
                Sunken = Rgb(0x0A0A0A);
                SunkenHovered = Rgb(0x191919);
                SunkenActive = Rgb(0x222222);
                Control = Rgb(0x333333);
                ControlHovered = Rgb(0x3F3F3F);
                ControlActive = Rgb(0x4A4A4A);
                Text = Rgb(0xE8E8E8);
                TextDim = Rgb(0x969696);
                TextDisabled = Rgb(0x5C5C5C);
                Border = Rgb(0x080808);
                Highlight = Rgb(0xED9E5C);
                Danger = Rgb(0xD9584A);
                Success = Rgb(0x7FBF6A);
                Viewport = Rgb(0x1C1C1D);
                ViewportTop = Rgb(0x2A2B2D);
                break;

            default:
                Background = Rgb(0x1D1D1D);
                Surface = Rgb(0x282828);
                SurfaceRaised = Rgb(0x303030);
                Sunken = Rgb(0x1E1E1E);
                SunkenHovered = Rgb(0x2A2A2A);
                SunkenActive = Rgb(0x323232);
                Control = Rgb(0x4B4B4B);
                ControlHovered = Rgb(0x5A5A5A);
                ControlActive = Rgb(0x656565);
                Text = Rgb(0xE5E5E5);
                TextDim = Rgb(0x9A9A9A);
                TextDisabled = Rgb(0x6B6B6B);
                Border = Rgb(0x161616);
                Highlight = Rgb(0xED9E5C);
                Danger = Rgb(0xD9584A);
                Success = Rgb(0x7FBF6A);
                Viewport = Rgb(0x323232);
                ViewportTop = Rgb(0x434547);
                break;
        }

        (Accent, AccentHovered, AccentActive) = AccentShades(accent);

        TextOnAccent = Rgb(0xF4F4F4);
        Apply();
    }

    /// <summary>An accent as it is shown at rest — for the swatches that choose it.</summary>
    public static Vector4 AccentOf(AccentKind accent) => AccentShades(accent).Normal;

    private static (Vector4 Normal, Vector4 Hovered, Vector4 Pressed) AccentShades(AccentKind accent) => accent switch
    {
        AccentKind.Teal => (Rgb(0x2E8C8C), Rgb(0x399B9B), Rgb(0x277878)),
        AccentKind.Green => (Rgb(0x3E8A4F), Rgb(0x4A9A5C), Rgb(0x347642)),
        AccentKind.Purple => (Rgb(0x7456B8), Rgb(0x8165C6), Rgb(0x6448A2)),
        AccentKind.Rose => (Rgb(0xB24C74), Rgb(0xC05A82), Rgb(0x9C4064)),
        _ => (Rgb(0x4772B3), Rgb(0x5480C4), Rgb(0x3B62A0)),
    };

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
        Set(style, ImGuiCol.FrameBgHovered, SunkenHovered);
        Set(style, ImGuiCol.FrameBgActive, SunkenActive);

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
        Set(style, ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, Kind == ThemeKind.Light ? 0.3f : 0.55f));
    }

    private static void Set(ImGuiStylePtr style, ImGuiCol target, Vector4 color) =>
        style.Colors[(int)target] = color;

    private static Vector4 Rgb(uint hex) => new(
        ((hex >> 16) & 0xFF) / 255f,
        ((hex >> 8) & 0xFF) / 255f,
        (hex & 0xFF) / 255f,
        1f);
}
