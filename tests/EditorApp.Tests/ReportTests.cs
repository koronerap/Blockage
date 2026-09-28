using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>Reports in the status bar: how long each kind stays, and what the edit actions say.</summary>
public class ReportLogTests
{
    private double _now;

    private ReportLog Log() => new(() => _now);

    [Fact]
    public void AReportShowsForItsTimeAndThenGoes()
    {
        ReportLog log = Log();
        log.Post("Saved castle.vxlevel.");

        _now = ReportLog.InfoSeconds - 0.1;
        Assert.Equal("Saved castle.vxlevel.", log.Current?.Text);

        _now = ReportLog.InfoSeconds + 0.1;
        Assert.Null(log.Current);
        Assert.Single(log.Recent);
    }

    /// <summary>An error is the report that must not be missed.</summary>
    [Fact]
    public void AnErrorStaysLongerThanNews()
    {
        ReportLog log = Log();
        log.Post("Could not save.", ReportKind.Error);

        _now = ReportLog.InfoSeconds + 1;

        Assert.Equal(ReportKind.Error, log.Current?.Kind);
    }

    [Fact]
    public void ItFadesOutOverItsLastMoments()
    {
        ReportLog log = Log();
        log.Post("Copied.");

        _now = 1;
        Assert.Equal(1f, log.Opacity);

        _now = ReportLog.InfoSeconds - (ReportLog.FadeSeconds / 2);
        Assert.Equal(0.5f, log.Opacity, 2);
    }

    [Fact]
    public void TheNewestIsShownAndOnlyTheLastFewKept()
    {
        ReportLog log = Log();
        for (int i = 0; i < 30; i++)
        {
            log.Post($"report {i}");
        }

        Assert.Equal("report 29", log.Current?.Text);
        Assert.Equal(20, log.Recent.Count);
    }

    [Fact]
    public void ABlankReportIsNotAReport()
    {
        ReportLog log = Log();
        log.Post("   ");

        Assert.Null(log.Current);
    }

    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        return session;
    }

    [Fact]
    public void CopyingSaysHowMuch()
    {
        ReportLog log = Log();

        ClipboardActions.Copy(Session(), log);

        Assert.Equal("Copied all of Object 1, 512 voxels.", log.Current?.Text);
    }

    [Fact]
    public void PastingWithNothingCopiedSaysSo()
    {
        ReportLog log = Log();

        ClipboardActions.Paste(Session(), new FlyCamera(), log);

        Assert.Equal(ReportKind.Warning, log.Current?.Kind);
    }

    /// <summary>A cut of the last object copies it and keeps it; the report has to say so, or it looks like a cut.</summary>
    [Fact]
    public void CuttingTheLastObjectSaysItStays()
    {
        ReportLog log = Log();

        ClipboardActions.Cut(Session(), log);

        Assert.Contains("stays", log.Current?.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatWillNotOpenIsAnErrorReport()
    {
        var project = new ProjectController(Session(), () => { });
        string missing = Path.Combine(Path.GetTempPath(), $"no-such-level-{Guid.NewGuid():N}.vxlevel");

        project.OpenRecent(missing);

        Assert.Contains(
            ReportLog.Shared.Recent,
            report => report.Kind == ReportKind.Error && report.Text.Contains(Path.GetFileName(missing), StringComparison.Ordinal));
    }
}

/// <summary>The Overlays list: each line switches its own overlay, and nothing else.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class OverlaysMenuTests : IDisposable
{
    private readonly ImGuiHarness _ui = new();

    public void Dispose() => _ui.Dispose();

    [Fact]
    public void TheFirstLineIsTheGridAndSwitchesOnlyIt()
    {
        bool grid = true;
        bool measurements = true;
        bool statistics = false;
        bool lights = true;
        bool planes = true;

        var view = new ViewActions
        {
            FrameLevel = () => { },
            FrameFocused = () => { },
            LookAtCenter = () => { },
            ResetCamera = () => { },
            Camera = new FlyCamera(),
            GridVisible = () => grid,
            ToggleGrid = () => grid = !grid,
            MeasurementsVisible = () => measurements,
            ToggleMeasurements = () => measurements = !measurements,
            StatisticsVisible = () => statistics,
            ToggleStatistics = () => statistics = !statistics,
            LightIconsVisible = () => lights,
            ToggleLightIcons = () => lights = !lights,
            MirrorPlanesVisible = () => planes,
            ToggleMirrorPlanes = () => planes = !planes,
            Lighting = new SceneLighting(),
        };

        void Draw() => OverlaysMenu.DrawItems(view);
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        // Down the list until something switches: the title and the rule above the lines do nothing.
        for (float y = 20f; y < 200f && grid; y += 3f)
        {
            _ui.Click(new Vector2(40f, y), Draw);
        }

        Assert.False(grid);
        Assert.True(measurements);
        Assert.False(statistics);
        Assert.True(lights);
        Assert.True(planes);
    }
}
