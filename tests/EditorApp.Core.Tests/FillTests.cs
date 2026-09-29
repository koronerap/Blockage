using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The two-colour fills and filling what a model closes off (Fullreleaseplan 4.2).</summary>
public class FillTests
{
    private const byte First = 30;
    private const byte Second = 90;

    /// <summary>An 8 × 1 × 8 plate, the one object, painted with the fills' two colours in hand.</summary>
    private static (EditorSession Session, VoxelObject Plate) Plate()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
            }
        }

        var scene = new VoxelScene();
        VoxelObject plate = scene.Add(grid, ObjectTransform.Identity, "Plate");
        var session = new EditorSession { ActiveTool = EditorTool.Paint, ActiveColorIndex = First, SecondaryColorIndex = Second };
        session.ReplaceScene(scene, projectPath: null);
        return (session, plate);
    }

    private static int Count(VoxelObject plate, byte colour)
    {
        int count = 0;
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                count += plate.Grid.GetFaceColor(new Int3(x, 0, z), Face.PosY) == colour ? 1 : 0;
            }
        }

        return count;
    }

    private static readonly RaycastHit Corner = new(new Int3(0, 0, 0), Face.PosY, 1f);

    [Fact]
    public void AGradientRunsFromTheColourInHandToTheSecondAsOneStep()
    {
        (EditorSession session, VoxelObject plate) = Plate();

        Assert.True(session.PaintGradient(Corner, new Int3(7, 0, 0)));

        Assert.Equal(First, plate.Grid.GetFaceColor(new Int3(0, 0, 3), Face.PosY));
        Assert.Equal(Second, plate.Grid.GetFaceColor(new Int3(7, 0, 3), Face.PosY));
        Assert.InRange(Count(plate, Second), 20, 44);
        Assert.Equal(1, session.History.UndoCount);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 64)]
    public void NoiseMixesInAsMuchOfTheSecondColourAsItIsTold(float mix, int expected)
    {
        (EditorSession session, VoxelObject plate) = Plate();
        session.PaintMode = PaintMode.Noise;
        session.MixAmount = mix;

        session.Paint(Corner);
        session.EndStroke();

        Assert.Equal(expected, Count(plate, Second));
    }

    [Fact]
    public void NoiseAtHalfHasBothAndIsTheSameEveryTime()
    {
        (EditorSession first, VoxelObject a) = Plate();
        (EditorSession second, VoxelObject b) = Plate();
        first.PaintMode = second.PaintMode = PaintMode.Noise;

        first.Paint(Corner);
        second.Paint(Corner);

        Assert.InRange(Count(a, Second), 16, 48);
        Assert.Equal(a.Grid.ContentHash(), b.Grid.ContentHash());
    }

    [Fact]
    public void DitherAtHalfIsExactlyHalfAndHalf()
    {
        (EditorSession session, VoxelObject plate) = Plate();
        session.PaintMode = PaintMode.Dither;
        session.MixAmount = 0.5f;

        session.Paint(Corner);
        session.EndStroke();

        Assert.Equal(32, Count(plate, Second));
        Assert.Equal(32, Count(plate, First));
    }

    [Fact]
    public void FillEnclosedFillsASealedHollowButNotAnOpenOne()
    {
        var sealedBox = new VoxelWorld();
        var openBox = new VoxelWorld();
        for (int x = 0; x < 5; x++)
        for (int y = 0; y < 5; y++)
        for (int z = 0; z < 5; z++)
        {
            bool wall = x == 0 || y == 0 || z == 0 || x == 4 || y == 4 || z == 4;
            if (wall)
            {
                sealedBox.SetVoxel(x, y, z, Palette.WhiteIndex);
                if (y != 4)
                {
                    openBox.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        Assert.Equal(27, VolumeOperations.FillEnclosed(First, new VoxelEditCommand("Fill", sealedBox)));
        Assert.Equal(First, sealedBox.GetVoxel(new Int3(2, 2, 2)));
        Assert.Equal(0, VolumeOperations.FillEnclosed(First, new VoxelEditCommand("Fill", openBox)));
    }
}
