using System.Globalization;
using System.Numerics;
using EditorApp.Core.Project;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What the welcome screen can do, handed over by the application.</summary>
public sealed class WelcomeActions
{
    public required Action<LevelTemplate> New { get; init; }

    public required Action<LevelSample> OpenSample { get; init; }

    public required Action StartTour { get; init; }

    public required Action Open { get; init; }

    public required Func<IReadOnlyList<string>> Recent { get; init; }

    public required Action<string> OpenRecent { get; init; }

    /// <summary>What the editor last closed on, or null when it has kept nothing yet.</summary>
    public required Func<LastSessionInfo?> LastSession { get; init; }

    public required Action RecoverLastSession { get; init; }

    /// <summary>Whether a crash left autosaved work behind.</summary>
    public required Func<bool> CanRecoverAutoSave { get; init; }

    public required Action RecoverAutoSave { get; init; }

    public required Action<string> OpenUrl { get; init; }

    /// <summary>A newer release GitHub has, when the editor has heard of one.</summary>
    public Func<NewRelease?> NewRelease { get; init; } = () => null;

    public required Action ShowShortcuts { get; init; }

    /// <summary>Where "show this at startup" is kept.</summary>
    public required Preferences Preferences { get; init; }
}

/// <summary>
/// Blender's splash screen: what the editor opens on. A picture across the top with the name and the
/// version; below it the templates a new level can start from, Open, the tour and the recoveries on
/// the left, the recent files and the samples on the right; and along the bottom the project's page,
/// the manual and the keyboard sheet.
/// Anything chosen closes it, and so do Esc and a click outside it. Help > Welcome Screen brings it
/// back.
/// </summary>
public static class WelcomeScreen
{
    /// <summary>More than this and the list stops being a glance.</summary>
    public const int RecentShown = 8;

    private const string PopupId = "##welcome";

    private static readonly Dictionary<string, (Vector2 Min, Vector2 Max)> Rects = [];

    private static bool _openRequested;
    private static bool _dismissRequested;

    /// <summary>Which recent files are still there, looked up once as it opens rather than every frame.</summary>
    private static Dictionary<string, bool> _present = [];

    public static bool IsOpen { get; private set; }

    /// <summary>Asks for it; it opens, centred, on the next frame drawn.</summary>
    public static void Open() => _openRequested = true;

    /// <summary>Asks it to go — a key pressed for something else, which the key then does.</summary>
    public static void Dismiss() => _dismissRequested = IsOpen;

    /// <summary>Where an entry was drawn last frame, by its id, for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? ItemRect(string id) => Rects.TryGetValue(id, out var rect) ? rect : null;

    public static void Draw(WelcomeActions actions)
    {
        if (_openRequested)
        {
            _openRequested = false;
            _present = actions.Recent().Take(RecentShown).Distinct().ToDictionary(path => path, File.Exists);
            ImGui.OpenPopup(PopupId);
        }

        float width = MathF.Round(ImGui.GetFontSize() * 40f);
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize * 0.5f, ImGuiCond.Always, new Vector2(0.5f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));

        // The picture runs to the window's edges; everything under it has its own margins.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        IsOpen = ImGui.BeginPopup(PopupId);
        ImGui.PopStyleVar();

        if (!IsOpen)
        {
            _dismissRequested = false;
            return;
        }

        Rects.Clear();
        Action? chosen = DrawContents(actions, width);

        if (chosen is not null || _dismissRequested || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGui.CloseCurrentPopup();
            IsOpen = false;
        }

        _dismissRequested = false;
        ImGui.EndPopup();

