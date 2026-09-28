using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class LoopCutTests
{
    private static VoxelWorld Bar(int length, byte index = 5)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < length; x++)
        {
            grid.SetVoxel(x, 0, 0, index);
        }

        return grid;
    }

    private static VoxelWorld Cube(int side, byte index = 5)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, index);
                }
            }
        }

        return grid;
    }

    private static (EditorSession Session, VoxelObject Target) SessionWith(VoxelWorld grid, ObjectTransform? transform = null)
    {
        var session = new EditorSession { ActiveTool = EditorTool.LoopCut };
        var scene = new VoxelScene();
        VoxelObject target = scene.Add(grid, transform ?? ObjectTransform.Identity, "whole");
        session.ReplaceScene(scene, projectPath: null);
        return (session, target);
    }

    [Fact]
    public void SplitDividesAtThePlaneAndKeepsOriginalCoordinates()
    {
        // Both halves keeping their coordinates is what lets them share one transform, so nothing
        // moves on screen at the moment of the cut.
        VoxelWorld grid = Bar(6);

        (VoxelWorld low, VoxelWorld high) = LoopCut.Split(grid, new CutPlane(Axis.X, 3));

        Assert.Equal(3, low.SolidCount);
        Assert.Equal(3, high.SolidCount);
        Assert.True(low.IsSolid(2, 0, 0));
        Assert.False(low.IsSolid(3, 0, 0));
        Assert.True(high.IsSolid(3, 0, 0));
        Assert.True(high.IsSolid(5, 0, 0));
    }

    [Fact]
    public void SplitHalvesShareTheScenePalette()
    {
        VoxelWorld grid = Bar(4);
        grid.Palette[5] = new Color32(7, 8, 9);

        (VoxelWorld low, VoxelWorld high) = LoopCut.Split(grid, new CutPlane(Axis.X, 2));

        Assert.Same(grid.Palette, low.Palette);
        Assert.Same(grid.Palette, high.Palette);
    }

    [Fact]
    public void NearestPlaneSnapsToTheGridBoundaryUnderTheCursor()
    {
        (_, VoxelObject target) = SessionWith(Cube(8));

        // Y and Z sit squarely between two boundaries (0.5 away), X is only 0.4 from one.
        CutPlane? plane = LoopCut.FindNearestPlane(target, new Vector3(3.4f, 2.5f, 2.5f));

        Assert.NotNull(plane);
        Assert.Equal(Axis.X, plane!.Value.Axis);
        Assert.Equal(3, plane.Value.Coordinate);
    }

    [Fact]
    public void NearestPlanePicksWhicheverAxisIsClosest()
    {
        (_, VoxelObject target) = SessionWith(Cube(8));

        // Nearly on the Y = 5 boundary, well away from any X or Z one.
        CutPlane? plane = LoopCut.FindNearestPlane(target, new Vector3(3.5f, 5.02f, 2.5f));

        Assert.Equal(Axis.Y, plane!.Value.Axis);
        Assert.Equal(5, plane.Value.Coordinate);
    }

    [Fact]
    public void PlanesAtTheOuterEdgeAreNeverOffered()
    {
        // A plane on the object's own boundary would leave nothing on one side.
        (_, VoxelObject target) = SessionWith(Cube(4));

        CutPlane? plane = LoopCut.FindNearestPlane(target, new Vector3(-5f, -5f, -5f));

        Assert.NotNull(plane);
        Assert.InRange(plane!.Value.Coordinate, 1, 3);
    }

    [Fact]
    public void AnObjectOneVoxelThickCannotBeCutOnThatAxis()
    {
        (_, VoxelObject target) = SessionWith(Bar(5));

        // Y and Z are one voxel deep, so only an X plane can divide this.
        CutPlane? plane = LoopCut.FindNearestPlane(target, new Vector3(2.5f, 0.5f, 0.5f));

        Assert.Equal(Axis.X, plane!.Value.Axis);
    }

    [Fact]
    public void CuttingProducesTwoObjectsWithTheSameTransform()
    {
        var transform = new ObjectTransform(
            new Vector3(4f, 1f, -2f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f));

        (EditorSession session, VoxelObject target) = SessionWith(Bar(6), transform);

        Assert.True(session.ApplyLoopCut(new CutPlane(Axis.X, 3)));

        Assert.Equal(2, session.Scene.Objects.Count);
        foreach (VoxelObject half in session.Scene.Objects)
        {
            Assert.Equal(transform, half.Transform);
        }

        Assert.DoesNotContain(session.Scene.Objects, o => o.Id == target.Id);
    }

    [Fact]
    public void CuttingMovesNothingVisually()
    {
        // Every voxel has to end up in exactly the same world position it was in before the cut.
        var transform = new ObjectTransform(
            new Vector3(3f, 0f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 3f));

        (EditorSession session, _) = SessionWith(Cube(4), transform);
        Assert.True(session.Scene.TryGetWorldBounds(out Vector3 beforeMin, out Vector3 beforeMax));

        session.ApplyLoopCut(new CutPlane(Axis.X, 2));

        Assert.True(session.Scene.TryGetWorldBounds(out Vector3 afterMin, out Vector3 afterMax));
        Assert.True(Vector3.Distance(beforeMin, afterMin) < 1e-4f);
        Assert.True(Vector3.Distance(beforeMax, afterMax) < 1e-4f);
    }

    [Fact]
    public void CuttingIsOneUndoStepAndRestoresTheOriginalObject()
    {
        (EditorSession session, VoxelObject target) = SessionWith(Bar(6));
        ulong before = session.Scene.ContentHash();

        session.ApplyLoopCut(new CutPlane(Axis.X, 3));
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();

        Assert.Single(session.Scene.Objects);
        Assert.Equal(target.Id, session.Scene.Objects[0].Id);
        Assert.Equal(before, session.Scene.ContentHash());

        session.Redo();
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    [Fact]
    public void ACutThatWouldEmptyOneSideIsRefused()
    {
        // A plane through a gap would produce an object with nothing in it.
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        grid.SetVoxel(1, 0, 0, 5);

        (EditorSession session, _) = SessionWith(grid);

        Assert.False(session.ApplyLoopCut(new CutPlane(Axis.Y, 1)));
        Assert.Single(session.Scene.Objects);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void CuttingTwiceGivesThreeObjects()
    {
        (EditorSession session, _) = SessionWith(Bar(9));

        session.ApplyLoopCut(new CutPlane(Axis.X, 3));
        Assert.True(session.ApplyLoopCut(new CutPlane(Axis.X, 6)));

        Assert.Equal(3, session.Scene.Objects.Count);
        Assert.Equal(9, session.Scene.SolidCount);
    }

    [Fact]
    public void TheHalfUnderTheCursorTakesFocus()
    {
        (EditorSession session, _) = SessionWith(Bar(6));

        session.ApplyLoopCut(new CutPlane(Axis.X, 3));

        Assert.NotNull(session.Scene.Focus);
        Assert.Contains(session.Scene.Objects, o => o.Id == session.Scene.FocusId);
    }
    /// <summary>A four by four floor with a one-voxel post standing on its corner.</summary>
    private static VoxelWorld FloorWithPost()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 4; z++)
            {
                grid.SetVoxel(x, 0, z, 5);
            }
        }

        for (int y = 1; y < 4; y++)
        {
            grid.SetVoxel(0, y, 0, 5);
        }

        return grid;
    }

    /// <summary>
    /// A cut through the post is a cut through one voxel, however wide the floor below it: the
    /// section is what the plane actually goes through, not the bounds.
    /// </summary>
    [Fact]
    public void TheCrossSectionIsOnlyWhatThePlaneGoesThrough()
    {
        HashSet<Int3> section = LoopCut.CrossSection(FloorWithPost(), new CutPlane(Axis.Y, 2));

        Assert.Equal([new Int3(0, 1, 0)], section);
    }

    [Fact]
    public void ACutAcrossTheFloorGoesThroughAWholeRow()
    {
        HashSet<Int3> section = LoopCut.CrossSection(FloorWithPost(), new CutPlane(Axis.X, 2));

        // The cells just below the plane, which are the ones whose far faces it opens.
        Assert.Equal(4, section.Count);
        Assert.All(section, cell => Assert.Equal(1, cell.X));
        Assert.All(section, cell => Assert.Equal(0, cell.Y));
    }

    /// <summary>
    /// Between two parts that never touch, the plane cuts nothing but still divides them — so what is
    /// shown is where they come apart.
    /// </summary>
    [Fact]
    public void BetweenPartsThatDoNotTouchTheSectionIsTheirFootprint()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        grid.SetVoxel(3, 0, 0, 5);
        grid.SetVoxel(3, 1, 0, 5);

        HashSet<Int3> section = LoopCut.CrossSection(grid, new CutPlane(Axis.X, 3));

        Assert.Equal(2, section.Count);
        Assert.Contains(new Int3(2, 0, 0), section);
        Assert.Contains(new Int3(2, 1, 0), section);
    }

    /// <summary>
    /// A step: two rows high below the plane, one row across it. Only the lower row goes through the
    /// plane; the upper one ends at it and is not cut.
    /// </summary>
    [Fact]
    public void OnlyWhereBothSidesAreSolidIsCut()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 4; z++)
            {
                grid.SetVoxel(x, 0, z, 5);
                if (x < 2)
                {
                    grid.SetVoxel(x, 1, z, 5);
                }
            }
        }

        HashSet<Int3> section = LoopCut.CrossSection(grid, new CutPlane(Axis.X, 2));

        Assert.Equal(4, section.Count);
        Assert.All(section, cell => Assert.Equal(0, cell.Y));
    }

    /// <summary>
    /// A part across the plane in a chunk of its own, with nothing in the chunk beside it on this
    /// side: its footprint is still found, from the chunk it is in.
    /// </summary>
    [Fact]
    public void APartAcrossThePlaneInAChunkOfItsOwnIsFound()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        grid.SetVoxel(Chunk.Size, Chunk.Size + 8, 0, 5);

        HashSet<Int3> section = LoopCut.CrossSection(grid, new CutPlane(Axis.X, Chunk.Size));

        Assert.Equal([new Int3(Chunk.Size - 1, Chunk.Size + 8, 0)], section);
    }

    /// <summary>A plane on a chunk boundary still finds the cells on both sides of it.</summary>
    [Fact]
    public void ASectionOnAChunkBoundaryIsFound()
    {
        VoxelWorld grid = Bar(40);

        HashSet<Int3> section = LoopCut.CrossSection(grid, new CutPlane(Axis.X, Chunk.Size));

        Assert.Equal([new Int3(Chunk.Size - 1, 0, 0)], section);
    }
}
