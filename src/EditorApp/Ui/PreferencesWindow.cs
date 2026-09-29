using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Edit > Preferences: the editor's own settings, in Blender's arrangement — the sections down the
/// left, the chosen one's settings on the right in the same label | value rows as Properties.
///
/// Changes take effect as they are made; there is no Apply button to forget. They are written to
/// disk when the window closes, and again when the editor does.
/// </summary>
public static class PreferencesWindow
{
    public enum Page
    {
        Interface,
        Viewport,
        Navigation,
        Editing,
        Keymap,
        Files,
    }

    private const float PageListWidth = 150f;

    private const float KeyColumnWidth = 150f;

    private static readonly Dictionary<(EditorAction Action, int Slot), (Vector2 Min, Vector2 Max)> KeyRects = [];

    private static string _filter = string.Empty;
    private static string _notice = string.Empty;
    private static KeymapPreset _pendingPreset;
    private static bool _askPreset;
    private static Preferences? _defaults;

    public static bool IsOpen { get; private set; }

    /// <summary>
    /// The window has the keyboard: shortcuts meant for the level wait until it is left. A Delete
    /// pressed while reading about keys should not delete an object behind the window.
    /// </summary>
    public static bool IsFocused { get; private set; }

    public static Page Current { get; set; } = Page.Interface;

    /// <summary>Where a key button was drawn last frame, for tests to aim at.</summary>
    public static (Vector2 Min, Vector2 Max)? KeyRect(EditorAction action, int slot) =>
        KeyRects.TryGetValue((action, slot), out var rect) ? rect : null;

    public static void Open() => IsOpen = true;

    public static void Close()
    {
        IsOpen = false;
        IsFocused = false;
        KeyCapture.Cancel();
    }

    public static void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// Draws the window if it is open. <paramref name="apply"/> puts a change into effect;
    /// <paramref name="clearRecent"/> empties the recent-files list. Returns true on the frame the
    /// window closes, which is when the host writes the preferences out.
    /// </summary>
    public static bool Draw(Preferences preferences, Action apply, Action clearRecent)
    {
        if (!IsOpen)
        {
            return false;
        }

        Vector2 screen = ImGui.GetIO().DisplaySize;
        ImGui.SetNextWindowPos(screen * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f));
        ImGui.SetNextWindowSize(new Vector2(MathF.Min(820f, screen.X - 40f), MathF.Min(560f, screen.Y - 60f)), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(600f, 380f), new Vector2(float.MaxValue, float.MaxValue));

        bool open = true;
        if (ImGui.Begin("Preferences###preferences", ref open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            IsFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            DrawPageList();
            ImGui.SameLine();

            if (ImGui.BeginChild("##preferences-page", Vector2.Zero))
            {
                bool changed = Current switch
                {
                    Page.Interface => DrawInterface(preferences),
                    Page.Viewport => DrawViewport(preferences),
                    Page.Navigation => DrawNavigation(preferences),
                    Page.Editing => DrawEditing(preferences),
                    Page.Keymap => DrawKeymap(preferences),
                    _ => DrawFiles(preferences, clearRecent),
                };

                if (changed)
                {
                    apply();
                }
            }

            ImGui.EndChild();
        }

        ImGui.End();

        if (!open)
        {
            Close();
            return true;
        }

        return false;
    }

    private static void DrawPageList()
    {
        if (ImGui.BeginChild("##preferences-pages", new Vector2(PageListWidth, 0f)))
        {
            float row = ImGui.GetFrameHeight();
            foreach (Page page in Enum.GetValues<Page>())
            {
                if (ImGui.Selectable(page.ToString(), Current == page, ImGuiSelectableFlags.None, new Vector2(0f, row)))
                {
                    Current = page;
                    KeyCapture.Cancel();
                }
            }
        }

        ImGui.EndChild();
    }

    // ---- Pages -----------------------------------------------------------------------------

