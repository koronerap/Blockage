using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Subdivide: every voxel into 2 × 2 × 2 of half the size. Eight times the voxels, the same object in
/// the world, the same colours face by face — and an undo that puts back exactly what was there.
/// </summary>
public class SubdivideTests
{
    private static VoxelWorld Box(Int3 min, Int3 max, byte colour = 5)
    {
        var grid = new VoxelWorld();
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    grid.SetVoxel(x, y, z, colour);
                }
            }
        }

        return grid;
    }

    private static (EditorSession Session, VoxelObject Target) SessionWith(VoxelWorld grid, ObjectTransform? transform = null)
    {
        var scene = new VoxelScene();
        VoxelObject target = scene.Add(grid, transform ?? ObjectTransform.Identity, "block");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return (session, target);
    }

    /// <summary>The corners of the object's box, in the world.</summary>
    private static (Vector3 Min, Vector3 Max) WorldBox(VoxelObject target)
    {
        Assert.True(target.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Vector3 a = target.Transform.TransformPoint(min.ToVector3());
        Vector3 b = target.Transform.TransformPoint((max + Int3.One).ToVector3());
        return (a, b);
    }

    [Fact]
    public void ATwoCubeBecomesAFourCubeTheSameSizeInTheWorld()
    {
        (EditorSession session, VoxelObject cube) = SessionWith(Box(Int3.Zero, new Int3(1, 1, 1)));
        (Vector3 min, Vector3 max) = WorldBox(cube);

        Assert.True(session.SubdivideFocus());

        Assert.Equal(64, cube.Grid.SolidCount);
        Assert.True(cube.Grid.TryGetBounds(out Int3 low, out Int3 high));
        Assert.Equal(Int3.Zero, low);
        Assert.Equal(new Int3(3, 3, 3), high);
        Assert.Equal(0.5f, cube.VoxelSize);

        (Vector3 afterMin, Vector3 afterMax) = WorldBox(cube);
        Assert.Equal(min, afterMin);
        Assert.Equal(max, afterMax);
    }

    /// <summary>Below the origin too: cell -1 becomes -2 and -1, not -2 and -1 shifted to 0.</summary>
    [Fact]
    public void NegativeCellsStayWhereTheyWereOnATurnedMovedObject()
    {
        var placed = new ObjectTransform(new Vector3(3f, -2f, 7f), Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, 0f), 1.5f);
        (EditorSession session, VoxelObject target) = SessionWith(Box(new Int3(-3, -1, -2), new Int3(0, 1, 1)), placed);
        (Vector3 min, Vector3 max) = WorldBox(target);

        Assert.True(session.SubdivideFocus());

        Assert.True(target.Grid.TryGetBounds(out Int3 low, out Int3 high));
        Assert.Equal(new Int3(-6, -2, -4), low);
        Assert.Equal(new Int3(1, 3, 3), high);

        (Vector3 afterMin, Vector3 afterMax) = WorldBox(target);
        Assert.True(Vector3.Distance(min, afterMin) < 1e-4f);
        Assert.True(Vector3.Distance(max, afterMax) < 1e-4f);
    }

    [Fact]
    public void EveryChildTakesItsBlocksColour()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        grid.SetVoxel(1, 0, 0, 9);
        (EditorSession session, VoxelObject target) = SessionWith(grid);

        session.SubdivideFocus();

        Assert.Equal(5, target.Grid.GetVoxel(1, 1, 1));
        Assert.Equal(9, target.Grid.GetVoxel(2, 0, 1));
        Assert.Equal(9, target.Grid.GetVoxel(3, 1, 0));
    }

    /// <summary>
    /// A painted face goes to every new face on that side, and to none inside the block — which would
    /// show up the moment an extrude opened the block.
    /// </summary>
    [Fact]
    public void APaintedFaceCoversTheSameSideAfterwards()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosX, 20);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.NegY, 30);
        (EditorSession session, VoxelObject target) = SessionWith(grid);

        session.SubdivideFocus();

        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                Assert.Equal(20, target.Grid.GetFaceColor(new Int3(1, a, b), Face.PosX));
                Assert.Equal(30, target.Grid.GetFaceColor(new Int3(a, 0, b), Face.NegY));

                // The same children's faces into the block keep the block's colour.
                Assert.Equal(5, target.Grid.GetFaceColor(new Int3(0, a, b), Face.PosX));
                Assert.Equal(5, target.Grid.GetFaceColor(new Int3(a, 1, b), Face.NegY));
            }
        }
    }

    [Fact]
    public void UndoPutsBackExactlyWhatWasThereAndRedoRepeatsIt()
    {
        var grid = Box(new Int3(-2, 0, -1), new Int3(2, 3, 1));
        grid.SetVoxel(0, 3, 0, 9);
        grid.SetFaceColor(new Int3(2, 1, 0), Face.PosX, 20);
        grid.SetFaceColor(new Int3(-2, 0, -1), Face.NegZ, 30);
        (EditorSession session, VoxelObject target) = SessionWith(grid, new ObjectTransform(new Vector3(1f, 2f, 3f), Quaternion.Identity, 0.3f));

        ulong before = target.Grid.ContentHash();
        ObjectTransform placed = target.Transform;
        var painted = target.Grid.Chunks.Values.SelectMany(c => c.FaceOverrides()).ToHashSet();

        session.SubdivideFocus();
        ulong subdivided = target.Grid.ContentHash();
        Assert.Equal("Subdivide", session.History.NextUndoName);

        session.Undo();
        Assert.Equal(before, target.Grid.ContentHash());
        Assert.Equal(placed, target.Transform);
        Assert.Equal(painted, target.Grid.Chunks.Values.SelectMany(c => c.FaceOverrides()).ToHashSet());

        session.Redo();
        Assert.Equal(subdivided, target.Grid.ContentHash());
        Assert.Equal(0.15f, target.VoxelSize, 6);
    }

    /// <summary>
    /// A lone voxel below zero: its children must fold back into it and nowhere else. Rounding
    /// towards zero would put half of them in the block next door and grow a voxel out of nothing.
    /// </summary>
    [Fact]
    public void ALoneVoxelBelowZeroUndoesToItself()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(-3, -5, -7, 9);
        grid.SetFaceColor(new Int3(-3, -5, -7), Face.PosX, 20);
        (EditorSession session, VoxelObject target) = SessionWith(grid);

        session.SubdivideFocus();
        Assert.Equal(8, target.Grid.SolidCount);

        session.Undo();

        Assert.Equal(1, target.Grid.SolidCount);
        Assert.Equal(9, target.Grid.GetVoxel(-3, -5, -7));
        Assert.Equal(20, target.Grid.GetFaceColor(new Int3(-3, -5, -7), Face.PosX));
    }

    /// <summary>Edits made on the finer voxels are undone first, and the subdivide then undoes cleanly.</summary>
    [Fact]
    public void EditsAfterASubdivideUndoBackToTheOriginal()
    {
        (EditorSession session, VoxelObject target) = SessionWith(Box(Int3.Zero, new Int3(2, 2, 2)));
        ulong before = target.Grid.ContentHash();

        session.SubdivideFocus();
        session.SubdivideFocus();
        Assert.Equal(27 * 64, target.Grid.SolidCount);

        session.SelectPatch(new Core.Raycast.RaycastHit(new Int3(5, 11, 5), Face.PosY, 1f));
        session.PreviewExtrude(2);
        session.ConfirmExtrude();

        session.Undo();
        session.Undo();
        session.Undo();

        Assert.Equal(before, target.Grid.ContentHash());
        Assert.Equal(1f, target.VoxelSize);
    }

    [Fact]
    public void TheSelectionIsLetGo()
    {
        (EditorSession session, _) = SessionWith(Box(Int3.Zero, new Int3(1, 1, 1)));
        session.SelectPatch(new Core.Raycast.RaycastHit(new Int3(0, 1, 0), Face.PosY, 1f));

        session.SubdivideFocus();

        Assert.False(session.HasSelection);
    }

    /// <summary>A plane left off-centre on purpose is still where it was, counted in the finer cells.</summary>
    [Fact]
    public void TheMirrorPlanesStayWhereTheyWere()
    {
        (EditorSession session, VoxelObject target) = SessionWith(Box(Int3.Zero, new Int3(3, 1, 1)));
        session.Symmetry.X = true;
        Vector3 planes = session.Symmetry.PlanesFor(target);

        // Fixed now; growing the object on one side does not move it.
        target.Grid.SetVoxel(4, 0, 0, 5);
        Assert.Equal(planes, session.Symmetry.PlanesFor(target));

        session.SubdivideFocus();
        Assert.Equal(planes * 2f, session.Symmetry.PlanesFor(target));

        session.Undo();
        Assert.Equal(planes, session.Symmetry.PlanesFor(target));
    }

    [Fact]
    public void VoxelsAsSmallAsTheyGoAreNotCutFurther()
    {
        (EditorSession session, VoxelObject target) = SessionWith(
            Box(Int3.Zero, Int3.One),
            ObjectTransform.Identity with { VoxelSize = ObjectTransform.MinVoxelSize * 1.5f });

        Assert.NotNull(session.SubdivideProblem(target));
        Assert.False(session.SubdivideFocus());
        Assert.Equal(8, target.Grid.SolidCount);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void AnObjectThatWouldGrowPastTheLimitIsNotCut()
    {
        // Just over an eighth of the limit: one press would take it past.
        int side = (int)MathF.Ceiling(MathF.Cbrt((Subdivide.MaxVoxels / 8f) + 1f));
        (EditorSession session, VoxelObject target) = SessionWith(Box(Int3.Zero, new Int3(side - 1, side - 1, side - 1)));

        Assert.Contains("most", session.SubdivideProblem(target), StringComparison.Ordinal);
        Assert.False(session.SubdivideFocus());
    }

    [Fact]
    public void NothingToCutIsSaid()
    {
        var session = new EditorSession();
        var scene = new VoxelScene();
        scene.Add(new VoxelWorld(), ObjectTransform.Identity, "empty");
        session.ReplaceScene(scene, projectPath: null);

        Assert.NotNull(session.SubdivideProblem(session.Scene.Focus));
        Assert.False(session.SubdivideFocus());
    }
}
