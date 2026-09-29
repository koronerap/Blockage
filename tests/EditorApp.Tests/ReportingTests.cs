namespace EditorApp.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v0.9.0", "0.9.0-dev", true)]
    [InlineData("v0.9.0", "0.9.0", false)]
    [InlineData("v1.0.0", "0.9.0", true)]
    [InlineData("v0.10.0", "0.9.0", true)]
    [InlineData("v0.8.0", "0.9.0-dev", false)]
    [InlineData("v0.9.1", "0.9.0", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("v1.0.0-beta", "1.0.0-dev", false)]
    [InlineData("not a version", "0.9.0", false)]
    public void ATagIsNewerOnlyWhenItIsALaterVersion(string tag, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(tag, current));

    private static string Release(string tag, bool draft = false, bool prerelease = false) =>
        $$"""{ "tag_name": "{{tag}}", "name": "Blockage", "html_url": "https://github.com/koronerap/Blockage/releases/tag/{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}} }""";

    [Fact]
    public void ANewerReleaseIsFoundWithItsPage()
    {
        NewRelease? found = UpdateCheck.Newer(Release("v1.0.0"), "0.9.0");

        Assert.Equal(new NewRelease("1.0.0", "https://github.com/koronerap/Blockage/releases/tag/v1.0.0"), found);
    }

    [Fact]
    public void TheReleaseThisBuildIsIsNoNews() =>
        Assert.Null(UpdateCheck.Newer(Release("v0.1.0"), "0.9.0-dev"));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DraftsAndPreReleasesAreNotOffered(bool draft, bool prerelease) =>
        Assert.Null(UpdateCheck.Newer(Release("v9.0.0", draft, prerelease), "0.9.0"));
}

public class CrashReportTests
{
    private const string Log = """
        [2026-09-29 21:00:00] started, version 0.9.0.0
        [2026-09-29 21:00:05] FAILED while opening C:\levels\broken.vxlevel
        System.IO.InvalidDataException: Central Directory corrupt.
        [2026-09-29 21:10:00] started, version 0.9.0.0
        [2026-09-29 21:12:30] FAILED while unhandled
        System.InvalidOperationException: The chunk was not there.
           at EditorApp.Core.Voxels.VoxelWorld.GetChunk(ChunkCoord coord)
           at EditorApp.EditorApplication.OnRender(Double deltaSeconds)
        [2026-09-29 21:12:30] FAILED while rescuing work while crashing
        System.IO.IOException: The disk is full.
        """;

    [Fact]
    public void TheLastSessionsCrashIsFoundWithItsStack()
    {
        string? crash = CrashReport.LastSessionCrash(Log.ReplaceLineEndings("\n"));

        Assert.NotNull(crash);
        Assert.StartsWith("[2026-09-29 21:12:30] FAILED while unhandled", crash, StringComparison.Ordinal);
        Assert.Contains("The chunk was not there.", crash, StringComparison.Ordinal);
        Assert.Contains("OnRender", crash, StringComparison.Ordinal);
        Assert.DoesNotContain("disk is full", crash, StringComparison.Ordinal);
    }

    /// <summary>Once the editor has started again, the crash is a session back: it is offered once, not every start.</summary>
    [Fact]
    public void ACrashBeforeTheLastStartIsNotOfferedAgain() =>
        Assert.Null(CrashReport.LastSessionCrash((Log + "\n[2026-09-29 21:20:00] started, version 0.9.0.0\n").ReplaceLineEndings("\n")));

    [Fact]
    public void AFailureTheEditorCaughtIsNotACrash() =>
        Assert.Null(CrashReport.LastSessionCrash(Log.ReplaceLineEndings("\n")[..Log.ReplaceLineEndings("\n").IndexOf("[2026-09-29 21:10:00]", StringComparison.Ordinal)]));

    [Fact]
    public void NoLogIsNoCrash() => Assert.Null(CrashReport.LastSessionCrash(string.Empty));

    [Fact]
    public void TheReportIsTheBugFormFilledIn()
    {
        string crash = CrashReport.LastSessionCrash(Log.ReplaceLineEndings("\n"))!;
        string url = CrashReport.IssueUrl(crash, "0.9.0-dev", "Windows");

        Assert.StartsWith("https://github.com/koronerap/Blockage/issues/new?template=bug_report.yml", url, StringComparison.Ordinal);
        Assert.Contains("&version=0.9.0-dev", url, StringComparison.Ordinal);
        Assert.Contains("&platform=Windows", url, StringComparison.Ordinal);
        Assert.Contains("&title=" + Uri.EscapeDataString("Crash: System.InvalidOperationException: The chunk was not there."), url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("The chunk was not there."), url, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongCrashIsCutShortOfWhatABrowserTakes()
    {
        string crash = "[2026-09-29 21:12:30] FAILED while unhandled\nSystem.Exception: deep\n" + string.Concat(Enumerable.Repeat("   at Somewhere.Deep.Down()\n", 2000));

        Assert.InRange(CrashReport.IssueUrl(crash, "0.9.0", "Linux").Length, 100, 12_000);
    }

    [Fact]
    public void AReportWithoutACrashIsTheFormWithTheVersion()
    {
        string url = CrashReport.IssueUrl(null, "0.9.0", "macOS");

        Assert.Contains("&version=0.9.0", url, StringComparison.Ordinal);
        Assert.DoesNotContain("&crash=", url, StringComparison.Ordinal);
    }
}