    private static bool DrawInterface(Preferences p)
    {
        bool changed = false;

        if (Props.Section("Theme"))
        {
            int theme = Props.Choice("Colours", "pref-theme", [(null, "Dark"), (null, "Darker"), (null, "Light")], (int)p.Theme);
            if (theme != (int)p.Theme)
            {
                p.Theme = (ThemeKind)theme;
                changed = true;
            }

            changed |= DrawAccents(p);
        }

        if (Props.Section("Text"))
        {
            ImGui.BeginDisabled(!Theme.HasSizes);
            int size = Props.Choice("Text size", "pref-text-size", [(null, "Small"), (null, "Normal"), (null, "Large"), (null, "Larger")], (int)p.TextSize);
            ImGui.EndDisabled();

            if (size != (int)p.TextSize)
            {
                p.TextSize = (TextSize)size;
                changed = true;
            }

            if (!Theme.HasSizes)
            {
                Props.Note(string.Empty, "Needs a TrueType font; the built-in one comes in one size.", Theme.TextDim);
            }
        }

        if (Props.Section("Status bar"))
        {
            bool hints = p.MouseHints;
            if (Props.Check("Mouse hints", "pref-hints", "What the buttons do right now", ref hints))
            {
                p.MouseHints = hints;
                changed = true;
            }
        }

        if (Props.Section("Startup"))
        {
            bool welcome = p.ShowWelcome;
            if (Props.Check("Welcome screen", "pref-welcome", "Templates, recent files and recovery", ref welcome))
            {
                p.ShowWelcome = welcome;
                changed = true;
            }

            bool updates = p.CheckForUpdates;
            if (Props.Check("New versions", "pref-updates", "Ask GitHub whether a newer Blockage is out. Nothing else is sent.", ref updates))
            {
                p.CheckForUpdates = updates;
                changed = true;
            }
        }

        return changed | RestoreDefaults(p, d =>
        {
            p.Theme = d.Theme;
            p.Accent = d.Accent;
            p.TextSize = d.TextSize;
            p.MouseHints = d.MouseHints;
            p.ShowWelcome = d.ShowWelcome;
            p.CheckForUpdates = d.CheckForUpdates;
        });
    }

    /// <summary>A swatch for each accent, the chosen one ringed.</summary>
    private static bool DrawAccents(Preferences p)
    {
        Props.Label("Accent");
        bool changed = false;
        float size = ImGui.GetFrameHeight();

        foreach (AccentKind accent in Enum.GetValues<AccentKind>())
        {
            if (accent != AccentKind.Blue)
            {
                ImGui.SameLine(0f, 6f);
            }

            bool chosen = accent == p.Accent;
            if (chosen)
            {
                ImGui.PushStyleColor(ImGuiCol.Border, Theme.Text);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            }

            if (ImGui.ColorButton($"##accent-{accent}", Theme.AccentOf(accent), ImGuiColorEditFlags.NoTooltip, new Vector2(size * 1.4f, size)))
            {
                p.Accent = accent;
                changed = true;
            }

            if (chosen)
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(accent.ToString());
            }
        }

