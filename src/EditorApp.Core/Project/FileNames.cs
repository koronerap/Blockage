namespace EditorApp.Core.Project;

/// <summary>
/// File names made from what someone typed: a level's, a prop's, a render's. What no system allows
/// becomes an underscore — the characters Windows refuses as well as the slash and control
/// characters every system does — so a file named on Linux or a phone still copies to Windows.
/// The system's own list would not do: on Linux and Android it is only the slash and NUL.
/// </summary>
public static class FileNames
{
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Safe(string name) =>
        string.Concat(name.Select(c => c < ' ' || Array.IndexOf(Forbidden, c) >= 0 ? '_' : c));
}
