namespace EditorApp;

/// <summary>The last few projects that were opened or saved, persisted between runs.</summary>
public sealed class RecentFiles
{
    private const int MaxEntries = 10;

    private readonly string _storePath;
    private readonly List<string> _paths = [];

    public RecentFiles()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EditorApp");

        _storePath = Path.Combine(directory, "recent.txt");
        Load();
    }

    public IReadOnlyList<string> Paths => _paths;

    public void Add(string path)
    {
        string full = Path.GetFullPath(path);
        _paths.RemoveAll(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);

        if (_paths.Count > MaxEntries)
        {
            _paths.RemoveRange(MaxEntries, _paths.Count - MaxEntries);
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
                _paths.AddRange(File.ReadAllLines(_storePath).Where(line => line.Length > 0).Take(MaxEntries));
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
            File.WriteAllLines(_storePath, _paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
