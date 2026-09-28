using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>A quarter turn, named after what it looks like on screen rather than after an axis.</summary>
public enum RotateDirection
{
    /// <summary>Clockwise seen from above.</summary>
    Right,

    Left,

    /// <summary>The top tips away from you.</summary>
    Up,

    Down,
}

/// <summary>
/// Turning and mirroring an object, in the voxel data itself.
///
/// Not the same thing as rotating its transform, and the difference is worth being clear about. A
/// transform rotation is a placement: the grid underneath is untouched, the object is simply drawn
/// turned, and anything that reads the voxels — export, the greedy mesher, a file format that has no
/// field for rotation — still sees the model the way it was built. This turns the lattice instead,
/// which is exact, because a quarter turn of a cubic grid is a permutation and nothing is resampled.
/// </summary>
public static class RotateOperations
{
    /// <summary>
    /// Where a cell ends up. Right and left turn about the vertical axis; up and down about the
    /// horizontal one running across the screen.
    /// </summary>
    public static Int3 Turn(Int3 cell, RotateDirection direction) => direction switch
    {
        RotateDirection.Right => new Int3(cell.Z, cell.Y, -cell.X),
        RotateDirection.Left => new Int3(-cell.Z, cell.Y, cell.X),
        RotateDirection.Up => new Int3(cell.X, -cell.Z, cell.Y),
        _ => new Int3(cell.X, cell.Z, -cell.Y),
    };

    public static RotateDirection Opposite(RotateDirection direction) => direction switch
    {
        RotateDirection.Right => RotateDirection.Left,
        RotateDirection.Left => RotateDirection.Right,
        RotateDirection.Up => RotateDirection.Down,
        _ => RotateDirection.Up,
    };

    /// <summary>
    /// Where a face points afterwards, worked out from the same permutation the cells go through
    /// rather than from a table of its own. A face left behind puts the paint on a different side of
    /// the block — invisible on anything one colour, and glaring on anything that is not.
    /// </summary>
    public static Face Turn(Face face, RotateDirection direction)
    {
        Vector3 normal = FaceInfo.Normal(face);
        var turned = new Int3((int)normal.X, (int)normal.Y, (int)normal.Z);
        turned = Turn(turned, direction);

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            Vector3 candidate = FaceInfo.Normal((Face)f);
            if ((int)candidate.X == turned.X && (int)candidate.Y == turned.Y && (int)candidate.Z == turned.Z)
            {
                return (Face)f;
            }
        }

