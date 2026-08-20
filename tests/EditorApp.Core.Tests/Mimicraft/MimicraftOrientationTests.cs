using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests.Mimicraft;

/// <summary>
/// Every orientation on offer has to be a rotation. A mapping that mirrors the model would look
/// almost right and be wrong in a way nobody notices until a letter or a trigger guard is on the
/// wrong side, so it is worth proving rather than reading off the matrices.
/// </summary>
public class MimicraftOrientationTests
{
    public static TheoryData<MimicraftUpAxis, int> Every()
    {
        var data = new TheoryData<MimicraftUpAxis, int>();
        foreach (MimicraftUpAxis up in Enum.GetValues<MimicraftUpAxis>())
        {
            foreach (int turn in MimicraftOrientation.Turns)
            {
                data.Add(up, turn);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void EveryAxisIsUsedExactlyOnce(MimicraftUpAxis up, int turn)
    {
        // A signed permutation, not something that drops or doubles an axis.
        (int Axis, int Sign)[] mapping = new MimicraftOrientation(up, turn).SourceOfEachAxis();

        Assert.Equal([0, 1, 2], mapping.Select(m => m.Axis).Order());
        Assert.All(mapping, m => Assert.True(m.Sign is 1 or -1));
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void TheSixFacesStaySixDistinctFaces(MimicraftUpAxis up, int turn)
    {
        // If two editor faces mapped to one Mimicraft face, paint would pile up on one side of the
        // block and vanish from another.
        var orientation = new MimicraftOrientation(up, turn);

        int[] numbers = [.. Enum.GetValues<Face>().Select(f => (int)orientation.FaceNumber(f)).Order()];

        Assert.Equal([0, 1, 2, 3, 4, 5], numbers);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void OppositeFacesStayOpposite(MimicraftUpAxis up, int turn)
    {
        // Mimicraft pairs its faces 0/1, 2/3, 4/5. A rotation keeps opposites opposite; a mapping
        // that did not would have folded the block somehow.
        var orientation = new MimicraftOrientation(up, turn);

        foreach ((Face a, Face b) in new[] { (Face.PosX, Face.NegX), (Face.PosY, Face.NegY), (Face.PosZ, Face.NegZ) })
        {
            Assert.Equal(orientation.FaceNumber(a) ^ 1, orientation.FaceNumber(b));
        }
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void EveryCellOfTheBoxIsFilledFromExactlyOneSourceCell(MimicraftUpAxis up, int turn)
    {
        // The mapping has to be a bijection over the box. Anything else and voxels are lost or
        // written twice, which the run encoder would then turn into nonsense.
        var orientation = new MimicraftOrientation(up, turn);
        var sourceSize = new Int3(3, 4, 5);
        Int3 size = orientation.Size(sourceSize);

        Assert.Equal(3 * 4 * 5, size.X * size.Y * size.Z);

        var seen = new HashSet<Int3>();

        for (int y = 0; y < size.Y; y++)
        {
            for (int z = 0; z < size.Z; z++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    Int3 from = orientation.Source(new Int3(x, y, z), sourceSize);

                    Assert.InRange(from.X, 0, sourceSize.X - 1);
                    Assert.InRange(from.Y, 0, sourceSize.Y - 1);
                    Assert.InRange(from.Z, 0, sourceSize.Z - 1);
                    Assert.True(seen.Add(from), $"{from} was read twice.");
                }
            }
        }

        Assert.Equal(3 * 4 * 5, seen.Count);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void AModelKeepsItsVoxelsAndItsColoursWhicheverWayItIsTurned(MimicraftUpAxis up, int turn)
    {
        Palette palette = Palette.CreateDefault();
        var grid = new VoxelWorld();
        var random = new Random(11);

        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 7; z++)
                {
                    if (random.NextDouble() < 0.7)
                    {
                        grid.SetVoxel(x, y, z, (byte)(30 + random.Next(4)));
                    }
                }
            }
        }

        var orientation = new MimicraftOrientation(up, turn);
        DecodedBody body = MimicraftReader.ReadBody(
            MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette, orientation));

        DecodedPiece piece = body.Pieces[0];

        Assert.Equal(grid.SolidCount, piece.Voxels.Count);
        Assert.Equal(orientation.Size(new Int3(5, 3, 7)), piece.BoxSize);

        // And every one of them is the colour it started as, in the place the mapping says.
        for (int y = 0; y < piece.BoxSize.Y; y++)
        {
            for (int z = 0; z < piece.BoxSize.Z; z++)
            {
                for (int x = 0; x < piece.BoxSize.X; x++)
                {
                    Int3 from = orientation.Source(new Int3(x, y, z), new Int3(5, 3, 7));
                    long index = (((long)y * piece.BoxSize.Z) + z) * piece.BoxSize.X + x;

                    if (grid.IsSolid(from))
                    {
                        Assert.Equal(palette[grid.GetVoxel(from)], piece.Voxels[index]);
                    }
                    else
                    {
                        Assert.False(piece.Voxels.ContainsKey(index));
                    }
                }
            }
        }
    }

    [Fact]
    public void TheDefaultIsTheHandednessFlipAndNothingElse()
    {
        // Y up and no turn has to keep meaning exactly what it did before orientations existed, or
        // every model already exported changes shape.
        var orientation = MimicraftOrientation.Default;

        Assert.Equal([(0, 1), (1, 1), (2, -1)], orientation.SourceOfEachAxis());
        Assert.Equal(5, orientation.FaceNumber(Face.PosX));
        Assert.Equal(2, orientation.FaceNumber(Face.PosY));
        Assert.Equal(0, orientation.FaceNumber(Face.PosZ));
    }

    [Fact]
    public void AQuarterTurnPutsTheLongSideOnTheOtherAxis()
    {
        // What the setting is for: a model built facing the wrong way round arrives facing the right
        // one, and the size is the quickest way to see it.
        var orientation = new MimicraftOrientation(MimicraftUpAxis.Y, 90);

        Assert.Equal(new Int3(7, 3, 5), orientation.Size(new Int3(5, 3, 7)));
    }

    [Fact]
    public void ZUpSwapsHeightForDepth()
    {
        var orientation = new MimicraftOrientation(MimicraftUpAxis.Z);

        Assert.Equal(new Int3(5, 7, 3), orientation.Size(new Int3(5, 3, 7)));
    }
}
