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
    private const string ConfirmPopupId = "discard-changes";

    private readonly FileBrowserDialog _browser = new();

    private Action? _pendingAction;
    private string _pendingDescription = string.Empty;
    private bool _shouldOpenConfirm;

    public RecentFiles Recent { get; } = new();

    /// <summary>Last outcome, shown in the status line.</summary>
    public string StatusMessage { get; private set; } = string.Empty;

    public bool IsError { get; private set; }

    public string WindowTitle =>
        $"{(session.HasUnsavedChanges ? "*" : string.Empty)}{session.ProjectName} — EditorApp";

    public void NewProject() => GuardUnsaved("start a new level", () =>
    {
        // A new level starts as a cube, not an empty world: with no Place tool there has to be a
        // surface for Extrude to pull on, in every direction.
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        onWorldReplaced();
        Report($"New level — {EditorSession.StarterCubeSize}³ white cube to extrude from.", isError: false);
    });

    public void OpenProject() => GuardUnsaved("open another level", () =>
        _browser.Show(
            FileBrowserMode.Open,
            "Open level",
            VxLevelFile.Extension,
            StartDirectory,
            suggestedName: null,
            LoadFrom));

    public void OpenRecent(string path) => GuardUnsaved("open another level", () => LoadFrom(path));

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
        WriteTo);

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
            Recent.Add(path);
            Report(
                $"Opened {Path.GetFileName(path)} — {scene.SolidCount:N0} voxels in {scene.Objects.Count} object(s).",
                isError: false);
        }
        catch (Exception exception) when (exception is VxLevelFormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Recent.Remove(path);
            Report($"Could not open {Path.GetFileName(path)}: {exception.Message}", isError: true);
        }
    }

    private void WriteTo(string path)
    {
        try
        {
            VxLevelFile.Save(session.Scene, path);
            session.ProjectPath = path;
            session.HasUnsavedChanges = false;
            Recent.Add(path);
            Report($"Saved {Path.GetFileName(path)}.", isError: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Report($"Could not save: {exception.Message}", isError: true);
        }
    }

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
        _browser.Draw();
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
        ImGui.Separator();

        if (ImGui.Button("Save first", new Vector2(120f, 0f)))
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
        if (ImGui.Button("Discard", new Vector2(120f, 0f)))
        {
            ImGui.CloseCurrentPopup();
            Action? pending = _pendingAction;
            _pendingAction = null;
            pending?.Invoke();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120f, 0f)))
        {
            _pendingAction = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();

        if (!open)
        {
            _pendingAction = null;
        }
    }

    private void Report(string message, bool isError)
    {
        StatusMessage = message;
        IsError = isError;
    }
}
