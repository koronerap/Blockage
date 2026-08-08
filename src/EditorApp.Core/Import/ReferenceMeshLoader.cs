using System.Globalization;
using System.Numerics;
using SharpGLTF.Schema2;

namespace EditorApp.Core.Import;

/// <summary>
/// Loads a reference model from OBJ, glTF or GLB. Everything is flattened into world-space
/// triangles with flat normals — materials, textures and hierarchy are irrelevant to something
/// that is only ever drawn as a guide.
/// </summary>
public static class ReferenceMeshLoader
{
    /// <summary>Guard against dragging in a film-scale asset by accident.</summary>
    public const int TriangleLimit = 2_000_000;

    public static readonly string[] SupportedExtensions = [".obj", ".gltf", ".glb"];

    public static ReferenceMesh Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ReferenceImportException($"No such file: {path}");
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        string name = Path.GetFileName(path);

        return extension switch
        {
            ".obj" => LoadObj(path, name),
            ".gltf" or ".glb" => LoadGltf(path, name),
            _ => throw new ReferenceImportException(
                $"Unsupported reference format '{extension}'. Supported: {string.Join(", ", SupportedExtensions)}."),
        };
    }

    private static ReferenceMesh LoadGltf(string path, string name)
    {
        ModelRoot model;
        try
        {
            model = ModelRoot.Load(path);
        }
        catch (Exception exception)
        {
            throw new ReferenceImportException($"Could not read {name}: {exception.Message}", exception);
        }

        var positions = new List<Vector3>();

        // Fully qualified: EditorApp.Core.Scene is a namespace of ours, and SharpGLTF has a type
        // with the same name.
        SharpGLTF.Schema2.Scene scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault()
            ?? throw new ReferenceImportException($"{name} contains no scene.");

        foreach (Node node in scene.VisualChildren)
        {
            AppendNode(node, positions);
        }

        if (positions.Count == 0)
        {
            throw new ReferenceImportException($"{name} contains no triangles.");
        }

        return Build(name, positions);
    }

    private static void AppendNode(Node node, List<Vector3> positions)
    {
        if (node.Mesh is { } mesh)
        {
            // WorldMatrix already folds in the whole parent chain, so nodes can be visited flat.
            Matrix4x4 transform = node.WorldMatrix;

            foreach (MeshPrimitive primitive in mesh.Primitives)
            {
                Accessor? accessor = primitive.GetVertexAccessor("POSITION");
                if (accessor is null)
                {
                    continue;
                }

                IList<Vector3> local = accessor.AsVector3Array();
                foreach ((int a, int b, int c) in primitive.GetTriangleIndices())
                {
                    if (positions.Count / 3 >= TriangleLimit)
                    {
                        return;
                    }

                    positions.Add(Vector3.Transform(local[a], transform));
                    positions.Add(Vector3.Transform(local[b], transform));
                    positions.Add(Vector3.Transform(local[c], transform));
                }
            }
        }

        foreach (Node child in node.VisualChildren)
        {
            AppendNode(child, positions);
        }
    }

    private static ReferenceMesh LoadObj(string path, string name)
    {
        var vertices = new List<Vector3>();
        var positions = new List<Vector3>();
        var face = new List<int>();

        foreach (string line in File.ReadLines(path))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (trimmed.StartsWith("v "))
            {
                vertices.Add(ParseVector(trimmed[2..], name));
            }
            else if (trimmed.StartsWith("f "))
            {
                ParseFace(trimmed[2..], vertices.Count, face, name);

                // OBJ faces may be any polygon; a triangle fan is correct for the convex faces a
                // voxel or blockout export produces.
                for (int i = 1; i + 1 < face.Count; i++)
                {
                    if (positions.Count / 3 >= TriangleLimit)
                    {
                        break;
                    }

                    positions.Add(vertices[face[0]]);
                    positions.Add(vertices[face[i]]);
                    positions.Add(vertices[face[i + 1]]);
                }
            }
        }

        if (positions.Count == 0)
        {
            throw new ReferenceImportException($"{name} contains no triangles.");
        }

        return Build(name, positions);
    }

    private static void ParseFace(ReadOnlySpan<char> span, int vertexCount, List<int> face, string name)
    {
        face.Clear();

        foreach (Range range in span.Split(' '))
        {
            ReadOnlySpan<char> token = span[range].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            // "v", "v/vt", "v//vn" and "v/vt/vn" all start with the position index.
            int slash = token.IndexOf('/');
            ReadOnlySpan<char> indexText = slash >= 0 ? token[..slash] : token;

            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                throw new ReferenceImportException($"{name}: malformed face index '{token}'.");
            }

            // OBJ indices are 1-based, and negative means "counting back from the end".
            int resolved = index > 0 ? index - 1 : vertexCount + index;
            if (resolved < 0 || resolved >= vertexCount)
            {
                throw new ReferenceImportException($"{name}: face index {index} is out of range.");
            }

            face.Add(resolved);
        }
    }

    private static Vector3 ParseVector(ReadOnlySpan<char> span, string name)
    {
        Span<float> components = stackalloc float[3];
        int count = 0;

        foreach (Range range in span.Split(' '))
        {
            ReadOnlySpan<char> token = span[range].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (count == 3)
            {
                break;   // trailing vertex colors on "v" lines are ignored
            }

            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out components[count]))
            {
                throw new ReferenceImportException($"{name}: malformed vertex '{token}'.");
            }

            count++;
        }

        if (count < 3)
        {
            throw new ReferenceImportException($"{name}: a vertex line needs three numbers.");
        }

        return new Vector3(components[0], components[1], components[2]);
    }

    /// <summary>Gives each triangle a flat normal, which is all a shaded guide needs.</summary>
    private static ReferenceMesh Build(string name, List<Vector3> positions)
    {
        var normals = new Vector3[positions.Count];

        for (int i = 0; i + 2 < positions.Count; i += 3)
        {
            Vector3 edge1 = positions[i + 1] - positions[i];
            Vector3 edge2 = positions[i + 2] - positions[i];
            Vector3 cross = Vector3.Cross(edge1, edge2);

            Vector3 normal = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.UnitY;
            normals[i] = normal;
            normals[i + 1] = normal;
            normals[i + 2] = normal;
        }

        return new ReferenceMesh(name, [.. positions], normals);
    }
}
