using System.Globalization;
using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp;

/// <summary>
/// New / Open / Save / Save As, plus the guard that stops unsaved work from being thrown away.
/// The renderer is told to drop its buffers whenever the world is replaced, since chunk buffers are
/// keyed by coordinates that no longer mean anything.
/// </summary>
public sealed class ProjectController(EditorSession session, Action onWorldReplaced)
{
    /// <summary>The level Save, Save As and the questions are about: the one in front. The host moves it as tabs change.</summary>
    public EditorSession Session { get; set; } = session;

    /// <summary>
    /// Makes room for a level being opened and returns the session it is to go into: a tab of its
    /// own, or the level in front when that is an untitled one nothing has been done to. Set by a
    /// host that keeps levels in tabs (Fullreleaseplan 7.8); left null, a level opened replaces the
    /// one in front, once its unsaved work has been answered for.
    /// </summary>
    public Func<EditorSession>? MakeRoom { get; set; }

    /// <summary>Brings forward the tab a file is open in already, and says whether there was one.</summary>
    public Func<string, bool>? ShowOpen { get; set; }

    /// <summary>Title and identity together; both have to be passed to OpenPopup and BeginPopupModal.</summary>
    private const string ConfirmPopupId = "Unsaved changes###discard-changes";

    private readonly FileBrowserDialog _browser = new();

    private const string RecoveryPopupId = "Recover unsaved work###recovery";

    private Action? _pendingAction;
    private string _pendingDescription = string.Empty;
    private bool _shouldOpenConfirm;

    private RecoveryEntry? _offer;
    private int _moreWaiting;
    private bool _shouldOpenRecovery;

    /// <summary>
    /// Keeps unsaved work in the recovery folder. Set by the host; left null for a smoke or
    /// screenshot run, which must neither write the user's recovery folder nor stop at a question
    /// about what is in it.
    /// </summary>
    public AutosaveController? Autosave { get; set; }

    /// <summary>
    /// Whether an earlier session left autosaved work behind. Remembered rather than asked every
    /// frame, since asking means reading the disk; refreshed whenever the answer can have changed.
    /// </summary>
    public bool CanRecover { get; private set; }

    public RecentFiles Recent { get; } = new();

    /// <summary>
    /// The level as the editor last closed on it. Set by the host; left null for a smoke or
    /// screenshot run, which keeps nothing.
    /// </summary>
    public LastSession? LastSession
    {
        get => _lastSession;
        set
        {
            _lastSession = value;
            LastSessionFound = value?.Read();
        }
    }

    private LastSession? _lastSession;

    /// <summary>What the last session left, read once — it is only written as the editor closes.</summary>
    public LastSessionInfo? LastSessionFound { get; private set; }

    /// <summary>Whether the crash-recovery question is up, or about to be.</summary>
    public bool IsOfferingRecovery => _offer is not null;

    /// <summary>Whether opening and saving add to the recent-files list. Off for smoke and screenshot runs.</summary>
    public bool RemembersRecent { get; set; } = true;

    public string WindowTitle =>
        $"{(Session.HasUnsavedChanges ? "*" : string.Empty)}{Session.ProjectName} - Blockage";

    /// <summary>
    /// A new level from one of the templates — the cube unless another is asked for. Never an empty
    /// world: with no Place tool there has to be a surface for Extrude to pull on.
    /// </summary>
    public void NewProject(LevelTemplate template = LevelTemplate.Cube) => Guarded("start a new level", () =>
    {
        Place(LevelTemplates.Build(template), projectPath: null);
        Report($"New level - {LevelTemplates.DescriptionOf(template)}.", isError: false);
    });

    /// <summary>One of the sample levels, as a new level named after it until it is saved.</summary>
    public void OpenSample(LevelSample sample) => Guarded("open a sample level", () =>
    {
        EditorSession target = Place(LevelSamples.Build(sample), projectPath: null);
        target.UntitledName = LevelSamples.NameOf(sample);
        Report($"The {LevelSamples.NameOf(sample)} sample - {LevelSamples.DescriptionOf(sample)}. Save it to keep it.", isError: false);
    });

