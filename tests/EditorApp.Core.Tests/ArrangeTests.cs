using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Align and distribute (Fullreleaseplan 6.6).</summary>
public class ArrangeTests
{
    private static VoxelWorld Cube(int size)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        for (int z = 0; z < size; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        return grid;
    }

    private static (EditorSession Session, VoxelObject A, VoxelObject B, VoxelObject C) Three()
    {
        var session = new EditorSession();
        session.ReplaceWorld(Cube(2), projectPath: null);
        VoxelObject a = session.Scene.Objects[0];
        VoxelObject b = session.Scene.Add(Cube(4), ObjectTransform.At(new Vector3(1f, 3f, 0f)), "B");
        VoxelObject c = session.Scene.Add(Cube(2), ObjectTransform.At(new Vector3(20f, -2f, 5f)), "C");
        session.SelectMany([a.Id, b.Id, c.Id], SelectionOperation.Replace);
        session.ChooseObject(a.Id);
        session.SelectMany([b.Id, c.Id], SelectionOperation.Add);
        return (session, a, b, c);
    }

    private static float MinX(VoxelObject o) => o.TryGetWorldBounds(out Vector3 min, out _) ? min.X : float.NaN;

    [Fact]
    public void EdgesLineUpWithTheActiveObject()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        Assert.Equal(a.Id, session.ActiveId);

        Assert.Equal(2, session.AlignSelected(Axis.X, AlignEdge.Min));

        Assert.Equal(MinX(a), MinX(b), 3);
        Assert.Equal(MinX(a), MinX(c), 3);
        Assert.Equal(3f, b.Transform.Position.Y);

        session.Undo();
        Assert.Equal(1f, b.Transform.Position.X);
        Assert.Equal(20f, c.Transform.Position.X);
    }

    [Fact]
    public void MiddlesAreSpacedEvenlyBetweenTheFurthestApart()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();

        Assert.Equal(1, session.DistributeSelected(Axis.X));

        // a's middle is 1, c's is 21: b's goes half way.
        Assert.True(b.TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(11f, (min.X + max.X) * 0.5f, 3);
        Assert.Equal(0f, a.Transform.Position.X);
        Assert.Equal(20f, c.Transform.Position.X);
    }

    [Fact]
    public void AChildGoesWithItsParentAndIsNotMovedTwice()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c) = Three();
        session.Scene.SetParent(c.Id, b.Id);
        Vector3 offset = c.Transform.Position - b.Transform.Position;

        session.AlignSelected(Axis.X, AlignEdge.Max);

        Assert.True(Vector3.Distance(offset, c.Transform.Position - b.Transform.Position) < 1e-4f);
    }
}