        throw new InvalidOperationException($"{face} turned into {turned}, which is not a face direction.");
    }

    /// <summary>
    /// Turns a grid in place and reports the shift it applied to keep the object where it was.
    ///
    /// The shift is returned so the turn can be undone exactly. Turning back and re-centring
    /// independently would land in the same place almost always — and a box whose sides differ in
    /// parity cannot keep its centre on the lattice, so "almost" is where an object would creep half
    /// a voxel every time somebody pressed undo.
    /// </summary>
    public static Int3 Rotate(VoxelWorld grid, RotateDirection direction, Int3? shift = null)
    {
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return Int3.Zero;
        }

        Int3 applied = shift ?? Centring(min, max, direction);
        Remap(grid, min, max, cell => Turn(cell, direction) + applied, face => Turn(face, direction));
        return applied;
    }

    /// <summary>
    /// Mirrors a grid in place across the middle of its own bounds.
    ///
    /// Simpler than a turn in the one way that matters: the mirrored box has exactly the bounds it
    /// started with, whatever the parity of its sides, so there is no half voxel to place and no
    /// shift to remember. A mirror is its own inverse — doing it twice is the undo.
    ///
    /// Faces go with the cells, and the ones facing along the axis swap sides: paint on the right-hand
    /// face of a block has to end up on the left-hand face of the block it becomes, or a flipped model
    /// comes out with its detail on the inside.
    /// </summary>
    public static void Flip(VoxelWorld grid, Axis axis)
    {
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return;
        }

        Remap(grid, min, max, cell => Flip(cell, axis, min, max), face => Flip(face, axis));
    }

    /// <summary>Where a cell lands when the box from <paramref name="min"/> to <paramref name="max"/> is mirrored.</summary>
    public static Int3 Flip(Int3 cell, Axis axis, Int3 min, Int3 max) => axis switch
    {
        Axis.X => new Int3(min.X + max.X - cell.X, cell.Y, cell.Z),
        Axis.Y => new Int3(cell.X, min.Y + max.Y - cell.Y, cell.Z),
        _ => new Int3(cell.X, cell.Y, min.Z + max.Z - cell.Z),
    };

    /// <summary>A face along the mirrored axis turns round; the other four are only carried along.</summary>
    public static Face Flip(Face face, Axis axis) =>
        FaceInfo.Axis(face) == (int)axis ? FaceInfo.Opposite(face) : face;

    /// <summary>
    /// Moves every solid cell of a grid, and every face painted differently from its block, to where
    /// the given maps send them.
    ///
    /// In place rather than into a replacement grid, because the undo stack holds references to the
    /// grids it edited: swapping an object's grid for a new one would leave every earlier command
    /// writing into a world nothing draws any more. Everything is read out before anything is
    /// written, since both a turn and a mirror move cells onto each other's places.
    /// </summary>
    private static void Remap(
        VoxelWorld grid,
        Int3 min,
        Int3 max,
        Func<Int3, Int3> moveCell,
        Func<Face, Face> moveFace)
    {
        var cells = new List<(Int3 To, byte Color, (Face Face, byte Color)[] Faces)>();

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var from = new Int3(x, y, z);
                    if (!grid.IsSolid(from))
                    {
                        continue;
                    }

                    byte color = grid.GetVoxel(from);
                    List<(Face, byte)>? faces = null;

                    for (int f = 0; f < FaceInfo.Count; f++)
                    {
                        byte painted = grid.GetFaceColor(from, (Face)f);
                        if (painted != color)
                        {
                            (faces ??= []).Add((moveFace((Face)f), painted));
                        }
                    }

                    cells.Add((moveCell(from), color, faces?.ToArray() ?? []));
                }
            }
        }

        grid.Clear();

        foreach ((Int3 to, byte color, (Face Face, byte Color)[] faces) in cells)
        {
            grid.SetVoxel(to, color);

            foreach ((Face face, byte painted) in faces)
            {
                grid.SetFaceColor(to, face, painted);
            }
        }
    }

    /// <summary>
    /// How far to move the turned model so it sits where it did. Turning about the origin would
    /// fling an object across the level; what people mean by rotating something is that it stays put
    /// and faces a different way.
    /// </summary>
    private static Int3 Centring(Int3 min, Int3 max, RotateDirection direction)
    {
        Int3 a = Turn(min, direction);
        Int3 b = Turn(max, direction);
        Int3 turnedMin = Int3.Min(a, b);

        Int3 size = max - min + Int3.One;
        Int3 turnedSize = Int3.Max(a, b) - turnedMin + Int3.One;

        // Worked out from the two box SIZES rather than from where the box happens to sit.
        //
        // That distinction is the whole of it. A box whose sides differ in parity cannot keep its
        // centre on the lattice, so half a voxel has to go somewhere — and if which way it goes
        // depends on the object's position, four turns do not bring it back: the roundings no longer
        // pair up and the model creeps. Depending only on the sizes, consecutive turns see exactly
        // opposite differences, and rounding away from zero makes those cancel.
        Int3 difference = size - turnedSize;
        Int3 offset = new(Half(difference.X), Half(difference.Y), Half(difference.Z));

        return min + offset - turnedMin;

        static int Half(int value) =>
            value >= 0 ? (value + 1) / 2 : -((-value + 1) / 2);
    }
}
