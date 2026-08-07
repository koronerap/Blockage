using System.Numerics;

namespace EditorApp.Core.Voxels;

/// <summary>One of the six axis-aligned faces of a voxel, identified by its outward normal.</summary>
public enum Face : byte
{
    PosX = 0,
    NegX = 1,
    PosY = 2,
    NegY = 3,
    PosZ = 4,
    NegZ = 5,
}

/// <summary>Per-face lookup tables: neighbour offsets, normals, quad corners and editor shading.</summary>
public static class FaceInfo
{
    public const int Count = 6;

    /// <summary>Offset from a voxel to the neighbour sharing this face.</summary>
    private static readonly Int3[] Offsets =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];

    private static readonly Vector3[] Normals =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];

    /// <summary>
    /// Flat shade baked per face at mesh build time so a voxel volume reads as a volume without any
    /// lighting math. Editor-only: it never reaches the exported file (see EditorApp.md §10.4).
    /// </summary>
    private static readonly float[] Shades = [0.80f, 0.80f, 1.00f, 0.50f, 0.65f, 0.65f];

    /// <summary>
    /// The four corners of each face in counter-clockwise order seen from outside, as offsets from
    /// the voxel's minimum corner. Quads are emitted as triangles (0,1,2) and (0,2,3).
    /// </summary>
    private static readonly Int3[] Corners =
    [
        // PosX (x = 1)
        new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1),
        // NegX (x = 0)
        new(0, 0, 0), new(0, 0, 1), new(0, 1, 1), new(0, 1, 0),
        // PosY (y = 1)
        new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0),
        // NegY (y = 0)
        new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1),
        // PosZ (z = 1)
        new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        // NegZ (z = 0)
        new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0),
    ];

    public static Int3 Offset(Face face) => Offsets[(int)face];

    public static Vector3 Normal(Face face) => Normals[(int)face];

    public static float Shade(Face face) => Shades[(int)face];

    public static Int3 Corner(Face face, int corner) => Corners[(int)face * 4 + corner];

    public static Face Opposite(Face face) => (Face)((int)face ^ 1);

    /// <summary>The axis this face's normal points along: 0 = X, 1 = Y, 2 = Z.</summary>
    public static int Axis(Face face) => (int)face >> 1;

    /// <summary>True when the face normal points along the positive direction of its axis.</summary>
    public static bool IsPositive(Face face) => ((int)face & 1) == 0;
}
