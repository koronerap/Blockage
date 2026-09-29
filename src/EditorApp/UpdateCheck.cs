using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using EditorApp.Ui;

namespace EditorApp;

/// <summary>A release on GitHub newer than this build: its version, and the page to get it from.</summary>
public sealed record NewRelease(string Version, string Url);

/// <summary>
/// Whether a newer Blockage is out (Fullreleaseplan 9.10): one request for GitHub's newest release
/// when the editor starts, and nothing sent but the request itself — no telemetry, nothing that
/// tells one user from another. Preferences can turn it off; with no network it says nothing.
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/koronerap/Blockage/releases/latest";

    private static NewRelease? _found;

    /// <summary>What the check found: a newer release, or null for none, or not yet.</summary>
    public static NewRelease? Found => Volatile.Read(ref _found);

    /// <summary>Asks GitHub in the background; <see cref="Found"/> says what came back.</summary>
    public static void Start(string current) => _ = Task.Run(async () =>
    {
        try
        {
            Volatile.Write(ref _found, await Fetch(current));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Offline, rate-limited, or GitHub answering oddly: there is simply no news.
        }
    });

    public static async Task<NewRelease?> Fetch(string current, CancellationToken token = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Blockage/{current}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        string json = await client.GetStringAsync(LatestReleaseApi, token);
        return Newer(json, current);
    }

    /// <summary>The release GitHub describes, when it is out and later than <paramref name="current"/>.</summary>
    public static NewRelease? Newer(string json, string current)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement release = document.RootElement;
        if (!release.TryGetProperty("tag_name", out JsonElement tag) || tag.GetString() is not { } name
            || Flag(release, "draft") || Flag(release, "prerelease") || !IsNewer(name, current))
        {
            return null;
        }

        string url = release.TryGetProperty("html_url", out JsonElement page) && page.GetString() is { } link ? link : Links.Releases;
        return new NewRelease(name.TrimStart('v', 'V'), url);

        static bool Flag(JsonElement release, string name) =>
            release.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Whether a release's tag is a later version than this build: by the numbers, and at the same
    /// numbers a build still in development ("0.9.0-dev") comes before the release it leads to.
    /// </summary>
    public static bool IsNewer(string tag, string current)
    {
        if (!TryParse(tag, out Version? released, out bool releasedEarly) || !TryParse(current, out Version? mine, out bool mineEarly))
        {
            return false;
        }

        int compare = released.CompareTo(mine);
        return compare > 0 || (compare == 0 && mineEarly && !releasedEarly);
    }

    /// <summary>"v1.2.3", "1.2" or "1.2.3-dev": the numbers, and whether something follows them.</summary>
    private static bool TryParse(string text, [NotNullWhen(true)] out Version? version, out bool early)
    {
        string trimmed = text.Trim().TrimStart('v', 'V');
        int dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        early = dash >= 0;
        if (Version.TryParse(dash >= 0 ? trimmed[..dash] : trimmed, out Version? parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return true;
        }

        version = null;
        return false;
    }
}
