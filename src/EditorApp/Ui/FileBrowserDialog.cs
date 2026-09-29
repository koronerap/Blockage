using System.Globalization;
using System.Numerics;
using ImGuiNET;

namespace EditorApp.Ui;

public enum FileBrowserMode
{
    Open,
    Save,
}

/// <summary>
/// A file browser. Deliberately not a native dialog: a native one means P/Invoke per platform, and
/// this tool only ever picks a folder plus a name.
///
/// Which is not an argument for it being bare. Saving is the harder half of the job — the folder you
/// want usually does not exist yet, it is rarely the one you are standing in, and overwriting the
/// wrong file is silent — so the places list, the New folder button and the overwrite prompt all
/// exist for that direction rather than for opening.
/// </summary>
public sealed class FileBrowserDialog
{
    private const string PopupId = "file-browser";

    private const float PlacesWidth = 190f;
    private const float ListHeight = 360f;

    /// <summary>A shortcut in the left-hand column.</summary>
    private readonly record struct Place(string Label, string Path);

    private FileBrowserMode _mode;
    private string _title = string.Empty;
    private string _extension = string.Empty;

    /// <summary>The kinds of file listed: <see cref="_extension"/> split at its semicolons — the first is what a name typed without one gets.</summary>
    private string[] _extensions = [];
    private string _directory = Directory.GetCurrentDirectory();
    private string _fileName = string.Empty;
    private string? _error;
    private Action<string>? _onConfirm;
    private bool _shouldOpenPopup;
    private bool _focusNameField;

    private readonly List<Place> _places = [];
    private readonly List<string> _history = [];

    private bool _creatingFolder;
    private string _newFolderName = string.Empty;

    /// <summary>Set when Save lands on a file that already exists; cleared by answering.</summary>
    private string? _pendingOverwrite;

    /// <summary>
    /// What the confirmed choice was, held until the popup has finished drawing. Opening a level
    /// tears the scene down and rebuilds the renderer's buffers; doing that from inside the dialog
    /// means doing it halfway through the frame the dialog is still part of.
    /// </summary>
    private Action? _deferredResult;

    public bool IsOpen { get; private set; }

    /// <summary>Whether a file is of a kind the dialog is for — exactly, since a pattern like "*.vox" also finds ".voxel".</summary>
    private bool IsListed(string path) =>
        _extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>The current folder, so the next dialog opens where the last one left off.</summary>
    public string CurrentDirectory => _directory;

    /// <summary>
    /// The visible half changes with the job — Open level, Save level as — while the half after
    /// "###" keeps ImGui looking at the same window. Both OpenPopup and BeginPopupModal have to be
    /// given this exact string: ImGui hashes "Name###id" as "###id", not as "id", so opening by one
    /// and beginning by the other addresses two different popups.
    /// </summary>
    private string Label => $"{_title}###{PopupId}";

    public void Show(
        FileBrowserMode mode,
        string title,
        string extension,
        string? startDirectory,
        string? suggestedName,
        Action<string> onConfirm,
        string? projectDirectory = null)
    {
        _mode = mode;
        _title = title;
        _extension = extension;
        _extensions = extension.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _onConfirm = onConfirm;
        _fileName = suggestedName ?? string.Empty;
        _error = null;
        _pendingOverwrite = null;
        _creatingFolder = false;
        _newFolderName = string.Empty;
        _history.Clear();

        if (!string.IsNullOrEmpty(startDirectory) && Directory.Exists(startDirectory))
        {
            _directory = startDirectory;
        }
        else if (!Directory.Exists(_directory))
        {
            _directory = Directory.GetCurrentDirectory();
        }

        BuildPlaces(projectDirectory);

        IsOpen = true;
        _shouldOpenPopup = true;

        // Saving usually means typing a name, so start there rather than making the user find it.
        _focusNameField = mode == FileBrowserMode.Save;
    }

