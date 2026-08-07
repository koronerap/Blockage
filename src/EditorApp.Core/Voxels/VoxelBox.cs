using System.Numerics;

namespace EditorApp.Core.Voxels;

/// <summary>A world axis: 0 = X, 1 = Y, 2 = Z, matching <see cref="FaceInfo.Axis"/>.</summary>
public enum Axis : byte
{
    X = 0,
    Y = 1,
    Z = 2,
}

/// <summary>An inclusive axis-aligned box of voxel cells. Used for selections, clips and bounds.</summary>
public readonly record struct VoxelBox(Int3 Min, Int3 Max)
{
    /// <summary>Builds the box spanning two corners, in either order.</summary>
    public static VoxelBox FromCorners(Int3 a, Int3 b) => new(Int3.Min(a, b), Int3.Max(a, b));

    public static VoxelBox Single(Int3 cell) => new(cell, cell);

    public Int3 Size => new(Max.X - Min.X + 1, Max.Y - Min.Y + 1, Max.Z - Min.Z + 1);

    public long Volume
    {
        get
        {
            Int3 size = Size;
            return (long)size.X * size.Y * size.Z;
        }
    }

    public bool Contains(Int3 cell) =>
        cell.X >= Min.X && cell.X <= Max.X
        && cell.Y >= Min.Y && cell.Y <= Max.Y
        && cell.Z >= Min.Z && cell.Z <= Max.Z;

    public VoxelBox Translate(Int3 delta) => new(Min + delta, Max + delta);

    public VoxelBox Expand(int amount)
    {
        var offset = new Int3(amount, amount, amount);
        return new VoxelBox(Min - offset, Max + offset);
    }

    /// <summary>
    /// The one-voxel-thick slab on the given side of the box — the layer an extrude copies outward.
    /// </summary>
    public VoxelBox FaceSlab(Face face)
    {
        int axis = FaceInfo.Axis(face);
        int plane = FaceInfo.IsPositive(face) ? Component(Max, axis) : Component(Min, axis);

        Int3 min = WithComponent(Min, axis, plane);
        Int3 max = WithComponent(Max, axis, plane);
        return new VoxelBox(min, max);
    }

    /// <summary>Corners in world units, where a cell spans one unit — so the box's outer surface.</summary>
    public (Vector3 Min, Vector3 Max) ToWorldBounds() =>
        (Min.ToVector3(), Max.ToVector3() + Vector3.One);

    public static int Component(Int3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };

    public static Int3 WithComponent(Int3 value, int axis, int component) => axis switch
    {
        0 => value with { X = component },
        1 => value with { Y = component },
        _ => value with { Z = component },
    };

    public override string ToString() => $"{Min} .. {Max} ({Size.X}x{Size.Y}x{Size.Z})";
}
