using System.Globalization;
using System.Numerics;
using System.Text;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>
/// Wavefront OBJ + MTL, written by hand (EditorApp.md §6). No dependency, a text format that can be
/// diffed and read, and every DCC tool opens it — which is why it is the first exporter built.
///
/// One material, one texture, one draw call: color reaches the file through UVs into the generated
/// palette texture, not through vertex colors (which OBJ has no standard for) and not through one
/// material per color (which would split the model into as many submeshes as the level has colors).
/// </summary>
public sealed class ObjExporter : IMeshExporter
{
    private const string MaterialName = "palette";

    public string DisplayName => "Wavefront OBJ (+ MTL + PNG)";

    public string Extension => ".obj";

    public ExportResult Export(ExportMesh mesh, Palette palette, string path, ExportOptions options)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);

        string baseName = Path.GetFileNameWithoutExtension(path);
        string mtlFileName = baseName + ".mtl";
        string textureFileName = options.ResolveTextureFileName();

        var written = new List<string>();

        if (options.WriteTexture)
        {
            string texturePath = Path.Combine(directory, textureFileName);
            File.WriteAllBytes(texturePath, options.EncodeTexture(mesh, palette));
            written.Add(texturePath);
        }

        // The material still names the texture when none was written: the mesh is unwrapped for it,
        // and dropping the reference would leave the material with nothing to say.
        File.WriteAllText(Path.Combine(directory, mtlFileName), BuildMtl(textureFileName), Encoding.UTF8);
        written.Add(Path.Combine(directory, mtlFileName));

        File.WriteAllText(path, BuildObj(mesh, mtlFileName), Encoding.UTF8);
        written.Add(path);

        if (options.WriteImportNotes)
        {
            string notesPath = Path.Combine(directory, baseName + "-texture-notes.txt");
            File.WriteAllText(notesPath, options.ImportNotes(), Encoding.UTF8);
            written.Add(notesPath);
        }

        return new ExportResult(written, mesh.VertexCount, mesh.TriangleCount, mesh.QuadCount);
    }

    private static string BuildObj(ExportMesh mesh, string mtlFileName)
    {
        var builder = new StringBuilder(mesh.VertexCount * 48);
        CultureInfo culture = CultureInfo.InvariantCulture;

        builder.Append("# Exported by EditorApp — greedy-meshed voxel level\n");
        builder.Append(culture, $"# {mesh.QuadCount} quads, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles\n");
        builder.Append(culture, $"# {mesh.PartsOrWhole.Count} object(s)\n");
        builder.Append(culture, $"mtllib {mtlFileName}\n");

        // OBJ indexes positions, texture coordinates and normals independently, which is the whole
        // reason this is worth doing: a corner where a top face meets a side face is one position
        // used twice, each time with its own normal and its own UV.
        //
        // The mesh arrives with four unshared vertices per quad, because that is what a GPU vertex
        // buffer needs — every corner carrying its own copy of everything. Written out that way the
        // file is a pile of loose quads: nothing touches anything, and Blender's select-linked picks
        // one face because as far as the file is concerned there is nothing else attached to it.
        //
        // Positions coincide exactly rather than approximately. Each corner comes from the same
        // integer lattice through the same object transform, so two quads meeting at an edge produce
        // identical floats, not merely close ones — no tolerance needed, and none wanted, since a
        // tolerance would start welding parts that were never joined.
        var positions = new Deduplicated<Vector3>();
        var uvs = new Deduplicated<Vector2>();
        var normals = new Deduplicated<Vector3>();

        var corners = new (int Position, int Uv, int Normal)[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            corners[i] = (
                positions.Add(mesh.Positions[i]),
                uvs.Add(mesh.Uvs[i]),
                normals.Add(mesh.Normals[i]));
        }

        foreach (Vector3 position in positions.Values)
        {
            builder.Append(culture, $"v {F(position.X)} {F(position.Y)} {F(position.Z)}\n");
        }

        // OBJ counts V from the bottom of the image; PaletteTexture works top-down like glTF.
        foreach (Vector2 uv in uvs.Values)
        {
            builder.Append(culture, $"vt {F(uv.X)} {F(1f - uv.Y)}\n");
        }

        foreach (Vector3 normal in normals.Values)
        {
            builder.Append(culture, $"vn {F(normal.X)} {F(normal.Y)} {F(normal.Z)}\n");
        }

        builder.Append(culture, $"usemtl {MaterialName}\n");
        builder.Append("s off\n");

        // One "o" per object in the level, so a scene built from several pieces arrives as several
        // objects rather than as a single lump that has to be split by hand. The vertex lists above
        // are shared and the indices are global, which is exactly how OBJ expects this to be done.
        // A "g" of the same name as well: Unity splits an OBJ by its groups and not by its objects,
        // and without one it welds the whole level into a single mesh (Fullreleaseplan 8.6).
        foreach (MeshPart part in mesh.PartsOrWhole)
        {
            builder.Append(culture, $"o {Sanitize(part.Name)}\n");
            builder.Append(culture, $"g {Sanitize(part.Name)}\n");

            // Corners come in groups of four, so the faces stay quads instead of being split into
            // triangles — half the face lines and a cleaner mesh in Blender.
            for (int quad = part.FirstQuad; quad < part.FirstQuad + part.QuadCount; quad++)
            {
                int first = quad * 4;
                builder.Append('f');
                for (int corner = 0; corner < 4; corner++)
                {
                    (int position, int uv, int normal) = corners[first + corner];

                    // OBJ indices are 1-based.
                    builder.Append(culture, $" {position + 1}/{uv + 1}/{normal + 1}");
                }

                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Assigns each distinct value one index, in first-seen order. Also shrinks the file
    /// considerably on its own: a level has six normals and one UV per colour, however many quads.
    /// </summary>
    private sealed class Deduplicated<T>
        where T : notnull
    {
        private readonly Dictionary<T, int> _indices = [];
        private readonly List<T> _values = [];

        public IReadOnlyList<T> Values => _values;

        public int Add(T value)
        {
            if (_indices.TryGetValue(value, out int existing))
            {
                return existing;
            }

            int index = _values.Count;
            _indices.Add(value, index);
            _values.Add(value);
            return index;
        }
    }

    private static string BuildMtl(string textureFileName)
    {
        var builder = new StringBuilder();
        builder.Append("# Exported by EditorApp\n");
        builder.Append("# One material for the whole level; color comes from the palette texture.\n");
        builder.Append(CultureInfo.InvariantCulture, $"newmtl {MaterialName}\n");
        builder.Append("Ka 0 0 0\n");
        builder.Append("Kd 1 1 1\n");
        builder.Append("Ks 0 0 0\n");
        builder.Append("d 1\n");
        builder.Append("illum 1\n");
        // Relative path on purpose: an absolute one opens textureless on any other machine.
        builder.Append(CultureInfo.InvariantCulture, $"map_Kd {textureFileName}\n");
        return builder.ToString();
    }

    private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Sanitize(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            buffer[i] = char.IsWhiteSpace(name[i]) ? '_' : name[i];
        }

        return buffer.Length == 0 ? "level" : new string(buffer);
    }
}
