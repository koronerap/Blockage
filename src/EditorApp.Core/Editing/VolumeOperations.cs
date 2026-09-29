using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>What a boolean does with the other objects' volume.</summary>
public enum BooleanOperation
{
    /// <summary>Their voxels are added.</summary>
    Union,

    /// <summary>Their volume is cut out.</summary>
    Difference,

    /// <summary>Only what lies inside them is kept.</summary>
    Intersect,
}

/// <summary>
/// Operations on an object's whole volume (Fullreleaseplan 3.4–3.6): booleans between objects,
/// hollowing, thickening and thinning, smoothing, clearing loose pieces, and resampling. Each writes
/// through a command, so it undoes as one step.
/// </summary>
public static class VolumeOperations
{
    // ---- Booleans ------------------------------------------------------------------------------

    /// <summary>
    /// Adds, cuts or intersects <paramref name="operand"/>'s volume into the grid
    /// <paramref name="command"/> writes, which is <paramref name="target"/>'s. Objects on one lattice
    /// meet cell for cell, painted faces and all; otherwise each of the target's cells asks whether
    /// its middle is inside the other, so a coarser or turned object is resampled rather than
    /// leaving holes.
    /// </summary>
    public static int Boolean(BooleanOperation operation, VoxelObject target, VoxelObject operand, VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;

        if (operation == BooleanOperation.Union
            && LatticeMap.Between(operand.Transform, target.Transform, out _) is { } map)
        {
            return ClipboardOperations.WriteInto(operand.Grid, map, command);
        }

        int changed = 0;
        switch (operation)
        {
            case BooleanOperation.Union:
                foreach (Int3 cell in CellsCovering(operand, target))
                {
                    if (!grid.IsSolid(cell) && ColourAt(operand, target, cell) is { } colour && command.Apply(cell, colour))
                    {
                        changed++;
                    }
                }

                break;

            case BooleanOperation.Difference:
                foreach (Int3 cell in CellsCovering(operand, target))
                {
                    if (grid.IsSolid(cell) && ColourAt(operand, target, cell) is not null && command.Apply(cell, Palette.EmptyIndex))
                    {
                        changed++;
                    }
                }

                break;

            default:
                foreach (Int3 cell in ClipboardOperations.Everything(grid).ToList())
                {
                    if (ColourAt(operand, target, cell) is null && command.Apply(cell, Palette.EmptyIndex))
                    {
                        changed++;
                    }
                }

                break;
        }

        return changed;
    }

    /// <summary>The colour of the other object at the middle of one of the target's cells, or null outside it.</summary>
    private static byte? ColourAt(VoxelObject operand, VoxelObject target, Int3 cell)
    {
        Vector3 world = target.Transform.TransformPoint(cell.ToVector3() + new Vector3(0.5f));
        Vector3 local = operand.Transform.InverseTransformPoint(world);
        var inOperand = new Int3((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), (int)MathF.Floor(local.Z));
        byte value = operand.Grid.GetVoxel(inOperand);
        return value == Palette.EmptyIndex ? null : value;
    }

    /// <summary>The target's cells the other object's box could reach, in the target's own lattice.</summary>
    private static IEnumerable<Int3> CellsCovering(VoxelObject operand, VoxelObject target)
    {
        if (!operand.Grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            yield break;
        }

        Vector3 low = new(float.MaxValue);
        Vector3 high = new(float.MinValue);
        foreach (Vector3 corner in Snapping.Corners((min.ToVector3(), max.ToVector3() + Vector3.One), operand.Transform))
        {
            Vector3 local = target.Transform.InverseTransformPoint(corner);
            low = Vector3.Min(low, local);
            high = Vector3.Max(high, local);
        }

        for (int x = (int)MathF.Floor(low.X); x <= (int)MathF.Floor(high.X); x++)
        {
            for (int y = (int)MathF.Floor(low.Y); y <= (int)MathF.Floor(high.Y); y++)
            {
                for (int z = (int)MathF.Floor(low.Z); z <= (int)MathF.Floor(high.Z); z++)
                {
                    yield return new Int3(x, y, z);
                }
            }
        }
    }

    // ---- Filters -------------------------------------------------------------------------------

    /// <summary>The solid cells with a face open to the air: the model's shell, one voxel thick.</summary>
    private static List<Int3> Shell(VoxelWorld grid) =>
        [.. ClipboardOperations.Everything(grid).Where(cell => VoxelSelecting.IsExposed(grid, cell))];

