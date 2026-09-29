using System.Globalization;
using System.Numerics;
using EditorApp.Core.Voxels;
using SharpGLTF.Schema2;

namespace EditorApp.Core.Import;

/// <summary>
/// A triangle and what colours it: its material's colour, times its corners' colours or its
/// texture's, whichever it has — sampled wherever on it a voxel asks.
/// </summary>
public sealed class MeshTriangle(Vector3 a, Vector3 b, Vector3 c)
{
    public Vector3 A { get; } = a;

    public Vector3 B { get; } = b;

    public Vector3 C { get; } = c;

    /// <summary>The material's colour, in the display's (sRGB) values.</summary>
    public Color32 Colour { get; init; } = new(255, 255, 255);

    /// <summary>The texture over it, when it has one — PNG only.</summary>
    public DecodedImage? Texture { get; init; }

    /// <summary>Where each corner is on the texture: 0 to 1, from the texture's top left.</summary>
    public (Vector2 A, Vector2 B, Vector2 C) Uv { get; init; }

    /// <summary>The corners' own colours, when the mesh has them.</summary>
    public (Color32 A, Color32 B, Color32 C)? Corners { get; init; }

    /// <summary>The colour at a point of it, by its weights for the three corners.</summary>
    public Color32 At(float wa, float wb, float wc)
    {
        Color32 colour = Colour;
        if (Texture is { } texture)
        {
            Vector2 uv = (Uv.A * wa) + (Uv.B * wb) + (Uv.C * wc);
            float u = uv.X - MathF.Floor(uv.X);
            float v = uv.Y - MathF.Floor(uv.Y);
            colour = Multiply(colour, texture[Math.Min((int)(u * texture.Width), texture.Width - 1), Math.Min((int)(v * texture.Height), texture.Height - 1)]);
        }

        if (Corners is { } corners)
        {
            colour = Multiply(colour, new Color32(
                (byte)Math.Clamp(MathF.Round((corners.A.R * wa) + (corners.B.R * wb) + (corners.C.R * wc)), 0f, 255f),
                (byte)Math.Clamp(MathF.Round((corners.A.G * wa) + (corners.B.G * wb) + (corners.C.G * wc)), 0f, 255f),
                (byte)Math.Clamp(MathF.Round((corners.A.B * wa) + (corners.B.B * wb) + (corners.C.B * wc)), 0f, 255f)));
        }

        return colour with { A = 255 };
    }

    private static Color32 Multiply(Color32 a, Color32 b) =>
        new((byte)((a.R * b.R + 127) / 255), (byte)((a.G * b.G + 127) / 255), (byte)((a.B * b.B + 127) / 255));
}

/// <summary>
/// A model read from OBJ (with its .mtl), glTF or GLB for making into voxels (Fullreleaseplan 8.3):
/// world-space triangles, each with its colour — the material's, a PNG texture's, its corners'.
/// </summary>
public sealed class ColouredMesh(string name, IReadOnlyList<MeshTriangle> triangles, IReadOnlyList<string> warnings)
{
    public const int TriangleLimit = ReferenceMeshLoader.TriangleLimit;

    public static readonly string[] SupportedExtensions = ReferenceMeshLoader.SupportedExtensions;

    public string Name { get; } = name;

    public IReadOnlyList<MeshTriangle> Triangles { get; } = triangles;

