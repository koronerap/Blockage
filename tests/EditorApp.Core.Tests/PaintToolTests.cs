using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
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
    public void RadiusZeroPaintsExactlyOneVoxel()
    {
        VoxelWorld world = SolidCube(3);
        var command = new VoxelEditCommand("paint", world);

        int changed = PaintOperations.Brush(new Int3(1, 2, 1), 0f, 9, command);

        Assert.Equal(1, changed);
        Assert.Equal(9, world.GetVoxel(1, 2, 1));
        Assert.Equal(5, world.GetVoxel(0, 2, 1));
    }

    [Fact]
    public void BrushIsEuclideanNotACube()
    {
        // A cube brush of radius 1 would take all 27 cells; a sphere takes the 7-cell plus shape,
        // and here the centre is buried, so only the 6 face neighbours qualify.
        VoxelWorld world = SolidCube(3);
        var command = new VoxelEditCommand("paint", world);

        PaintOperations.Brush(new Int3(1, 1, 1), 1f, 9, command);

        Assert.Equal(9, world.GetVoxel(0, 1, 1));       // distance 1, visible
        Assert.Equal(5, world.GetVoxel(0, 0, 1));       // distance sqrt(2), outside the radius
        Assert.Equal(5, world.GetVoxel(1, 1, 1));       // the centre itself is buried
    }

    [Fact]
    public void PaintNeverCreatesOrRemovesVoxels()
    {
        VoxelWorld world = SolidCube(2);
        int before = world.SolidCount;

        var command = new VoxelEditCommand("paint", world);
        PaintOperations.Brush(new Int3(0, 0, 0), 4f, 12, command);

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
        int changed = PaintOperations.Bucket(Int3.Zero, 8, 0, command);

        Assert.Equal(2, changed);
        Assert.Equal(8, world.GetVoxel(1, 0, 0));
        Assert.Equal(4, world.GetVoxel(2, 0, 0));
        Assert.Equal(3, world.GetVoxel(3, 0, 0));
        Assert.Equal(3, world.GetVoxel(50, 0, 0));
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
        Assert.Equal(1, PaintOperations.Bucket(Int3.Zero, 20, 0, exact));
        exact.Undo();

        var loose = new VoxelEditCommand("loose", world);
        Assert.Equal(2, PaintOperations.Bucket(Int3.Zero, 20, 8, loose));
        Assert.Equal(12, world.GetVoxel(2, 0, 0));
    }

    [Fact]
    public void BucketDoesNotSpreadThroughBuriedVoxels()
    {
        // Two exposed shells joined only through the inside of a solid block: fill must not tunnel.
        VoxelWorld world = SolidCube(4, index: 3);

        var command = new VoxelEditCommand("bucket", world);
        PaintOperations.Bucket(Int3.Zero, 9, 0, command);

        Assert.Equal(9, world.GetVoxel(0, 0, 0));
        Assert.Equal(3, world.GetVoxel(1, 1, 1));    // interior untouched
        Assert.Equal(3, world.GetVoxel(2, 2, 2));
    }

    [Fact]
    public void SampleReturnsNullOnEmptySpace()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 42);

        Assert.Equal((byte)42, PaintOperations.Sample(world, Int3.Zero));
        Assert.Null(PaintOperations.Sample(world, new Int3(5, 5, 5)));
    }
}

public class PaintSessionTests
{
    private static EditorSession SessionWithPlate(byte index = 5)
    {
        var session = new EditorSession { ActiveTool = EditorTool.Paint };
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
        Assert.Equal(20, session.World.GetVoxel(4, 0, 0));

        session.Undo();
        Assert.Equal(5, session.World.GetVoxel(4, 0, 0));
    }

    [Fact]
    public void BucketModePaintsTheWholeConnectedSurface()
    {
        EditorSession session = SessionWithPlate();
        session.PaintMode = PaintMode.Bucket;
        session.ActiveColorIndex = 30;

        session.Paint(new RaycastHit(new Int3(2, 0, 2), Face.PosY, 1f));
        session.EndStroke();

        Assert.Equal(30, session.World.GetVoxel(0, 0, 0));
        Assert.Equal(30, session.World.GetVoxel(4, 0, 4));
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
