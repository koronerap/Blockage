using System.Numerics;

namespace EditorApp.Core.Import;

/// <summary>
/// A model loaded purely to look at while modelling — a blockout to trace, or the thing the level
/// has to fit around. It is never converted to voxels and never exported; it exists only as a
/// visual guide, so a flat-shaded triangle soup is all it needs to be.
/// </summary>
public sealed class ReferenceMesh(string name, Vector3[] positions, Vector3[] normals)
{
    public string Name { get; } = name;

    /// <summary>Three positions per triangle; vertices are not shared.</summary>
    public Vector3[] Positions { get; } = positions;

    /// <summary>Flat face normal, repeated for each of the triangle's three vertices.</summary>
    public Vector3[] Normals { get; } = normals;

    public int TriangleCount => Positions.Length / 3;

    public bool IsEmpty => Positions.Length == 0;

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Positions.Length == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        Vector3 min = Positions[0];
        Vector3 max = Positions[0];

        foreach (Vector3 position in Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return (min, max);
    }
}

/// <summary>Raised when a reference model cannot be read.</summary>
public sealed class ReferenceImportException(string message, Exception? inner = null)
    : Exception(message, inner);
