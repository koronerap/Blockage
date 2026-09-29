using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The Sculpt tool's brushes (Fullreleaseplan 3.7), dabbed on a flat plate of ground.</summary>
public class SculptTests
{
    private const byte Grass = 40;

    /// <summary>A 15 × 1 × 15 plate, its top at y = 0, the one object of the level.</summary>
    private static (EditorSession Session, VoxelObject Plate) Plate()
    {
        var grid = new VoxelWorld();
        for (int x = -7; x <= 7; x++)
        {
            for (int z = -7; z <= 7; z++)
            {
                grid.SetVoxel(x, 0, z, Grass);
            }
        }

        var scene = new VoxelScene();
        VoxelObject plate = scene.Add(grid, ObjectTransform.Identity, "Ground");
        var session = new EditorSession { ActiveTool = EditorTool.Sculpt, ActiveColorIndex = 12, SculptRadius = 2f };
        session.ReplaceScene(scene, projectPath: null);
        return (session, plate);
    }

    private static RaycastHit Top(int x = 0, int z = 0) => new(new Int3(x, 0, z), Face.PosY, 1f);

    [Fact]
    public void AddBuildsOnTheSurfaceAndAStrokeIsOneStep()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        int before = plate.Grid.SolidCount;

        session.SculptMode = SculptMode.Add;
        Assert.True(session.Sculpt(Top()));
        session.Sculpt(Top(1, 0));
        session.EndStroke();

        Assert.True(plate.Grid.SolidCount > before);
        Assert.True(plate.Grid.IsSolid(new Int3(0, 1, 0)));
        Assert.Equal(12, plate.Grid.GetVoxel(new Int3(0, 1, 0)));
        Assert.Equal(1, session.History.UndoCount);

        session.Undo();
        Assert.Equal(before, plate.Grid.SolidCount);
    }

    [Fact]
    public void RemoveCarvesIntoTheSurface()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        session.SculptMode = SculptMode.Remove;

        session.Sculpt(Top());
        session.EndStroke();

        Assert.False(plate.Grid.IsSolid(new Int3(0, 0, 0)));
        Assert.False(plate.Grid.IsSolid(new Int3(2, 0, 0)));
        Assert.True(plate.Grid.IsSolid(new Int3(3, 0, 0)));
    }

    [Fact]
    public void RaiseLiftsTheSurfaceOneLayerInItsOwnColour()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        session.SculptMode = SculptMode.Raise;

        session.Sculpt(Top());
        session.EndStroke();

        Assert.Equal(Grass, plate.Grid.GetVoxel(new Int3(0, 1, 0)));
        Assert.Equal(Grass, plate.Grid.GetVoxel(new Int3(2, 1, 0)));
        Assert.False(plate.Grid.IsSolid(new Int3(0, 2, 0)));
        Assert.False(plate.Grid.IsSolid(new Int3(3, 1, 0)));
    }

    [Fact]
    public void FlattenCutsWhatStandsAboveAndFillsWhatIsBelow()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        plate.Grid.SetVoxel(1, 1, 0, Grass);                    // a bump
        plate.Grid.SetVoxel(-1, 0, 0, Palette.EmptyIndex);      // a pit
        session.SculptMode = SculptMode.Flatten;

        session.Sculpt(Top());
        session.EndStroke();

        Assert.False(plate.Grid.IsSolid(new Int3(1, 1, 0)));
        Assert.True(plate.Grid.IsSolid(new Int3(-1, 0, 0)));
    }

    [Fact]
    public void SmoothWearsABumpAwayFillsAHoleAndLeavesThePlate()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        plate.Grid.SetVoxel(0, 1, 0, Grass);
        plate.Grid.SetVoxel(1, 0, 1, Palette.EmptyIndex);
        session.SculptMode = SculptMode.Smooth;
        int before = plate.Grid.SolidCount;

        session.Sculpt(Top());
        session.EndStroke();

        Assert.False(plate.Grid.IsSolid(new Int3(0, 1, 0)));
        Assert.Equal(Grass, plate.Grid.GetVoxel(new Int3(1, 0, 1)));
        Assert.Equal(before, plate.Grid.SolidCount);
    }

    [Fact]
    public void LowerTakesTheSurfacesTopLayer()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        plate.Grid.SetVoxel(0, 1, 0, Grass);
        plate.Grid.SetVoxel(1, 1, 0, Grass);
        session.SculptMode = SculptMode.Lower;

        session.Sculpt(new RaycastHit(new Int3(0, 1, 0), Face.PosY, 1f));
        session.EndStroke();

        Assert.False(plate.Grid.IsSolid(new Int3(0, 1, 0)));
        Assert.False(plate.Grid.IsSolid(new Int3(1, 1, 0)));
    }

    [Theory]
    [InlineData(SculptMode.Add, SculptMode.Remove)]
    [InlineData(SculptMode.Raise, SculptMode.Lower)]
    [InlineData(SculptMode.Smooth, SculptMode.Smooth)]
    public void CtrlTurnsABrushRound(SculptMode mode, SculptMode turned)
    {
        Assert.Equal(turned, SculptOperations.Inverse(mode));
        Assert.Equal(mode, SculptOperations.Inverse(turned));
    }

    [Fact]
    public void AnotherModeCanStandInForADab()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        session.SculptMode = SculptMode.Add;

        session.Sculpt(Top(), SculptMode.Remove);
        session.EndStroke();

        Assert.False(plate.Grid.IsSolid(new Int3(0, 0, 0)));
    }

    [Fact]
    public void TheCubeBrushReachesItsCorners()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        session.SculptMode = SculptMode.Remove;
        session.SculptShape = SculptShape.Cube;

        session.Sculpt(Top());
        session.EndStroke();

        // Two across and two along: a corner a sphere of radius 2 would miss.
        Assert.False(plate.Grid.IsSolid(new Int3(2, 0, 2)));
    }

    [Fact]
    public void TheRadiusStaysInItsRange()
    {
        var session = new EditorSession { SculptRadius = 1000f };
        Assert.Equal(SculptOperations.MaxRadius, session.SculptRadius);

        session.SculptRadius = -3f;
        Assert.Equal(SculptOperations.MinRadius, session.SculptRadius);
    }

    [Fact]
    public void WithNothingToSculptNothingHappens()
    {
        var session = new EditorSession { ActiveTool = EditorTool.Sculpt };

        Assert.False(session.Sculpt(Top()));
        session.EndStroke();
        Assert.False(session.History.CanUndo);
    }
}
