using System.Numerics;
using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests.Mimicraft;

/// <summary>
/// Every file goes back through a reader written separately from the writer, so what is tested is
/// that the bytes say what Mimicraft will read — not that the writer agrees with itself.
/// </summary>
public class MimicraftCodecTests
{
    private static VoxelWorld Grid(params (int X, int Y, int Z, byte Color)[] voxels)
    {
        var grid = new VoxelWorld();
        foreach ((int x, int y, int z, byte color) in voxels)
        {
            grid.SetVoxel(x, y, z, color);
        }

        return grid;
    }

    private static VoxelWorld Box(int sizeX, int sizeY, int sizeZ, byte color = 40)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    grid.SetVoxel(x, y, z, color);
                }
            }
        }

        return grid;
    }

    private static Palette DefaultPalette() => Palette.CreateDefault();

    private static long Index(int x, int y, int z, Int3 size) => (((long)y * size.Z) + z) * size.X + x;

    [Fact]
    public void ASolidBoxComesBackWithEveryVoxelInPlace()
    {
        Palette palette = DefaultPalette();
        var piece = new MimicraftPiece("head", Box(3, 4, 5));

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([piece], palette));

        Assert.Single(body.Pieces);
        DecodedPiece decoded = body.Pieces[0];

        Assert.Equal(new Int3(3, 4, 5), decoded.BoxSize);
        Assert.Equal(3 * 4 * 5, decoded.Voxels.Count);
        Assert.All(decoded.Voxels.Values, c => Assert.Equal(palette[40], c));
    }

    [Fact]
    public void RunsNeverCrossARow()
    {
        // The rule the reader enforces and the encoder has to respect: x wraps but the index keeps
        // counting, so a run carried into the next row would look continuous and be wrong. A solid
        // box is the worst case — every row is one full run and the next starts one index later.
        Palette palette = DefaultPalette();
        var piece = new MimicraftPiece("slab", Box(6, 3, 4));

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([piece], palette));

        // One run per row: 3 * 4 rows, never merged across the wrap.
        Assert.Equal(12, body.Pieces[0].RunCount);
    }

    [Fact]
    public void AGapInARowSplitsTheRun()
    {
        Palette palette = DefaultPalette();
        var grid = Grid((0, 0, 0, 40), (1, 0, 0, 40), (3, 0, 0, 40));

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));
        DecodedPiece decoded = body.Pieces[0];

        Assert.Equal(2, decoded.RunCount);
        Assert.Equal(3, decoded.Voxels.Count);
        Assert.Equal(new Int3(4, 1, 1), decoded.BoxSize);
    }

    [Fact]
    public void AColourChangeSplitsTheRun()
    {
        Palette palette = DefaultPalette();
        var grid = Grid((0, 0, 0, 40), (1, 0, 0, 41), (2, 0, 0, 41));

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));
        DecodedPiece decoded = body.Pieces[0];

        Assert.Equal(2, decoded.RunCount);
        Assert.Equal(palette[40], decoded.Voxels[0]);
        Assert.Equal(palette[41], decoded.Voxels[1]);
        Assert.Equal(palette[41], decoded.Voxels[2]);
    }

    [Fact]
    public void EveryVoxelIsWrittenWhereItWasBuilt()
    {
        // Rebasing has to move the whole model together, not shift parts of it: an L keeps its shape
        // and its lowest corner lands on the origin.
        Palette palette = DefaultPalette();
        var grid = Grid((5, 2, 7, 40), (6, 2, 7, 41), (5, 3, 7, 42));

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));
        DecodedPiece decoded = body.Pieces[0];

        Assert.Equal(new Int3(2, 2, 1), decoded.BoxSize);
        Assert.Equal(Int3.Zero, decoded.BoxMin);

        Assert.Equal(palette[40], decoded.Voxels[Index(0, 0, 0, decoded.BoxSize)]);
        Assert.Equal(palette[41], decoded.Voxels[Index(1, 0, 0, decoded.BoxSize)]);
        Assert.Equal(palette[42], decoded.Voxels[Index(0, 1, 0, decoded.BoxSize)]);
    }

    [Fact]
    public void PaintedFacesTravelAsExceptions()
    {
        Palette palette = DefaultPalette();
        var grid = Grid((0, 0, 0, 40));
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 90);

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));
        DecodedPiece decoded = body.Pieces[0];

        // +Y is face 2 over there, whatever it is called here.
        Assert.Single(decoded.Faces);
        Assert.Equal(palette[90], decoded.Faces[(0, 2)]);

        // And the voxel underneath still carries its own colour.
        Assert.Equal(palette[40], decoded.Voxels[0]);
    }

    [Theory]
    [InlineData(Face.PosX, 5)]
    [InlineData(Face.NegX, 4)]
    [InlineData(Face.PosY, 2)]
    [InlineData(Face.NegY, 3)]
    [InlineData(Face.PosZ, 1)]
    [InlineData(Face.NegZ, 0)]
    public void EachFaceIsNumberedTheWayMimicraftNumbersIt(Face face, byte expected)
    {
        // The two orderings disagree, and a face sent through the wrong number lands on the wrong
        // side of the voxel — which looks like a painting mistake rather than an encoding one.
        Palette palette = DefaultPalette();
        var grid = Grid((0, 0, 0, 40));
        grid.SetFaceColor(new Int3(0, 0, 0), face, 90);

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));

        Assert.Equal(expected, Assert.Single(body.Pieces[0].Faces).Key.Face);
    }

    [Fact]
    public void SeveralFacesOfOneVoxelComeOutInOrder()
    {
        // The face gap counts from the previous entry's index, so a second face of the same voxel is
        // a gap of zero — and the reader rejects a pair that does not advance.
        Palette palette = DefaultPalette();
        var grid = Grid((0, 0, 0, 40));

        foreach (Face face in new[] { Face.PosX, Face.NegZ, Face.PosY })
        {
            grid.SetFaceColor(new Int3(0, 0, 0), face, 90);
        }

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));

        Assert.Equal(3, body.Pieces[0].Faces.Count);
        Assert.Equal([(0L, (byte)0), (0L, (byte)2), (0L, (byte)5)], body.Pieces[0].Faces.Keys.Order());
    }

    [Fact]
    public void FacesOnDifferentVoxelsUseTheGapBetweenTheirIndices()
    {
        Palette palette = DefaultPalette();
        var grid = Box(4, 1, 1);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 90);
        grid.SetFaceColor(new Int3(3, 0, 0), Face.PosY, 91);

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));
        DecodedPiece decoded = body.Pieces[0];

        Assert.Equal(palette[90], decoded.Faces[(0, 2)]);
        Assert.Equal(palette[91], decoded.Faces[(3, 2)]);
    }

    [Fact]
    public void AnEmptyPieceStillWritesItsWholeHeader()
    {
        // Leaving the face count off a piece with nothing in it makes the reader take the next
        // piece's position as a face entry, and everything after it is nonsense.
        Palette palette = DefaultPalette();

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode(
            [new MimicraftPiece("empty", new VoxelWorld()), new MimicraftPiece("solid", Box(2, 2, 2))],
            palette));

        Assert.Equal(2, body.Pieces.Count);
        Assert.Empty(body.Pieces[0].Voxels);
        Assert.Equal(Int3.Zero, body.Pieces[0].BoxSize);
        Assert.Equal(8, body.Pieces[1].Voxels.Count);
    }

    [Fact]
    public void OnlyTheColoursActuallyUsedReachThePalette()
    {
        Palette palette = DefaultPalette();
        var grid = Grid((0, 0, 0, 40), (1, 0, 0, 41), (2, 0, 0, 40));

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));

        Assert.Equal(2, body.Palette.Count);
    }

    [Fact]
    public void ACharacterFileNamesEveryPart()
    {
        Palette palette = DefaultPalette();
        MimicraftPiece[] parts =
        [
            new MimicraftPiece("head", Box(2, 2, 2, 40)),
            new MimicraftPiece("torso", Box(3, 4, 2, 41)),
        ];

        byte[] file = MimicraftFiles.EncodeCharacter("Ahmet", "steve", parts, palette);
        (string name, string rigId, List<DecodedPiece> decoded) = MimicraftReader.ReadCharacterFile(file);

        Assert.Equal("Ahmet", name);
        Assert.Equal("steve", rigId);
        Assert.Equal(["head", "torso"], decoded.Select(p => p.Id));
        Assert.Equal(8, decoded[0].Voxels.Count);
        Assert.Equal(24, decoded[1].Voxels.Count);
    }

    [Fact]
    public void ACharacterWithNothingModelledIsStillAFile()
    {
        byte[] file = MimicraftFiles.EncodeCharacter("Empty", "steve", [], DefaultPalette());
        (string name, string rigId, List<DecodedPiece> parts) = MimicraftReader.ReadCharacterFile(file);

        Assert.Equal("Empty", name);
        Assert.Equal("steve", rigId);
        Assert.Empty(parts);
    }

    [Fact]
    public void NonAsciiNamesSurviveTheTrip()
    {
        // The length prefix counts UTF-8 bytes, not characters.
        byte[] file = MimicraftFiles.EncodeCharacter("Kamyon şoförü", "steve", [], DefaultPalette());
        (string name, _, _) = MimicraftReader.ReadCharacterFile(file);

        Assert.Equal("Kamyon şoförü", name);
    }

    [Fact]
    public void AWeaponFileMatchesItsVoxelsToItsId()
    {
        Palette palette = DefaultPalette();
        var piece = new MimicraftPiece("ignored", Box(3, 3, 8, 40));

        byte[] file = MimicraftFiles.EncodeWeapon("spas12", piece, palette);
        (List<MimicraftReader.DecodedWeapon> weapons, List<DecodedPiece> pieces) =
            MimicraftReader.ReadWeaponFile(file);

        MimicraftReader.DecodedWeapon weapon = Assert.Single(weapons);
        Assert.Equal("spas12", weapon.Id);
        Assert.False(weapon.HasPoints);

        // The two halves are matched by id, so the piece has to be renamed to the weapon.
        Assert.Equal("spas12", Assert.Single(pieces).Id);
        Assert.Equal(3 * 3 * 8, pieces[0].Voxels.Count);
    }

    [Fact]
    public void AWeaponWithNoVoxelsIsStillAFile()
    {
        byte[] file = MimicraftFiles.EncodeWeapon("knife", piece: null, DefaultPalette());
        (List<MimicraftReader.DecodedWeapon> weapons, List<DecodedPiece> pieces) =
            MimicraftReader.ReadWeaponFile(file);

        Assert.Equal("knife", Assert.Single(weapons).Id);
        Assert.Empty(pieces);
    }

    [Fact]
    public void ABoxOfTheLargestAllowedSizeIsAccepted()
    {
        // Right on the limit, because an off-by-one here is a file the reader refuses.
        Palette palette = DefaultPalette();
        var grid = new VoxelWorld();
        for (int x = 0; x < 64; x++)
        {
            grid.SetVoxel(x, 0, 0, 40);
            grid.SetVoxel(0, x, 0, 40);
            grid.SetVoxel(0, 0, x, 40);
        }

        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([new MimicraftPiece("p", grid)], palette));

        Assert.Equal(new Int3(64, 64, 64), body.Pieces[0].BoxSize);
    }

    [Fact]
    public void ALongRowIsWrittenAsOneRun()
    {
        Palette palette = DefaultPalette();
        DecodedBody body = MimicraftReader.ReadBody(
            MimicraftBody.Encode([new MimicraftPiece("p", Box(64, 1, 1))], palette));

        Assert.Equal(1, body.Pieces[0].RunCount);
        Assert.Equal(64, body.Pieces[0].Voxels.Count);
    }

    [Fact]
    public void ARealLevelGoesThroughWithEveryVoxelAndColourIntact()
    {
        // The whole point, on something shaped like real work rather than like a test.
        var scene = new VoxelScene();
        var random = new Random(20260820);

        foreach (string name in new[] { "head", "torso", "arm_left" })
        {
            var grid = new VoxelWorld();
            for (int x = 0; x < 7; x++)
            {
                for (int y = 0; y < 9; y++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        if (random.NextDouble() < 0.8)
                        {
                            grid.SetVoxel(x, y, z, (byte)(30 + random.Next(6)));
                        }
                    }
                }
            }

            // A few painted faces, since that is the part with two different gap rules.
            for (int i = 0; i < 12; i++)
            {
                int x = random.Next(7);
                int y = random.Next(9);
                int z = random.Next(5);
                if (grid.IsSolid(x, y, z))
                {
                    grid.SetFaceColor(new Int3(x, y, z), (Face)random.Next(6), (byte)(60 + random.Next(4)));
                }
            }

            scene.Add(grid, ObjectTransform.Identity, name);
        }

        IReadOnlyList<MimicraftPiece> parts = MimicraftScene.BuildCharacterParts(scene);
        byte[] file = MimicraftFiles.EncodeCharacter("Test", "steve", parts, scene.Palette);

        (_, _, List<DecodedPiece> decoded) = MimicraftReader.ReadCharacterFile(file);

        Assert.Equal(3, decoded.Count);

        for (int i = 0; i < parts.Count; i++)
        {
            VoxelWorld grid = parts[i].Grid;
            DecodedPiece piece = decoded[i];

            Assert.Equal(parts[i].Id, piece.Id);
            Assert.Equal(grid.SolidCount, piece.Voxels.Count);

            grid.TryGetBounds(out Int3 min, out Int3 max);
            Int3 size = max - min + Int3.One;

            for (int y = 0; y < size.Y; y++)
            {
                for (int z = 0; z < size.Z; z++)
                {
                    for (int x = 0; x < size.X; x++)
                    {
                        Int3 source = min + new Int3(x, y, z);
                        long index = Index(x, y, z, size);

                        if (!grid.IsSolid(source))
                        {
                            Assert.False(piece.Voxels.ContainsKey(index));
                            continue;
                        }

                        Assert.Equal(scene.Palette[grid.GetVoxel(source)], piece.Voxels[index]);

                        for (int f = 0; f < FaceInfo.Count; f++)
                        {
                            byte painted = grid.GetFaceColor(source, (Face)f);
                            byte number = MimicraftBody.ToMimicraftFace((Face)f);

                            if (painted == grid.GetVoxel(source))
                            {
                                Assert.False(piece.Faces.ContainsKey((index, number)));
                            }
                            else
                            {
                                Assert.Equal(scene.Palette[painted], piece.Faces[(index, number)]);
                            }
                        }
                    }
                }
            }
        }
    }
}