    public void OpenProject() => Guarded("open another level", () =>
        _browser.Show(
            FileBrowserMode.Open,
            "Open level",
            VxLevelFile.Extension,
            StartDirectory,
            suggestedName: null,
            LoadFrom,
            ProjectDirectory));

    public void OpenRecent(string path) => Guarded("open another level", () => LoadFrom(path));

    /// <summary>Asks before letting the editor close on unsaved work.</summary>
    public void RequestExit(Action exit) => GuardUnsaved("exit", exit);

    /// <summary>A MagicaVoxel file as a level of its own (Fullreleaseplan 8.1), named after it until it is saved.</summary>
    public void ImportVox() => Guarded("import a MagicaVoxel file", () =>
        _browser.Show(
            FileBrowserMode.Open,
            "Import MagicaVoxel",
            VoxFile.Extension,
            StartDirectory,
            suggestedName: null,
            ImportVoxFrom,
            ProjectDirectory));

    /// <summary>The level as a MagicaVoxel file. What cannot go across as it is, is said.</summary>
    public void ExportVox() => _browser.Show(
        FileBrowserMode.Save,
        "Export MagicaVoxel",
        VoxFile.Extension,
        StartDirectory,
        Session.ProjectName + VoxFile.Extension,
        ExportVoxTo,
        ProjectDirectory);

    /// <summary>Opens a .vox the way Import does, without the dialog.</summary>
    public void ImportVoxFrom(string path)
    {
        try
        {
            VoxelScene scene = VoxFile.Load(path);
            EditorSession target = Place(scene, projectPath: null);
            target.UntitledName = Path.GetFileNameWithoutExtension(path);
            target.HasUnsavedChanges = true;
            Report(
                $"Imported {Path.GetFileName(path)} - {scene.SolidCount:N0} voxels in {scene.Objects.Count(o => !o.IsEmpty)} object(s). Save it to keep it as a level.",
                isError: false);
        }
        catch (Exception exception)
        {
            Report($"Could not import {Path.GetFileName(path)}: {exception.Message}", isError: true);
            CrashLog.Record($"importing {path}", exception);
        }
    }

    private void ExportVoxTo(string path)
    {
        try
        {
            VoxReport report = VoxFile.Save(Session.Scene, path);
            Report($"Exported {Path.GetFileName(path)} - {report.Models} model(s), placed {report.Instances} time(s).", isError: false);
            foreach (string warning in report.Warnings)
            {
                ReportLog.Shared.Post(warning, ReportKind.Warning);
            }
        }
        catch (Exception exception)
        {
            Report($"Could not export {Path.GetFileName(path)}: {exception.Message}", isError: true);
            CrashLog.Record($"exporting {path}", exception);
        }
    }

    /// <summary>Asks before letting the level in front be closed on unsaved work.</summary>
    public void RequestClose(Action close) => GuardUnsaved("close it", close);

    /// <summary>Saves to the current path, or asks for one when the project has never been saved.</summary>
    public void Save()
    {
        if (Session.ProjectPath is null)
        {
            SaveAs();
            return;
        }

        WriteTo(Session.ProjectPath);
    }

    public void SaveAs() => _browser.Show(
        FileBrowserMode.Save,
        "Save level as",
        VxLevelFile.Extension,
        StartDirectory,
        Session.ProjectName + VxLevelFile.Extension,
        WriteTo,
        ProjectDirectory);

    /// <summary>
    /// Where the level being worked on lives, or null when it has never been saved. Kept apart from
    /// <see cref="StartDirectory"/>, which always has an answer — the browser offers this one as a
    /// shortcut, and a shortcut labelled "Project folder" that led to wherever the dialog happened
    /// to open would be a lie.
    /// </summary>
    private string? ProjectDirectory =>
        Session.ProjectPath is not null ? Path.GetDirectoryName(Session.ProjectPath) : null;

    private string StartDirectory =>
        Session.ProjectPath is not null
            ? Path.GetDirectoryName(Session.ProjectPath) ?? _browser.CurrentDirectory
            : _browser.CurrentDirectory;

