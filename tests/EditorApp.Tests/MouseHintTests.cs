using EditorApp.Core.Editing;
using EditorApp.Ui;

namespace EditorApp.Tests;

/// <summary>What the status bar's hint strip says, for the combinations that change it.</summary>
public class MouseHintTests
{
    private static IReadOnlyList<MouseHint> Hints(
        EditorTool tool,
        bool looking = false,
        bool shift = false,
        bool control = false,
        bool alt = false,
        bool nothingSelected = false)
    {
        // A new level opens with its cube selected, so the tools have something to work on.
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        session.ActiveTool = tool;
        if (nothingSelected)
        {
            session.DeselectAll();
        }

        return MouseHints.For(session, looking, shift, control, alt);
    }

    /// <summary>The tools that write voxels reach only what is selected, and say so when that is nothing.</summary>
    [Theory]
    [InlineData(EditorTool.Extrude)]
    [InlineData(EditorTool.Paint)]
    [InlineData(EditorTool.LoopCut)]
    public void AVoxelToolWithNothingSelectedSaysHowToSelect(EditorTool tool)
    {
        Assert.StartsWith("Nothing selected", Left(Hints(tool, nothingSelected: true)));
        Assert.DoesNotContain("Nothing selected", Left(Hints(tool)));
    }

    [Fact]
    public void TheSelectToolSaysWhatShiftAndCtrlDo()
    {
        Assert.Equal("Select, drag for a box", Left(Hints(EditorTool.Select)));
        Assert.Equal("Add to the selection", Left(Hints(EditorTool.Select, shift: true)));
        Assert.Equal("Take from the selection", Left(Hints(EditorTool.Select, control: true)));
    }

    private static string? Left(IReadOnlyList<MouseHint> hints) =>
        hints.FirstOrDefault(hint => hint.Icon == Icons.MouseLeft).Action;

    [Theory]
    [InlineData(EditorTool.Transform)]
    [InlineData(EditorTool.Extrude)]
    [InlineData(EditorTool.Paint)]
    [InlineData(EditorTool.LoopCut)]
    [InlineData(EditorTool.View)]
    public void TheWaysToMoveTheViewAreAlwaysListed(EditorTool tool)
    {
        IReadOnlyList<MouseHint> hints = Hints(tool);

        Assert.Contains(hints, hint => hint.Icon == Icons.MouseMiddle && hint.Keys.Length == 0 && hint.Action == "Orbit");
        Assert.Contains(hints, hint => hint.Icon == Icons.MouseMiddle && hint.Keys == "Shift" && hint.Action == "Pan");
        Assert.Contains(hints, hint => hint.Icon == Icons.MouseWheel && hint.Action == "Zoom");
    }

    [Theory]
    [InlineData(EditorTool.Transform)]
    [InlineData(EditorTool.Extrude)]
    [InlineData(EditorTool.Paint)]
    [InlineData(EditorTool.LoopCut)]
    public void EveryEditingToolSaysWhatTheLeftButtonDoes(EditorTool tool) =>
        Assert.False(string.IsNullOrEmpty(Left(Hints(tool))));

    [Fact]
    public void TheViewToolHasNothingForTheLeftButton() =>
        Assert.Null(Left(Hints(EditorTool.View)));

    [Fact]
    public void HoldingAModifierChangesWhatTheLeftButtonSays()
    {
        string? plain = Left(Hints(EditorTool.Extrude));

        Assert.Equal("Add to selection", Left(Hints(EditorTool.Extrude, shift: true)));
        Assert.Equal("Remove from selection", Left(Hints(EditorTool.Extrude, alt: true)));
        Assert.NotEqual(plain, Left(Hints(EditorTool.Extrude, shift: true)));

        Assert.Equal("Pick a colour", Left(Hints(EditorTool.Paint, alt: true)));
        Assert.Equal("Paint a box", Left(Hints(EditorTool.Paint, control: true)));
        Assert.Equal("Paint a line", Left(Hints(EditorTool.Paint, shift: true)));
    }

    /// <summary>Ctrl turns the wheel into the brush size in Paint, and only there.</summary>
    [Fact]
    public void CtrlTurnsTheWheelIntoTheBrushSizeOnlyInPaint()
    {
        Assert.Contains(Hints(EditorTool.Paint, control: true), hint => hint.Icon == Icons.MouseWheel && hint.Action == "Brush size");
        Assert.Contains(Hints(EditorTool.Extrude, control: true), hint => hint.Icon == Icons.MouseWheel && hint.Action == "Zoom");
    }

    [Fact]
    public void WhileLookingTheKeysThatFlyAreShownInstead()
    {
        IReadOnlyList<MouseHint> hints = Hints(EditorTool.Extrude, looking: true);

        Assert.All(hints, hint => Assert.Null(hint.Icon));
        Assert.Contains(hints, hint => hint.Action == "Fly");
    }
}
