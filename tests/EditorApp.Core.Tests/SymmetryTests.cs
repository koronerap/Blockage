using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Live symmetry: every write Paint or Extrude makes is repeated across the mirror planes, in the
/// same undo step, and the planes stay where they were put while the model grows.
/// </summary>
public class SymmetryTests
{
    private const byte Base = 20;
    private const byte Ink = 90;

    /// <summary>A 4 x 2 x 3 block, x from 0 to 3 — its X plane falls between x = 1 and x = 2.</summary>
    private static EditorSession Block()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 3; z++)
        {
            grid.SetVoxel(x, y, z, Base);
        }

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "block");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.ActiveColorIndex = Ink;
        return session;
    }

    private static VoxelWorld Grid(EditorSession session) => session.Scene.Objects[0].Grid;

    // ---- The mirror arithmetic ------------------------------------------------------------------

    [Fact]
    public void AnImageOfAnImageIsTheCellItself()
    {
        var image = new MirrorImage(true, false, true, new Int3(3, 0, 7));
        var cell = new Int3(-2, 5, 9);

        Assert.Equal(cell, image.Cell(image.Cell(cell)));
        Assert.Equal(new Int3(5, 5, -2), image.Cell(cell));
    }

    [Fact]
    public void OnlyFacesAcrossAFlippedAxisTurnRound()
    {
        var image = new MirrorImage(true, false, false, Int3.Zero);

        Assert.Equal(Face.NegX, image.Face(Face.PosX));
        Assert.Equal(Face.PosX, image.Face(Face.NegX));
        Assert.Equal(Face.PosY, image.Face(Face.PosY));
        Assert.Equal(Face.NegZ, image.Face(Face.NegZ));
    }

    [Fact]
    public void ThePlaneGoesThroughTheMiddleOfTheObject()
    {
        EditorSession session = Block();
        session.Symmetry.X = true;

        Assert.Equal(new Int3(3, 1, 2), session.Symmetry.SumsFor(session.Scene.Objects[0]));
        Assert.Equal(2f, session.Symmetry.PlanesFor(session.Scene.Objects[0]).X);
    }

    [Theory]
    [InlineData(true, false, false, 1)]
    [InlineData(true, false, true, 3)]
    [InlineData(true, true, true, 7)]
    [InlineData(false, false, false, 0)]
    public void EveryCombinationOfTheAxesThatAreOnIsAnImage(bool x, bool y, bool z, int expected)
    {
        EditorSession session = Block();
        session.Symmetry.X = x;
        session.Symmetry.Y = y;
        session.Symmetry.Z = z;

        Assert.Equal(expected, session.Symmetry.ImagesFor(session.Scene.Objects[0]).Count);
    }

    /// <summary>Planes that followed the bounds would wander off as soon as one side grew.</summary>
    [Fact]
    public void ThePlaneStaysPutWhileTheObjectGrowsUntilRecentred()
    {
        EditorSession session = Block();
        VoxelObject block = session.Scene.Objects[0];
        session.Symmetry.X = true;
        Int3 sums = session.Symmetry.SumsFor(block);

        block.Grid.SetVoxel(9, 0, 0, Base);
        Assert.Equal(sums, session.Symmetry.SumsFor(block));

        session.Symmetry.Recentre(block);
        Assert.Equal(9, session.Symmetry.SumsFor(block).X);
    }

    [Fact]
    public void ComingOnFromOffPutsThePlanesThroughWhatIsThereNow()
    {
        EditorSession session = Block();
        VoxelObject block = session.Scene.Objects[0];
        session.Symmetry.X = true;
        session.Symmetry.SumsFor(block);
        block.Grid.SetVoxel(9, 0, 0, Base);

        // Another axis while on does not move anything...
        session.Symmetry.Z = true;
        Assert.Equal(3, session.Symmetry.SumsFor(block).X);

        // ...but everything off and back on starts over.
        session.Symmetry.X = false;
        session.Symmetry.Z = false;
        session.Symmetry.X = true;
        Assert.Equal(9, session.Symmetry.SumsFor(block).X);
    }

    // ---- Paint -------------------------------------------------------------------------------------

    [Fact]
    public void ABrushDabIsRepeatedOnTheOtherSide()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Paint;
        session.Symmetry.X = true;

        session.Paint(new RaycastHit(new Int3(0, 1, 0), Face.PosY, 1f));
        session.EndStroke();

        Assert.Equal(Ink, Grid(session).GetFaceColor(new Int3(0, 1, 0), Face.PosY));
        Assert.Equal(Ink, Grid(session).GetFaceColor(new Int3(3, 1, 0), Face.PosY));
        Assert.Equal(Base, Grid(session).GetFaceColor(new Int3(1, 1, 0), Face.PosY));
    }

    /// <summary>A side face mirrors to the opposite side face: the left of the model to its right.</summary>
    [Fact]
    public void ASideFaceMirrorsToTheOppositeSide()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Paint;
        session.Symmetry.X = true;

        session.Paint(new RaycastHit(new Int3(0, 1, 1), Face.NegX, 1f));
        session.EndStroke();

        Assert.Equal(Ink, Grid(session).GetFaceColor(new Int3(3, 1, 1), Face.PosX));
        Assert.Equal(Base, Grid(session).GetFaceColor(new Int3(3, 1, 1), Face.NegX));
    }

    [Fact]
    public void BothSidesComeBackInOneUndo()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Paint;
        session.Symmetry.X = true;
        ulong before = Grid(session).ContentHash();

        session.Paint(new RaycastHit(new Int3(0, 1, 0), Face.PosY, 1f));
        session.Paint(new RaycastHit(new Int3(0, 1, 2), Face.PosY, 1f));
        session.EndStroke();
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();

        Assert.Equal(before, Grid(session).ContentHash());
    }

    /// <summary>
    /// With the cursor's side already done, only the mirror image changes. That is still a change,
    /// and has to reach history like any other.
    /// </summary>
    [Fact]
    public void AnEditThatOnlyChangesTheMirroredSideIsStillAnUndoStep()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Paint;
        session.PaintMode = PaintMode.Bucket;
        Grid(session).SetFaceColor(new Int3(0, 1, 0), Face.PosY, Ink);
        session.Symmetry.X = true;

        Assert.True(session.PaintShape(new Int3(0, 1, 0), new Int3(0, 1, 0), Face.PosY, asBox: true));

        Assert.Equal(Ink, Grid(session).GetFaceColor(new Int3(3, 1, 0), Face.PosY));
        Assert.True(session.History.CanUndo);
    }

    // ---- Extrude ------------------------------------------------------------------------------------

    private static void SelectFrontOfLeftColumn(EditorSession session) =>
        session.SetSelection(FaceSelection.Box(Grid(session), Face.PosZ, 2, new Int3(0, 0, 2), new Int3(0, 1, 2)));

    [Fact]
    public void AnExtrudeIsRepeatedOnTheOtherSide()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Extrude;
        session.Symmetry.X = true;
        SelectFrontOfLeftColumn(session);

        session.PreviewExtrude(2);
        Assert.True(session.ConfirmExtrude());

        foreach (int z in new[] { 3, 4 })
        {
            Assert.True(Grid(session).IsSolid(new Int3(0, 0, z)));
            Assert.True(Grid(session).IsSolid(new Int3(3, 1, z)));
            Assert.False(Grid(session).IsSolid(new Int3(1, 0, z)));
        }

        Assert.Equal(24 + 8, Grid(session).SolidCount);

        session.Undo();
        Assert.Equal(24, Grid(session).SolidCount);
    }

    [Fact]
    public void AnExtrudedNewObjectTakesTheMirroredVoxelsWithIt()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Extrude;
        session.ExtrudeCreatesObject = true;
        session.Symmetry.X = true;
        SelectFrontOfLeftColumn(session);

        session.PreviewExtrude(1);
        Assert.True(session.ConfirmExtrude());

        VoxelObject created = session.Scene.Objects[1];
        Assert.Equal(4, created.Grid.SolidCount);
        Assert.True(created.Grid.IsSolid(new Int3(3, 0, 3)));
        Assert.Equal(24, Grid(session).SolidCount);
    }

    /// <summary>
    /// Pushing in removes voxels, and removing a voxel forgets what was painted on its faces. Undo has
    /// to bring them back painted — on the side the cursor was on, and on the mirrored side the
    /// operation never saw.
    /// </summary>
    [Fact]
    public void APushInGivesBackPaintedFacesOnBothSides()
    {
        EditorSession session = Block();
        Grid(session).SetFaceColor(new Int3(0, 1, 2), Face.PosZ, 55);
        Grid(session).SetFaceColor(new Int3(3, 1, 2), Face.PosZ, 66);
        session.ActiveTool = EditorTool.Extrude;
        session.Symmetry.X = true;
        SelectFrontOfLeftColumn(session);

        session.PreviewExtrude(-1);
        Assert.True(session.ConfirmExtrude());
        Assert.False(Grid(session).IsSolid(new Int3(3, 1, 2)));

        session.Undo();

        Assert.Equal(55, Grid(session).GetFaceColor(new Int3(0, 1, 2), Face.PosZ));
        Assert.Equal(66, Grid(session).GetFaceColor(new Int3(3, 1, 2), Face.PosZ));
    }

    [Fact]
    public void WithSymmetryOffNothingIsMirrored()
    {
        EditorSession session = Block();
        session.ActiveTool = EditorTool.Extrude;
        SelectFrontOfLeftColumn(session);

        session.PreviewExtrude(1);
        session.ConfirmExtrude();

        Assert.False(Grid(session).IsSolid(new Int3(3, 0, 3)));
        Assert.Equal(26, Grid(session).SolidCount);
    }

    /// <summary>Turning an object is not a symmetric edit, and a loop cut is not either.</summary>
    [Fact]
    public void ToolsSymmetryIsNotForAreLeftAlone()
    {
        EditorSession session = Block();
        session.Symmetry.X = true;
        ulong before = Grid(session).ContentHash();

        session.FlipFocus(Axis.X);
        session.FlipFocus(Axis.X);

        Assert.Equal(before, Grid(session).ContentHash());
    }

    [Fact]
    public void ANewLevelForgetsThePlanes()
    {
        EditorSession session = Block();
        session.Symmetry.X = true;
        session.Symmetry.SumsFor(session.Scene.Objects[0]);

        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);

        // Same id, different object: the planes are the new one's, not what was kept for the old.
        VoxelObject cube = session.Scene.Objects[0];
        Assert.True(cube.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(min + max, session.Symmetry.SumsFor(cube));
    }
}
