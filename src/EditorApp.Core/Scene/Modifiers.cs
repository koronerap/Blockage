using EditorApp.Core.Editing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Scene;

/// <summary>What a modifier does to what an object shows.</summary>
public enum ModifierKind
{
    /// <summary>The voxels, and their mirror image across a plane of the object's own lattice.</summary>
    Mirror,

    /// <summary>The voxels, and copies of them in a row, a set step apart.</summary>
    Array,
}

/// <summary>
/// A non-destructive change drawn over an object's own voxels (Fullreleaseplan 3.10): shown and
/// exported, but the voxels the tools edit stay what they were, so the mirror or the row can be
/// changed, switched off or taken away at any time — or applied, and made voxels for good.
/// </summary>
/// <param name="Plane">
/// Mirror: the lattice line the mirror stands on, counted in cells — 0 is the object's origin, so a
/// half built on the positive side is completed on the negative one.
/// </param>
/// <param name="Count">Array: how many there are in the row, the original included.</param>
/// <param name="Step">Array: how many voxels one copy stands from the next; negative runs the other way.</param>
public sealed record VoxelModifier(
    ModifierKind Kind,
    Axis Axis = Axis.X,
    int Plane = 0,
    int Count = 3,
    int Step = 8,
    bool Enabled = true)
{
    /// <summary>A row longer than this is a level, not a modifier.</summary>
    public const int MaxCount = 64;

    public const int MaxStep = 4096;

    /// <summary>The same with every number held inside what it may be.</summary>
    public VoxelModifier Clamped() => this with
    {
        Count = Math.Clamp(Count, 1, MaxCount),
        Step = Math.Clamp(Step, -MaxStep, MaxStep),
        Plane = Math.Clamp(Plane, -MaxStep, MaxStep),
    };

    /// <summary>A few words for a list: "Mirror X", "Array Z x4".</summary>
    public string Label => Kind == ModifierKind.Mirror ? $"Mirror {Axis}" : $"Array {Axis} x{Count}";
}

/// <summary>
/// An object's shown grid: its own voxels, and every copy its modifiers make of them. Each modifier
/// is a set of lattice maps — the images of a cell — composed in order; a shown cell is the first of
/// its preimages that is solid, the original winning over its copies. Kept up to date chunk by chunk
/// as the voxels change, so a brush stroke on a mirrored object costs the chunks it touches, not the
/// whole object again.
/// </summary>
internal sealed class ModifierEvaluator
{
    /// <summary>Past this many images the modifiers are refused rather than the editor brought to its knees.</summary>
    public const int MaxImages = 512;

    private readonly List<LatticeMap> _maps;
    private readonly List<LatticeMap> _inverses;

    public ModifierEvaluator(VoxelWorld source, IReadOnlyList<VoxelModifier> modifiers)
    {
        _maps = Images(modifiers);
        _inverses = [.. _maps.Select(Inverse)];

        Shown = new VoxelWorld();
        Shown.ReplacePalette(source.Palette);

        var written = new HashSet<Int3>();
        List<Int3> cells = [.. ClipboardOperations.Everything(source)];
        foreach (LatticeMap map in _maps)
        {
            foreach (Int3 cell in cells)
            {
                Int3 image = map.Cell(cell);
                if (written.Add(image))
                {
                    Write(source, cell, map, image);
                }
            }
        }
    }

    public VoxelWorld Shown { get; }

    /// <summary>The source's cells in these chunks changed: every image of them is worked out again.</summary>
    public void Update(VoxelWorld source, IEnumerable<ChunkCoord> dirty)
    {
        var affected = new HashSet<Int3>();
        foreach (ChunkCoord coord in dirty)
        {
            Int3 origin = new(coord.X * Chunk.Size, coord.Y * Chunk.Size, coord.Z * Chunk.Size);
            for (int x = 0; x < Chunk.Size; x++)
            {
                for (int y = 0; y < Chunk.Size; y++)
                {
                    for (int z = 0; z < Chunk.Size; z++)
                    {
                        Int3 cell = origin + new Int3(x, y, z);
                        foreach (LatticeMap map in _maps)
                        {
                            affected.Add(map.Cell(cell));
                        }
                    }
                }
            }
        }

        foreach (Int3 image in affected)
        {
            Recompute(source, image);
        }
    }

