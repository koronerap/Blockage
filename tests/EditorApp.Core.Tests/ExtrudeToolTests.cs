using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class FaceSelectionTests
{
    private static VoxelWorld Slab(int width = 4, int depth = 3, byte index = 5)
    {
        var world = new VoxelWorld();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                world.SetVoxel(x, 0, z, index);
            }
        }

        return world;
    }

    [Fact]
    public void OnlyExposedFacesCanBeSelected()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, 1);
        world.SetVoxel(0, 1, 0, 1);   // covers the first voxel's top face

        FaceSelection selection = FaceSelection.Box(
            world, Face.PosY, plane: 0, new Int3(0, 0, 0), new Int3(0, 0, 0));

        Assert.True(selection.IsEmpty);
        Assert.False(FaceSelection.IsFaceExposed(world, new Int3(0, 0, 0), Face.PosY));
        Assert.True(FaceSelection.IsFaceExposed(world, new Int3(0, 1, 0), Face.PosY));
    }

    [Fact]
    public void BoxSelectionFlattensOntoTheFacePlane()
    {
        VoxelWorld world = Slab();

        // Corners given at different heights still land on the plane of the face.
        FaceSelection selection = FaceSelection.Box(
            world, Face.PosY, plane: 0, new Int3(0, 9, 0), new Int3(2, -4, 1));

        Assert.Equal(6, selection.Count);
        Assert.Equal(Face.PosY, selection.Direction);
        Assert.Equal(0, selection.Plane);
        Assert.True(selection.Contains(new Int3(2, 0, 1)));
        Assert.False(selection.Contains(new Int3(3, 0, 0)));
    }

    [Fact]
    public void BoxSelectionSkipsEmptyCellsInsideTheRectangle()
    {
        VoxelWorld world = Slab();
        world.SetVoxel(1, 0, 1, Palette.EmptyIndex);

        FaceSelection selection = FaceSelection.Box(
            world, Face.PosY, 0, new Int3(0, 0, 0), new Int3(3, 0, 2));

        Assert.Equal(11, selection.Count);
        Assert.False(selection.Contains(new Int3(1, 0, 1)));
    }

    [Fact]
    public void ConnectedPatchTakesTheWholeFlatSurface()
    {
        VoxelWorld world = Slab(5, 5);

        FaceSelection selection = FaceSelection.ConnectedPatch(world, new Int3(2, 0, 2), Face.PosY);

        Assert.Equal(25, selection.Count);
    }

    [Fact]
    public void ConnectedPatchStopsAtCoveredCellsAndDoesNotLeaveThePlane()
    {
        VoxelWorld world = Slab(5, 1);
        world.SetVoxel(2, 1, 0, 5);       // a lump sitting on the middle of the strip

        FaceSelection selection = FaceSelection.ConnectedPatch(world, new Int3(0, 0, 0), Face.PosY);

        Assert.Equal(2, selection.Count);                       // stopped at the covered cell
        Assert.False(selection.Contains(new Int3(2, 0, 0)));
        Assert.False(selection.Contains(new Int3(2, 1, 0)));    // the lump is in a different plane
    }

    [Fact]
    public void AddCombinesAndSubtractRemoves()
    {
        VoxelWorld world = Slab(4, 1);

        FaceSelection left = FaceSelection.Box(world, Face.PosY, 0, new Int3(0, 0, 0), new Int3(1, 0, 0));
        FaceSelection right = FaceSelection.Box(world, Face.PosY, 0, new Int3(2, 0, 0), new Int3(3, 0, 0));

        FaceSelection both = left.Combine(right, SelectionOperation.Add);
        Assert.Equal(4, both.Count);

        FaceSelection trimmed = both.Combine(right, SelectionOperation.Subtract);
        Assert.Equal(2, trimmed.Count);
        Assert.True(trimmed.Contains(new Int3(0, 0, 0)));
        Assert.False(trimmed.Contains(new Int3(3, 0, 0)));
    }

    [Fact]
    public void CombiningAcrossPlanesOrDirectionsReplacesInstead()
    {
        // There is no sensible single arrow for a mixed set, so the newer selection simply wins.
        VoxelWorld world = Slab(4, 1);
        world.SetVoxel(0, 1, 0, 5);

        FaceSelection top = FaceSelection.Box(world, Face.PosY, 0, new Int3(1, 0, 0), new Int3(3, 0, 0));
        FaceSelection side = FaceSelection.Box(world, Face.PosX, 3, new Int3(3, 0, 0), new Int3(3, 0, 0));

        FaceSelection combined = top.Combine(side, SelectionOperation.Add);

        Assert.Equal(Face.PosX, combined.Direction);
        Assert.Equal(1, combined.Count);
    }

    [Fact]
    public void TranslatingMovesTheSelectionAlongItsOwnAxis()
    {
        VoxelWorld world = Slab(2, 1);
        FaceSelection selection = FaceSelection.Box(world, Face.PosY, 0, new Int3(0, 0, 0), new Int3(1, 0, 0));

        FaceSelection moved = selection.Translated(3);

        Assert.Equal(3, moved.Plane);
        Assert.True(moved.Contains(new Int3(0, 3, 0)));
        Assert.False(moved.Contains(new Int3(0, 0, 0)));
    }
}