    /// <summary>
    /// The shortcuts, rebuilt each time the dialog opens so that "Project folder" follows the level
    /// currently being worked on. Anything that is not there on this machine is simply left out.
    /// </summary>
    private void BuildPlaces(string? projectDirectory)
    {
        _places.Clear();

        void Add(string label, string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)
                && !_places.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _places.Add(new Place(label, path));
            }
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(projectDirectory))
        {
            Add("Project folder", projectDirectory);
        }

        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        // No SpecialFolder for Downloads on any platform, and it is the folder people actually use.
        Add("Downloads", Path.Combine(home, "Downloads"));

        Add("Home", home);
    }

    public void Draw()
    {
        if (_shouldOpenPopup)
        {
            ImGui.OpenPopup(Label);
            _shouldOpenPopup = false;
        }

        if (!IsOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(900f, 0f), ImGuiCond.Always);

        bool open = true;
        if (!ImGui.BeginPopupModal(Label, ref open, ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        DrawToolbar();
        ImGui.Spacing();

        DrawPlaces();
        ImGui.SameLine();
        DrawEntryList();

        ImGui.Spacing();
        DrawFooter();

        if (!open)
        {
            // Dismissed with the window's own close button. This has to happen before EndPopup:
            // CloseCurrentPopup works on the popup being drawn, and after EndPopup there is no
            // longer one.
            Close();
        }

        ImGui.EndPopup();

        // Out here the dialog is no longer part of the frame, so the caller is free to replace the
        // whole scene.
        Action? result = _deferredResult;
        _deferredResult = null;
        result?.Invoke();
    }

    private void DrawToolbar()
    {
        ImGui.BeginDisabled(_history.Count == 0);
        if (ImGui.Button("Back"))
        {
            string previous = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            _directory = previous;
            _error = null;
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        DirectoryInfo? parent = SafeParent(_directory);

        ImGui.BeginDisabled(parent is null);
        if (ImGui.Button("Up") && parent is not null)
        {
            Navigate(parent.FullName);
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        DrawBreadcrumb();

        // Right-aligned, because it belongs to the folder rather than to the path.
        float button = 110f;
        ImGui.SameLine(ImGui.GetWindowWidth() - button - ImGui.GetStyle().WindowPadding.X);
        if (ImGui.Button("New folder", new Vector2(button, 0f)))
        {
            _creatingFolder = true;
            _newFolderName = "New folder";
        }

        DrawNewFolderRow();
    }

    /// <summary>The path as buttons, so any ancestor is one click away rather than several Ups.</summary>
    private void DrawBreadcrumb()
    {
        string full = Path.GetFullPath(_directory);
        string root = Path.GetPathRoot(full) ?? full;

        ImGui.AlignTextToFramePadding();

        if (ImGui.SmallButton(root.TrimEnd(Path.DirectorySeparatorChar) is { Length: > 0 } label ? label : root))
        {
            Navigate(root);
        }

        string relative = full.Length > root.Length ? full[root.Length..] : string.Empty;
        string walked = root;

        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            walked = Path.Combine(walked, segment);

            ImGui.SameLine(0f, 2f);
            ImGui.TextDisabled(">");
            ImGui.SameLine(0f, 2f);

            // The id has to be the whole path: two folders on the same route can share a name.
            if (ImGui.SmallButton($"{segment}##{walked}"))
            {
                Navigate(walked);
            }
        }
    }

    private void DrawNewFolderRow()
    {
        if (!_creatingFolder)
        {
            return;
        }

        ImGui.SetNextItemWidth(320f);
        string name = _newFolderName;
        bool submitted = ImGui.InputText(
            "##new-folder",
            ref name,
            120,
            ImGuiInputTextFlags.EnterReturnsTrue);
        _newFolderName = name;

        ImGui.SameLine();
        if (ImGui.Button("Create") || submitted)
        {
            CreateFolder();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##new-folder"))
        {
            _creatingFolder = false;
        }
    }

    private void CreateFolder()
    {
        string name = _newFolderName.Trim();
        if (name.Length == 0)
        {
            _error = "Enter a folder name.";
            return;
        }

        try
        {
            string created = Path.Combine(_directory, name);
            Directory.CreateDirectory(created);
            _creatingFolder = false;

            // Straight into it: making a folder while saving means wanting to save in it.
            Navigate(created);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error = $"Could not create the folder: {exception.Message}";
        }
    }

    private void DrawPlaces()
    {
        ImGui.BeginChild("places", new Vector2(PlacesWidth, ListHeight), ImGuiChildFlags.Border);

        ImGui.TextDisabled("Places");
        foreach (Place place in _places)
        {
            bool here = string.Equals(place.Path, _directory, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(place.Label, here))
            {
                Navigate(place.Path);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(place.Path);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Drives");
        foreach (DriveInfo drive in SafeDrives())
        {
            string root = drive.RootDirectory.FullName;
            bool here = string.Equals(root, _directory, StringComparison.OrdinalIgnoreCase);

            if (ImGui.Selectable($"{drive.Name}##{root}", here))
            {
                Navigate(root);
            }
        }

        ImGui.EndChild();
    }

    private void DrawEntryList()
    {
        ImGui.BeginChild("entries", new Vector2(0f, ListHeight), ImGuiChildFlags.Border);

        try
        {
            DrawEntryTable();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ImGui.TextColored(Theme.Danger, $"Cannot list this folder: {exception.Message}");
        }

        ImGui.EndChild();
    }

    private void DrawEntryTable()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("files", 3, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("Modified", ImGuiTableColumnFlags.WidthFixed, 140f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        string? entered = null;

        foreach (string path in Directory.EnumerateDirectories(_directory)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            // Spanning the columns makes the whole row the target, which is what a row looks like.
            if (ImGui.Selectable(
                    $"{Path.GetFileName(path)}##{path}",
                    false,
                    ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.SpanAllColumns)
                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                entered = path;
            }

            // Marked in the size column rather than with a prefix on the name: the names stay
            // aligned with the files below them, and nothing has to be spelled with punctuation the
            // font may not have.
            ImGui.TableNextColumn();
            ImGui.TextDisabled("Folder");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(Modified(path, directory: true));
        }

        foreach (string path in Directory.EnumerateFiles(_directory)
                     .Where(IsListed)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(path);
            bool selected = string.Equals(name, _fileName, StringComparison.OrdinalIgnoreCase);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            if (ImGui.Selectable(
                    $"{name}##{path}",
                    selected,
                    ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.SpanAllColumns))
            {
                // One click fills the name in, which is most of what picking a file to overwrite is.
                _fileName = name;
                _error = null;
                _pendingOverwrite = null;

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    Confirm();
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextDisabled(Size(path));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(Modified(path, directory: false));
        }

        ImGui.EndTable();

        // Navigating inside the loop would keep enumerating a folder that is no longer the one.
        if (entered is not null)
        {
            Navigate(entered);
        }
    }

    private void DrawFooter()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("File name");
        ImGui.SameLine();

        if (_focusNameField)
        {
            ImGui.SetKeyboardFocusHere();
            _focusNameField = false;
        }

        ImGui.SetNextItemWidth(-260f);
        string fileName = _fileName;
        if (ImGui.InputText("##name", ref fileName, 260, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _fileName = fileName;
            Confirm();
        }
        else
        {
            if (!string.Equals(fileName, _fileName, StringComparison.Ordinal))
            {
                _pendingOverwrite = null;
            }

            _fileName = fileName;
        }

        ImGui.SameLine();
        ImGui.TextDisabled(string.Join(" ", _extensions));

        if (_pendingOverwrite is { } existing)
        {
            ImGui.TextColored(Theme.Highlight, $"{Path.GetFileName(existing)} already exists. Overwrite it?");
        }
        else if (_error is not null)
        {
            ImGui.TextColored(Theme.Danger, _error);
        }
        else
        {
            // Reserves the line either way, so the buttons do not jump when a message appears.
            ImGui.TextDisabled(_mode == FileBrowserMode.Save
                ? "Enter saves. Double-click a file to replace it."
                : "Enter opens. Double-click a file to open it.");
        }

        string confirmLabel = _pendingOverwrite is not null
            ? "Overwrite"
            : _mode == FileBrowserMode.Save ? "Save" : "Open";

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

    private void Confirm()
    {
        string name = _fileName.Trim();
        if (name.Length == 0)
        {
            _error = "Enter a file name.";
            return;
        }

        // A pasted absolute path should work as typed rather than being appended to the folder.
        string typed = Path.IsPathRooted(name) ? name : Path.Combine(_directory, name);

        // Tested before the extension is added, or typing a folder name would go looking for
        // "Documents.vxlevel" and report that it does not exist.
        if (Directory.Exists(typed))
        {
            Navigate(typed);
            _fileName = string.Empty;
            return;
        }

        string full = IsListed(typed) || _extensions.Length == 0
            ? typed
            : typed + _extensions[0];

        if (_mode == FileBrowserMode.Open && !File.Exists(full))
        {
            _error = "That file does not exist.";
            return;
        }

        // Asked once, in place, rather than as a second modal on top of this one. Confirming is
        // pressing the same button again, now labelled Overwrite.
        if (_mode == FileBrowserMode.Save
            && File.Exists(full)
            && !string.Equals(_pendingOverwrite, full, StringComparison.OrdinalIgnoreCase))
        {
            _pendingOverwrite = full;
            _error = null;
            return;
        }

        Action<string>? callback = _onConfirm;
        Close();
        _deferredResult = () => callback?.Invoke(full);
    }

    private void Navigate(string path)
    {
        if (string.Equals(path, _directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _history.Add(_directory);
        _directory = path;
        _error = null;
        _pendingOverwrite = null;
        _creatingFolder = false;
    }

    private void Close()
    {
        IsOpen = false;
        _onConfirm = null;
        ImGui.CloseCurrentPopup();
    }

    private static DirectoryInfo? SafeParent(string directory)
    {
        try
        {
            return Directory.GetParent(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Size(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            return bytes < 1024
                ? $"{bytes} B"
                : bytes < 1024 * 1024
                    ? $"{bytes / 1024.0:0.#} KB"
                    : $"{bytes / (1024.0 * 1024.0):0.#} MB";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string Modified(string path, bool directory)
    {
        try
        {
            DateTime time = directory
                ? Directory.GetLastWriteTime(path)
                : File.GetLastWriteTime(path);

            return time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

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
            bool ready;
            try
            {
                ready = drive.IsReady;
            }
            catch (IOException)
            {
                continue;
            }

            if (ready)
            {
                yield return drive;
            }
        }
    }
}
