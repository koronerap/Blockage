using System.Text;

namespace EditorApp;

/// <summary>The last few projects that were opened or saved, persisted between runs.</summary>
public sealed class RecentFiles
{
    /// <summary>The most a list may be set to keep.</summary>
    public const int MaxCapacity = 30;

    private int _capacity = 10;

    private readonly string _storePath;
    private readonly List<string> _paths = [];

    /// <param name="storePath">
    /// Where the list lives. Defaults to the one beside the crash log; a test passes its own, so
    /// running the suite does not rewrite the list of the person whose machine it runs on.
    /// </param>
    public RecentFiles(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EditorApp",
            "recent.txt");

        Load();
    }

    /// <summary>
    /// A copy, not the list itself.
    ///
    /// Opening one of these entries promotes it to the front, which is a write to the very list the
    /// caller is almost certainly still walking — the Open Recent menu draws an item and acts on the
    /// click in the same loop. Handing out the live list made that an
    /// <see cref="InvalidOperationException"/> on the next iteration, and it took the editor with it.
    /// Ten short strings are not worth being clever about.
    /// </summary>
    public IReadOnlyList<string> Paths => [.. _paths];

    /// <summary>How many are kept. A preference; shrinking it drops the oldest at once.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Clamp(value, 1, MaxCapacity);
            if (_paths.Count > _capacity)
            {
                _paths.RemoveRange(_capacity, _paths.Count - _capacity);
                Save();
            }
        }
    }

    /// <summary>Forgets every entry.</summary>
    public void Clear()
    {
        if (_paths.Count > 0)
        {
            _paths.Clear();
            Save();
        }
    }

    public void Add(string path)
    {
        string full = Path.GetFullPath(path);
        _paths.RemoveAll(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);

        if (_paths.Count > _capacity)
        {
            _paths.RemoveRange(_capacity, _paths.Count - _capacity);
        }

        Save();
    }

    public void Remove(string path)
    {
        if (_paths.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_storePath))
            {
                // Explicit UTF-8 on both sides: these are paths, and a path can hold any character
                // the file system does.
                _paths.AddRange(File.ReadAllLines(_storePath, Encoding.UTF8)
                    .Where(line => line.Length > 0)
                    .Take(MaxCapacity));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable recent list is not worth interrupting startup for.
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllLines(_storePath, _paths, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