    private void LoadFrom(string path)
    {
        // A MagicaVoxel file handed over as a level — on the command line, say — is imported.
        if (path.EndsWith(VoxFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            ImportVoxFrom(path);
            return;
        }

        if (ShowOpen?.Invoke(path) == true)
        {
            Report($"{Path.GetFileName(path)} is open already.", isError: false);
            return;
        }

        try
        {
            VoxelScene scene = VxLevelFile.LoadScene(path);
            Place(scene, path);
            Remember(path);
            Report(
                $"Opened {Path.GetFileName(path)} - {scene.SolidCount:N0} voxels in {scene.Objects.Count} object(s).",
                isError: false);
        }
        // Every exception, not a chosen few. A file that cannot be read is a bad afternoon; a file
        // that takes the editor down with unsaved work still in it is a lost one, and the list of
        // ways a stored format can surprise its reader is not one worth betting the session on.
        catch (Exception exception)
        {
            if (RemembersRecent)
            {
                Recent.Remove(path);
            }

            Report($"Could not open {Path.GetFileName(path)}: {exception.Message}", isError: true);
            CrashLog.Record($"opening {path}", exception);
        }
    }

    private void WriteTo(string path)
    {
        try
        {
            VxLevelFile.Save(Session.Scene, path);
            Session.ProjectPath = path;
            Session.HasUnsavedChanges = false;
            Remember(path);
            Report($"Saved {Path.GetFileName(path)}.", isError: false);
        }
        catch (Exception exception)
        {
            Report($"Could not save: {exception.Message}", isError: true);
            CrashLog.Record($"saving {path}", exception);
        }
    }

    /// <summary>Offers the newest autosave an earlier session left behind, if there is one.</summary>
    public void OfferRecovery()
    {
        IReadOnlyList<RecoveryEntry> found = Autosave?.FindAbandoned() ?? [];
        CanRecover = found.Count > 0;

        if (found.Count == 0)
        {
            _offer = null;
            return;
        }

        _offer = found[0];
        _moreWaiting = found.Count - 1;
        _shouldOpenRecovery = true;
    }

    /// <summary>
    /// Opens an abandoned autosave in place of the current level. It comes back unsaved even when it
    /// knows its project's path — the file at that path is older than what was recovered — and the
    /// copy becomes this session's own autosave, so a second crash straight away loses nothing.
    /// </summary>
    public void Recover(RecoveryEntry entry) => Guarded("recover the autosaved work", () =>
    {
        try
        {
            VoxelScene scene = VxLevelFile.LoadScene(entry.LevelPath);
            Place(scene, entry.ProjectPath).HasUnsavedChanges = true;
            Autosave?.Adopt(entry);
            Report($"Recovered {entry.ProjectName} - save it to keep it.", isError: false);
        }
        catch (Exception exception)
        {
            Report($"Could not recover {entry.ProjectName}: {exception.Message}", isError: true);
            CrashLog.Record($"recovering {entry.LevelPath}", exception);
        }

        CanRecover = Autosave?.FindAbandoned().Count > 0;
    });

    /// <summary>
    /// Opens the level the editor last closed on, in place of the current one. It keeps the path it
    /// had, so saving puts it back where it came from, and comes back unsaved if it closed on unsaved
    /// work — which is exactly the work this is for.
    /// </summary>
    public void RecoverLastSession() => Guarded("recover the last session", () =>
    {
        if (LastSession?.Read() is not { } last)
        {
            Report("There is no last session to recover.", isError: true);
            return;
        }

        try
        {
            VoxelScene scene = VxLevelFile.LoadScene(last.LevelPath);
            Place(scene, last.ProjectPath).HasUnsavedChanges = last.HadUnsavedChanges;
            Report(
                last.HadUnsavedChanges
                    ? $"Recovered the last session: {last.ProjectName}, as it was left - save it to keep it."
                    : $"Recovered the last session: {last.ProjectName}.",
                isError: false);
        }
        catch (Exception exception)
        {
            Report($"Could not recover the last session: {exception.Message}", isError: true);
            CrashLog.Record($"recovering the last session from {last.LevelPath}", exception);
        }
    });

    /// <summary>True while the discard prompt is waiting for an answer.</summary>
    public bool IsAwaitingConfirmation => _pendingAction is not null;

    /// <summary>
    /// Throws the unsaved work away and goes ahead. Separate from the button that calls it so the
    /// decision can be driven without a mouse — the discard path ends in tearing down and rebuilding
    /// the renderer's buffers, which is not something to leave only ever exercised by hand.
    /// </summary>
    public void ConfirmDiscard()
    {
        Action? pending = _pendingAction;
        _pendingAction = null;
        pending?.Invoke();
    }

    public void CancelPending() => _pendingAction = null;

    /// <summary>
    /// Puts a level in — in a tab of its own when the host keeps tabs, else in place of the level in
    /// front — and returns the session it went into.
    /// </summary>
    private EditorSession Place(VoxelScene scene, string? projectPath)
    {
        EditorSession target = MakeRoom?.Invoke() ?? Session;
        target.ReplaceScene(scene, projectPath);
        onWorldReplaced();
        return target;
    }

    /// <summary>Asks about unsaved work only when what comes next would replace it, not when it goes in a tab of its own.</summary>
    private void Guarded(string description, Action action)
    {
        if (MakeRoom is not null)
        {
            action();
            return;
        }

        GuardUnsaved(description, action);
    }

    /// <summary>Runs an action immediately, or asks first when there is unsaved work.</summary>
    private void GuardUnsaved(string description, Action action)
    {
        if (!Session.HasUnsavedChanges)
        {
            action();
            return;
        }

        _pendingAction = action;
        _pendingDescription = description;
        _shouldOpenConfirm = true;
    }

    public void DrawDialogs()
    {
        if (_shouldOpenConfirm)
        {
            ImGui.OpenPopup(ConfirmPopupId);
            _shouldOpenConfirm = false;
        }

        DrawConfirmPopup();
        DrawRecoveryPopup();
        _browser.Draw();
    }

    private void DrawRecoveryPopup()
    {
        if (_shouldOpenRecovery)
        {
            ImGui.OpenPopup(RecoveryPopupId);
            _shouldOpenRecovery = false;
        }

        if (_offer is not { } offer)
        {
            return;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(RecoveryPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        string when = offer.SavedUtc.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);

        ImGui.Text("Blockage did not close properly last time.");
        ImGui.Text($"Unsaved work on {offer.ProjectName} was autosaved at {when}.");
        ImGui.TextDisabled(offer.ProjectPath ?? "It had never been saved.");

        if (_moreWaiting > 0)
        {
            ImGui.TextDisabled($"{_moreWaiting} older autosave(s) are waiting as well.");
        }

        ImGui.Spacing();

        if (ImGui.Button("Recover", Theme.ModalButton))
        {
            ImGui.CloseCurrentPopup();
            _offer = null;
            Recover(offer);
        }

        ImGui.SameLine();
        if (ImGui.Button("Delete it", Theme.ModalButton))
        {
            ImGui.CloseCurrentPopup();
            _offer = null;
            Autosave?.Discard(offer);

            // The next one, if there is one, rather than leaving it to be found by accident.
            OfferRecovery();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Throws the autosaved work away for good.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Decide later", Theme.ModalButton))
        {
            ImGui.CloseCurrentPopup();
            _offer = null;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Keeps it. File > Recover Unsaved Work brings this back.");
        }

        ImGui.EndPopup();

        if (!open)
        {
            _offer = null;
        }
    }

    private void DrawConfirmPopup()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal(ConfirmPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.Text($"{Session.ProjectName} has unsaved changes.");
        ImGui.Text($"Discard them and {_pendingDescription}?");
        ImGui.Spacing();

        if (ImGui.Button("Save first", Theme.ModalButton))
        {
            ImGui.CloseCurrentPopup();
            Action? pending = _pendingAction;
            _pendingAction = null;
            Save();

            // Save As opens its own dialog; only chain straight through when the path was known.
            if (!Session.HasUnsavedChanges)
            {
                pending?.Invoke();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Discard", Theme.ModalButton))
        {
            ImGui.CloseCurrentPopup();
            ConfirmDiscard();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", Theme.ModalButton))
        {
            CancelPending();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();

        if (!open)
        {
            CancelPending();
        }
    }

    private void Remember(string path)
    {
        if (RemembersRecent)
        {
            Recent.Add(path);
        }
    }

    private static void Report(string message, bool isError) =>
        ReportLog.Shared.Post(message, isError ? ReportKind.Error : ReportKind.Info);
}
