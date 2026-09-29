using System.Numerics;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>One thing F3 can find: what it is called, the menu it belongs to, and how it is done.</summary>
/// <param name="Id">Kept in the list of recent ones. Never changed once shipped, like an action's id.</param>
/// <param name="Menu">Where it lives, shown in front of its name — "Object", "View".</param>
public sealed record SearchCommand(string Id, string Name, string Menu, Action Run)
{
    /// <summary>The keyboard action it is, whose key is shown beside it.</summary>
    public EditorAction? Action { get; init; }

    /// <summary>Other words it answers to: what someone looking for it might type instead of its name.</summary>
    public string Keywords { get; init; } = string.Empty;

    /// <summary>Why it cannot be done now — shown on hover, the command greyed — or null when it can.</summary>
    public Func<string?>? Problem { get; init; }
}

/// <summary>
/// Blender's F3: a search field at the mouse, and every command the editor has — the keymap's actions
/// and what only a menu offers — found by typing a few letters of it. The arrows move down the list,
/// Enter or a click does the one lit, Esc goes away. Before anything is typed, what was done last is
/// at the top.
/// </summary>
public static class CommandSearch
{
    /// <summary>How many recent commands are remembered.</summary>
    public const int RecentKept = 10;

    private const string PopupId = "##command-search";

    /// <summary>Rows shown before the list scrolls.</summary>
    private const int VisibleRows = 14;

    private static readonly Dictionary<string, (Vector2 Min, Vector2 Max)> RowRects = [];

    private static bool _openRequested;
    private static bool _focusField;
    private static string _query = string.Empty;
    private static int _lit;
    private static bool _scrollToLit;

    /// <summary>Whether the search is up. Everything typed goes into it while it is.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Where a command's row was drawn last frame, for tests to aim at; null if it was not listed.</summary>
    public static (Vector2 Min, Vector2 Max)? RowRect(string id) => RowRects.TryGetValue(id, out var rect) ? rect : null;

    /// <summary>The row the arrows and Enter act on.</summary>
    public static int Lit => _lit;

    /// <summary>Asks for the search at the mouse; it opens on the next frame drawn, empty.</summary>
    public static void Open() => _openRequested = true;

