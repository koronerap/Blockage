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

    /// <summary>Whether opening and saving add to the recent-files list. Off for smoke and screenshot runs.</summary>
    public bool RemembersRecent { get; set; } = true;

    /// <summary>Last outcome, shown in the status line.</summary>
    public string StatusMessage { get; private set; } = string.Empty;

    public bool IsError { get; private set; }

    public string WindowTitle =>
        $"{(session.HasUnsavedChanges ? "*" : string.Empty)}{session.ProjectName} - Blockage";

    public void NewProject() => GuardUnsaved("start a new level", () =>
    {
        // A new level starts as a cube, not an empty world: with no Place tool there has to be a
        // surface for Extrude to pull on, in every direction.
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        onWorldReplaced();
        int side = EditorSession.StarterCubeSize;
        Report($"New level - {side}x{side}x{side} white cube to extrude from.", isError: false);
    });

    public void OpenProject() => GuardUnsaved("open another level", () =>
        _browser.Show(
            FileBrowserMode.Open,
            "Open level",
            VxLevelFile.Extension,
            StartDirectory,
            suggestedName: null,
            LoadFrom,
            ProjectDirectory));

    public void OpenRecent(string path) => GuardUnsaved("open another level", () => LoadFrom(path));

    /// <summary>Asks before letting the editor close on unsaved work.</summary>
    public void RequestExit(Action exit) => GuardUnsaved("exit", exit);

    /// <summary>Saves to the current path, or asks for one when the project has never been saved.</summary>
    public void Save()
    {
        if (session.ProjectPath is null)
        {
            SaveAs();
            return;
        }

        WriteTo(session.ProjectPath);
    }

    public void SaveAs() => _browser.Show(
        FileBrowserMode.Save,
        "Save level as",
        VxLevelFile.Extension,
        StartDirectory,
        session.ProjectName + VxLevelFile.Extension,
        WriteTo,
        ProjectDirectory);

    /// <summary>
    /// Where the level being worked on lives, or null when it has never been saved. Kept apart from
    /// <see cref="StartDirectory"/>, which always has an answer — the browser offers this one as a
    /// shortcut, and a shortcut labelled "Project folder" that led to wherever the dialog happened
    /// to open would be a lie.
    /// </summary>
    private string? ProjectDirectory =>
        session.ProjectPath is not null ? Path.GetDirectoryName(session.ProjectPath) : null;

    private string StartDirectory =>
        session.ProjectPath is not null
            ? Path.GetDirectoryName(session.ProjectPath) ?? _browser.CurrentDirectory
            : _browser.CurrentDirectory;

    private void LoadFrom(string path)
    {
        try
        {
            VoxelScene scene = VxLevelFile.LoadScene(path);
            session.ReplaceScene(scene, path);
            onWorldReplaced();
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
            VxLevelFile.Save(session.Scene, path);
            session.ProjectPath = path;
            session.HasUnsavedChanges = false;
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
    public void Recover(RecoveryEntry entry) => GuardUnsaved("recover the autosaved work", () =>
    {
        try
        {
            VoxelScene scene = VxLevelFile.LoadScene(entry.LevelPath);
            session.ReplaceScene(scene, entry.ProjectPath);
            session.HasUnsavedChanges = true;
            onWorldReplaced();
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

    /// <summary>Runs an action immediately, or asks first when there is unsaved work.</summary>
    private void GuardUnsaved(string description, Action action)
    {
        if (!session.HasUnsavedChanges)
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

        ImGui.Text($"{session.ProjectName} has unsaved changes.");
        ImGui.Text($"Discard them and {_pendingDescription}?");
        ImGui.Spacing();

        if (ImGui.Button("Save first", Theme.ModalButton))
        {
            ImGui.CloseCurrentPopup();
            Action? pending = _pendingAction;
            _pendingAction = null;
            Save();

            // Save As opens its own dialog; only chain straight through when the path was known.
            if (!session.HasUnsavedChanges)
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

    private void Report(string message, bool isError)
    {
        StatusMessage = message;
        IsError = isError;
    }
}