    /// <summary>What was read but could not be used — a texture that is not a PNG, say.</summary>
    public IReadOnlyList<string> Warnings { get; } = warnings;

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (MeshTriangle triangle in Triangles)
        {
            min = Vector3.Min(min, Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C)));
            max = Vector3.Max(max, Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C)));
        }

        return (min, max);
    }

    public static ColouredMesh Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ReferenceImportException($"No such file: {path}");
        }

        string name = Path.GetFileNameWithoutExtension(path);
        ColouredMesh mesh = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => LoadObj(path, name),
            ".gltf" or ".glb" => LoadGltf(path, name),
            string other => throw new ReferenceImportException($"Unsupported model format '{other}'. Supported: {string.Join(", ", SupportedExtensions)}."),
        };

        return mesh.Triangles.Count > 0 ? mesh : throw new ReferenceImportException($"{Path.GetFileName(path)} contains no triangles.");
    }

    // ---- glTF ---------------------------------------------------------------------------------------

    private static ColouredMesh LoadGltf(string path, string name)
    {
        ModelRoot model;
        try
        {
            model = ModelRoot.Load(path);
        }
        catch (Exception exception)
        {
            throw new ReferenceImportException($"Could not read {Path.GetFileName(path)}: {exception.Message}", exception);
        }

        var triangles = new List<MeshTriangle>();
        var warnings = new List<string>();
        var textures = new Dictionary<int, DecodedImage?>();

        SharpGLTF.Schema2.Scene scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault()
            ?? throw new ReferenceImportException($"{Path.GetFileName(path)} contains no scene.");

        foreach (Node node in scene.VisualChildren)
        {
            AppendNode(node, triangles, warnings, textures);
        }

        return new ColouredMesh(name, triangles, warnings);
    }

    private static void AppendNode(Node node, List<MeshTriangle> triangles, List<string> warnings, Dictionary<int, DecodedImage?> textures)
    {
        if (node.Mesh is { } mesh)
        {
            Matrix4x4 transform = node.WorldMatrix;
            foreach (MeshPrimitive primitive in mesh.Primitives)
            {
                if (primitive.GetVertexAccessor("POSITION") is not { } positionAccessor)
                {
                    continue;
                }

                IList<Vector3> positions = positionAccessor.AsVector3Array();
                IList<Vector2>? uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                IList<Vector4>? colours = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();

                Color32 factor = new(255, 255, 255);
                DecodedImage? texture = null;
                if (primitive.Material?.FindChannel("BaseColor") is { } channel)
                {
                    Vector4 linear = channel.Parameters.FirstOrDefault(parameter => parameter.Name == "RGBA")?.Value is Vector4 rgba ? rgba : Vector4.One;
                    factor = new Color32(ToDisplay(linear.X), ToDisplay(linear.Y), ToDisplay(linear.Z));
                    if (channel.Texture is { } source && uvs is not null)
                    {
                        texture = TextureOf(source, warnings, textures);
                    }
                }

                foreach ((int a, int b, int c) in primitive.GetTriangleIndices())
                {
                    if (triangles.Count >= TriangleLimit)
                    {
                        return;
                    }

                    triangles.Add(new MeshTriangle(Vector3.Transform(positions[a], transform), Vector3.Transform(positions[b], transform), Vector3.Transform(positions[c], transform))
                    {
                        Colour = factor,
                        Texture = texture,
                        Uv = texture is not null && uvs is not null ? (uvs[a], uvs[b], uvs[c]) : default,
                        Corners = colours is not null ? (FromLinear(colours[a]), FromLinear(colours[b]), FromLinear(colours[c])) : null,
                    });
                }
            }
        }

        foreach (Node child in node.VisualChildren)
        {
            AppendNode(child, triangles, warnings, textures);
        }
    }

    private static DecodedImage? TextureOf(Texture texture, List<string> warnings, Dictionary<int, DecodedImage?> textures)
    {
        if (textures.TryGetValue(texture.LogicalIndex, out DecodedImage? known))
        {
            return known;
        }

        DecodedImage? decoded = null;
        SharpGLTF.Memory.MemoryImage image = texture.PrimaryImage.Content;
        if (image.IsPng)
        {
            try
            {
                decoded = PngReader.Decode(image.Content.ToArray());
            }
            catch (ImageDecodeException exception)
            {
                warnings.Add($"A texture could not be read ({exception.Message}); its material's colour is used instead.");
            }
        }
        else
        {
            warnings.Add($"A texture is {image.FileExtension.TrimStart('.').ToUpperInvariant()}, and only PNG textures are read: its material's colour is used instead.");
        }

        textures[texture.LogicalIndex] = decoded;
        return decoded;
    }

    /// <summary>glTF keeps colours linear; a voxel is coloured as it is seen.</summary>
    private static byte ToDisplay(float linear)
    {
        float value = Math.Clamp(linear, 0f, 1f);
        float display = value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(display * 255f), 0f, 255f);
    }

    private static Color32 FromLinear(Vector4 colour) => new(ToDisplay(colour.X), ToDisplay(colour.Y), ToDisplay(colour.Z));

    // ---- OBJ and MTL --------------------------------------------------------------------------------

    private sealed record ObjMaterial(Color32 Colour, DecodedImage? Texture);

    private static ColouredMesh LoadObj(string path, string name)
    {
        var positions = new List<Vector3>();
        var vertexColours = new List<Color32?>();
        var uvs = new List<Vector2>();
        var triangles = new List<MeshTriangle>();
        var warnings = new List<string>();
        var materials = new Dictionary<string, ObjMaterial>(StringComparer.Ordinal);
        ObjMaterial? current = null;
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var face = new List<(int Position, int Uv)>();

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(Number(parts[1]), Number(parts[2]), Number(parts[3])));
                    vertexColours.Add(parts.Length >= 7
                        ? new Color32(Channel(Number(parts[4])), Channel(Number(parts[5])), Channel(Number(parts[6])))
                        : null);
                    break;

                case "vt" when parts.Length >= 3:
                    // OBJ counts v up from the bottom of the texture; the image is read from its top.
                    uvs.Add(new Vector2(Number(parts[1]), 1f - Number(parts[2])));
                    break;

                case "mtllib" when parts.Length >= 2:
                    ReadMaterials(Path.Combine(directory, line["mtllib".Length..].Trim()), materials, warnings);
                    break;

                case "usemtl" when parts.Length >= 2:
                    current = materials.GetValueOrDefault(line["usemtl".Length..].Trim());
                    break;

                case "f" when parts.Length >= 4:
                    face.Clear();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string[] indices = parts[i].Split('/');
                        int position = Index(indices[0], positions.Count, name);
                        int uv = indices.Length > 1 && indices[1].Length > 0 ? Index(indices[1], uvs.Count, name) : -1;
                        face.Add((position, uv));
                    }

                    for (int i = 1; i + 1 < face.Count && triangles.Count < TriangleLimit; i++)
                    {
                        triangles.Add(Triangle(face[0], face[i], face[i + 1], positions, vertexColours, uvs, current));
                    }

                    break;
            }
        }

        return new ColouredMesh(name, triangles, warnings);
    }

    private static MeshTriangle Triangle(
        (int Position, int Uv) a,
        (int Position, int Uv) b,
        (int Position, int Uv) c,
        List<Vector3> positions,
        List<Color32?> vertexColours,
        List<Vector2> uvs,
        ObjMaterial? material)
    {
        bool textured = material?.Texture is not null && a.Uv >= 0 && b.Uv >= 0 && c.Uv >= 0;
        bool cornered = vertexColours[a.Position] is not null && vertexColours[b.Position] is not null && vertexColours[c.Position] is not null;
        return new MeshTriangle(positions[a.Position], positions[b.Position], positions[c.Position])
        {
            Colour = material?.Colour ?? new Color32(255, 255, 255),
            Texture = textured ? material!.Texture : null,
            Uv = textured ? (uvs[a.Uv], uvs[b.Uv], uvs[c.Uv]) : default,
            Corners = cornered ? (vertexColours[a.Position]!.Value, vertexColours[b.Position]!.Value, vertexColours[c.Position]!.Value) : null,
        };
    }

    private static void ReadMaterials(string path, Dictionary<string, ObjMaterial> materials, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            warnings.Add($"{Path.GetFileName(path)} is not there, so the model's materials are white.");
            return;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string? name = null;
        Color32 colour = new(255, 255, 255);
        DecodedImage? texture = null;

        void Keep()
        {
            if (name is not null)
            {
                materials[name] = new ObjMaterial(colour, texture);
            }
        }

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            switch (parts[0])
            {
                case "newmtl" when parts.Length >= 2:
                    Keep();
                    name = line["newmtl".Length..].Trim();
                    colour = new Color32(255, 255, 255);
                    texture = null;
                    break;

                case "Kd" when parts.Length >= 4:
                    colour = new Color32(Channel(Number(parts[1])), Channel(Number(parts[2])), Channel(Number(parts[3])));
                    break;

                case "map_Kd" when parts.Length >= 2:
                    // The file is what comes last on the line; options such as -s come before it.
                    string file = Path.Combine(directory, parts[^1]);
                    if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"{parts[^1]} is not a PNG, and only PNG textures are read: its material's colour is used instead.");
                    }
                    else
                    {
                        try
                        {
                            texture = PngReader.Decode(file);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageDecodeException)
                        {
                            warnings.Add($"{parts[^1]} could not be read ({exception.Message}): its material's colour is used instead.");
                        }
                    }

                    break;
            }
        }

        Keep();
    }

    private static int Index(string text, int count, string name)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            throw new ReferenceImportException($"{name}: malformed face index '{text}'.");
        }

        int resolved = index > 0 ? index - 1 : count + index;
        return resolved >= 0 && resolved < count ? resolved : throw new ReferenceImportException($"{name}: face index {index} is out of range.");
    }

    private static float Number(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;

    private static byte Channel(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
