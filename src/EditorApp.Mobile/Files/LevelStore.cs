using EditorApp.Core.Project;
using EditorApp.Core.Scene;

namespace EditorApp.Mobile.Files;

/// <param name="Name">What the level is called, without the extension.</param>
/// <param name="Path">Where it actually lives. Never shown; the name is what the user recognises.</param>
public readonly record struct LevelEntry(string Name, string Path, DateTime Modified, long Bytes);

/// <summary>
/// Where levels live on a phone.
///
/// Android has no directory a process may simply write to, and the desktop's file browser does not
/// come across at all — there is no path for it to browse. What replaces it is a library the app
/// owns: one private directory, listed by name, with the system document picker used only to carry a
/// level in from somewhere else or out to somewhere else.
///
/// The directory is private storage, so it is emptied if the app is uninstalled. That is the reason
/// export exists, and the reason it is offered next to save rather than buried.
/// </summary>
public sealed class LevelStore(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>Newest first, which is nearly always the order they are wanted in.</summary>
    public IReadOnlyList<LevelEntry> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var entries = new List<LevelEntry>();

        foreach (string path in System.IO.Directory.EnumerateFiles(Directory, "*" + VxLevelFile.Extension))
        {
            var file = new FileInfo(path);
            entries.Add(new LevelEntry(
                Path.GetFileNameWithoutExtension(path),
                path,
                file.LastWriteTime,
                file.Length));
        }

        entries.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return entries;
    }

    public string PathFor(string name) =>
        Path.Combine(Directory, MakeFileSafe(name) + VxLevelFile.Extension);

    public bool Exists(string name) => File.Exists(PathFor(name));

    public string Save(VoxelScene scene, string name)
    {
        System.IO.Directory.CreateDirectory(Directory);

        string path = PathFor(name);
        VxLevelFile.Save(scene, path, name);
        return path;
    }

    public static VoxelScene Load(string path) => VxLevelFile.LoadScene(path);

    public void Delete(string path)
    {
        if (File.Exists(path) && Path.GetDirectoryName(Path.GetFullPath(path)) == Directory)
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Turns whatever was typed into something that can be a filename, without silently renaming the
    /// level: the name the user chose is what goes into the manifest either way, and only the file on
    /// disk is sanitised.
    /// </summary>
    public static string MakeFileSafe(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return "Untitled";
        }

        // What no system allows, not only what Android refuses: a level shared off the phone
        // should still copy onto a Windows machine.
        char[] cleaned = EditorApp.Core.Project.FileNames.Safe(name).ToCharArray();

        return new string(cleaned);
    }

    /// <summary>
    /// A name nothing in the library is using yet. Saving over another level by accident is a whole
    /// afternoon, and the phone has no folder to notice the collision in.
    /// </summary>
    public string UnusedName(string preferred)
    {
        if (!Exists(preferred))
        {
            return preferred;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = $"{preferred} {suffix}";
            if (!Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{preferred} {DateTime.Now:yyyyMMdd-HHmmss}";
    }
}