public class ExtrudeOperationTests
{
    private static VoxelWorld Plate(int width, int depth, byte index = 5)
    {
        var world = new VoxelWorld();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                world.SetVoxel(x, 0, z, index);
            }
        }

        return world;
    }

    private static FaceSelection TopOf(VoxelWorld world, int width, int depth) =>
        FaceSelection.Box(world, Face.PosY, 0, Int3.Zero, new Int3(width - 1, 0, depth - 1));

    [Fact]
    public void PullingOutAddsVoxelsInTheSourceColour()
    {
        VoxelWorld world = Plate(2, 1);
        world.SetVoxel(0, 0, 0, 11);
        world.SetVoxel(1, 0, 0, 22);

        var command = new VoxelEditCommand("extrude", world);
        int changed = ExtrudeOperation.Apply(TopOf(world, 2, 1), 2, command);

        Assert.Equal(4, changed);
        Assert.Equal(11, world.GetVoxel(0, 1, 0));
        Assert.Equal(11, world.GetVoxel(0, 2, 0));
        Assert.Equal(22, world.GetVoxel(1, 2, 0));

        command.Undo();
        Assert.Equal(2, world.SolidCount);
    }

    [Fact]
    public void PushingInDeletesStartingWithTheSelectedVoxels()
    {
        var world = new VoxelWorld();
        for (int y = 0; y < 4; y++)
        {
            world.SetVoxel(0, y, 0, 5);
        }

        FaceSelection top = FaceSelection.Box(world, Face.PosY, 3, new Int3(0, 3, 0), new Int3(0, 3, 0));

        var command = new VoxelEditCommand("intrude", world);
        ExtrudeOperation.Apply(top, -2, command);

        Assert.Equal(2, world.SolidCount);
        Assert.False(world.IsSolid(0, 3, 0));
        Assert.False(world.IsSolid(0, 2, 0));
        Assert.True(world.IsSolid(0, 1, 0));
    }

    [Fact]
    public void ZeroStepsChangesNothing()
    {
        VoxelWorld world = Plate(3, 3);
        var command = new VoxelEditCommand("none", world);

        Assert.Equal(0, ExtrudeOperation.Apply(TopOf(world, 3, 3), 0, command));
        Assert.True(command.IsEmpty);
    }

    [Fact]
    public void ExtrudingSidewaysWorksTheSameWay()
    {
        VoxelWorld world = Plate(1, 3, index: 8);
        FaceSelection side = FaceSelection.Box(world, Face.PosX, 0, Int3.Zero, new Int3(0, 0, 2));

        var command = new VoxelEditCommand("extrude", world);
        ExtrudeOperation.Apply(side, 3, command);

        Assert.Equal(12, world.SolidCount);
        Assert.Equal(8, world.GetVoxel(3, 0, 2));
    }

    [Fact]
    public void AdvanceLandsOnTheNewlyFormedSurface()
    {
        VoxelWorld world = Plate(2, 2);
        FaceSelection selection = TopOf(world, 2, 2);

        var command = new VoxelEditCommand("extrude", world);
        ExtrudeOperation.Apply(selection, 2, command);
        FaceSelection advanced = ExtrudeOperation.Advance(selection, 2);

        Assert.Equal(2, advanced.Plane);
        foreach (Int3 voxel in advanced.Voxels)
        {
            Assert.True(FaceSelection.IsFaceExposed(world, voxel, Face.PosY));
        }
    }
}

