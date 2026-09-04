using Android.App;
using Android.Content;
using Android.Text;
using Android.Widget;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Mobile.Files;
using EditorApp.Mobile.Rendering;

namespace EditorApp.Mobile;

/// <summary>
/// Everything the editor does with files, kept away from the activity that hosts it.
///
/// Two kinds of storage meet here and they behave nothing alike. The library is a directory the app
/// owns, where a level has a name and a path and can be saved over. Import and export go through the
/// system document picker, which hands back a content URI and a stream and never a path — and
/// which the app cannot open on its own, so those two are the only operations that have to go out to
/// the system and come back later.
/// </summary>
public sealed class FileActions(Activity activity, EditorSurfaceView surface, LevelStore store)
{
    public const int ImportRequest = 1;
    public const int ExportRequest = 2;

    /// <summary>
    /// A .vxlevel has no registered type, so the picker is asked for anything and the file is
    /// checked by trying to read it. Filtering on a made-up MIME type would hide the file the user
    /// is looking for on some devices and show everything anyway on others.
    /// </summary>
    private const string AnyType = "*/*";

    public event Action? LibraryChanged;

    /// <summary>Saves over the level's own file, or asks for a name if it has never had one.</summary>
    public void Save()
    {
        if (surface.ProjectPath is not { } path)
        {
            SaveAs();
            return;
        }

        Write(path, Path.GetFileNameWithoutExtension(path));
    }

    public void SaveAs() => AskForName(
        "Save level as",
        store.UnusedName(surface.ProjectName),
        name =>
        {
            if (store.Exists(name) && store.PathFor(name) != surface.ProjectPath)
            {
                Confirm(
                    $"\"{name}\" already exists. Replace it?",
                    "Replace",
                    () => Write(store.PathFor(name), name));
                return;
            }

            Write(store.PathFor(name), name);
        });

    private void Write(string path, string name)
    {
        try
        {
            surface.UseScene(scene => VxLevelFile.Save(scene, path, name));
            surface.MarkSaved(path);
            Say($"Saved \"{name}\".");
            LibraryChanged?.Invoke();
        }
        catch (Exception error)
        {
            Say($"Could not save: {error.Message}");
        }
    }

    public void New() => GuardUnsaved("start a new level", () =>
    {
        surface.NewLevel();
        Say("New level.");
    });

    public void Open(LevelEntry level) => GuardUnsaved($"open \"{level.Name}\"", () =>
    {
        try
        {
            VoxelScene scene = LevelStore.Load(level.Path);
            surface.ReplaceScene(scene, level.Path);
            Say($"Opened \"{level.Name}\".");
        }
        catch (Exception error)
        {
            Say($"Could not open \"{level.Name}\": {error.Message}");
        }
    });

    public void Delete(LevelEntry level) => Confirm(
        $"Delete \"{level.Name}\"? This cannot be undone.",
        "Delete",
        () =>
        {
            try
            {
                store.Delete(level.Path);

                // The level is gone, so the open one no longer has a file behind it — saving must
                // ask for a name again rather than quietly recreating what was just deleted.
                if (surface.ProjectPath == level.Path)
                {
                    surface.MarkSaved(null);
                }

                Say($"Deleted \"{level.Name}\".");
                LibraryChanged?.Invoke();
            }
            catch (Exception error)
            {
                Say($"Could not delete: {error.Message}");
            }
        });

    public void Import() => GuardUnsaved("import a level", () =>
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(AnyType);
        activity.StartActivityForResult(intent, ImportRequest);
    });

    public void Export()
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(AnyType);
        intent.PutExtra(Intent.ExtraTitle, surface.ProjectName + VxLevelFile.Extension);
        activity.StartActivityForResult(intent, ExportRequest);
    }

    /// <summary>Handles the picker coming back. Returns false when the result was not ours.</summary>
    public bool OnPicked(int requestCode, Result resultCode, Intent? data)
    {
        if (resultCode != Result.Ok || data?.Data is not { } uri)
        {
            return requestCode is ImportRequest or ExportRequest;
        }

        switch (requestCode)
        {
            case ImportRequest:
                ReadFrom(uri);
                return true;

            case ExportRequest:
                WriteTo(uri);
                return true;

            default:
                return false;
        }
    }

    private void ReadFrom(Android.Net.Uri uri)
    {
        try
        {
            using Stream? stream = activity.ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException("The file could not be opened.");

            // Straight into a scene: an imported document has no path of its own that the editor
            // could ever save back to, so it arrives as an unsaved level with a name to be chosen.
            VoxelScene scene = VxLevelFile.LoadScene(stream);
            surface.ReplaceScene(scene, path: null);

            Say("Imported. Save it to keep it on this device.");
        }
        catch (Exception error)
        {
            Say($"Could not import: {error.Message}");
        }
    }

    private void WriteTo(Android.Net.Uri uri)
    {
        try
        {
            using Stream? stream = activity.ContentResolver?.OpenOutputStream(uri)
                ?? throw new IOException("The file could not be created.");

            surface.UseScene(scene => VxLevelFile.Save(scene, stream, surface.ProjectName));
            Say("Exported.");
        }
        catch (Exception error)
        {
            Say($"Could not export: {error.Message}");
        }
    }

    /// <summary>
    /// Anything that throws the current level away asks first, and only when there is something to
    /// lose. A phone has no window title to carry a quiet asterisk, so this is the only warning.
    /// </summary>
    private void GuardUnsaved(string what, Action proceed)
    {
        if (!surface.HasUnsavedChanges)
        {
            proceed();
            return;
        }

        Confirm($"\"{surface.ProjectName}\" has unsaved changes. Discard them and {what}?", "Discard", proceed);
    }

    private void Confirm(string message, string confirmLabel, Action proceed) =>
        new AlertDialog.Builder(activity)
            .SetMessage(message)!
            .SetPositiveButton(confirmLabel, (_, _) => proceed())!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();

    private void AskForName(string title, string suggestion, Action<string> accepted)
    {
        var input = new EditText(activity)
        {
            Text = suggestion,
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences,
        };

        input.SetSelection(suggestion.Length);

        new AlertDialog.Builder(activity)
            .SetTitle(title)!
            .SetView(input)!
            .SetPositiveButton("Save", (_, _) =>
            {
                string name = input.Text?.Trim() ?? string.Empty;
                accepted(name.Length == 0 ? suggestion : name);
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void Say(string message) => Toast.MakeText(activity, message, ToastLength.Short)?.Show();
}
