using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Edit Mode's selection: voxels of the object being edited, by cell. Where Extrude's selection is
/// a surface — faces — this is volume: what the gizmo moves, P separates, Delete empties and Fill
/// colours. Like the object selection, choosing voxels is not an undo step; the edits made to them
/// are, and bring the selection back with them.
/// </summary>
public sealed class VoxelSelection
{
    private readonly HashSet<Int3> _cells = [];

    public IReadOnlyCollection<Int3> Cells => _cells;

    public int Count => _cells.Count;

    public bool IsEmpty => _cells.Count == 0;

    public bool Contains(Int3 cell) => _cells.Contains(cell);

    public void Clear() => _cells.Clear();

    /// <summary>Replaces the selection with these cells, adds them to it or takes them from it.</summary>
    public void Set(IEnumerable<Int3> cells, SelectionOperation operation)
    {
        if (operation == SelectionOperation.Replace)
        {
            _cells.Clear();
        }

        foreach (Int3 cell in cells)
        {
            if (operation == SelectionOperation.Subtract)
            {
                _cells.Remove(cell);
            }
            else
            {
                _cells.Add(cell);
            }
        }
    }

    /// <summary>Exactly these cells, for undo putting back what was chosen before an edit.</summary>
    public void Restore(IEnumerable<Int3> cells)
    {
        _cells.Clear();
        _cells.UnionWith(cells);
    }

    /// <summary>Drops whatever is no longer a voxel — after an undo took it away.</summary>
    public void Retain(VoxelWorld grid) => _cells.RemoveWhere(cell => !grid.IsSolid(cell));

    public bool TryGetBounds(out Int3 min, out Int3 max)
    {
        min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        max = new Int3(int.MinValue, int.MinValue, int.MinValue);

        foreach (Int3 cell in _cells)
        {
            min = Int3.Min(min, cell);
            max = Int3.Max(max, cell);
        }

        return _cells.Count > 0;
    }

    /// <summary>The middle of the selection's box, in the object's own cell space.</summary>
    public Vector3 Centre() =>
        TryGetBounds(out Int3 min, out Int3 max) ? (min.ToVector3() + max.ToVector3() + Vector3.One) * 0.5f : Vector3.Zero;
}

/// <summary>Ways of choosing voxels, as pure functions of a grid.</summary>
public static class VoxelSelecting
{
    /// <summary>A flood of the magic wand is stopped here, so one click on a huge object stays quick.</summary>
    public const int MaxFlood = 2_000_000;

    /// <summary>
    /// The magic wand: the voxels joined to <paramref name="start"/> face to face that are its
    /// colour, it included.
    /// </summary>
    public static List<Int3> Connected(VoxelWorld grid, Int3 start)
    {
        byte colour = grid.GetVoxel(start);
        var found = new List<Int3>();
        if (colour == Palette.EmptyIndex)
        {
            return found;
        }

        var seen = new HashSet<Int3> { start };
        var queue = new Queue<Int3>();
        queue.Enqueue(start);

        while (queue.Count > 0 && found.Count < MaxFlood)
        {
            Int3 cell = queue.Dequeue();
            found.Add(cell);

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 next = cell + FaceInfo.Offset((Face)f);
                if (grid.GetVoxel(next) == colour && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return found;
    }

    /// <summary>Every voxel of the object that is <paramref name="colour"/>, wherever it is.</summary>
    public static IEnumerable<Int3> OfColour(VoxelWorld grid, byte colour) =>
        ClipboardOperations.Everything(grid).Where(cell => grid.GetVoxel(cell) == colour);

    /// <summary>The solid neighbours, face to face, of the selection that are not in it yet: what Grow adds.</summary>
    public static List<Int3> Border(VoxelWorld grid, VoxelSelection selection)
    {
        var border = new HashSet<Int3>();
        foreach (Int3 cell in selection.Cells)
        {
            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 next = cell + FaceInfo.Offset((Face)f);
                if (!selection.Contains(next) && grid.IsSolid(next))
                {
                    border.Add(next);
                }
            }
        }

        return [.. border];
    }

    /// <summary>
    /// The selected voxels with a face against something unselected: what Shrink takes away. Only
    /// along the axes the selection spreads over — a patch of a surface, one voxel thick, shrinks
    /// round its edge rather than vanishing because everything above and below it is unselected.
    /// </summary>
    public static List<Int3> Rim(VoxelSelection selection)
    {
        var rim = new List<Int3>();
        if (!selection.TryGetBounds(out Int3 min, out Int3 max))
        {
            return rim;
        }

        foreach (Int3 cell in selection.Cells)
        {
            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 step = FaceInfo.Offset((Face)f);
                bool flat = (step.X != 0 && min.X == max.X) || (step.Y != 0 && min.Y == max.Y) || (step.Z != 0 && min.Z == max.Z);
                if (!flat && !selection.Contains(cell + step))
                {
                    rim.Add(cell);
                    break;
                }
            }
        }

        return rim;
    }

