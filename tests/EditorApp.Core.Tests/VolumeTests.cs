using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Booleans, volume filters and resampling (Fullreleaseplan 3.4–3.6).</summary>
public class VolumeTests
{
    private static VoxelWorld Box(int sx, int sy, int sz, byte colour = Palette.WhiteIndex)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < sx; x++)
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        {
            grid.SetVoxel(x, y, z, colour);
        }

        return grid;
    }

    /// <summary>A 4³ block, and a 2³ one over its corner at (3, 3, 3); the block is active, both selected.</summary>
    private static (EditorSession Session, VoxelObject Block, VoxelObject Tool) Pair(float toolVoxelSize = 1f)
    {
        var scene = new VoxelScene();
        VoxelObject block = scene.Add(Box(4, 4, 4), ObjectTransform.Identity, "Block");
        VoxelObject tool = scene.Add(Box(2, 2, 2, 30), new ObjectTransform(new Vector3(3f, 3f, 3f), Quaternion.Identity, toolVoxelSize), "Tool");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.ClickSelect(tool.Id);
        session.ClickSelect(block.Id, extend: true);
        return (session, block, tool);
    }

    [Fact]
    public void UnionAddsTheOthersVolumeAndTakesThemAway()
    {
        (EditorSession session, VoxelObject block, _) = Pair();

        Assert.Equal(1, session.BooleanSelected(BooleanOperation.Union, out _));

        // 64 + 8, less the one voxel they shared.
        Assert.Equal(71, block.Grid.SolidCount);
        Assert.Equal(30, block.Grid.GetVoxel(new Int3(4, 4, 4)));
        Assert.Single(session.Scene.Objects);

        session.Undo();
        Assert.Equal(64, block.Grid.SolidCount);
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    [Fact]
    public void DifferenceCutsTheOthersShapeOut()
    {
        (EditorSession session, VoxelObject block, _) = Pair();
        session.BooleanKeepsOthers = true;

        session.BooleanSelected(BooleanOperation.Difference, out _);

        Assert.Equal(63, block.Grid.SolidCount);
        Assert.False(block.Grid.IsSolid(new Int3(3, 3, 3)));
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    [Fact]
    public void IntersectKeepsOnlyWhatIsInsideTheOther()
    {
        (EditorSession session, VoxelObject block, _) = Pair();

        session.BooleanSelected(BooleanOperation.Intersect, out _);

        Assert.Equal(1, block.Grid.SolidCount);
        Assert.True(block.Grid.IsSolid(new Int3(3, 3, 3)));
    }

    [Fact]
    public void AnObjectOfAnotherVoxelSizeIsResampledWithoutHoles()
    {
        // The tool's voxels are twice the size: its 2³ covers 4³ of the block's lattice.
        (EditorSession session, VoxelObject block, _) = Pair(toolVoxelSize: 2f);

        session.BooleanSelected(BooleanOperation.Union, out _);

        // (3..6)³ is 64 cells, one of them inside the block already.
        Assert.Equal(64 + 63, block.Grid.SolidCount);
        Assert.True(block.Grid.IsSolid(new Int3(6, 6, 6)));
    }

    [Fact]
    public void ABooleanNeedsSomethingToUseAndAnObjectToChange()
    {
        (EditorSession session, VoxelObject block, _) = Pair();
        session.ClickSelect(block.Id);

        Assert.Equal(0, session.BooleanSelected(BooleanOperation.Union, out string? problem));
        Assert.NotNull(problem);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void HollowKeepsAWallOfTheThicknessAsked()
    {
        (EditorSession session, VoxelObject block, VoxelObject tool) = Pair();
        session.ClickSelect(block.Id);
        session.HollowThickness = 1;

        Assert.Equal(1, session.HollowSelected());

        // The 2³ middle of a 4³ block is emptied.
        Assert.Equal(56, block.Grid.SolidCount);
        Assert.False(block.Grid.IsSolid(new Int3(1, 1, 1)));
        Assert.Equal(8, tool.Grid.SolidCount);
    }

    [Fact]
    public void ThickenAndThinAreOneVoxelEachWay()
    {
        (EditorSession session, VoxelObject block, _) = Pair();
        session.ClickSelect(block.Id);

        session.ThinSelected();
        Assert.Equal(8, block.Grid.SolidCount);

        session.ThickenSelected();
        Assert.True(block.Grid.IsSolid(new Int3(0, 1, 1)));
        Assert.Equal(8 + (6 * 4), block.Grid.SolidCount);
    }

    [Fact]
    public void LoosePiecesSmallerThanTheMinimumAreCleared()
    {
        (EditorSession session, VoxelObject block, _) = Pair();
        block.Grid.SetVoxel(10, 0, 0, Palette.WhiteIndex);
        block.Grid.SetVoxel(11, 0, 0, Palette.WhiteIndex);
        session.ClickSelect(block.Id);
        session.LooseMinimum = 3;

        session.RemoveLooseSelected();

        Assert.False(block.Grid.IsSolid(new Int3(10, 0, 0)));
        Assert.Equal(64, block.Grid.SolidCount);
    }

    [Fact]
    public void HalvingIsSubdividesReverseAndKeepsTheObjectsPlace()
    {
        (EditorSession session, VoxelObject block, _) = Pair();
        session.ClickSelect(block.Id);
        Assert.True(block.TryGetWorldBounds(out Vector3 beforeMin, out Vector3 beforeMax));

        Assert.Equal(1, session.HalveSelected());

        Assert.Equal(8, block.Grid.SolidCount);
        Assert.Equal(2f, block.VoxelSize);
        Assert.True(block.TryGetWorldBounds(out Vector3 afterMin, out Vector3 afterMax));
        Assert.Equal(beforeMin, afterMin);
        Assert.Equal(beforeMax, afterMax);

        session.Undo();
        Assert.Equal(64, block.Grid.SolidCount);
        Assert.Equal(1f, block.VoxelSize);
    }

    [Fact]
    public void ScalingMultipliesTheModelInVoxelsOfTheSameSize()
    {
        (EditorSession session, VoxelObject block, _) = Pair();
        session.ClickSelect(block.Id);
        session.ScaleFactor = 2f;

        session.ScaleSelected();

        Assert.Equal(512, block.Grid.SolidCount);
        Assert.Equal(1f, block.VoxelSize);
    }
}
