using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class PaintOperationsTests
{
    private static VoxelWorld SolidCube(int side, byte index = 5)
    {
        var world = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    world.SetVoxel(x, y, z, index);
                }
            }
        }

        return world;
    }

    [Fact]
    public void BuriedVoxelsAreNotVisible()
    {
        VoxelWorld world = SolidCube(3);

        Assert.True(PaintOperations.IsVisible(world, new Int3(0, 0, 0)));
        Assert.False(PaintOperations.IsVisible(world, new Int3(1, 1, 1)));
        Assert.False(PaintOperations.IsVisible(world, new Int3(9, 9, 9)));
    }

    [Fact]
    public void RadiusZeroPaintsExactlyOneFace()
    {
        VoxelWorld world = SolidCube(3);
        var command = new VoxelEditCommand("paint", world);

        int changed = PaintOperations.Brush(new Int3(1, 2, 1), Face.PosY, 0f, 9, command);

        Assert.Equal(1, changed);
        Assert.Equal(9, world.GetFaceColor(new Int3(1, 2, 1), Face.PosY));

        // The rest of that voxel keeps its own colour — the whole point of painting a face.
        Assert.Equal(5, world.GetFaceColor(new Int3(1, 2, 1), Face.PosX));
        Assert.Equal(5, world.GetVoxel(1, 2, 1));
    }

    [Fact]
    public void AnEdgeVoxelCanCarryADifferentColourOnEachSide()
    {
        VoxelWorld world = SolidCube(2);
        var corner = new Int3(0, 0, 0);

        var command = new VoxelEditCommand("paint", world);
        command.ApplyFace(corner, Face.NegX, 20);
        command.ApplyFace(corner, Face.NegY, 30);
        command.ApplyFace(corner, Face.NegZ, 40);

        Assert.Equal(20, world.GetFaceColor(corner, Face.NegX));
        Assert.Equal(30, world.GetFaceColor(corner, Face.NegY));
        Assert.Equal(40, world.GetFaceColor(corner, Face.NegZ));

        command.Undo();
        for (int f = 0; f < FaceInfo.Count; f++)
        {
            Assert.Equal(5, world.GetFaceColor(corner, (Face)f));
        }
    }

    [Fact]
    public void BrushOnlyPaintsFacesPointingTheSameWay()
    {
        // A brush aimed at the top of a block must not wrap onto its sides.
        VoxelWorld world = SolidCube(3);
        var command = new VoxelEditCommand("paint", world);

        PaintOperations.Brush(new Int3(1, 2, 1), Face.PosY, 2f, 9, command);

        Assert.Equal(9, world.GetFaceColor(new Int3(0, 2, 0), Face.PosY));
        Assert.Equal(5, world.GetFaceColor(new Int3(0, 2, 0), Face.NegX));
    }

    [Fact]
    public void BrushIsEuclideanNotACube()
    {
        VoxelWorld world = SolidCube(5);
        var command = new VoxelEditCommand("paint", world);

        PaintOperations.Brush(new Int3(2, 4, 2), Face.PosY, 1f, 9, command);

        Assert.Equal(9, world.GetFaceColor(new Int3(1, 4, 2), Face.PosY));   // distance 1
        Assert.Equal(5, world.GetFaceColor(new Int3(1, 4, 1), Face.PosY));   // distance sqrt(2)
    }

    [Fact]
    public void PaintNeverCreatesOrRemovesVoxels()
    {
        VoxelWorld world = SolidCube(2);
        int before = world.SolidCount;

        var command = new VoxelEditCommand("paint", world);
        PaintOperations.Brush(new Int3(0, 0, 0), Face.NegY, 4f, 12, command);

        Assert.Equal(before, world.SolidCount);
        Assert.True(world.Chunks.Count <= 1);
    }

    [Fact]
    public void BucketFillsTheConnectedRunOfMatchingColour()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 5; x++)
        {
            world.SetVoxel(x, 0, 0, 3);
        }

        world.SetVoxel(2, 0, 0, 4);            // splits the run
        world.SetVoxel(50, 0, 0, 3);           // same colour, disconnected

        var command = new VoxelEditCommand("bucket", world);
        int changed = PaintOperations.Bucket(Int3.Zero, Face.PosY, 8, 0, command);

        Assert.Equal(2, changed);
        Assert.Equal(8, world.GetFaceColor(new Int3(1, 0, 0), Face.PosY));
        Assert.Equal(4, world.GetFaceColor(new Int3(2, 0, 0), Face.PosY));
        Assert.Equal(3, world.GetFaceColor(new Int3(3, 0, 0), Face.PosY));
        Assert.Equal(3, world.GetFaceColor(new Int3(50, 0, 0), Face.PosY));
    }

    [Fact]
    public void BucketThresholdReachesNearbyColours()
    {
        var world = new VoxelWorld();
        world.Palette[10] = new Color32(100, 100, 100);
        world.Palette[11] = new Color32(105, 100, 100);   // 5 away
        world.Palette[12] = new Color32(200, 100, 100);   // far away

        world.SetVoxel(0, 0, 0, 10);
        world.SetVoxel(1, 0, 0, 11);
        world.SetVoxel(2, 0, 0, 12);

        var exact = new VoxelEditCommand("exact", world);
        Assert.Equal(1, PaintOperations.Bucket(Int3.Zero, Face.PosY, 20, 0, exact));
        exact.Undo();

        var loose = new VoxelEditCommand("loose", world);
        Assert.Equal(2, PaintOperations.Bucket(Int3.Zero, Face.PosY, 20, 8, loose));
        Assert.Equal(12, world.GetFaceColor(new Int3(2, 0, 0), Face.PosY));
    }

    [Fact]
    public void BucketStaysOnTheSurfaceItStartedOn()
    {
        // A fill on the top of a cube must not turn the corner onto its sides.
        VoxelWorld world = SolidCube(4, index: 3);

        var command = new VoxelEditCommand("bucket", world);
        int changed = PaintOperations.Bucket(new Int3(0, 3, 0), Face.PosY, 9, 0, command);

        Assert.Equal(16, changed);   // the 4x4 top, and nothing else
        Assert.Equal(9, world.GetFaceColor(new Int3(3, 3, 3), Face.PosY));
        Assert.Equal(3, world.GetFaceColor(new Int3(0, 3, 0), Face.NegX));
    }

    [Fact]
    public void SampleReadsTheFaceUnderTheCursor()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 42);

        var command = new VoxelEditCommand("paint", world);
        command.ApplyFace(Int3.Zero, Face.PosY, 77);

        Assert.Equal((byte)77, PaintOperations.Sample(world, Int3.Zero, Face.PosY));
        Assert.Equal((byte)42, PaintOperations.Sample(world, Int3.Zero, Face.PosX));
        Assert.Null(PaintOperations.Sample(world, new Int3(5, 5, 5), Face.PosY));
    }

    [Fact]
    public void RecolouringAVoxelForgetsWhatWasPaintedOnIt()
    {
        // The overrides described the colour it used to be.
        VoxelWorld world = SolidCube(2);
        var command = new VoxelEditCommand("paint", world);
        command.ApplyFace(Int3.Zero, Face.NegY, 60);

        world.SetVoxel(0, 0, 0, 7);

        Assert.Equal(7, world.GetFaceColor(Int3.Zero, Face.NegY));
    }

    /// <summary>What the preview draws is what the stroke paints: the same cells, face for face.</summary>
    [Fact]
    public void TheBrushPaintsExactlyTheCellsItPreviews()
    {
        VoxelWorld world = SolidCube(9);
        var centre = new Int3(4, 8, 4);

        List<Int3> previewed = PaintOperations.BrushCells(world, centre, Face.PosY, 2.5f);

        var command = new VoxelEditCommand("paint", world);
        int painted = PaintOperations.Brush(centre, Face.PosY, 2.5f, 9, command);

        Assert.Equal(previewed.Count, painted);
        Assert.All(previewed, cell => Assert.Equal(9, world.GetFaceColor(cell, Face.PosY)));
    }

    /// <summary>
    /// Round on a flat surface: the disc of radius three has its cells at (2, 2) but not the square's
    /// corners at (3, 3), nor (3, 1), which is further than three away.
    /// </summary>
    [Fact]
    public void TheBrushIsADiscOnAFlatFace()
    {
        VoxelWorld world = SolidCube(9);
        var centre = new Int3(4, 8, 4);

        var cells = new HashSet<Int3>(PaintOperations.BrushCells(world, centre, Face.PosY, 3f));

        Assert.Contains(centre + new Int3(2, 0, 2), cells);
        Assert.Contains(centre + new Int3(3, 0, 0), cells);
        Assert.DoesNotContain(centre + new Int3(3, 0, 1), cells);
        Assert.DoesNotContain(centre + new Int3(3, 0, 3), cells);
        Assert.Equal(29, cells.Count);
    }

    /// <summary>Only faces on the outside: the brush reaching into the block below paints nothing there.</summary>
    [Fact]
    public void TheBrushCellsAreAllOnTheSurface()
    {
        VoxelWorld world = SolidCube(9);

        List<Int3> cells = PaintOperations.BrushCells(world, new Int3(4, 8, 4), Face.PosY, 3f);

        Assert.All(cells, cell => Assert.Equal(8, cell.Y));
    }
}

