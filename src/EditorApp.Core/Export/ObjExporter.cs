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
        string texturePath = Path.Combine(directory, options.TextureFileName);

        var written = new List<string>();

        File.WriteAllBytes(texturePath, PaletteTexture.EncodePng(palette));
        written.Add(texturePath);

        File.WriteAllText(Path.Combine(directory, mtlFileName), BuildMtl(options.TextureFileName), Encoding.UTF8);
        written.Add(Path.Combine(directory, mtlFileName));

        File.WriteAllText(path, BuildObj(mesh, baseName, mtlFileName), Encoding.UTF8);
        written.Add(path);

        if (options.WriteImportNotes)
        {
            string notesPath = Path.Combine(directory, baseName + "-texture-notes.txt");
            File.WriteAllText(notesPath, PaletteTexture.ImportNotes, Encoding.UTF8);
            written.Add(notesPath);
        }

        return new ExportResult(written, mesh.VertexCount, mesh.TriangleCount, mesh.QuadCount);
    }

    private static string BuildObj(ExportMesh mesh, string objectName, string mtlFileName)
    {
        var builder = new StringBuilder(mesh.VertexCount * 48);
        CultureInfo culture = CultureInfo.InvariantCulture;

        builder.Append("# Exported by EditorApp — greedy-meshed voxel level\n");
        builder.Append(culture, $"# {mesh.QuadCount} quads, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles\n");
        builder.Append(culture, $"mtllib {mtlFileName}\n");
        builder.Append(culture, $"o {Sanitize(objectName)}\n");

        foreach (Vector3 position in mesh.Positions)
        {
            builder.Append(culture, $"v {F(position.X)} {F(position.Y)} {F(position.Z)}\n");
        }

        // OBJ counts V from the bottom of the image; PaletteTexture works top-down like glTF.
        foreach (Vector2 uv in mesh.Uvs)
        {
            builder.Append(culture, $"vt {F(uv.X)} {F(1f - uv.Y)}\n");
        }

        foreach (Vector3 normal in mesh.Normals)
        {
            builder.Append(culture, $"vn {F(normal.X)} {F(normal.Y)} {F(normal.Z)}\n");
        }

        builder.Append(culture, $"usemtl {MaterialName}\n");
        builder.Append("s off\n");

        // Vertices come in groups of four per quad, so the faces can stay quads instead of being
        // split into triangles — half the face lines and a cleaner mesh in Blender.
        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            int first = quad * 4 + 1;   // OBJ indices are 1-based
            builder.Append(culture, $"f {Vertex(first)} {Vertex(first + 1)} {Vertex(first + 2)} {Vertex(first + 3)}\n");
        }

        return builder.ToString();

        static string Vertex(int index) => $"{index}/{index}/{index}";
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
