using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// One mirror image of an object's lattice: some of its axes flipped across their planes.
///
/// A plane is kept as a sum rather than a position: a cell <c>c</c> and its image <c>Sum - c</c>
/// sit either side of it. That keeps a plane between two cells and a plane through the middle of one
/// equally exact — the first is an odd sum, the second an even one — with no half voxels anywhere.
/// </summary>
public readonly record struct MirrorImage(bool X, bool Y, bool Z, Int3 Sum)
{
    public Int3 Cell(Int3 cell) => new(
        X ? Sum.X - cell.X : cell.X,
        Y ? Sum.Y - cell.Y : cell.Y,
        Z ? Sum.Z - cell.Z : cell.Z);

    /// <summary>A face across a flipped axis turns round; one along the others stays as it was.</summary>
    public Face Face(Face face)
    {
        bool flipped = FaceInfo.Axis(face) switch
        {
            0 => X,
            1 => Y,
            _ => Z,
        };

        return flipped ? FaceInfo.Opposite(face) : face;
    }
}

/// <summary>
/// Live symmetry: Paint and Extrude repeat every write across mirror planes on the chosen axes, in
/// the same undo step, so a symmetric model is built from one side. MagicaVoxel's mirror, or
/// Blender's.
///
/// Each object's planes are fixed the first time they are needed — through the middle of what it
/// holds at that moment — and then stay put while it is edited. Planes that followed the bounds would
/// wander off as soon as one side grew. Turning symmetry on from off, or Recentre, sets them afresh.
///
/// A tool setting, like the brush size: not saved with the level.
/// </summary>
public sealed class Symmetry
{
    private readonly Dictionary<int, Int3> _sums = [];
    private bool _x;
    private bool _y;
    private bool _z;

    public bool X
    {
        get => _x;
        set => Switch(ref _x, value);
    }

    public bool Y
    {
        get => _y;
        set => Switch(ref _y, value);
    }

    public bool Z
    {
        get => _z;
        set => Switch(ref _z, value);
    }

    public bool IsOn => _x || _y || _z;

    public bool this[Axis axis]
    {
        get => axis switch
        {
            Axis.X => _x,
            Axis.Y => _y,
            _ => _z,
        };
        set
        {
            switch (axis)
            {
                case Axis.X: X = value; break;
                case Axis.Y: Y = value; break;
                default: Z = value; break;
            }
        }
    }

    private void Switch(ref bool axis, bool value)
    {
        // Coming on from off means starting over: the planes go through what is there now.
        if (value && !IsOn)
        {
            _sums.Clear();
        }

        axis = value;
    }

    /// <summary>An object's plane sums, fixed from its bounds the first time they are asked for.</summary>
    public Int3 SumsFor(VoxelObject target)
    {
        if (!_sums.TryGetValue(target.Id, out Int3 sums))
        {
            sums = target.Grid.TryGetBounds(out Int3 min, out Int3 max) ? min + max : Int3.Zero;
            _sums[target.Id] = sums;
        }

        return sums;
    }

    /// <summary>Where the planes are in the object's own space: halfway between a cell and its image.</summary>
    public Vector3 PlanesFor(VoxelObject target) => (SumsFor(target).ToVector3() + Vector3.One) * 0.5f;

    /// <summary>Puts an object's planes back through the middle of what it holds now.</summary>
    public void Recentre(VoxelObject target) => _sums.Remove(target.Id);

    /// <summary>
    /// Keeps an object's planes where they were through a subdivide. The planes are fixed in cells,
    /// and the cells just got smaller: a box from min to max becomes f·min to f·max + f - 1, so the
    /// sum that places a plane becomes f·sum + f - 1 — the same plane, counted in the new cells.
    /// Recentring instead would move a plane that was deliberately left off-centre.
    /// </summary>
    public void Subdivided(VoxelObject target, int factor)
    {
        if (_sums.TryGetValue(target.Id, out Int3 sums))
        {
            _sums[target.Id] = (sums * factor) + new Int3(factor - 1, factor - 1, factor - 1);
        }
    }

    /// <summary>The reverse of <see cref="Subdivided"/>, for its undo.</summary>
    public void Unsubdivided(VoxelObject target, int factor)
    {
        if (_sums.TryGetValue(target.Id, out Int3 sums))
        {
            Int3 whole = sums - new Int3(factor - 1, factor - 1, factor - 1);
            _sums[target.Id] = new Int3(whole.X / factor, whole.Y / factor, whole.Z / factor);
        }
    }

    /// <summary>Every plane forgotten — the level was replaced, and ids mean other objects now.</summary>
    public void Forget() => _sums.Clear();

    /// <summary>
    /// The images a write on this object is repeated at: every combination of the axes that are on,
    /// so X and Z give the X image, the Z image and the one flipped across both. None when off.
    /// </summary>
    public IReadOnlyList<MirrorImage> ImagesFor(VoxelObject? target)
    {
        if (!IsOn || target is null)
        {
            return [];
        }

        Int3 sums = SumsFor(target);
        var images = new List<MirrorImage>(7);

        for (int mask = 1; mask < 8; mask++)
        {
            bool x = (mask & 1) != 0;
            bool y = (mask & 2) != 0;
            bool z = (mask & 4) != 0;

            if ((x && !_x) || (y && !_y) || (z && !_z))
            {
                continue;
            }

            images.Add(new MirrorImage(x, y, z, sums));
        }

        return images;
    }
}
