using System.Numerics;
using System.Text;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace EditorApp.Core.Export;

/// <summary>
/// glTF 2.0 / GLB via SharpGLTF (EditorApp.md §6). The palette texture becomes the material's
/// <c>baseColorTexture</c>; in a <c>.glb</c> the PNG is embedded, which removes any chance of the
/// mesh and its texture being separated.
/// </summary>
public sealed class GltfExporter(bool binary = true) : IMeshExporter
{
    private const string MaterialName = "palette";

    public bool Binary { get; } = binary;

    public string DisplayName => Binary
        ? "glTF binary (.glb — single file, texture embedded)"
        : "glTF (.gltf + .bin + .png)";

    public string Extension => Binary ? ".glb" : ".gltf";

    public ExportResult Export(ExportMesh mesh, Palette palette, string path, ExportOptions options)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);

        byte[] png = PaletteTexture.EncodePng(palette);

        MaterialBuilder material = new MaterialBuilder(MaterialName)
            .WithDoubleSide(false)
            .WithMetallicRoughnessShader()
            .WithMetallicRoughness(0f, 1f)
            .WithBaseColor(new MemoryImage(png), Vector4.One);

        var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1>("level");
        PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> primitive =
            meshBuilder.UsePrimitive(material);

        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            primitive.AddTriangle(
                Vertex(mesh, mesh.Indices[i]),
                Vertex(mesh, mesh.Indices[i + 1]),
                Vertex(mesh, mesh.Indices[i + 2]));
        }

        var scene = new SceneBuilder();
        scene.AddRigidMesh(meshBuilder, Matrix4x4.Identity);

        ModelRoot model = scene.ToGltf2();
        model.Asset.Generator = "EditorApp voxel level editor";

        var written = new List<string>();

        if (Binary)
        {
            model.SaveGLB(path);
            written.Add(path);
        }
        else
        {
            // Keep the image beside the mesh under the agreed name instead of letting the writer
            // invent one, so the .gltf and the .png stay a matched pair.
            string texturePath = Path.Combine(directory, options.TextureFileName);
            File.WriteAllBytes(texturePath, png);
            written.Add(texturePath);

            var settings = new WriteSettings
            {
                ImageWriting = ResourceWriteMode.SatelliteFile,
                ImageWriteCallback = (_, _, _) => options.TextureFileName,
            };

            model.Save(path, settings);
            written.Add(path);
        }

        if (options.WriteImportNotes && !Binary)
        {
            string notesPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-texture-notes.txt");
            File.WriteAllText(notesPath, PaletteTexture.ImportNotes, Encoding.UTF8);
            written.Add(notesPath);
        }

        return new ExportResult(written, mesh.VertexCount, mesh.TriangleCount, mesh.QuadCount);
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(ExportMesh mesh, int index) =>
        new(
            new VertexPositionNormal(mesh.Positions[index], mesh.Normals[index]),
            new VertexTexture1(mesh.Uvs[index]));
}
