using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Turning an object's voxels rather than its transform. A quarter turn of a cubic lattice is a
/// permutation, so nothing may be lost, gained, recoloured or left half a voxel out of place.
/// </summary>
public class RotateObjectTests
{
    public static TheoryData<RotateDirection> Directions() =>
        new(Enum.GetValues<RotateDirection>());

    /// <summary>Deliberately lopsided on every axis, so a wrong turn cannot hide behind symmetry.</summary>
    private static VoxelWorld Lopsided()
    {
        var grid = new VoxelWorld();
        var random = new Random(4242);

        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 8; z++)
                {
                    if (random.NextDouble() < 0.7)
                    {
                        grid.SetVoxel(x, y, z, (byte)(30 + random.Next(5)));
                    }
                }
            }
        }

        // A painted face or two, since those have to travel with the block they are on.
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 90);
        grid.SetFaceColor(new Int3(4, 2, 7), Face.NegX, 91);

        return grid;
    }

    private static Dictionary<Int3, (byte Color, byte[] Faces)> Snapshot(VoxelWorld grid)
    {
        var cells = new Dictionary<Int3, (byte, byte[])>();

        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return cells;
        }

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var at = new Int3(x, y, z);
                    if (grid.IsSolid(at))
                    {
                        cells[at] = (
                            grid.GetVoxel(at),
                            [.. Enum.GetValues<Face>().Select(f => grid.GetFaceColor(at, f))]);
                    }
                }
            }
        }

        return cells;
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void NothingIsLostOrGained(RotateDirection direction)
    {
        VoxelWorld grid = Lopsided();
        int before = grid.SolidCount;

        RotateOperations.Rotate(grid, direction);

        Assert.Equal(before, grid.SolidCount);
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void TheBoxSwapsTheTwoAxesTheTurnIsAbout(RotateDirection direction)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 8; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        RotateOperations.Rotate(grid, direction);
        grid.TryGetBounds(out Int3 min, out Int3 max);
        Int3 size = max - min + Int3.One;

        Int3 expected = direction is RotateDirection.Right or RotateDirection.Left
            ? new Int3(8, 3, 5)   // x and z change places
            : new Int3(5, 8, 3);  // y and z do

        Assert.Equal(expected, size);
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void FourTurnsComeBackToTheStart(RotateDirection direction)
    {
        // The strongest statement of "this is a permutation and nothing drifts": go all the way
        // round and every voxel, colour and painted face has to be exactly where it began.
        VoxelWorld grid = Lopsided();
        Dictionary<Int3, (byte, byte[])> before = Snapshot(grid);

        for (int i = 0; i < 4; i++)
        {
            RotateOperations.Rotate(grid, direction);
        }

        Assert.Equal(before.Count, Snapshot(grid).Count);
        foreach ((Int3 at, (byte color, byte[] faces)) in before)
        {
            Assert.True(grid.IsSolid(at), $"{at} came back empty.");
            Assert.Equal(color, grid.GetVoxel(at));

            foreach (Face face in Enum.GetValues<Face>())
            {
                Assert.Equal(faces[(int)face], grid.GetFaceColor(at, face));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void UndoPutsItBackExactly(RotateDirection direction)
    {
        // Not "back to roughly there". A box whose sides differ in parity cannot keep its centre on
        // the lattice, so undo has to reverse the shift it was given rather than work one out again.
        VoxelWorld grid = Lopsided();
        Dictionary<Int3, (byte, byte[])> before = Snapshot(grid);

        var command = new RotateObjectCommand(grid, direction);
        command.Redo();
        command.Undo();

        Dictionary<Int3, (byte Color, byte[] Faces)> after = Snapshot(grid);

        Assert.Equal(before.Count, after.Count);
        foreach ((Int3 at, (byte color, byte[] faces)) in before)
        {
            Assert.Equal(color, after[at].Color);
            Assert.Equal(faces, after[at].Faces);
        }
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void RedoingAfterUndoLandsInTheSamePlaceAsTheFirstTime(RotateDirection direction)
    {
        VoxelWorld grid = Lopsided();

        var command = new RotateObjectCommand(grid, direction);
        command.Redo();
        Dictionary<Int3, (byte Color, byte[] Faces)> once = Snapshot(grid);

        command.Undo();
        command.Redo();

        Dictionary<Int3, (byte Color, byte[] Faces)> again = Snapshot(grid);

        // Compared key by key: the face colours are arrays, and a dictionary comparison would be
        // asking whether they are the same array rather than whether they say the same thing.
        Assert.Equal(once.Count, again.Count);
        foreach ((Int3 at, (byte color, byte[] faces)) in once)
        {
            Assert.Equal(color, again[at].Color);
            Assert.Equal(faces, again[at].Faces);
        }
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void TheObjectStaysWhereItWas(RotateDirection direction)
    {
        // Turning about the origin would fling it across the level. What people mean by rotating
        // something is that it stays put and faces a different way.
        VoxelWorld grid = Lopsided();
        grid.TryGetBounds(out Int3 min, out Int3 max);
        Int3 centreBefore = min + max;

        RotateOperations.Rotate(grid, direction);

        grid.TryGetBounds(out Int3 turnedMin, out Int3 turnedMax);
        Int3 centreAfter = turnedMin + turnedMax;

        // Doubled centres, so "within one" here is within half a voxel - the most a box with sides
        // of different parity can manage.
        Assert.InRange(Math.Abs(centreAfter.X - centreBefore.X), 0, 1);
        Assert.InRange(Math.Abs(centreAfter.Y - centreBefore.Y), 0, 1);
        Assert.InRange(Math.Abs(centreAfter.Z - centreBefore.Z), 0, 1);
    }

    [Fact]
    public void APaintedFaceEndsUpOnTheSideItTurnedTo()
    {
        // The half that flat-coloured models hide: turning right takes a block's +X face round to
        // where -Z was pointing.
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 40);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosX, 90);

        RotateOperations.Rotate(grid, RotateDirection.Right);

        grid.TryGetBounds(out Int3 min, out _);

        Assert.Equal(Face.NegZ, RotateOperations.Turn(Face.PosX, RotateDirection.Right));
        Assert.Equal(90, grid.GetFaceColor(min, Face.NegZ));
        Assert.Equal(40, grid.GetFaceColor(min, Face.PosX));
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryFaceTurnsIntoADifferentFace(RotateDirection direction)
    {
        // Two faces mapping onto one would pile paint on one side and lose it from another.
        Face[] turned = [.. Enum.GetValues<Face>().Select(f => RotateOperations.Turn(f, direction))];

        Assert.Equal(6, turned.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void OppositeFacesStayOpposite(RotateDirection direction)
    {
        foreach ((Face a, Face b) in new[] { (Face.PosX, Face.NegX), (Face.PosY, Face.NegY), (Face.PosZ, Face.NegZ) })
        {
            Assert.Equal(
                FaceInfo.Opposite(RotateOperations.Turn(a, direction)),
                RotateOperations.Turn(b, direction));
        }
    }

    [Fact]
    public void AnEmptyObjectIsLeftAlone()
    {
        var grid = new VoxelWorld();

        Assert.Equal(Int3.Zero, RotateOperations.Rotate(grid, RotateDirection.Right));
        Assert.Equal(0, grid.SolidCount);
    }

    [Fact]
    public void TurningCostsNoMemoryToUndo()
    {
        // The reason this is a direction and a shift rather than a copy of the grid: an undo stack
        // trimmed by retained cells should not lose a session's history to a few rotations.
        var grid = new VoxelWorld();
        for (int i = 0; i < 500; i++)
        {
            grid.SetVoxel(i, 0, 0, 40);
        }

        Assert.Equal(0, new RotateObjectCommand(grid, RotateDirection.Right).RetainedCells);
    }
}
