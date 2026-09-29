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
    public void CuttingTheLastObjectTakesItAndSaysSo()
    {
        ReportLog log = Log();
        EditorSession session = Session();

        ClipboardActions.Cut(session, log);

        Assert.Empty(session.Scene.Objects);
        Assert.StartsWith("Cut Object 1", log.Current?.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyingNothingSelectedSaysToSelectFirst()
    {
        ReportLog log = Log();
        EditorSession session = Session();
        session.DeselectAll();

        ClipboardActions.Copy(session, log);

        Assert.Contains("select something", log.Current?.Text, StringComparison.Ordinal);
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
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(420f, 520f) };
    private readonly ViewportSettings _viewport = new();
    private bool _statistics;

    public void Dispose() => _ui.Dispose();

    private ViewActions View() => new()
    {
        FrameLevel = () => { },
        FrameFocused = () => { },
        LookAtCenter = () => { },
        ResetCamera = () => { },
        Camera = new FlyCamera(),
        GridVisible = () => _viewport.Grid,
        ToggleGrid = () => _viewport.Grid = !_viewport.Grid,
        MeasurementsVisible = () => _viewport.Measurements,
        ToggleMeasurements = () => _viewport.Measurements = !_viewport.Measurements,
        SidebarVisible = () => true,
        ToggleSidebar = () => { },
        StatisticsVisible = () => _statistics,
        ToggleStatistics = () => _statistics = !_statistics,
        LightIconsVisible = () => _viewport.LightIcons,
        ToggleLightIcons = () => _viewport.LightIcons = !_viewport.LightIcons,
        MirrorPlanesVisible = () => _viewport.MirrorPlanes,
        ToggleMirrorPlanes = () => _viewport.MirrorPlanes = !_viewport.MirrorPlanes,
        Lighting = new SceneLighting(),
        Viewport = _viewport,
    };

    [Fact]
    public void TheFirstLineIsTheFloorAndSwitchesOnlyIt()
    {
        ViewActions view = View();
        void Draw() => OverlaysMenu.DrawItems(view);
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        // Down the list until something switches: the titles above the lines do nothing.
        for (float y = 20f; y < 200f && _viewport.Grid; y += 3f)
        {
            _ui.Click(new Vector2(40f, y), Draw);
        }

        Assert.False(_viewport.Grid);
        Assert.True(_viewport.Measurements);
        Assert.True(_viewport.TextInfo);
        Assert.False(_statistics);
        Assert.True(_viewport.LightIcons);
        Assert.True(_viewport.AxisX);
    }

    /// <summary>With the header's switch off, the list is shown but greyed: nothing in it switches.</summary>
    [Fact]
    public void WithOverlaysOffNothingInTheListSwitches()
    {
        _viewport.Overlays = false;
        ViewActions view = View();
        void Draw() => OverlaysMenu.DrawItems(view);
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        for (float y = 20f; y < 400f; y += 6f)
        {
            _ui.Click(new Vector2(40f, y), Draw);
        }

        Assert.True(_viewport.Grid);
        Assert.True(_viewport.Measurements);
        Assert.False(_viewport.Origins);
        Assert.False(_viewport.Wireframe);
    }
}

/// <summary>The right-hand end of the header: gizmos, overlays, X-Ray and the three shadings.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class ViewportHeaderTests : IDisposable
{
    private readonly ImGuiHarness _ui = new() { WindowSize = new Vector2(600f, 80f) };
    private readonly ViewportSettings _viewport = new();

    public void Dispose() => _ui.Dispose();

    private ViewActions View() => new()
    {
        FrameLevel = () => { },
        FrameFocused = () => { },
        LookAtCenter = () => { },
        ResetCamera = () => { },
        Camera = new FlyCamera(),
        GridVisible = () => true,
        ToggleGrid = () => { },
        MeasurementsVisible = () => true,
        ToggleMeasurements = () => { },
        SidebarVisible = () => true,
        ToggleSidebar = () => { },
        StatisticsVisible = () => false,
        ToggleStatistics = () => { },
        LightIconsVisible = () => true,
        ToggleLightIcons = () => { },
        MirrorPlanesVisible = () => true,
        ToggleMirrorPlanes = () => { },
        Lighting = new SceneLighting(),
        Viewport = _viewport,
    };

    /// <summary>Each button in the row, left to right, by its centre: found by clicking along it.</summary>
    [Fact]
    public void TheButtonsDoWhatTheyShow()
    {
        ViewActions view = View();
        void Draw() => ViewportHeader.Draw(view, 24f);
        _ui.Frame(Draw);
        _ui.Frame(Draw);

        float button = 24f;
        float x = 20f + 10f;   // the harness window's corner and padding
        float y = 20f + 8f + (button * 0.5f);

        // Gizmos, then its popover's narrow button, then Overlays.
        _ui.Click(new Vector2(x + (button * 0.5f), y), Draw);
        Assert.False(_viewport.Gizmos);

        float overlays = x + button + 1f + (button * 0.6f) + 5f;
        _ui.Click(new Vector2(overlays + (button * 0.5f), y), Draw);
        Assert.False(_viewport.Overlays);

        float xray = overlays + button + 1f + (button * 0.6f) + 10f;
        _ui.Click(new Vector2(xray + (button * 0.5f), y), Draw);
        Assert.True(_viewport.XRay);

        float wire = xray + button + 10f;
        _ui.Click(new Vector2(wire + (button * 0.5f), y), Draw);
        Assert.Equal(ShadingMode.Wireframe, _viewport.Shading);

        _ui.Click(new Vector2(wire + button + 1f + (button * 0.5f), y), Draw);
        Assert.Equal(ShadingMode.Unlit, _viewport.Shading);

        _ui.Click(new Vector2(wire + ((button + 1f) * 2f) + (button * 0.5f), y), Draw);
        Assert.Equal(ShadingMode.Lit, _viewport.Shading);
    }

    [Fact]
    public void TheWidthCoversEveryButton() =>
        Assert.Equal((24f * 1.6f + 1f) * 2f + 5f + 10f + 24f + 10f + (24f * 3f) + 3f + (24f * 0.6f), ViewportHeader.Width(24f), 3);
}
