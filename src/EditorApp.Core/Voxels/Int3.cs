using System.Numerics;

namespace EditorApp.Core.Voxels;

/// <summary>Integer 3D vector used for voxel coordinates.</summary>
public readonly record struct Int3(int X, int Y, int Z)
{
    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 One = new(1, 1, 1);

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator *(Int3 a, int s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Int3 Min(Int3 a, Int3 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    public static Int3 Max(Int3 a, Int3 b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public Vector3 ToVector3() => new(X, Y, Z);

    public override string ToString() => $"({X}, {Y}, {Z})";
}
