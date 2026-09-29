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

        // A node for every object, marker and light, under its parent's where it has one and placed
        // relative to it; linked copies share one mesh. What the game is told about each besides is
        // in its extras.
        if (mesh.Instances.Count > 0 || mesh.Lights.Count > 0)
        {
            AddNodes(scene, mesh, builders);
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
    /// <summary>glTF's lights shine along their −Z, the level's along their −Y: a quarter turn about X before the light's own.</summary>
    private static readonly Matrix4x4 LightFacing = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f));

    private static void AddNodes(SceneBuilder scene, ExportMesh mesh, List<MeshBuilder<VertexPositionNormal, VertexTexture1>> builders)
    {
        // Everything that can be a parent, by id: where it stands in the world, and what it is under.
        var things = new Dictionary<int, (string Name, Matrix4x4 World, int ParentId)>();
        foreach (MeshInstance instance in mesh.Instances.Where(i => i.Id != 0))
        {
            things[instance.Id] = (instance.Name, instance.Transform, instance.ParentId);
        }

        foreach (ExportLight light in mesh.Lights.Where(l => l.Id != 0))
        {
            things[light.Id] = (light.Name, LightFacing * light.Transform, light.ParentId);
        }

        var nodes = new Dictionary<int, NodeBuilder>();
        var making = new HashSet<int>();
        NodeBuilder NodeOf(int id)
        {
            if (nodes.TryGetValue(id, out NodeBuilder? made))
            {
                return made;
            }

            making.Add(id);
            (string name, Matrix4x4 world, int parentId) = things[id];
            NodeBuilder node;
            if (parentId != 0 && things.TryGetValue(parentId, out var parent) && !making.Contains(parentId))
            {
                node = NodeOf(parentId).CreateNode(name);
                Place(node, Matrix4x4.Invert(parent.World, out Matrix4x4 inverse) ? world * inverse : world);
            }
            else
            {
                node = Place(new NodeBuilder(name), world);
            }

            making.Remove(id);
            nodes[id] = node;
            return node;
        }

        var collisionMeshes = new Dictionary<int, MeshBuilder<VertexPosition>>();
        foreach (MeshInstance instance in mesh.Instances)
        {
            NodeBuilder node = instance.Id != 0 ? NodeOf(instance.Id) : Place(new NodeBuilder(instance.Name), instance.Transform);
            if (instance.Extras is { } extras)
            {
                node.Extras = extras;
            }

            if (instance.Part < 0)
            {
                scene.AddNode(node);
                continue;
            }

            scene.AddRigidMesh(builders[instance.Part], node);

            // Godot takes a "-colonly" node as collision and nothing else; others read the extras.
            if (instance.Colliders >= 0 && instance.Colliders < mesh.Colliders.Count)
            {
                if (!collisionMeshes.TryGetValue(instance.Colliders, out MeshBuilder<VertexPosition>? boxes))
                {
                    boxes = CollisionMesh(mesh.Colliders[instance.Colliders], $"{instance.Name}-collision");
                    collisionMeshes[instance.Colliders] = boxes;
                }

                NodeBuilder collider = node.CreateNode($"{instance.Name}-colonly");
                collider.Extras = new System.Text.Json.Nodes.JsonObject
                {
                    ["collider"] = "boxes",
                    ["boxes"] = mesh.Colliders[instance.Colliders].Count,
                };
                scene.AddRigidMesh(boxes, collider);
            }
        }

        foreach (ExportLight light in mesh.Lights)
        {
            NodeBuilder node = light.Id != 0 ? NodeOf(light.Id) : Place(new NodeBuilder(light.Name), LightFacing * light.Transform);
            scene.AddLight(LightOf(light), node);
        }
    }

    /// <summary>
    /// A node's place as a move, a turn and a scale rather than a matrix — what engines and animation
    /// expect to find — whenever it comes apart into them, as the level's placements always do.
    /// </summary>
    private static NodeBuilder Place(NodeBuilder node, Matrix4x4 local)
    {
        if (Matrix4x4.Decompose(local, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            node.LocalTransform = new SharpGLTF.Transforms.AffineTransform(scale, Quaternion.Normalize(rotation), translation);
        }
        else
        {
            node.LocalMatrix = local;
        }

        return node;
    }

    /// <summary>A level's light as KHR_lights_punctual has them: its colour and strength, how far it reaches, its cone.</summary>
    private static LightBuilder LightOf(ExportLight light)
    {
        float outer = MathF.Min(light.SpotAngle * 0.5f, 89.9f) * (MathF.PI / 180f);
        return light.Kind switch
        {
            Scene.LightKind.Point => new LightBuilder.Point { Name = light.Name, Color = light.Colour, Intensity = light.Intensity, Range = light.Range },
            Scene.LightKind.Spot => new LightBuilder.Spot
            {
                Name = light.Name,
                Color = light.Colour,
                Intensity = light.Intensity,
                Range = light.Range,
                OuterConeAngle = outer,
                InnerConeAngle = outer * Math.Clamp(1f - light.SpotBlend, 0f, 1f),
            },
            _ => new LightBuilder.Directional { Name = light.Name, Color = light.Colour, Intensity = light.Intensity },
        };
    }

    private static readonly MaterialBuilder CollisionMaterial = new("collision");

    /// <summary>The boxes as one mesh, each face turned outward.</summary>
    private static MeshBuilder<VertexPosition> CollisionMesh(IReadOnlyList<(Int3 Min, Int3 Max)> boxes, string name)
    {
        var builder = new MeshBuilder<VertexPosition>(name);
        var primitive = builder.UsePrimitive(CollisionMaterial);
        (int, int, int, int)[] faces = [(0, 4, 6, 2), (1, 3, 7, 5), (0, 1, 5, 4), (2, 6, 7, 3), (0, 2, 3, 1), (4, 5, 7, 6)];
        foreach ((Int3 min, Int3 max) in boxes)
        {
            VertexPosition Corner(int i) => new(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z));
            foreach ((int a, int b, int c, int d) in faces)
            {
                primitive.AddQuadrangle(Corner(a), Corner(b), Corner(c), Corner(d));
            }
        }

        return builder;
    }

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