    /// <summary>Whether a voxel has a face open to the air, and so can be seen at all.</summary>
    public static bool IsExposed(VoxelWorld grid, Int3 cell)
    {
        for (int f = 0; f < FaceInfo.Count; f++)
        {
            if (!grid.IsSolid(cell + FaceInfo.Offset((Face)f)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The lattice map that turns or mirrors a block of cells by <paramref name="axes"/> — the images
    /// of the three unit steps, each a whole unit step — about <paramref name="pivot"/>, the middle of
    /// the block in cell space, then shifts it by <paramref name="shift"/>. Where the turn would leave
    /// the block half a voxel off the lattice, it is rounded onto it.
    /// </summary>
    public static LatticeMap About(Vector3 pivot, Int3 x, Int3 y, Int3 z, Int3 shift)
    {
        // A cell's centre c + ½ goes to R(c + ½ − p) + p; its index is that less ½ again.
        Vector3 half = new(0.5f);
        Vector3 turnedHalf = Turn(half - pivot, x, y, z);
        Vector3 origin = turnedHalf + pivot - half;

        return new LatticeMap(
            new Int3(RoundAway(origin.X), RoundAway(origin.Y), RoundAway(origin.Z)) + shift,
            x,
            y,
            z);
    }

    private static Vector3 Turn(Vector3 v, Int3 x, Int3 y, Int3 z) => (x.ToVector3() * v.X) + (y.ToVector3() * v.Y) + (z.ToVector3() * v.Z);

    private static int RoundAway(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>A quarter turn, k times, about one of the object's own axes, as the images of the unit steps.</summary>
    public static (Int3 X, Int3 Y, Int3 Z) QuarterTurns(Axis axis, int turns)
    {
        Int3 x = new(1, 0, 0);
        Int3 y = new(0, 1, 0);
        Int3 z = new(0, 0, 1);

        for (int i = 0; i < ((turns % 4) + 4) % 4; i++)
        {
            x = TurnOnce(x, axis);
            y = TurnOnce(y, axis);
            z = TurnOnce(z, axis);
        }

        return (x, y, z);
    }

    /// <summary>One quarter turn, counter-clockwise looking down the axis, as a right-handed turn is.</summary>
    private static Int3 TurnOnce(Int3 v, Axis axis) => axis switch
    {
        Axis.X => new Int3(v.X, -v.Z, v.Y),
        Axis.Y => new Int3(v.Z, v.Y, -v.X),
        _ => new Int3(-v.Y, v.X, v.Z),
    };

    /// <summary>A mirror across one of the object's own axes, as the images of the unit steps.</summary>
    public static (Int3 X, Int3 Y, Int3 Z) Mirror(Axis axis) => axis switch
    {
        Axis.X => (new Int3(-1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1)),
        Axis.Y => (new Int3(1, 0, 0), new Int3(0, -1, 0), new Int3(0, 0, 1)),
        _ => (new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, -1)),
    };
}

/// <summary>
/// The selected voxels of the object in Edit Mode, as something the Transform gizmo can take hold
/// of. It stands at the selection's middle with the object's turn; what the gizmo does to it, the
/// session turns into whole voxels moved and quarter turns, in the object's own lattice.
/// </summary>
public sealed class VoxelSelectionHandle : IPlaceable
{
    internal VoxelSelectionHandle(VoxelObject owner, ObjectTransform rest)
    {
        Owner = owner;
        Rest = rest;
        Transform = rest;
    }

    /// <summary>The object whose voxels these are.</summary>
    public VoxelObject Owner { get; }

    /// <summary>Where the handle stood when it was made: the selection's middle, before any drag.</summary>
    public ObjectTransform Rest { get; }

    public int Id => -Owner.Id;

    public string Name => "Selection";

    public ObjectTransform Transform { get; set; }

    public int ParentId => 0;

    public Vector3 WorldCentre() => Transform.Position;
}