    /// <summary>
    /// The search while it is up; <paramref name="commands"/> is only asked for then. A command
    /// chosen is run once the search has closed, so one that opens something of its own — Ctrl+P's
    /// list — opens it at the mouse like any other.
    /// </summary>
    public static void Draw(Func<IReadOnlyList<SearchCommand>> commands, List<string> recent)
    {
        if (_openRequested)
        {
            _openRequested = false;
            _query = string.Empty;
            _lit = 0;
            _focusField = true;
            ImGui.OpenPopup(PopupId);
        }

        float width = MathF.Round(ImGui.GetFontSize() * 28f);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));

        IsOpen = ImGui.BeginPopup(PopupId);
        if (!IsOpen)
        {
            return;
        }

        RowRects.Clear();
        SearchCommand? chosen = DrawContents(commands(), recent);

        ImGui.EndPopup();

        if (chosen is not null)
        {
            IsOpen = false;
            Remember(recent, chosen.Id);
            chosen.Run();
        }
    }

    private static SearchCommand? DrawContents(IReadOnlyList<SearchCommand> commands, List<string> recent)
    {
        SearchCommand? chosen = null;
        float row = ImGui.GetFrameHeight();

        // The field, with a lens in front of it.
        ImGui.Dummy(new Vector2(row * 0.8f, row));
        Vector2 lens = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        Icons.Search(new ImGuiIconCanvas(ImGui.GetWindowDrawList(), ImGui.ColorConvertFloat4ToU32(Theme.TextDim), 1.3f), lens, row * 0.3f);
        ImGui.SameLine(0f, 2f);

        if (_focusField)
        {
            ImGui.SetKeyboardFocusHere();
            _focusField = false;
        }

        string before = _query;
        ImGui.SetNextItemWidth(-1f);
        bool entered = ImGui.InputTextWithHint("##query", "Search commands...", ref _query, 96, ImGuiInputTextFlags.EnterReturnsTrue);

        if (_query != before)
        {
            _lit = 0;
            _scrollToLit = true;
        }

        IReadOnlyList<SearchCommand> found = Find(commands, _query, recent);
        _lit = found.Count == 0 ? 0 : Math.Clamp(_lit, 0, found.Count - 1);

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow) && found.Count > 0)
        {
            _lit = (_lit + 1) % found.Count;
            _scrollToLit = true;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow) && found.Count > 0)
        {
            _lit = (_lit + found.Count - 1) % found.Count;
            _scrollToLit = true;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ImGui.CloseCurrentPopup();
            return null;
        }

        entered |= ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
        if (entered && found.Count > 0)
        {
            SearchCommand lit = found[_lit];
            if (lit.Problem?.Invoke() is { } problem)
            {
                // Enter let go of the field; it takes the keys back for another try.
                ReportLog.Shared.Post($"{lit.Name}: {problem}", ReportKind.Warning);
                _focusField = true;
            }
            else
            {
                chosen = lit;
            }
        }

        ImGui.Separator();

        int shown = Math.Clamp(found.Count, 1, VisibleRows);
        float spacing = ImGui.GetStyle().ItemSpacing.Y;
        float height = (shown * (row + spacing)) + (ImGui.GetStyle().WindowPadding.Y * 0.5f);

        if (ImGui.BeginChild("##results", new Vector2(-1f, height)))
        {
            if (found.Count == 0)
            {
                ImGui.TextDisabled($"Nothing is called \"{_query.Trim()}\".");
            }

            // Before anything is typed, the recent ones come first, set off from the rest.
            int recentShown = _query.Trim().Length == 0 ? found.TakeWhile(c => recent.Contains(c.Id)).Count() : 0;

            for (int i = 0; i < found.Count; i++)
            {
                if (i == recentShown && recentShown > 0)
                {
                    ImGui.Separator();
                }

                if (DrawRow(found[i], i, row) && chosen is null)
                {
                    chosen = found[i];
                }
            }

            _scrollToLit = false;
        }

        ImGui.EndChild();

        if (chosen is not null)
        {
            ImGui.CloseCurrentPopup();
        }

        return chosen;
    }

    /// <summary>One command: its menu, dim, then its name, and its key at the far end. Returns whether it was clicked.</summary>
    private static bool DrawRow(SearchCommand command, int index, float row)
    {
        string? problem = command.Problem?.Invoke();
        bool lit = index == _lit;

        if (lit)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent);
        }

        ImGuiSelectableFlags flags = problem is null ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled;
        bool clicked = ImGui.Selectable($"##{command.Id}", lit, flags, new Vector2(0f, row));

        if (lit)
        {
            ImGui.PopStyleColor(2);
        }

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        RowRects[command.Id] = (min, max);

        // The mouse lights what it moves over, as the arrows do; resting still, it leaves the arrows' choice.
        bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        if (hovered && ImGui.GetIO().MouseDelta != Vector2.Zero)
        {
            _lit = index;
        }

        if (lit && _scrollToLit && (min.Y < ImGui.GetWindowPos().Y || max.Y > ImGui.GetWindowPos().Y + ImGui.GetWindowHeight()))
        {
            ImGui.SetScrollHereY(0.5f);
        }

        if (hovered && problem is not null)
        {
            ImGui.SetTooltip(problem);
        }

        DrawLabel(command, min, max, lit, problem is null);
        return clicked;
    }

    private static void DrawLabel(SearchCommand command, Vector2 min, Vector2 max, bool lit, bool enabled)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float padding = ImGui.GetStyle().FramePadding.X;
        float y = min.Y + ((max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f);

        Vector4 text = lit ? Theme.TextOnAccent : Theme.Text;
        Vector4 dim = lit ? Theme.TextOnAccent with { W = 0.7f } : Theme.TextDim;
        if (!enabled)
        {
            text = text with { W = 0.45f };
            dim = dim with { W = 0.35f };
        }

        uint dimColour = ImGui.ColorConvertFloat4ToU32(dim);

        string key = command.Action is { } action ? Shortcut.Of(action) : string.Empty;
        float keyWidth = key.Length > 0 ? ImGui.CalcTextSize(key).X + (padding * 2f) : 0f;

        float x = min.X + padding;
        drawList.AddText(new Vector2(x, y), dimColour, command.Menu);
        x += ImGui.CalcTextSize(command.Menu).X + padding;

        float arrow = ImGui.GetTextLineHeight() * 0.28f;
        Icons.ChevronRight(new ImGuiIconCanvas(drawList, dimColour, 1.1f), new Vector2(x + arrow, (min.Y + max.Y) * 0.5f), arrow);
        x += (arrow * 2f) + padding;

        drawList.PushClipRect(new Vector2(x, min.Y), new Vector2(max.X - keyWidth, max.Y), true);
        drawList.AddText(new Vector2(x, y), ImGui.ColorConvertFloat4ToU32(text), command.Name);
        drawList.PopClipRect();

        if (key.Length > 0)
        {
            drawList.AddText(new Vector2(max.X - keyWidth + padding, y), dimColour, key);
        }
    }

    /// <summary>Puts a command at the head of the recent ones, keeping <see cref="RecentKept"/>.</summary>
    public static void Remember(List<string> recent, string id)
    {
        recent.Remove(id);
        recent.Insert(0, id);

        if (recent.Count > RecentKept)
        {
            recent.RemoveRange(RecentKept, recent.Count - RecentKept);
        }
    }

    /// <summary>
    /// The commands a query finds, best first. Every word typed must be found in the command — at the
    /// start of a word of its name best, then of its menu or the words it answers to, then inside a
    /// word of its name (never inside one of the others: "par" is not in "transparent"); failing all
    /// of those, a word of four letters or more typed with letters left out still finds one, so
    /// "prnt" is Parent and "dupicate" Duplicate. Among equals, the one used more
    /// lately, then the shorter name. Nothing typed: the recent ones, then everything by menu and name.
    /// </summary>
    public static IReadOnlyList<SearchCommand> Find(IReadOnlyList<SearchCommand> commands, string query, IReadOnlyList<string> recent)
    {
        string[] typed = Words(query.ToLowerInvariant());

        if (typed.Length == 0)
        {
            List<SearchCommand> listed = [.. recent.Select(id => commands.FirstOrDefault(c => c.Id == id)).OfType<SearchCommand>()];
            listed.AddRange(commands
                .Where(c => !listed.Contains(c))
                .OrderBy(c => c.Menu, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
            return listed;
        }

        var scored = new List<(SearchCommand Command, int Score)>();
        foreach (SearchCommand command in commands)
        {
            if (Score(command, typed, recent) is { } score)
            {
                scored.Add((command, score));
            }
        }

        return [.. scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Command.Name.Length)
            .ThenBy(s => s.Command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Command)];
    }

    private static int? Score(SearchCommand command, string[] typed, IReadOnlyList<string> recent)
    {
        string name = command.Name.ToLowerInvariant();
        string[] nameWords = Words(name);
        string[] allWords = Words($"{command.Menu} {command.Name} {command.Keywords}".ToLowerInvariant());
        int score = 0;

        foreach (string word in typed)
        {
            // The name's first word, where the eye starts, counts for a little more than the rest.
            bool first = nameWords.Length > 0 && nameWords[0].StartsWith(word, StringComparison.Ordinal);

            if (first || nameWords.Any(w => w.StartsWith(word, StringComparison.Ordinal)))
            {
                score += first ? 40 : 30;
            }
            else if (allWords.Any(w => w.StartsWith(word, StringComparison.Ordinal)))
            {
                score += 20;
            }
            else if (nameWords.Any(w => w.Contains(word, StringComparison.Ordinal)))
            {
                score += 10;
            }
            else if (word.Length >= 4 && nameWords.Length > 0 && InOrder(word, nameWords[0]))
            {
                score += 7;
            }
            else if (word.Length >= 4 && nameWords.Any(w => InOrder(word, w)))
            {
                score += 5;
            }
            else if (word.Length >= 4 && allWords.Any(w => InOrder(word, w)))
            {
                score += 3;
            }
            else
            {
                return null;
            }
        }

        // The name begun with everything typed, as typed, is what was meant.
        if (name.StartsWith(string.Join(' ', typed), StringComparison.Ordinal))
        {
            score += 40;
        }

        int used = IndexOf(recent, command.Id);
        if (used >= 0)
        {
            score += 1 + (RecentKept - used) / 4;
        }

        return score;
    }

    private static int IndexOf(IReadOnlyList<string> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static string[] Words(string text) =>
        text.Split([' ', '-', '(', ')', ',', '.', '/', ':', '+'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether every letter of <paramref name="typed"/> comes in <paramref name="word"/>, in order — the
    /// first at its start, and no more than three of the word's left out, so a word typed short is
    /// found but a few consonants do not find everything.
    /// </summary>
    private static bool InOrder(string typed, string word)
    {
        if (word.Length == 0 || word[0] != typed[0] || word.Length > typed.Length + 3)
        {
            return false;
        }

        int at = 0;
        foreach (char c in word)
        {
            if (at < typed.Length && c == typed[at])
            {
                at++;
            }
        }

        return at == typed.Length;
    }
}