public class ExtrudeSessionTests
{
    private static EditorSession SessionWithPlate(int width = 3, int depth = 3, byte index = 5)
    {
        var session = new EditorSession { ActiveTool = EditorTool.Extrude };
        session.Scene.Add(new VoxelWorld(), ObjectTransform.Identity);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                session.World.SetVoxel(x, 0, z, index);
            }
        }

        session.HasUnsavedChanges = false;
        return session;
    }

    private static void SelectTop(EditorSession session, int width, int depth) =>
        session.SetSelection(FaceSelection.Box(
            session.World, Face.PosY, 0, Int3.Zero, new Int3(width - 1, 0, depth - 1)));

    [Fact]
    public void DraggingPreviewsLiveAndConfirmingKeepsOneUndoStep()
    {
        EditorSession session = SessionWithPlate();
        SelectTop(session, 3, 3);

        session.PreviewExtrude(1);
        Assert.Equal(18, session.World.SolidCount);

        session.PreviewExtrude(3);
        Assert.Equal(36, session.World.SolidCount);
        Assert.True(session.IsExtruding);

        Assert.True(session.ConfirmExtrude());
        Assert.Equal(1, session.History.UndoCount);
        Assert.False(session.IsExtruding);

        session.Undo();
        Assert.Equal(9, session.World.SolidCount);
    }

    [Fact]
    public void DraggingBackAndForthLeavesNothingBehind()
    {
        EditorSession session = SessionWithPlate();
        ulong before = session.World.ContentHash();
        SelectTop(session, 3, 3);

        session.PreviewExtrude(4);
        session.PreviewExtrude(2);
        session.PreviewExtrude(0);

        Assert.Equal(before, session.World.ContentHash());
    }

    [Fact]
    public void CancellingRestoresTheWorldExactly()
    {
        EditorSession session = SessionWithPlate();
        ulong before = session.World.ContentHash();
        SelectTop(session, 3, 3);

        session.PreviewExtrude(5);
        session.CancelExtrude();

        Assert.Equal(before, session.World.ContentHash());
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void ConfirmingAdvancesTheSelectionSoTheNextDragContinues()
    {
        EditorSession session = SessionWithPlate();
        SelectTop(session, 3, 3);

        session.PreviewExtrude(2);
        session.ConfirmExtrude();

        Assert.Equal(2, session.Selection!.Plane);

        session.PreviewExtrude(1);
        session.ConfirmExtrude();

        Assert.Equal(3, session.Selection.Plane);
        Assert.Equal(9 * 4, session.World.SolidCount);
        Assert.Equal(2, session.History.UndoCount);
    }

    [Fact]
    public void PushingInThroughTheSelectionRemovesVolume()
    {
        EditorSession session = SessionWithPlate();
        SelectTop(session, 3, 3);

        session.PreviewExtrude(-1);
        Assert.True(session.ConfirmExtrude());

        Assert.Equal(0, session.World.SolidCount);
        session.Undo();
        Assert.Equal(9, session.World.SolidCount);
    }

    [Fact]
    public void ConfirmingWithoutADragDoesNothing()
    {
        EditorSession session = SessionWithPlate();
        SelectTop(session, 3, 3);

        Assert.False(session.ConfirmExtrude());
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void FaceSubModeSelectsTheWholePatchFromOneHit()
    {
        EditorSession session = SessionWithPlate(4, 4);
        session.ExtrudeSelectionMode = ExtrudeSelectionMode.Face;

        session.SelectPatch(new RaycastHit(new Int3(1, 0, 1), Face.PosY, 1f));

        Assert.Equal(16, session.Selection!.Count);
    }

    [Fact]
    public void UndoDuringADragCancelsItFirst()
    {
        EditorSession session = SessionWithPlate();
        ulong before = session.World.ContentHash();
        SelectTop(session, 3, 3);

        session.PreviewExtrude(3);
        session.Undo();

        // The in-flight preview is rolled back rather than half-committed to history.
        Assert.Equal(before, session.World.ContentHash());
    }

    [Fact]
    public void ANewLevelStartsAsAWhiteCubeWithEveryFaceExtrudable()
    {
        // With no Place tool, an empty world would be a dead end. A cube gives all six directions
        // a real surface from the first click.
        VoxelWorld starter = EditorSession.CreateStarterWorld();
        const int side = EditorSession.StarterCubeSize;

        Assert.Equal(side * side * side, starter.SolidCount);
        Assert.Equal(Palette.WhiteIndex, starter.GetVoxel(0, 0, 0));
        Assert.Equal(new Color32(255, 255, 255), starter.Palette[Palette.WhiteIndex]);

        // Centred on the origin horizontally, resting on the ground plane.
        Assert.True(starter.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(-side / 2, 0, -side / 2), min);
        Assert.Equal(new Int3(side / 2 - 1, side - 1, side / 2 - 1), max);

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            var face = (Face)f;
            Int3 corner = FaceInfo.IsPositive(face) ? max : min;

            Assert.True(
                FaceSelection.IsFaceExposed(starter, corner, face),
                $"{face} has no surface to extrude from.");
        }
    }
}
