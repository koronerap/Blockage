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

        // What the colours used are made of: textures only for what some colour has, so a level
        // without materials exports exactly as it always has.
        IReadOnlyList<byte> used = mesh.UsedPaletteIndices();
        bool shiny = used.Any(i => palette.Material(i) is { Metallic: > 0f } or { Smoothness: > 0f });
        bool glowing = used.Any(i => palette.Material(i).Emission > 0f);
        bool seeThrough = used.Any(i => palette.Material(i).IsTransparent);

        byte[]? metallicRoughness = shiny ? options.EncodeChannel(mesh, palette, i => MaterialTextures.MetallicRoughness(palette, i)) : null;
        byte[]? emissive = glowing ? options.EncodeChannel(mesh, palette, i => MaterialTextures.Emissive(palette, i)) : null;

        MaterialBuilder material = Material(MaterialName, png, metallicRoughness, emissive);

        // See-through faces go in a material of their own, blended: the rest stay opaque, which an
        // engine draws and sorts far more cheaply.
        MaterialBuilder? glass = seeThrough
            ? Material(MaterialName + "-transparent", png, metallicRoughness, emissive).WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND)
            : null;

        var scene = new SceneBuilder();

        // One mesh per object in the level rather than one for the whole thing. They share the
        // material and the sheet, so this stays a single texture and a single draw call's worth of
        // state — it only stops the pieces arriving welded into one lump that has to be separated
        // by hand on the other side.
        var builders = new List<MeshBuilder<VertexPositionNormal, VertexTexture1>>();
        foreach (MeshPart part in mesh.PartsOrWhole)
        {
            var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexTexture1>(part.Name);
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> primitive =
                meshBuilder.UsePrimitive(material);
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty>? glassPrimitive =
                glass is null ? null : meshBuilder.UsePrimitive(glass);

            for (int quad = part.FirstQuad; quad < part.FirstQuad + part.QuadCount; quad++)
            {
                var target = glassPrimitive is not null && IsSeeThrough(mesh, palette, quad) ? glassPrimitive : primitive;

                // Six indices per quad, two triangles.
                for (int i = quad * 6; i < (quad * 6) + 6; i += 3)
                {
                    target.AddTriangle(
                        Vertex(mesh, mesh.Indices[i]),
                        Vertex(mesh, mesh.Indices[i + 1]),
                        Vertex(mesh, mesh.Indices[i + 2]));
                }
            }

            builders.Add(meshBuilder);
        }

        // A node for every object, each where it stands, linked copies sharing one mesh; what the
        // game is told about each besides is in its extras. Markers come after, as empty nodes.
        if (mesh.Instances.Count > 0)
        {
            foreach (MeshInstance instance in mesh.Instances.Where(i => i.Part >= 0))
            {
                var node = new NodeBuilder(instance.Name) { LocalMatrix = instance.Transform };
                if (instance.Extras is { } extras)
                {
                    node.Extras = extras;
                }

                scene.AddRigidMesh(builders[instance.Part], node);
            }
        }
        else
        {
            foreach (var builder in builders)
            {
                scene.AddRigidMesh(builder, Matrix4x4.Identity);
            }
        }

        ModelRoot model = scene.ToGltf2();
        model.Asset.Generator = "EditorApp voxel level editor";

        foreach (MeshInstance marker in mesh.Instances.Where(i => i.Part < 0))
        {
            Node node = model.UseScene(0).CreateNode(marker.Name);
            node.LocalMatrix = marker.Transform;
            if (marker.Extras is { } extras)
            {
                node.Extras = extras;
            }
        }

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

            // Each texture under a name of its own beside the colour one: the channel's name added.
            string stem = Path.GetFileNameWithoutExtension(textureFileName);
            string extension = Path.GetExtension(textureFileName);
            string metallicName = $"{stem}-metallic-roughness{extension}";
            string emissiveName = $"{stem}-emissive{extension}";

            var settings = new WriteSettings
            {
                ImageWriting = ResourceWriteMode.SatelliteFile,
                ImageWriteCallback = (_, _, image) =>
                    metallicRoughness is not null && image.Content.Span.SequenceEqual(metallicRoughness) ? metallicName
                    : emissive is not null && image.Content.Span.SequenceEqual(emissive) ? emissiveName
                    : textureFileName,
            };

            model.Save(path, settings);
            written.Add(path);

            if (metallicRoughness is not null)
            {
                written.Add(Path.Combine(directory, metallicName));
            }

            if (emissive is not null)
            {
                written.Add(Path.Combine(directory, emissiveName));
            }
        }

        if (options.WriteImportNotes && !Binary)
        {
            string notesPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-texture-notes.txt");
            File.WriteAllText(notesPath, options.ImportNotes(), Encoding.UTF8);
            written.Add(notesPath);
        }

        return new ExportResult(written, mesh.VertexCount, mesh.TriangleCount, mesh.QuadCount);
    }

    /// <summary>The metallic-roughness material over the colour texture, with the other channels where there are any.</summary>
    private static MaterialBuilder Material(string name, byte[] colour, byte[]? metallicRoughness, byte[]? emissive)
    {
        MaterialBuilder material = new MaterialBuilder(name)
            .WithDoubleSide(false)
            .WithMetallicRoughnessShader()
            .WithBaseColor(new MemoryImage(colour), Vector4.One);

        material = metallicRoughness is null
            ? material.WithMetallicRoughness(0f, 1f)
            : material.WithMetallicRoughness(new MemoryImage(metallicRoughness), 1f, 1f);

        if (emissive is not null)
        {
            material = material.WithEmissive(new MemoryImage(emissive), Vector3.One);
        }

        return material;
    }

    /// <summary>Whether any cell of a quad is a see-through colour.</summary>
    private static bool IsSeeThrough(ExportMesh mesh, Palette palette, int quad)
    {
        if (quad < mesh.QuadCells.Count)
        {
            QuadColors cells = mesh.QuadCells[quad];
            foreach (byte index in cells.Cells)
            {
                if (palette.Material(index).IsTransparent)
                {
                    return true;
                }
            }

            return false;
        }

        return quad < mesh.QuadPaletteIndices.Count && palette.Material(mesh.QuadPaletteIndices[quad]).IsTransparent;
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(ExportMesh mesh, int index) =>
        new(
            new VertexPositionNormal(mesh.Positions[index], mesh.Normals[index]),
            new VertexTexture1(mesh.Uvs[index]));
}
