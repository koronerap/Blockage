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

    // ---- Flip ----------------------------------------------------------------------------------

    public static TheoryData<Axis> Axes() => new(Enum.GetValues<Axis>());

    private static void AssertSame(
        Dictionary<Int3, (byte Color, byte[] Faces)> expected,
        Dictionary<Int3, (byte Color, byte[] Faces)> actual)
    {
        // Key by key: the face colours are arrays, and a dictionary comparison would ask whether
        // they are the same array rather than whether they say the same thing.
        Assert.Equal(expected.Count, actual.Count);
        foreach ((Int3 at, (byte color, byte[] faces)) in expected)
        {
            Assert.True(actual.ContainsKey(at), $"{at} came back empty.");
            Assert.Equal(color, actual[at].Color);
            Assert.Equal(faces, actual[at].Faces);
        }
    }

    [Theory]
    [MemberData(nameof(Axes))]
    public void FlippingTwiceIsNoChangeAtAll(Axis axis)
    {
        // A mirror is its own inverse, which is the whole of how its undo works. So this has to hold
        // to the painted face, not just to the voxel count.
        VoxelWorld grid = Lopsided();
        Dictionary<Int3, (byte, byte[])> before = Snapshot(grid);

        RotateOperations.Flip(grid, axis);
        RotateOperations.Flip(grid, axis);

        AssertSame(before, Snapshot(grid));
    }

    [Theory]
    [MemberData(nameof(Axes))]
    public void FlippingKeepsTheBoundsExactly(Axis axis)
    {
        // Unlike a quarter turn there is no half voxel to place: a lopsided box mirrored across its
        // own middle covers exactly the same cells it did. Not "about the same place" - the same.
        VoxelWorld grid = Lopsided();
        grid.TryGetBounds(out Int3 min, out Int3 max);
        int count = grid.SolidCount;

        RotateOperations.Flip(grid, axis);

        grid.TryGetBounds(out Int3 flippedMin, out Int3 flippedMax);
        Assert.Equal(min, flippedMin);
        Assert.Equal(max, flippedMax);
        Assert.Equal(count, grid.SolidCount);
    }

    [Theory]
    [MemberData(nameof(Axes))]
    public void FlippingActuallyMirrors(Axis axis)
    {
        // The two tests above would both pass for a flip that did nothing. This one would not.
        VoxelWorld grid = Lopsided();
        grid.TryGetBounds(out Int3 min, out Int3 max);
        Dictionary<Int3, (byte Color, byte[] Faces)> before = Snapshot(grid);

        RotateOperations.Flip(grid, axis);

        foreach ((Int3 at, (byte color, _)) in before)
        {
            Int3 mirrored = RotateOperations.Flip(at, axis, min, max);
            Assert.True(grid.IsSolid(mirrored), $"{at} should have landed on {mirrored}.");
            Assert.Equal(color, grid.GetVoxel(mirrored));
        }
    }

    [Fact]
    public void APaintedFaceAlongTheAxisSwapsSides()
    {
        // The case a plain copy of the voxels gets wrong. A block painted on its right-hand face,
        // mirrored left to right, is the same block painted on its left-hand face - otherwise the
        // detail on a flipped model ends up facing inwards.
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 40);
        grid.SetVoxel(3, 0, 0, 40);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosX, 90);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 91);

        RotateOperations.Flip(grid, Axis.X);

        var landed = new Int3(3, 0, 0);
        Assert.Equal(90, grid.GetFaceColor(landed, Face.NegX));
        Assert.Equal(40, grid.GetFaceColor(landed, Face.PosX));

        // A face across the axis is only carried along.
        Assert.Equal(91, grid.GetFaceColor(landed, Face.PosY));
    }

    [Theory]
    [MemberData(nameof(Axes))]
    public void OnlyTheFacesAlongTheAxisTurnRound(Axis axis)
    {
        foreach (Face face in Enum.GetValues<Face>())
        {
            Face flipped = RotateOperations.Flip(face, axis);

            if (FaceInfo.Axis(face) == (int)axis)
            {
                Assert.Equal(FaceInfo.Opposite(face), flipped);
            }
            else
            {
                Assert.Equal(face, flipped);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Axes))]
    public void FlipUndoPutsItBackExactly(Axis axis)
    {
        VoxelWorld grid = Lopsided();
        Dictionary<Int3, (byte, byte[])> before = Snapshot(grid);

        var command = new FlipObjectCommand(grid, axis);
        command.Redo();
        command.Undo();

        AssertSame(before, Snapshot(grid));
        Assert.Equal(0, command.RetainedCells);
    }

    [Fact]
    public void FlippingAnEmptyObjectIsLeftAlone()
    {
        var grid = new VoxelWorld();

        RotateOperations.Flip(grid, Axis.Z);

        Assert.Equal(0, grid.SolidCount);
    }
}
