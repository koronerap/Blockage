using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// A detached block of voxels — the clipboard, and the scratch buffer every region operation reads
/// through. Copying the region before writing is what makes mirror and move safe when the source
/// and destination overlap.
/// </summary>
public sealed class VoxelClip
{
    private readonly byte[] _indices;

    public VoxelClip(Int3 size)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "A clip needs a positive size.");
        }

        Size = size;
        _indices = new byte[(long)size.X * size.Y * size.Z <= int.MaxValue
            ? size.X * size.Y * size.Z
            : throw new ArgumentOutOfRangeException(nameof(size), "Region is too large to copy.")];
    }

    public Int3 Size { get; }

    public int CellCount => _indices.Length;

    /// <summary>Number of non-empty cells; what the paste actually contributes.</summary>
    public int SolidCount
    {
        get
        {
            int count = 0;
            foreach (byte index in _indices)
            {
                if (index != Palette.EmptyIndex)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public byte this[int x, int y, int z]
    {
        get => _indices[LinearIndex(x, y, z)];
        set => _indices[LinearIndex(x, y, z)] = value;
    }

    private int LinearIndex(int x, int y, int z) => (y * Size.Z + z) * Size.X + x;

    public static VoxelClip Copy(VoxelWorld world, VoxelBox box)
    {
        var clip = new VoxelClip(box.Size);

        for (int y = 0; y < clip.Size.Y; y++)
        {
            for (int z = 0; z < clip.Size.Z; z++)
            {
                for (int x = 0; x < clip.Size.X; x++)
                {
                    clip[x, y, z] = world.GetVoxel(box.Min + new Int3(x, y, z));
                }
            }
        }

        return clip;
    }

    /// <summary>
    /// Writes the clip with its minimum corner at <paramref name="origin"/>.
    /// </summary>
    /// <param name="skipEmpty">
    /// True stamps only the solid cells, leaving whatever is already there — what pasting a shape
    /// into an existing level should do. False writes the empty cells too, which is what mirror and
    /// move need so they do not leave the original behind.
    /// </param>
    public int Paste(VoxelWorld world, Int3 origin, VoxelEditCommand command, bool skipEmpty = true)
    {
        int changed = 0;

        for (int y = 0; y < Size.Y; y++)
        {
            for (int z = 0; z < Size.Z; z++)
            {
                for (int x = 0; x < Size.X; x++)
                {
                    byte index = this[x, y, z];
                    if (skipEmpty && index == Palette.EmptyIndex)
                    {
                        continue;
                    }

                    if (command.Apply(world, origin + new Int3(x, y, z), index))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>A copy flipped along one axis.</summary>
    public VoxelClip Mirrored(Axis axis)
    {
        var mirrored = new VoxelClip(Size);

        for (int y = 0; y < Size.Y; y++)
        {
            for (int z = 0; z < Size.Z; z++)
            {
                for (int x = 0; x < Size.X; x++)
                {
                    (int sx, int sy, int sz) = axis switch
                    {
                        Axis.X => (Size.X - 1 - x, y, z),
                        Axis.Y => (x, Size.Y - 1 - y, z),
                        _ => (x, y, Size.Z - 1 - z),
                    };

                    mirrored[x, y, z] = this[sx, sy, sz];
                }
            }
        }

        return mirrored;
    }

    /// <summary>The box this clip would occupy if pasted at an origin.</summary>
    public VoxelBox BoxAt(Int3 origin) => new(origin, origin + Size - Int3.One);
}
