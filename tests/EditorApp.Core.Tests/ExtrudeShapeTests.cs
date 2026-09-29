using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Extrude's drawn shapes (Fullreleaseplan 3.8): an ellipse and a line on a face, ready to pull.</summary>
public class ExtrudeShapeTests
{
    private static VoxelWorld Plate()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 10; x++)
        {
            for (int z = 0; z < 10; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
            }
        }

        return grid;
    }

    [Fact]
    public void AnEllipseTakesTheRoundInsideTheBoxDragged()
    {
        FaceSelection round = FaceSelection.Ellipse(Plate(), Face.PosY, 0, new Int3(0, 0, 0), new Int3(4, 0, 4));

        // A 5 x 5 disc: the box less its four corners.
        Assert.Equal(21, round.Count);
        Assert.False(round.Contains(new Int3(0, 0, 0)));
        Assert.True(round.Contains(new Int3(2, 0, 0)));
    }

    [Fact]
    public void ALineTakesOneVoxelAlongItsLength()
    {
        FaceSelection line = FaceSelection.Line(Plate(), Face.PosY, 0, new Int3(0, 0, 0), new Int3(4, 0, 2));

        Assert.Equal(5, line.Count);
        Assert.True(line.Contains(new Int3(0, 0, 0)));
        Assert.True(line.Contains(new Int3(4, 0, 2)));
    }

    [Fact]
    public void ADrawnShapePullsOutLikeABox()
    {
        var session = new EditorSession { ActiveTool = EditorTool.Extrude };
        session.Scene.Add(Plate(), Core.Scene.ObjectTransform.Identity);
        session.SetSelection(FaceSelection.Ellipse(session.World, Face.PosY, 0, new Int3(2, 0, 2), new Int3(6, 0, 6)));

        session.PreviewExtrude(3);
        Assert.True(session.ConfirmExtrude());

        Assert.True(session.World.IsSolid(new Int3(4, 3, 4)));
        Assert.False(session.World.IsSolid(new Int3(2, 1, 2)));
    }
}
