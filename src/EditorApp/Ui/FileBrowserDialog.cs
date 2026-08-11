using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

public enum FileBrowserMode
{
    Open,
    Save,
}

/// <summary>
/// A small ImGui file browser. Deliberately not a native dialog: a native one means P/Invoke per
/// platform, and this tool only ever picks a folder plus a name.
/// </summary>
public sealed class FileBrowserDialog
{
    private const string PopupId = "file-browser";

    private FileBrowserMode _mode;
    private string _title = string.Empty;
    private string _extension = string.Empty;
    private string _directory = Directory.GetCurrentDirectory();
    private string _fileName = string.Empty;
    private string? _error;
    private Action<string>? _onConfirm;
    private bool _shouldOpenPopup;

    public bool IsOpen { get; private set; }

    public void Show(
        FileBrowserMode mode,
        string title,
        string extension,
        string? startDirectory,
        string? suggestedName,
        Action<string> onConfirm)
    {
        _mode = mode;
        _title = title;
        _extension = extension;
        _onConfirm = onConfirm;
        _fileName = suggestedName ?? string.Empty;
        _error = null;

        if (!string.IsNullOrEmpty(startDirectory) && Directory.Exists(startDirectory))
        {
            _directory = startDirectory;
        }
        else if (!Directory.Exists(_directory))
        {
            _directory = Directory.GetCurrentDirectory();
        }

        IsOpen = true;
        _shouldOpenPopup = true;
    }

    public void Draw()
    {
        if (_shouldOpenPopup)
        {
            ImGui.OpenPopup(PopupId);
            _shouldOpenPopup = false;
        }

        if (!IsOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(720, 520), ImGuiCond.FirstUseEver);

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.SeparatorText(_title);
        DrawPathBar();
        DrawEntryList();
        DrawFooter();

        if (!open)
        {
            // Dismissed with the window's own close button. This has to happen before EndPopup:
            // CloseCurrentPopup works on the popup being drawn, and after EndPopup there is no
            // longer one — ImGui asserts on that, and a native assert kills the process outright
            // rather than raising anything catchable.
            Close();
        }

        ImGui.EndPopup();

        // Out here the dialog is no longer part of the frame, so the caller is free to replace the
        // whole scene.
        Action? result = _deferredResult;
        _deferredResult = null;
        result?.Invoke();
    }

    private void DrawPathBar()
    {
        if (ImGui.Button("Up"))
        {
            DirectoryInfo? parent = Directory.GetParent(_directory);
            if (parent is not null)
            {
                Navigate(parent.FullName);
            }
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(_directory);

        foreach (DriveInfo drive in SafeDrives())
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(drive.Name))
            {
                Navigate(drive.RootDirectory.FullName);
            }
        }
    }

    private void DrawEntryList()
    {
        ImGui.BeginChild("entries", new Vector2(0f, 340f), ImGuiChildFlags.Border);

        try
        {
            foreach (string path in Directory.EnumerateDirectories(_directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable($"[dir] {Path.GetFileName(path)}", false, ImGuiSelectableFlags.AllowDoubleClick)
                    && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    Navigate(path);
                    break;
                }
            }

            foreach (string path in Directory.EnumerateFiles(_directory, "*" + _extension).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(path);
                bool selected = string.Equals(name, _fileName, StringComparison.OrdinalIgnoreCase);

                if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    _fileName = name;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        Confirm();
                        break;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ImGui.TextColored(Theme.Danger, $"Cannot list this folder: {exception.Message}");
        }

        ImGui.EndChild();
    }

    private void DrawFooter()
    {
        ImGui.SetNextItemWidth(-160f);
        string fileName = _fileName;
        if (ImGui.InputText("File name", ref fileName, 260))
        {
            _fileName = fileName;
        }

        if (_error is not null)
        {
            ImGui.TextColored(Theme.Danger, _error);
        }

        string confirmLabel = _mode == FileBrowserMode.Save ? "Save" : "Open";
        if (ImGui.Button(confirmLabel, Theme.ModalButton))
        {
            Confirm();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", Theme.ModalButton))
        {
            Close();
        }
    }

    /// <summary>
    /// What the confirmed choice was, held until the popup has finished drawing. Opening a level
    /// tears the scene down and rebuilds the renderer's buffers; doing that from inside the dialog
    /// means doing it halfway through the frame the dialog is still part of.
    /// </summary>
    private Action? _deferredResult;

    private void Confirm()
    {
        string name = _fileName.Trim();
        if (name.Length == 0)
        {
            _error = "Enter a file name.";
            return;
        }

        if (!name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
        {
            name += _extension;
        }

        string full = Path.Combine(_directory, name);

        if (_mode == FileBrowserMode.Open && !File.Exists(full))
        {
            _error = "That file does not exist.";
            return;
        }

        Action<string>? callback = _onConfirm;
        Close();
        _deferredResult = () => callback?.Invoke(full);
    }

    private void Navigate(string path)
    {
        _directory = path;
        _error = null;
    }

    private void Close()
    {
        IsOpen = false;
        _onConfirm = null;
        ImGui.CloseCurrentPopup();
    }

    /// <summary>The current folder, so the next dialog opens where the last one left off.</summary>
    public string CurrentDirectory => _directory;

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (DriveInfo drive in drives)
        {
            if (drive.IsReady)
            {
                yield return drive;
            }
        }
    }
}
