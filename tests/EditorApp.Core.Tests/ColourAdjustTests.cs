using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Recolouring what is selected (Fullreleaseplan 4.3): replacing a colour, and shifting hue, saturation and value.</summary>
public class ColourAdjustTests
{
    private const byte Red = 30;
    private const byte Blue = 90;

    /// <summary>A 3 × 1 × 1 bar: red, red, blue, with one of the reds painted blue on top; selected.</summary>
    private static (EditorSession Session, VoxelObject Bar) Bar()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Red);
        grid.SetVoxel(1, 0, 0, Red);
        grid.SetVoxel(2, 0, 0, Blue);
        grid.SetFaceColor(new Int3(1, 0, 0), Face.PosY, Blue);

        var scene = new VoxelScene();
        VoxelObject bar = scene.Add(grid, ObjectTransform.Identity, "Bar");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return (session, bar);
    }

    [Fact]
    public void AHueShiftTurnsAColourRoundTheCircle()
    {
        Color32 red = new(255, 0, 0);

        Color32 green = ColourMath.Shift(red, 120f, 0f, 0f);

        Assert.True(green.G > 250 && green.R < 5 && green.B < 5);
        Assert.Equal(red, ColourMath.Shift(red, 360f, 0f, 0f));
        Assert.Equal(new Color32(0, 0, 0), ColourMath.Shift(red, 0f, 0f, -1f));
    }

    [Fact]
    public void ReplaceChangesTheColourEverywhereItIsPaintedFacesToo()
    {
        (EditorSession session, VoxelObject bar) = Bar();

        Assert.Equal(2, session.ReplaceColour(Blue, Red));

        Assert.Equal(Red, bar.Grid.GetVoxel(new Int3(2, 0, 0)));
        Assert.Equal(Red, bar.Grid.GetFaceColor(new Int3(1, 0, 0), Face.PosY));
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(Blue, bar.Grid.GetVoxel(new Int3(2, 0, 0)));
        Assert.Equal(Blue, bar.Grid.GetFaceColor(new Int3(1, 0, 0), Face.PosY));
    }

    [Fact]
    public void ReplacingKeepsAPaintedFaceOfAnotherColour()
    {
        (EditorSession session, VoxelObject bar) = Bar();

        session.ReplaceColour(Red, 12);

        Assert.Equal(12, bar.Grid.GetVoxel(new Int3(1, 0, 0)));
        Assert.Equal(Blue, bar.Grid.GetFaceColor(new Int3(1, 0, 0), Face.PosY));
    }

    [Fact]
    public void AShiftToANewColourClaimsASlotAndUndoesAsOneStep()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        int free = session.Scene.Palette.FreeCustomSlots;

        Assert.True(session.ShiftColours(17f, 0f, 0f) > 0);

        byte shifted = bar.Grid.GetVoxel(new Int3(0, 0, 0));
        Assert.NotEqual(Red, shifted);
        Assert.True(session.Scene.Palette.FreeCustomSlots < free);
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(Red, bar.Grid.GetVoxel(new Int3(0, 0, 0)));
        Assert.Equal(free, session.Scene.Palette.FreeCustomSlots);
    }

    [Fact]
    public void InEditModeOnlyTheChosenVoxelsChange()
    {
        (EditorSession session, VoxelObject bar) = Bar();
        session.EnterEditMode();
        session.SelectVoxels([new Int3(0, 0, 0)], SelectionOperation.Replace);

        session.ReplaceColour(Red, 12);

        Assert.Equal(12, bar.Grid.GetVoxel(new Int3(0, 0, 0)));
        Assert.Equal(Red, bar.Grid.GetVoxel(new Int3(1, 0, 0)));
    }
}
