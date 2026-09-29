using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>How scattered copies are turned.</summary>
public enum ScatterTurn
{
    /// <summary>As the original is.</summary>
    None,

    /// <summary>A random quarter turn about the upright, staying on the lattice.</summary>
    Quarter,

    /// <summary>Any angle about the upright.</summary>
    Free,
}

/// <summary>
/// How to scatter: how many copies, how far apart at the least, how they are turned, whether they
/// share the originals' voxels, and the seed — the same seed scatters the same way again.
/// </summary>
public sealed record ScatterSettings(int Count = 20, float Spacing = 3f, ScatterTurn Turn = ScatterTurn.Quarter, bool Linked = true, int Seed = 1)
{
    public const int MaxCount = 2000;

    public ScatterSettings Clamped() => this with
    {
        Count = Math.Clamp(Count, 1, MaxCount),
        Spacing = Math.Clamp(Spacing, 0f, 1000f),
    };
}

/// <summary>
/// The scatter (Fullreleaseplan 6.7): copies of the selected objects — trees, rocks, tufts of grass —
/// set down at random on the upward faces of the active one, the ground, turned at random and kept
/// apart. One undo step; changing the settings straight after scatters again in its place, as
/// Blender's Adjust Last Operation does.
/// </summary>
public sealed partial class EditorSession
{
    private (int Ground, int[] Sources, ICommand Command)? _lastScatter;

    /// <summary>Whether the last thing done was a scatter that can still be scattered again differently.</summary>
    public bool CanAdjustScatter => _lastScatter is { } last && ReferenceEquals(History.LastDone, last.Command);

    /// <summary>What a scatter now would put where, or why it cannot: the ground is the active object, the rest are what is scattered.</summary>
    public string? ScatterProblem()
    {
        if (Scene.Focus is not { IsEmpty: false } ground)
        {
            return "Choose the ground last, so it is the active object, with what to scatter selected before it.";
        }

        return Scene.SelectedObjects.Any(o => o.Id != ground.Id && (!o.IsEmpty || o.IsMarker))
            ? null
            : $"Select what to scatter as well as {ground.Name}, the ground.";
    }

    /// <summary>Copies of the selected objects scattered over the active one. The copies are selected; none when it cannot be done.</summary>
    public IReadOnlyList<VoxelObject> Scatter(ScatterSettings settings)
    {
        if (ScatterProblem() is not null || Scene.Focus is not { } ground)
        {
            return [];
        }

        int[] sources = [.. Scene.SelectedObjects.Where(o => o.Id != ground.Id && (!o.IsEmpty || o.IsMarker)).Select(o => o.Id)];
        return ScatterFrom(ground.Id, sources, settings);
    }

    /// <summary>The last scatter done again with other settings, in its place. Nothing when it is no longer the last thing done.</summary>
    public IReadOnlyList<VoxelObject> AdjustScatter(ScatterSettings settings)
    {
        if (!CanAdjustScatter || _lastScatter is not { } last)
        {
            return [];
        }

        Undo();
        return ScatterFrom(last.Ground, last.Sources, settings);
    }

    private IReadOnlyList<VoxelObject> ScatterFrom(int groundId, int[] sourceIds, ScatterSettings settings)
    {
        settings = settings.Clamped();
        if (Scene.Find(groundId) is not { } ground)
        {
            return [];
        }

        List<VoxelObject> sources = [.. sourceIds.Select(Scene.Find).OfType<VoxelObject>()];
        if (sources.Count == 0)
        {
            return [];
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        var random = new Random(settings.Seed);
        List<Vector3> spots = Spots(ground, settings, random);
        var steps = new List<ICommand>();
        var made = new List<VoxelObject>();

        foreach (Vector3 spot in spots)
        {
            VoxelObject source = sources[random.Next(sources.Count)];
            float angle = settings.Turn switch
            {
                ScatterTurn.Quarter => random.Next(4) * (MathF.PI / 2f),
                ScatterTurn.Free => (float)(random.NextDouble() * MathF.Tau),
                _ => 0f,
            };

            Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle) * source.Transform.Rotation);
            float size = source.VoxelSize;

            // The middle of its underside on the spot, whichever way it is turned.
            Vector3 foot = Vector3.Zero;
            if (source.TryGetLocalBounds(out Vector3 low, out Vector3 high))
            {
                foot = new Vector3((low.X + high.X) * 0.5f, low.Y, (low.Z + high.Z) * 0.5f) * size;
            }

            Vector3 position = spot - Vector3.Transform(foot, rotation);
            var command = new CreateObjectCommand(
                Scene,
                settings.Linked ? source.Grid : source.Grid.Copy(),
                new ObjectTransform(position, rotation, size),
                DuplicateName(source.Name, Scene.Objects.Select(o => o.Name)),
                "Scatter",
                collectionId: source.CollectionId);

            command.Redo();
            command.Created!.CopyDataFrom(source);
            steps.Add(command);
            made.Add(command.Created!);
        }

        if (steps.Count == 0)
        {
            return [];
        }

        ICommand scatter = CompositeCommand.Of($"Scatter {steps.Count} on {ground.Name}", steps);
        History.Push(scatter);
        _lastScatter = (groundId, sourceIds, scatter);
        SelectOnly([.. made.Select(o => o.Id)]);
        HasUnsavedChanges = true;
        return made;
    }

    /// <summary>
    /// Where copies go: tops of the ground's columns that face up in the world, in random order,
    /// each at least the spacing from those chosen before it, as many as asked for or as fit.
    /// </summary>
    private static List<Vector3> Spots(VoxelObject ground, ScatterSettings settings, Random random)
    {
        var tops = new List<Vector3>();
        Vector3 up = Vector3.Normalize(ground.Transform.TransformDirection(Vector3.UnitY));
        if (up.Y < 0.7f)
        {
            return tops;
        }

        VoxelWorld grid = ground.Grid;
        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            Int3 origin = coord.Origin;
            for (int x = 0; x < Chunk.Size; x++)
            for (int z = 0; z < Chunk.Size; z++)
            for (int y = 0; y < Chunk.Size; y++)
            {
                if (chunk.Get(x, y, z) == Palette.EmptyIndex)
                {
                    continue;
                }

                int wx = origin.X + x, wy = origin.Y + y, wz = origin.Z + z;
                if (!grid.IsSolid(wx, wy + 1, wz))
                {
                    tops.Add(ground.Transform.TransformPoint(new Vector3(wx + 0.5f, wy + 1f, wz + 0.5f)));
                }
            }
        }

        // A fair shuffle, then the first that keep their distance.
        for (int i = tops.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (tops[i], tops[j]) = (tops[j], tops[i]);
        }

        var chosen = new List<Vector3>();
        float apart = settings.Spacing * settings.Spacing;
        foreach (Vector3 top in tops)
        {
            if (chosen.Count >= settings.Count)
            {
                break;
            }

            if (chosen.All(other => Vector3.DistanceSquared(other, top) >= apart))
            {
                chosen.Add(top);
            }
        }

        return chosen;
    }
}