        return changed;
    }

    /// <summary>
    /// The walker's build, in metres, and how many voxels make a metre — the game's scale, which the
    /// editor does not assume.
    /// </summary>
    private static bool DrawWalk(Preferences p)
    {
        if (!Props.Section("Walk", openByDefault: false))
        {
            return false;
        }

        EditorApp.Core.Editing.WalkSettings walk = p.Walk;
        EditorApp.Core.Editing.WalkSettings edited = walk;

        float perMetre = walk.VoxelsPerMetre;
        if (Props.Float("Voxels a metre", "walk-scale", ref perMetre, 0.1f, 0.01f, 1000f, "%.2f"))
        {
            edited = edited with { VoxelsPerMetre = perMetre };
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("How many voxels (of size 1) make a metre in the game the level is for. Everything below is in metres.");
        }

        float height = walk.Height;
        if (Props.Float("Height", "walk-height", ref height, 0.01f, 0.1f, 100f, "%.2f m"))
        {
            edited = edited with { Height = height };
        }

        float radius = walk.Radius;
        if (Props.Float("Radius", "walk-radius", ref radius, 0.01f, 0.05f, 50f, "%.2f m"))
        {
            edited = edited with { Radius = radius };
        }

        float step = walk.Step;
        if (Props.Float("Step", "walk-step", ref step, 0.01f, 0f, 50f, "%.2f m"))
        {
            edited = edited with { Step = step };
        }

        float speed = walk.Speed;
        if (Props.Float("Speed", "walk-speed", ref speed, 0.05f, 0.1f, 500f, "%.1f m/s"))
        {
            edited = edited with { Speed = speed };
        }

        float jump = walk.Jump;
        if (Props.Float("Jump", "walk-jump", ref jump, 0.01f, 0f, 100f, "%.2f m"))
        {
            edited = edited with { Jump = jump };
        }

        float gravity = walk.Gravity;
        if (Props.Float("Gravity", "walk-gravity", ref gravity, 0.05f, 0f, 1000f, "%.2f m/s²"))
        {
            edited = edited with { Gravity = gravity };
        }

        if (edited == walk)
        {
            return false;
        }

        p.Walk = edited.Clamped();
        return true;
    }

    private static bool DrawViewport(Preferences p)
    {
        bool changed = DrawWalk(p);

        if (Props.Section("View"))
        {
            float fov = p.FieldOfView;
            if (Props.Slider("Field of view", "pref-fov", ref fov, Preferences.MinFieldOfView, Preferences.MaxFieldOfView, "%.0f°"))
            {
                p.FieldOfView = fov;
                changed = true;
            }

            bool vsync = p.VSync;
            if (Props.Check("Frames", "pref-vsync", "Wait for the screen (vertical sync)", ref vsync))
            {
                p.VSync = vsync;
                changed = true;
            }
        }

        if (Props.Section("Overlays"))
        {
            changed |= Percent("Line width", "pref-line-width", p.LineWidth, 0.5f, 2f, v => p.LineWidth = v);
            changed |= Percent("Gizmo size", "pref-gizmo-size", p.GizmoSize, 0.5f, 2f, v => p.GizmoSize = v);

            Props.Note(string.Empty, "What is drawn over the model, and how it is shaded, are in the viewport header: Gizmos, Overlays, X-Ray and Shading. They are kept from one session to the next.", Theme.TextDim);
        }

        return changed | RestoreDefaults(p, d =>
        {
            p.FieldOfView = d.FieldOfView;
            p.VSync = d.VSync;
            p.LineWidth = d.LineWidth;
            p.GizmoSize = d.GizmoSize;
            p.Viewport = d.Viewport.Clone();
        });
    }

    private static bool DrawNavigation(Preferences p)
    {
        bool changed = false;

        if (Props.Section("Mouse"))
        {
            changed |= Percent("Orbit speed", "pref-orbit", p.OrbitSpeed, 0.25f, 3f, v => p.OrbitSpeed = v);

            bool invert = p.InvertZoom;
            if (Props.Check("Wheel", "pref-invert-zoom", "Forward zooms out", ref invert))
            {
                p.InvertZoom = invert;
                changed = true;
            }

            Props.Note(string.Empty, "Middle drag orbits, Shift + middle pans, the wheel zooms, the right button looks round.", Theme.TextDim);
        }

        if (Props.Section("Flying"))
        {
            changed |= Percent("Fly speed", "pref-fly", p.FlySpeed, 0.25f, 4f, v => p.FlySpeed = v);
            Props.Note(string.Empty, "With the right button held: W A S D, Q down, E up, Shift to go faster.", Theme.TextDim);
        }

        return changed | RestoreDefaults(p, d =>
        {
            p.OrbitSpeed = d.OrbitSpeed;
            p.InvertZoom = d.InvertZoom;
            p.FlySpeed = d.FlySpeed;
        });
    }

    private static bool DrawEditing(Preferences p)
    {
        bool changed = false;

        if (Props.Section("Snapping"))
        {
            bool enabled = p.Snap.Enabled;
            if (Props.Check("Magnet", "pref-snap", "On from the start (Shift does the opposite)", ref enabled))
            {
                p.Snap.Enabled = enabled;
                changed = true;
            }

            float increment = p.Snap.RotationIncrement;
            if (Props.Slider("Rotation increment", "pref-rotation-step", ref increment, SnapSettings.MinRotationIncrement, SnapSettings.MaxRotationIncrement, "%.0f°"))
            {
                p.Snap.RotationIncrement = MathF.Round(increment);
                changed = true;
            }

            Props.Note(string.Empty, "What to snap to is in the Transform tool's header, beside the magnet.", Theme.TextDim);
        }

        if (Props.Section("Undo"))
        {
            string[] sizes = [.. Preferences.UndoChoices.Select(m => $"{m} million voxels")];
            int undo = Array.IndexOf(Preferences.UndoChoices, p.UndoMemory);
            if (Combo("Undo holds", "pref-undo", sizes, ref undo))
            {
                p.UndoMemory = Preferences.UndoChoices[undo];
                changed = true;
            }

            Props.Note(string.Empty, "Past it the oldest steps go. More holds longer histories of big edits, and uses more memory.", Theme.TextDim);
        }

        if (Props.Section("Recovery"))
        {
            string[] intervals = [.. Preferences.AutosaveChoices.Select(m => m == 0 ? "Off" : m == 1 ? "Every minute" : $"Every {m} minutes")];
            int autosave = Array.IndexOf(Preferences.AutosaveChoices, p.AutosaveMinutes);
            if (Combo("Autosave", "pref-autosave", intervals, ref autosave))
            {
                p.AutosaveMinutes = Preferences.AutosaveChoices[autosave];
                changed = true;
            }

            Props.Note(string.Empty, "A copy of unsaved work, offered back if the editor did not close properly.", Theme.TextDim);
        }

        return changed | RestoreDefaults(p, d =>
        {
            p.Snap.CopyFrom(d.Snap);
            p.UndoMemory = d.UndoMemory;
            p.AutosaveMinutes = d.AutosaveMinutes;
        });
    }

    private static bool DrawFiles(Preferences p, Action clearRecent)
    {
        bool changed = false;

        if (Props.Section("Recent files"))
        {
            int kept = p.RecentFilesKept;
            if (Props.Int("Keep", "pref-recent", ref kept, 0.2f, 1, RecentFiles.MaxCapacity, "%d files"))
            {
                p.RecentFilesKept = kept;
                changed = true;
            }

            if (Props.Buttons(string.Empty, "pref-clear-recent", "Clear the list") == 0)
            {
                clearRecent();
                ReportLog.Shared.Post("Cleared the recent files.");
            }
        }

        if (Props.Section("Where things are kept"))
        {
            Props.Value("Preferences", Preferences.DefaultPath);
            Props.Value("Layout", LayoutSettings.DefaultPath);
            Props.Value("Recovery", AutosaveController.DefaultDirectory);
        }

        return changed | RestoreDefaults(p, d => p.RecentFilesKept = d.RecentFilesKept);
    }

    // ---- Keymap ------------------------------------------------------------------------------

    private static bool DrawKeymap(Preferences p)
    {
        bool changed = false;
        Keymap keymap = Keymap.Active;

        Props.Label("Preset");
        if (ImGui.BeginCombo("##keymap-preset", Keymap.NameOf(keymap.Preset)))
        {
            foreach (KeymapPreset preset in Enum.GetValues<KeymapPreset>())
            {
                if (ImGui.Selectable(Keymap.NameOf(preset), preset == keymap.Preset) && preset != keymap.Preset)
                {
                    // Changes made on top of a preset do not carry over to another; ask before they go.
                    if (keymap.Changes().Count > 0)
                    {
                        _pendingPreset = preset;
                        _askPreset = true;
                    }
                    else
                    {
                        changed |= SwitchPreset(p, preset);
                    }
                }
            }

            ImGui.EndCombo();
        }

        Props.Note(string.Empty, keymap.Preset == KeymapPreset.MimicBusters
            ? "The keys Mimic Busters itself uses: Q W E R for the tools."
            : "The keys an editing program answers to: G moves, R rotates, E extrudes, B paints, Ctrl+R cuts.",
            Theme.TextDim);

        int count = keymap.Changes().Count;
        if (count > 0)
        {
            Props.Label(string.Empty);
            ImGui.TextColored(Theme.Highlight, $"{count} {(count == 1 ? "change" : "changes")} from the preset.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset all"))
            {
                changed |= SwitchPreset(p, keymap.Preset);
            }
        }

        if (_askPreset)
        {
            ImGui.OpenPopup("Change preset?##keymap");
            _askPreset = false;
        }

        if (ImGui.BeginPopupModal("Change preset?##keymap", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Your {count} {(count == 1 ? "change" : "changes")} to {Keymap.NameOf(keymap.Preset)} will be lost.");
            if (ImGui.Button($"Use {Keymap.NameOf(_pendingPreset)}", Theme.ModalButton))
            {
                changed |= SwitchPreset(p, _pendingPreset);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Keep mine", Theme.ModalButton))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##keymap-filter", "Find an action or a key", ref _filter, 64);

        if (_notice.Length > 0)
        {
            ImGui.TextColored(Theme.Highlight, _notice);
        }
        else
        {
            ImGui.TextDisabled("Click a key, then press the new one. Esc gives up; right-click clears it.");
        }

        changed |= DrawBindings(p, keymap);
        return changed;
    }

    private static bool SwitchPreset(Preferences p, KeymapPreset preset)
    {
        KeyCapture.Cancel();
        Keymap.Active = Keymap.For(preset);
        p.Remember(Keymap.Active);
        _notice = string.Empty;
        return true;
    }

    private static bool DrawBindings(Preferences p, Keymap keymap)
    {
        bool changed = false;
        string filter = _filter.Trim();

        if (!ImGui.BeginTable("##keymap", 4, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp, new Vector2(0f, -1f)))
        {
            return false;
        }

        float reset = ImGui.GetFrameHeight();
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, KeyColumnWidth);
        ImGui.TableSetupColumn("Or", ImGuiTableColumnFlags.WidthFixed, KeyColumnWidth);
        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, reset);
        ImGui.TableHeadersRow();

        foreach (string category in EditorActions.Categories)
        {
            ActionInfo[] rows = [.. EditorActions.All.Where(a => a.Category == category && Matches(a, keymap, filter))];
            if (rows.Length == 0)
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextDisabled(category);

            foreach (ActionInfo info in rows)
            {
                ImGui.PushID(info.Id);
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(info.Name);

                for (int slot = 0; slot < Keymap.SlotsPerAction; slot++)
                {
                    ImGui.TableSetColumnIndex(1 + slot);
                    changed |= DrawKeyButton(p, keymap, info.Action, slot);
                }

                ImGui.TableSetColumnIndex(3);
                if (!keymap.IsDefault(info.Action)
                    && IconButton.Draw("reset", Icons.Undo, active: false, $"Back to {Keymap.NameOf(keymap.Preset)}'s keys", reset))
                {
                    KeyCapture.Cancel();
                    keymap.Reset(info.Action);
                    p.Remember(keymap);
                    changed = true;
                }

                ImGui.PopID();
            }
        }

        ImGui.EndTable();
        return changed;
    }

    private static bool Matches(ActionInfo info, Keymap keymap, string filter) =>
        filter.Length == 0
        || info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || keymap.Bindings(info.Action).Any(c => c.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));

    private static bool DrawKeyButton(Preferences p, Keymap keymap, EditorAction action, int slot)
    {
        bool waiting = Equals(KeyCapture.Owner, (action, slot));
        KeyChord? chord = keymap.Slot(action, slot);
        string text = waiting ? "Press a key..." : chord?.ToString() ?? "-";

        if (waiting)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextOnAccent);
        }
        else if (chord is null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDisabled);
        }

        bool clicked = ImGui.Button($"{text}##key-{slot}", new Vector2(-1f, 0f));
        KeyRects[(action, slot)] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        ImGui.PopStyleColor(waiting ? 2 : chord is null ? 1 : 0);

        if (clicked)
        {
            _notice = string.Empty;
            KeyCapture.Begin((action, slot), pressed => Bound(p, action, slot, pressed));
        }

        bool changed = false;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && chord is not null)
        {
            KeyCapture.Cancel();
            keymap.Bind(action, slot, null);
            p.Remember(keymap);
            changed = true;
        }

        if (ImGui.IsItemHovered() && !waiting)
        {
            ImGui.SetTooltip("Click, then press the new key. Right-click to clear it.");
        }

        return changed;
    }

    /// <summary>A key pressed for a waiting button. Arrives from the key handler, between frames.</summary>
    private static void Bound(Preferences p, EditorAction action, int slot, KeyChord chord)
    {
        Keymap keymap = Keymap.Active;
        EditorAction? takenFrom = keymap.Bind(action, slot, chord);
        p.Remember(keymap);
        PendingApply = true;

        _notice = takenFrom is { } other
            ? $"{chord} was {EditorActions.Info(other).Name}; it is {EditorActions.Info(action).Name} now."
            : string.Empty;
    }

    /// <summary>A key was rebound between frames; the host applies it on the next.</summary>
    public static bool PendingApply { get; set; }

    // ---- Pieces ------------------------------------------------------------------------------

    /// <summary>A setting shown as a percentage of its designed value.</summary>
    private static bool Percent(string label, string id, float value, float min, float max, Action<float> set)
    {
        float percent = value * 100f;
        if (!Props.Slider(label, id, ref percent, min * 100f, max * 100f, "%.0f%%"))
        {
            return false;
        }

        set(percent / 100f);
        return true;
    }

    private static bool Combo(string label, string id, string[] items, ref int selected)
    {
        Props.Label(label);
        int chosen = Math.Max(selected, 0);
        bool changed = ImGui.Combo($"##{id}", ref chosen, items, items.Length);
        if (changed)
        {
            selected = chosen;
        }

        return changed;
    }

    /// <summary>The page's own defaults, at its foot.</summary>
    private static bool RestoreDefaults(Preferences p, Action<Preferences> restore)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (Props.Buttons(string.Empty, $"pref-restore-{Current}", "Restore defaults") != 0)
        {
            return false;
        }

        restore(_defaults ??= new Preferences());
        return true;
    }
}
