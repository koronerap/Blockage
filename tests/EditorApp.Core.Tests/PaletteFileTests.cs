using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Palettes in and out, and ramps (Fullreleaseplan 4.4).</summary>
public class PaletteFileTests
{
    private static readonly Color32[] Three = [new(255, 0, 64), new(10, 200, 30), new(0, 0, 255)];

    [Fact]
    public void AGimpPaletteIsReadPastItsHeaderAndComments()
    {
        const string gpl = "GIMP Palette\nName: Test\nColumns: 4\n#\n255   0  64\tRed\n 10 200  30 Green\n# a comment\n  0   0 255\n";

        Assert.Equal(Three, PaletteFiles.ReadGpl(gpl));
        Assert.Equal(Three, PaletteFiles.ReadGpl(PaletteFiles.WriteGpl(Three, "Again")));
    }

    [Fact]
    public void AHexListIsReadWithOrWithoutHashes()
    {
        Assert.Equal(Three, PaletteFiles.ReadHex("ff0040\n#0ac81e\r\n0000FF\n\nnot a colour\n"));
        Assert.Equal(Three, PaletteFiles.ReadHex(PaletteFiles.WriteHex(Three)));
    }

    [Fact]
    public void AnImageGivesItsColoursOnceEachInReadingOrder()
    {
        byte[] strip = PaletteFiles.WritePng([.. Three, Three[0]]);

        Assert.Equal(Three, PaletteFiles.ReadPng(strip));
    }

    private static EditorSession Session()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 1);
        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "One");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return session;
    }

    [Fact]
    public void LoadingAPaletteRecoloursWhatWasPaintedAsOneStep()
    {
        EditorSession session = Session();
        Color32 before = session.Scene.Palette[1];

        Assert.Equal(3, session.ImportPalette(Three, replace: true));

        Assert.Equal(Three[0], session.Scene.Palette[1]);
        Assert.Equal(Three[2], session.Scene.Palette[3]);
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(before, session.Scene.Palette[1]);
    }

    [Fact]
    public void AddingKeepsNewColoursInCustomAndSkipsOnesThePaletteHas()
    {
        EditorSession session = Session();
        int free = session.Scene.Palette.FreeCustomSlots;
        Color32 already = session.Scene.Palette[Palette.WhiteIndex];

        Assert.Equal(3, session.ImportPalette([.. Three, already], replace: false));

        Assert.Equal(free - 3, session.Scene.Palette.FreeCustomSlots);
        Assert.NotNull(session.Scene.Palette.FindExact(Three[1]));
        Assert.Equal(3, session.Scene.Palette.SavedCustomCount);
    }

    [Fact]
    public void ARampKeepsTheColoursBetweenTheTwoEnds()
    {
        EditorSession session = Session();
        session.ActiveColorIndex = 30;
        session.SecondaryColorIndex = 90;

        // Two hues far apart: the ends are in the palette already, the three between are new.
        Assert.Equal(3, session.AddRamp(5));
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void WritingOutTakesTheLibraryAndTheKeptColoursOnly()
    {
        EditorSession session = Session();
        int before = session.PaletteColours().Count;

        session.ImportPalette(Three, replace: false);

        Assert.Equal(before + 3, session.PaletteColours().Count);
    }
}