        chosen?.Invoke();
    }

    private static Action? DrawContents(WelcomeActions actions, float width)
    {
        Action? chosen = null;
        float unit = ImGui.GetFontSize();
        float margin = MathF.Round(unit * 1.1f);

        DrawBanner(width, MathF.Round(width * 0.4f));

        ImGui.Dummy(new Vector2(0f, margin * 0.5f));
        ImGui.SetCursorPosX(margin);

        if (ImGui.BeginTable("##welcome-columns", 2, ImGuiTableFlags.SizingStretchSame, new Vector2(width - (margin * 2f), 0f)))
        {
            ImGui.TableNextColumn();
            chosen = DrawStart(actions) ?? chosen;

            ImGui.TableNextColumn();
            chosen = DrawRecent(actions) ?? chosen;

            ImGui.EndTable();
        }

        ImGui.Dummy(new Vector2(0f, margin * 0.35f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, margin * 0.1f));
        ImGui.SetCursorPosX(margin * 0.6f);
        chosen = DrawLinks(actions, width, margin) ?? chosen;
        ImGui.Dummy(new Vector2(0f, margin * 0.4f));

        return chosen;
    }

    /// <summary>The picture, and the name and version over it.</summary>
    private static void DrawBanner(float width, float height)
    {
        ImGui.Dummy(new Vector2(width, height));
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        WelcomeArt.Draw(drawList, min, max, ImGui.GetStyle().PopupRounding);

        float unit = ImGui.GetFontSize();
        float margin = MathF.Round(unit * 1.2f);
        uint white = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
        uint faint = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.6f));

        (ImFontPtr font, float size) = Theme.Title;
        var titleAt = new Vector2(min.X + margin, max.Y - margin - size - unit);
        drawList.AddText(font, size, titleAt, white, "Blockage");
        drawList.AddText(new Vector2(titleAt.X + 2f, titleAt.Y + size + 2f), faint, "Voxel level editor");

        string version = AppVersion.Label;
        Vector2 versionSize = ImGui.CalcTextSize(version);
        drawList.AddText(new Vector2(max.X - margin - versionSize.X, min.Y + (margin * 0.6f)), faint, version);

        if (ImGui.IsItemHovered() && AppVersion.Commit is { } commit)
        {
            ImGui.SetTooltip($"Blockage {AppVersion.Number}, built from {commit}");
        }
    }

    /// <summary>The left column: a new level from a template, Open, and getting lost work back.</summary>
    private static Action? DrawStart(WelcomeActions actions)
    {
        Action? chosen = null;

        Heading("New File");
        foreach (LevelTemplate template in LevelTemplates.All)
        {
            string shortcut = template == LevelTemplate.Cube ? Shortcut.Of(EditorAction.NewLevel) : string.Empty;
            if (Item($"new-{template}".ToLowerInvariant(), IconFor(template), LevelTemplates.NameOf(template), shortcut, null, "A new level: " + LevelTemplates.DescriptionOf(template) + "."))
            {
                chosen = () => actions.New(template);
            }
        }

        ImGui.Spacing();

        if (Item("open", Icons.Folder, "Open...", Shortcut.Of(EditorAction.Open), null, "Open a .vxlevel file."))
        {
            chosen = actions.Open;
        }

        if (Item("tour", Icons.MouseLeft, "Take the Tour", string.Empty, null, "Seven steps on a fresh cube: turning the view, pulling a face out,\npainting, adding a shape, selecting, undoing and saving."))
        {
            chosen = actions.StartTour;
        }

        LastSessionInfo? last = actions.LastSession();
        string lastTip = last is null
            ? "Nothing kept yet. The level is kept each time Blockage closes,\nwhatever the answer to saving it was."
            : $"{last.ProjectName}, as Blockage closed on it {When(last.SavedUtc)}"
                + (last.HadUnsavedChanges ? ",\nwith changes that were never saved." : ".");

        if (Item("recover-last", Icons.Recover, "Recover Last Session", string.Empty, last is null ? lastTip : null, lastTip))
        {
            chosen = actions.RecoverLastSession;
        }

        // Only when a crash left something: otherwise it is a line that is never of use.
        if (actions.CanRecoverAutoSave()
            && Item("recover-autosave", Icons.Recover, "Recover Auto Save...", string.Empty, null, "Work autosaved before Blockage last closed without warning."))
        {
            chosen = actions.RecoverAutoSave;
        }

        return chosen;
    }

    /// <summary>The right column: the files opened last, newest first, and the samples under them.</summary>
    private static Action? DrawRecent(WelcomeActions actions)
    {
        Action? chosen = null;
        Heading("Recent Files");

        IReadOnlyList<string> recent = actions.Recent();
        if (recent.Count == 0)
        {
            ImGui.TextDisabled("Nothing opened yet.");
        }

        foreach (string path in recent.Take(RecentShown))
        {
            bool present = _present.GetValueOrDefault(path, true);
            string tip = present ? path : $"{path}\nNot there any more - moved, renamed or deleted.";

            if (Item($"recent:{path}", Icons.Document, Path.GetFileName(path), string.Empty, present ? null : tip, tip))
            {
                chosen = () => actions.OpenRecent(path);
            }
        }

        ImGui.Spacing();
        Heading("Samples");
        foreach (LevelSample sample in LevelSamples.All)
        {
            if (Item($"sample-{sample}".ToLowerInvariant(), IconFor(sample), LevelSamples.NameOf(sample), string.Empty, null, "A finished little level to look round and take apart: " + LevelSamples.DescriptionOf(sample) + "."))
            {
                chosen = () => actions.OpenSample(sample);
            }
        }

        return chosen;
    }

    /// <summary>The bottom row: the project's page and the keys, and whether this shows at startup.</summary>
    private static Action? DrawLinks(WelcomeActions actions, float width, float margin)
    {
        Action? chosen = null;

        if (Link("github", Icons.Repository, "GitHub", $"{Links.Repository}\nThe project's page: its code, its releases, and where to report a problem."))
        {
            chosen = () => actions.OpenUrl(Links.Repository);
        }

        ImGui.SameLine(0f, margin * 0.5f);
        if (Link("manual", Icons.Document, "Manual", $"{Links.Manual}\nHow everything in Blockage works."))
        {
            chosen = () => actions.OpenUrl(Links.Manual);
        }

        ImGui.SameLine(0f, margin * 0.5f);
        if (Link("shortcuts", Icons.Keyboard, "Keyboard Shortcuts", $"Every key of the keymap in use{Shortcut.Hint(EditorAction.ShortcutSheet)}"))
        {
            chosen = actions.ShowShortcuts;
        }

        if (actions.NewRelease() is { } release)
        {
            ImGui.SameLine(0f, margin * 0.5f);
            if (Link("new-release", Icons.Plus, $"Blockage {release.Version} is out", $"{release.Url}\nWhat is new in it, and the downloads."))
            {
                chosen = () => actions.OpenUrl(release.Url);
            }
        }

        string label = "Show at startup";
        float check = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
        ImGui.SameLine(width - margin - check);

        bool show = actions.Preferences.ShowWelcome;
        if (ImGui.Checkbox(label, ref show))
        {
            actions.Preferences.ShowWelcome = show;
        }

        Rects["show-at-startup"] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        return chosen;
    }

    private static void Heading(string text)
    {
        ImGui.TextDisabled(text);
        ImGui.Dummy(new Vector2(0f, 2f));
    }

    /// <summary>
    /// One entry: its icon, its name, a key at the far end if it has one. Greyed, with
    /// <paramref name="problem"/> on hover instead of doing anything, when that is not null.
    /// </summary>
    private static bool Item(string id, Icons.Painter icon, string label, string shortcut, string? problem, string tooltip)
    {
        float row = MathF.Round(ImGui.GetFrameHeight() * 1.05f);

        // Closed by the screen itself once the choice is made, not by the entry: one place decides.
        ImGuiSelectableFlags flags = ImGuiSelectableFlags.DontClosePopups | (problem is null ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled);
        bool clicked = ImGui.Selectable($"##{id}", false, flags, new Vector2(0f, row));

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        Rects[id] = (min, max);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(problem ?? tooltip);
        }

        DrawLabel(icon, label, shortcut, min, max, problem is null);
        return clicked;
    }

    /// <summary>A small entry, only as wide as what it says, for a row of them.</summary>
    private static bool Link(string id, Icons.Painter icon, string label, string tooltip)
    {
        float row = ImGui.GetFrameHeight();
        float width = row + ImGui.CalcTextSize(label).X + (ImGui.GetStyle().FramePadding.X * 2f);
        bool clicked = ImGui.Selectable($"##{id}", false, ImGuiSelectableFlags.DontClosePopups, new Vector2(width, row));

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        Rects[id] = (min, max);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        DrawLabel(icon, label, string.Empty, min, max, enabled: true);
        return clicked;
    }

    private static void DrawLabel(Icons.Painter icon, string label, string shortcut, Vector2 min, Vector2 max, bool enabled)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float height = max.Y - min.Y;
        float padding = ImGui.GetStyle().FramePadding.X;
        float y = min.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f);

        Vector4 text = enabled ? Theme.Text : Theme.TextDim with { W = 0.55f };
        uint dim = ImGui.ColorConvertFloat4ToU32(enabled ? Theme.TextDim : Theme.TextDim with { W = 0.4f });

        float radius = height * 0.27f;
        var iconCentre = new Vector2(min.X + padding + radius, min.Y + (height * 0.5f));
        icon(new ImGuiIconCanvas(drawList, ImGui.ColorConvertFloat4ToU32(text with { W = text.W * 0.85f }), 1.3f), iconCentre, radius);

        float x = iconCentre.X + radius + (padding * 1.2f);
        float keyWidth = shortcut.Length > 0 ? ImGui.CalcTextSize(shortcut).X + padding : 0f;

        drawList.PushClipRect(new Vector2(x, min.Y), new Vector2(max.X - keyWidth - padding, max.Y), true);
        drawList.AddText(new Vector2(x, y), ImGui.ColorConvertFloat4ToU32(text), label);
        drawList.PopClipRect();

        if (shortcut.Length > 0)
        {
            drawList.AddText(new Vector2(max.X - keyWidth, y), dim, shortcut);
        }
    }

    public static Icons.Painter IconFor(LevelSample sample) => sample switch
    {
        LevelSample.Island => Icons.PropTree,
        LevelSample.Village => Icons.PropCrate,
        _ => Icons.ShapeArch,
    };

    public static Icons.Painter IconFor(LevelTemplate template) => template switch
    {
        LevelTemplate.Ground => Icons.TemplateGround,
        LevelTemplate.Room => Icons.TemplateRoom,
        LevelTemplate.Voxel => Icons.TemplateVoxel,
        LevelTemplate.Empty => Icons.BoxSelect,
        _ => Icons.TemplateCube,
    };

    /// <summary>"today at 14:05", "yesterday at 09:12", or the date.</summary>
    private static string When(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        string time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        return (DateTime.Now.Date - local.Date).Days switch
        {
            0 => $"today at {time}",
            1 => $"yesterday at {time}",
            _ => local.ToString("d MMM 'at' HH:mm", CultureInfo.InvariantCulture),
        };
    }
}
