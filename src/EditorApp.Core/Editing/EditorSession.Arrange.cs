using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>Which side of the things lines up: their low edges, their middles or their high edges.</summary>
public enum AlignEdge
{
    Min,
    Centre,
    Max,
}

/// <summary>
/// Lining things up and spacing them out (Fullreleaseplan 6.6): the selection aligned to the active
/// object by an edge or the middle, or spread evenly between the two furthest apart. Each is one
/// undo step; children go with their parents, and are not moved twice.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>Everything selected lined up with the active one along <paramref name="axis"/>. Returns how many moved.</summary>
    public int AlignSelected(Axis axis, AlignEdge edge)
    {
        List<IPlaceable> things = Arrangeable();
        if (things.Count < 2 || FindPlaceable(ActiveId) is not { } active || !things.Contains(active))
        {
            return 0;
        }

        float target = Edge(active, axis, edge);
        var moves = things.Where(t => t != active).Select(t => (t, target - Edge(t, axis, edge))).ToList();
        return Move(moves, axis, $"Align {Several("", things).Trim()}");
    }

    /// <summary>
    /// Everything selected spread along <paramref name="axis"/> so their middles are evenly spaced
    /// between the two furthest apart, which stay. Returns how many moved; three are needed.
    /// </summary>
    public int DistributeSelected(Axis axis)
    {
        List<IPlaceable> things = [.. Arrangeable().OrderBy(t => Edge(t, axis, AlignEdge.Centre))];
        if (things.Count < 3)
        {
            return 0;
        }

        float first = Edge(things[0], axis, AlignEdge.Centre);
        float last = Edge(things[^1], axis, AlignEdge.Centre);
        var moves = new List<(IPlaceable, float)>();
        for (int i = 1; i < things.Count - 1; i++)
        {
            float wanted = first + ((last - first) * i / (things.Count - 1));
            moves.Add((things[i], wanted - Edge(things[i], axis, AlignEdge.Centre)));
        }

        return Move(moves, axis, $"Distribute {things.Count} objects");
    }

    /// <summary>What is selected and may be moved, leaving out anything whose parent is moved with it.</summary>
    private List<IPlaceable> Arrangeable()
    {
        List<IPlaceable> things =
        [
            .. Scene.SelectedObjects.Where(o => !o.Locked && (!o.IsEmpty || o.IsMarker)),
            .. Scene.SelectedLights.Where(l => !l.Locked),
        ];

        HashSet<int> ids = [.. things.Select(t => t.Id)];
        return [.. things.Where(t => !HasAncestorIn(t, ids))];
    }

    private bool HasAncestorIn(IPlaceable thing, HashSet<int> ids)
    {
        int hops = 0;
        for (VoxelObject? parent = Scene.ParentOf(thing); parent is not null && hops <= Scene.Objects.Count; parent = Scene.ParentOf(parent), hops++)
        {
            if (ids.Contains(parent.Id))
            {
                return true;
            }
        }

        return false;
    }

    private IPlaceable? FindPlaceable(int id) => Scene.FindPlaceable(id);

    /// <summary>A thing's low edge, middle or high edge along an axis, in the world: an object by its box, the rest by where they stand.</summary>
    private static float Edge(IPlaceable thing, Axis axis, AlignEdge edge)
    {
        Vector3 min, max;
        if (thing is VoxelObject o && o.TryGetWorldBounds(out Vector3 low, out Vector3 high))
        {
            (min, max) = (low, high);
        }
        else
        {
            min = max = thing.Transform.Position;
        }

        int index = (int)axis;
        return edge switch
        {
            AlignEdge.Min => min[index],
            AlignEdge.Max => max[index],
            _ => (min[index] + max[index]) * 0.5f,
        };
    }

    private int Move(List<(IPlaceable Thing, float By)> moves, Axis axis, string name)
    {
        Vector3 direction = axis switch
        {
            Axis.X => Vector3.UnitX,
            Axis.Y => Vector3.UnitY,
            _ => Vector3.UnitZ,
        };

        EndStroke();
        CancelExtrude();

        var moved = new List<(IPlaceable Target, ObjectTransform Before)>();
        foreach ((IPlaceable thing, float by) in moves)
        {
            if (MathF.Abs(by) < 1e-5f)
            {
                continue;
            }

            moved.Add((thing, thing.Transform));
            ApplyTransform(thing, thing.Transform.Translated(direction * by));
        }

        PushTransformEdits(moved, name);
        return moved.Count;
    }
}