public class PaintSessionTests
{
    private static EditorSession SessionWithPlate(byte index = 5)
    {
        var session = new EditorSession { ActiveTool = EditorTool.Paint };
        session.Scene.Add(new VoxelWorld(), ObjectTransform.Identity);
        for (int x = 0; x < 5; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                session.World.SetVoxel(x, 0, z, index);
            }
        }

        session.HasUnsavedChanges = false;
        return session;
    }

    [Fact]
    public void AWholeStrokeIsOneUndoStep()
    {
        EditorSession session = SessionWithPlate();
        session.ActiveColorIndex = 20;

        session.BeginStroke();
        for (int x = 0; x < 5; x++)
        {
            session.Paint(new RaycastHit(new Int3(x, 0, 0), Face.PosY, 1f));
        }

        session.EndStroke();

        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(20, session.World.GetFaceColor(new Int3(4, 0, 0), Face.PosY));

        session.Undo();
        Assert.Equal(5, session.World.GetFaceColor(new Int3(4, 0, 0), Face.PosY));
    }

    [Fact]
    public void BucketModePaintsTheWholeConnectedSurface()
    {
        EditorSession session = SessionWithPlate();
        session.PaintMode = PaintMode.Bucket;
        session.ActiveColorIndex = 30;

        session.Paint(new RaycastHit(new Int3(2, 0, 2), Face.PosY, 1f));
        session.EndStroke();

        Assert.Equal(30, session.World.GetFaceColor(new Int3(0, 0, 0), Face.PosY));
        Assert.Equal(30, session.World.GetFaceColor(new Int3(4, 0, 4), Face.PosY));
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void EyedropperAdoptsTheColourAndWritesNothing()
    {
        EditorSession session = SessionWithPlate(index: 77);
        session.ActiveColorIndex = 1;
        ulong before = session.World.ContentHash();

        Assert.True(session.SampleColor(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f)));

        Assert.Equal(77, session.ActiveColorIndex);
        Assert.Equal(before, session.World.ContentHash());
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void PaintingAnUnchangedColourCreatesNoHistory()
    {
        EditorSession session = SessionWithPlate(index: 5);
        session.ActiveColorIndex = 5;

        session.BeginStroke();
        session.Paint(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f));
        session.EndStroke();

        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void TransformIsTheDefaultTool()
    {
        Assert.Equal(EditorTool.Transform, new EditorSession().ActiveTool);
    }
}