    private void Recompute(VoxelWorld source, Int3 image)
    {
        for (int i = 0; i < _maps.Count; i++)
        {
            Int3 cell = _inverses[i].Cell(image);
            if (source.IsSolid(cell))
            {
                Write(source, cell, _maps[i], image);
                return;
            }
        }

        Shown.SetVoxel(image, Palette.EmptyIndex);
    }

    private void Write(VoxelWorld source, Int3 cell, LatticeMap map, Int3 image)
    {
        byte value = source.GetVoxel(cell);
        Shown.SetVoxel(image, value);

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            Shown.SetFaceColor(image, map.Face((Face)f), source.GetFaceColor(cell, (Face)f));
        }
    }

    /// <summary>
    /// The maps a stack of modifiers makes: the identity first, then, modifier by modifier, what each
    /// does to everything before it — a mirror adds the mirror image of each, an array a copy of each a
    /// step further along.
    /// </summary>
    public static List<LatticeMap> Images(IReadOnlyList<VoxelModifier> modifiers)
    {
        List<LatticeMap> maps = [Identity];

        foreach (VoxelModifier raw in modifiers)
        {
            if (!raw.Enabled)
            {
                continue;
            }

            VoxelModifier modifier = raw.Clamped();
            int axis = (int)modifier.Axis;
            var next = new List<LatticeMap>(maps);

            if (modifier.Kind == ModifierKind.Mirror)
            {
                LatticeMap mirror = Mirror(axis, modifier.Plane);
                next.AddRange(maps.Select(m => Compose(mirror, m)));
            }
            else
            {
                for (int k = 1; k < modifier.Count; k++)
                {
                    LatticeMap shift = Identity with { Origin = VoxelBox.WithComponent(Int3.Zero, axis, k * modifier.Step) };
                    next.AddRange(maps.Select(m => Compose(shift, m)));
                }
            }

            if (next.Count > MaxImages)
            {
                break;
            }

            maps = next;
        }

        return maps;
    }

    private static readonly LatticeMap Identity = new(Int3.Zero, new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1));

    /// <summary>A cell index c goes to 2P − 1 − c along the axis: the cell either side of the line P swaps.</summary>
    private static LatticeMap Mirror(int axis, int plane) => axis switch
    {
        0 => new LatticeMap(new Int3((2 * plane) - 1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, 1)),
        1 => new LatticeMap(new Int3(0, (2 * plane) - 1, 0), new Int3(1, 0, 0), new Int3(0, -1, 0), new Int3(0, 0, 1)),
        _ => new LatticeMap(new Int3(0, 0, (2 * plane) - 1), new Int3(1, 0, 0), new Int3(0, 1, 0), new Int3(0, 0, -1)),
    };

    /// <summary>First <paramref name="inner"/>, then <paramref name="outer"/>.</summary>
    private static LatticeMap Compose(LatticeMap outer, LatticeMap inner) => new(
        outer.Cell(inner.Origin),
        outer.Cell(inner.X) - outer.Origin,
        outer.Cell(inner.Y) - outer.Origin,
        outer.Cell(inner.Z) - outer.Origin);

    /// <summary>The way back: the turn's transpose, since every turn and mirror of the lattice is orthogonal.</summary>
    private static LatticeMap Inverse(LatticeMap map)
    {
        var x = new Int3(map.X.X, map.Y.X, map.Z.X);
        var y = new Int3(map.X.Y, map.Y.Y, map.Z.Y);
        var z = new Int3(map.X.Z, map.Y.Z, map.Z.Z);
        var back = new LatticeMap(Int3.Zero, x, y, z);
        Int3 origin = back.Cell(map.Origin);
        return back with { Origin = new Int3(-origin.X, -origin.Y, -origin.Z) };
    }
}
