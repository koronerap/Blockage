namespace EditorApp.Ui;

/// <summary>The project's pages the editor points at, and the one way it opens them.</summary>
public static class Links
{
    public const string Repository = "https://github.com/koronerap/Blockage";

    /// <summary>The manual: docs/ of the repository, published as its GitHub Pages site.</summary>
    public const string Manual = "https://koronerap.github.io/Blockage/";

    /// <summary>Opens a web page in the default browser, saying so in the status bar if it cannot.</summary>
    public static void Open(string url)
    {
        try
        {
            using var browser = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            ReportLog.Shared.Post($"Could not open {url}: {exception.Message}", ReportKind.Error);
        }
    }
}
