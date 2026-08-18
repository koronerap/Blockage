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

    // ASCII only: this reaches the interface, and the font atlas is built with ImGui's default
    // glyph ranges, so anything past Latin-1 arrives as a hollow box.
    public string DisplayName => Binary
        ? "glTF binary (.glb - single file, texture embedded)"
        : "glTF (.gltf + .bin + .png)";

    public string Extension => Binary ? ".glb" : ".gltf";

    public ExportResult Export(ExportMesh mesh, Palette palette, string path, ExportOptions options)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);

        string textureFileName = options.ResolveTextureFileName();
        byte[] png = options.EncodeTexture(mesh, palette);

        MaterialBuilder material = new MaterialBuilder(MaterialName)
            .WithDoubleSide(false)
            .WithMetallicRoughnessShader()
            .WithMetallicRoughness(0f, 1f)
            .WithBaseColor(new MemoryImage(png), Vector4.One);

        var scene = new SceneBuilder();

        // One mesh per object in the level rather than one for the whole thing. They share the
        // material and the sheet, so this stays a single texture and a single draw call's worth of
        // state — it only stops the pieces arriving welded into one lump that has to be separated
        // by hand on the other side.
        foreach (MeshPart part in mesh.PartsOrWhole)
        {
            var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1>(part.Name);
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> primitive =
                meshBuilder.UsePrimitive(material);

            // Six indices per quad, two triangles.
            int from = part.FirstQuad * 6;
            int to = from + (part.QuadCount * 6);

            for (int i = from; i < to; i += 3)
            {
                primitive.AddTriangle(
                    Vertex(mesh, mesh.Indices[i]),
                    Vertex(mesh, mesh.Indices[i + 1]),
                    Vertex(mesh, mesh.Indices[i + 2]));
            }

            scene.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
        }

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
            string texturePath = Path.Combine(directory, textureFileName);
            File.WriteAllBytes(texturePath, png);
            written.Add(texturePath);

            var settings = new WriteSettings
            {
                ImageWriting = ResourceWriteMode.SatelliteFile,
                ImageWriteCallback = (_, _, _) => textureFileName,
            };

            model.Save(path, settings);
            written.Add(path);
        }

        if (options.WriteImportNotes && !Binary)
        {
            string notesPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-texture-notes.txt");
            File.WriteAllText(notesPath, options.ImportNotes(), Encoding.UTF8);
            written.Add(notesPath);
        }

        return new ExportResult(written, mesh.VertexCount, mesh.TriangleCount, mesh.QuadCount);
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(ExportMesh mesh, int index) =>
        new(
            new VertexPositionNormal(mesh.Positions[index], mesh.Normals[index]),
            new VertexTexture1(mesh.Uvs[index]));
}