    /// <summary>
    /// Empties the inside, keeping a wall <paramref name="thickness"/> voxels thick measured in from
    /// every open face — a hollow shell, far fewer voxels, looking the same from outside.
    /// </summary>
    public static int Hollow(int thickness, VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;
        thickness = Math.Max(thickness, 1);

        var kept = new HashSet<Int3>();
        var frontier = new Queue<(Int3 Cell, int Depth)>();
        foreach (Int3 cell in Shell(grid))
        {
            kept.Add(cell);
            frontier.Enqueue((cell, 1));
        }

        while (frontier.Count > 0)
        {
            (Int3 cell, int depth) = frontier.Dequeue();
            if (depth >= thickness)
            {
                continue;
            }

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 next = cell + FaceInfo.Offset((Face)f);
                if (grid.IsSolid(next) && kept.Add(next))
                {
                    frontier.Enqueue((next, depth + 1));
                }
            }
        }

        int changed = 0;
        foreach (Int3 cell in ClipboardOperations.Everything(grid).ToList())
        {
            if (!kept.Contains(cell) && command.Apply(cell, Palette.EmptyIndex))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>One voxel more all round, each new one the colour of a voxel it touches.</summary>
    public static int Thicken(VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;
        var added = new Dictionary<Int3, byte>();

        foreach (Int3 cell in Shell(grid))
        {
            byte colour = grid.GetVoxel(cell);
            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 next = cell + FaceInfo.Offset((Face)f);
                if (!grid.IsSolid(next))
                {
                    added.TryAdd(next, colour);
                }
            }
        }

        int changed = 0;
        foreach ((Int3 cell, byte colour) in added)
        {
            if (command.Apply(cell, colour))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>One voxel less all round: the shell taken off.</summary>
    public static int Thin(VoxelEditCommand command)
    {
        int changed = 0;
        foreach (Int3 cell in Shell(command.Target))
        {
            if (command.Apply(cell, Palette.EmptyIndex))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>The Sculpt tool's smoothing over the whole object: spikes off, holes filled.</summary>
    public static int Smooth(VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return 0;
        }

        var decisions = new List<(Int3 Cell, byte Value)>();
        for (int x = min.X - 1; x <= max.X + 1; x++)
        {
            for (int y = min.Y - 1; y <= max.Y + 1; y++)
            {
                for (int z = min.Z - 1; z <= max.Z + 1; z++)
                {
                    var cell = new Int3(x, y, z);
                    int touching = 0;
                    byte near = Palette.EmptyIndex;
                    for (int f = 0; f < FaceInfo.Count; f++)
                    {
                        byte value = grid.GetVoxel(cell + FaceInfo.Offset((Face)f));
                        if (value != Palette.EmptyIndex)
                        {
                            touching++;
                            near = value;
                        }
                    }

                    bool solid = grid.IsSolid(cell);
                    if (solid && touching <= 1)
                    {
                        decisions.Add((cell, Palette.EmptyIndex));
                    }
                    else if (!solid && touching >= 4)
                    {
                        decisions.Add((cell, near));
                    }
                }
            }
        }

        int changed = 0;
        foreach ((Int3 cell, byte value) in decisions)
        {
            if (command.Apply(cell, value))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Clears every piece — voxels joined face to face — smaller than <paramref name="minimum"/>
    /// voxels: the crumbs a cut or a carve leaves floating.
    /// </summary>
    public static int RemoveLoose(int minimum, VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;
        var seen = new HashSet<Int3>();
        int changed = 0;

        foreach (Int3 start in ClipboardOperations.Everything(grid).ToList())
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var piece = new List<Int3> { start };
            var queue = new Queue<Int3>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Int3 cell = queue.Dequeue();
                for (int f = 0; f < FaceInfo.Count; f++)
                {
                    Int3 next = cell + FaceInfo.Offset((Face)f);
                    if (grid.IsSolid(next) && seen.Add(next))
                    {
                        piece.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            if (piece.Count < minimum)
            {
                foreach (Int3 cell in piece)
                {
                    if (command.Apply(cell, Palette.EmptyIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Fills every hollow the model closes off — the empty cells no way leads to from outside — in
    /// <paramref name="colour"/>: a sealed room made solid, a shell made a block.
    /// </summary>
    public static int FillEnclosed(byte colour, VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            return 0;
        }

        // Everything outside the box, one cell round, is outside; what the air reaches from there is too.
        Int3 low = min - Int3.One;
        Int3 high = max + Int3.One;
        var outside = new HashSet<Int3>();
        var queue = new Queue<Int3>();
        queue.Enqueue(low);
        outside.Add(low);

        while (queue.Count > 0)
        {
            Int3 cell = queue.Dequeue();
            for (int f = 0; f < FaceInfo.Count; f++)
            {
                Int3 next = cell + FaceInfo.Offset((Face)f);
                if (next.X < low.X || next.Y < low.Y || next.Z < low.Z || next.X > high.X || next.Y > high.Y || next.Z > high.Z)
                {
                    continue;
                }

                if (!grid.IsSolid(next) && outside.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        int changed = 0;
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var cell = new Int3(x, y, z);
                    if (!grid.IsSolid(cell) && !outside.Contains(cell) && command.Apply(cell, colour))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    // ---- Resampling ----------------------------------------------------------------------------

    /// <summary>
    /// Subdivide's reverse: every 2 × 2 × 2 block becomes one voxel of twice the size, solid when at
    /// least half of the block was, in the colour most of it had. The grid is new; the object's place
    /// in the world does not change, only its voxel size.
    /// </summary>
    public static VoxelWorld Halve(VoxelWorld grid)
    {
        var counts = new Dictionary<Int3, Dictionary<byte, int>>();
        foreach (Int3 cell in ClipboardOperations.Everything(grid))
        {
            var coarse = new Int3(FloorDiv(cell.X, 2), FloorDiv(cell.Y, 2), FloorDiv(cell.Z, 2));
            if (!counts.TryGetValue(coarse, out Dictionary<byte, int>? colours))
            {
                counts[coarse] = colours = [];
            }

            byte value = grid.GetVoxel(cell);
            colours[value] = colours.GetValueOrDefault(value) + 1;
        }

        var result = new VoxelWorld();
        result.ReplacePalette(grid.Palette);
        foreach ((Int3 coarse, Dictionary<byte, int> colours) in counts)
        {
            if (colours.Values.Sum() >= 4)
            {
                result.SetVoxel(coarse, colours.MaxBy(pair => pair.Value).Key);
            }
        }

        return result;
    }

    /// <summary>
    /// The model <paramref name="factor"/> times the size, in voxels of the same size — each new voxel
    /// the one of the old it falls in. Whole factors are exact; a fraction samples.
    /// </summary>
    public static VoxelWorld Scale(VoxelWorld grid, float factor)
    {
        var result = new VoxelWorld();
        result.ReplacePalette(grid.Palette);
        if (!grid.TryGetBounds(out Int3 min, out Int3 max) || !(factor > 0f))
        {
            return result;
        }

        Int3 low = new((int)MathF.Floor(min.X * factor), (int)MathF.Floor(min.Y * factor), (int)MathF.Floor(min.Z * factor));
        Int3 high = new((int)MathF.Ceiling((max.X + 1) * factor) - 1, (int)MathF.Ceiling((max.Y + 1) * factor) - 1, (int)MathF.Ceiling((max.Z + 1) * factor) - 1);

        for (int x = low.X; x <= high.X; x++)
        {
            for (int y = low.Y; y <= high.Y; y++)
            {
                for (int z = low.Z; z <= high.Z; z++)
                {
                    var source = new Int3(
                        (int)MathF.Floor((x + 0.5f) / factor),
                        (int)MathF.Floor((y + 0.5f) / factor),
                        (int)MathF.Floor((z + 0.5f) / factor));

                    byte value = grid.GetVoxel(source);
                    if (value != Palette.EmptyIndex)
                    {
                        result.SetVoxel(x, y, z, value);
                    }
                }
            }
        }

        return result;
    }

    private static int FloorDiv(int value, int by) => (int)MathF.Floor(value / (float)by);
}

/// <summary>An object's whole grid and placement swapped for others, reversibly: resampling.</summary>
public sealed class ReplaceGridCommand(VoxelObject target, VoxelWorld after, ObjectTransform afterTransform, string name) : ICommand
{
    private readonly VoxelWorld _before = target.Grid.Copy();
    private readonly ObjectTransform _beforeTransform = target.Transform;

    public string Name { get; } = name;

    public int RetainedCells { get; } = target.Grid.SolidCount + after.SolidCount;

    public void Redo()
    {
        target.Grid.ReplaceWith(after.Copy());
        target.Transform = afterTransform;
    }

    public void Undo()
    {
        target.Grid.ReplaceWith(_before.Copy());
        target.Transform = _beforeTransform;
    }
}
