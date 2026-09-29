using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The scatter (Fullreleaseplan 6.7): copies set down on the ground, kept apart, the same way for the same seed.</summary>
public class ScatterTests
{
    private static (EditorSession Session, VoxelObject Ground, VoxelObject Rock) Level()
    {
        var ground = new VoxelWorld();
        for (int x = 0; x < 24; x++)
        for (int z = 0; z < 24; z++)
        {
            ground.SetVoxel(x, 0, z, 8);
        }

        var rock = new VoxelWorld();
        rock.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        rock.SetVoxel(1, 0, 0, Palette.WhiteIndex);
        rock.SetVoxel(0, 1, 0, Palette.WhiteIndex);

        var session = new EditorSession();
        session.ReplaceWorld(ground, projectPath: null);
        VoxelObject g = session.Scene.Objects[0];
        VoxelObject r = session.Scene.Add(rock, ObjectTransform.At(new Vector3(40f, 5f, 40f)), "Rock");

        // What to scatter first, the ground last: the active one.
        session.ChooseObject(r.Id);
        session.SelectMany([g.Id], SelectionOperation.Add);
        session.ChooseObject(g.Id);
        session.SelectMany([r.Id], SelectionOperation.Add);
        return (session, g, r);
    }

    [Fact]
    public void CopiesStandOnTheGroundApartAndLinked()
    {
        (EditorSession session, VoxelObject ground, VoxelObject rock) = Level();
        Assert.Null(session.ScatterProblem());

        IReadOnlyList<VoxelObject> copies = session.Scatter(new ScatterSettings(Count: 12, Spacing: 4f));

        Assert.Equal(12, copies.Count);
        foreach (VoxelObject copy in copies)
        {
            Assert.Same(rock.Grid, copy.Grid);
            Assert.True(copy.TryGetWorldBounds(out Vector3 min, out Vector3 max));
            Assert.Equal(1f, min.Y, 3);
            Assert.InRange((min.X + max.X) * 0.5f, 0f, 24f);
            Assert.InRange((min.Z + max.Z) * 0.5f, 0f, 24f);
        }

        for (int i = 0; i < copies.Count; i++)
        for (int j = i + 1; j < copies.Count; j++)
        {
            Assert.True(Vector3.Distance(Foot(copies[i]), Foot(copies[j])) >= 4f - 1e-3f);
        }

        session.Undo();
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    private static Vector3 Foot(VoxelObject o)
    {
        o.TryGetWorldBounds(out Vector3 min, out Vector3 max);
        return new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
    }

    [Fact]
    public void TheSameSeedScattersTheSameWay()
    {
        (EditorSession first, _, _) = Level();
        (EditorSession second, _, _) = Level();

        var a = first.Scatter(new ScatterSettings(Count: 8, Seed: 5)).Select(o => o.Transform).ToList();
        var b = second.Scatter(new ScatterSettings(Count: 8, Seed: 5)).Select(o => o.Transform).ToList();
        (EditorSession third, _, _) = Level();
        var c = third.Scatter(new ScatterSettings(Count: 8, Seed: 6)).Select(o => o.Transform).ToList();

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void AdjustingScattersAgainInItsPlace()
    {
        (EditorSession session, VoxelObject ground, _) = Level();
        session.Scatter(new ScatterSettings(Count: 10, Spacing: 2f));
        Assert.True(session.CanAdjustScatter);

        IReadOnlyList<VoxelObject> again = session.AdjustScatter(new ScatterSettings(Count: 3, Spacing: 2f, Linked: false));

        Assert.Equal(3, again.Count);
        Assert.Equal(2 + 3, session.Scene.Objects.Count);
        Assert.All(again, copy => Assert.NotSame(ground.Grid, copy.Grid));

        session.Undo();
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    [Fact]
    public void WithoutAGroundOrSomethingToScatterItSaysWhy()
    {
        var session = new EditorSession();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 1);
        session.ReplaceWorld(grid, projectPath: null);
        session.ChooseObject(session.Scene.Objects[0].Id);

        Assert.NotNull(session.ScatterProblem());
        Assert.Empty(session.Scatter(new ScatterSettings()));
    }
}
