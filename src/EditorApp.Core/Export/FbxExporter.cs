using System.Numerics;
using System.Text.Json.Nodes;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>
/// Binary FBX 7.4 (Fullreleaseplan 8.5), for the programs that want FBX: each object a model under
/// its parent's, placed relative to it, linked copies sharing one geometry; markers as empty models;
/// custom properties as user properties; the palette texture on one material, embedded as well as
/// written beside the file. Y is up, the front is +Z and a unit is a metre, as the level has them.
/// </summary>
public sealed class FbxExporter : IMeshExporter
{
    private const string MaterialName = "palette";

    public string DisplayName => "FBX binary (.fbx - texture embedded)";

    public string Extension => ".fbx";

    public ExportResult Export(ExportMesh mesh, Palette palette, string path, ExportOptions options)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);

        string textureFileName = options.ResolveTextureFileName();
        byte[] png = options.EncodeTexture(mesh, palette);
        var written = new List<string>();
        if (options.WriteTexture)
        {
            string texturePath = Path.Combine(directory, textureFileName);
            File.WriteAllBytes(texturePath, png);
            written.Add(texturePath);
        }

        long next = 1_000_000;
        long NewId() => next++;

        var objects = new FbxNode("Objects");
        var connections = new FbxNode("Connections");
        IReadOnlyList<MeshPart> parts = mesh.PartsOrWhole;

        var geometries = new long[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            geometries[i] = NewId();
            objects.Children.Add(Geometry(mesh, parts[i], geometries[i]));
        }

        long material = NewId(), texture = NewId(), video = NewId();
        objects.Children.Add(Material(material));
        objects.Children.Add(Video(video, textureFileName, png));
        objects.Children.Add(Texture(texture, textureFileName));
        connections.Add("C", "OP", texture, material, "DiffuseColor");
        connections.Add("C", "OO", video, texture);

        // What is placed: the instances when the mesh has them, else each part where it was baked.
        List<MeshInstance> placed = mesh.Instances.Count > 0
            ? mesh.Instances
            : [.. parts.Select((part, i) => new MeshInstance(part.Name, i, Matrix4x4.Identity))];

        var models = new Dictionary<int, (long Model, Matrix4x4 World)>();
        var ids = new long[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            ids[i] = NewId();
            if (placed[i].Id != 0)
            {
                models[placed[i].Id] = (ids[i], placed[i].Transform);
            }
        }

        int empties = 0, levelModels = 0;
        void Empty(long model)
        {
            long attribute = NewId();
            var node = new FbxNode("NodeAttribute", attribute, FbxNode.ObjectName(string.Empty, "NodeAttribute"), "Null");
            node.Add("TypeFlags", "Null");
            objects.Children.Add(node);
            connections.Add("C", "OO", attribute, model);
            empties++;
        }

        for (int i = 0; i < placed.Count; i++)
        {
            MeshInstance instance = placed[i];
            long parent = 0;
            Matrix4x4 local = instance.Transform;
            if (instance.ParentId != 0 && models.TryGetValue(instance.ParentId, out var above) && Matrix4x4.Invert(above.World, out Matrix4x4 inverse))
            {
                parent = above.Model;
                local = instance.Transform * inverse;
            }

            bool isMesh = instance.Part >= 0 && instance.Part < parts.Count;
            bool levels = isMesh && instance.Lods is { Count: > 0 };
            objects.Children.Add(Model(ids[i], instance.Name, local, isMesh && !levels, instance.Extras));
            connections.Add("C", "OO", ids[i], parent);

            if (levels)
            {
                // Its levels of detail as children named _LOD0, _LOD1 and on, which Unity makes an LOD group of.
                Empty(ids[i]);
                List<int> detail = [instance.Part, .. instance.Lods!];
                for (int level = 0; level < detail.Count; level++)
                {
                    long lod = NewId();
                    objects.Children.Add(Model(lod, $"{instance.Name}_LOD{level}", Matrix4x4.Identity, isMesh: true, extras: null));
                    connections.Add("C", "OO", lod, ids[i]);
                    connections.Add("C", "OO", geometries[detail[level]], lod);
                    connections.Add("C", "OO", material, lod);
                    levelModels++;
                }
            }
            else if (isMesh)
            {
                connections.Add("C", "OO", geometries[instance.Part], ids[i]);
                connections.Add("C", "OO", material, ids[i]);
            }
            else
            {
                Empty(ids[i]);
            }
        }

        List<FbxNode> top =
        [
            Header(),
            new("FileId", FbxWriter.FileId),
            new("CreationTime", FbxWriter.CreationTime),
            new("Creator", "Blockage"),
            GlobalSettings(),
            Documents(NewId()),
            new("References"),
            Definitions(placed.Count + levelModels, parts.Count, empties),
            objects,
            connections,
            Takes(),
        ];

        using (FileStream stream = File.Create(path))
        {
            FbxWriter.Write(stream, top);
        }

        written.Add(path);

        if (options.WriteImportNotes)
        {
            string notesPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-texture-notes.txt");
            File.WriteAllText(notesPath, options.ImportNotes());
            written.Add(notesPath);
        }

        return new ExportResult(written, mesh.VertexCount, mesh.TriangleCount, mesh.QuadCount);
    }

    /// <summary>
    /// A part's quads as one mesh: its corners welded where they meet, each face a quad, with a normal
    /// and a UV at each of its corners — V counted up from the bottom of the image, as FBX counts it.
    /// </summary>
    private static FbxNode Geometry(ExportMesh mesh, MeshPart part, long id)
    {
        var corners = new Dictionary<Vector3, int>();
        var vertices = new List<double>();
        var polygons = new List<int>();
        var normals = new List<double>();
        var uvIndexOf = new Dictionary<Vector2, int>();
        var uvs = new List<double>();
        var uvIndices = new List<int>();
        bool lightmapped = mesh.LightmapUvs.Count == mesh.VertexCount && mesh.VertexCount > 0;
        var lightIndexOf = new Dictionary<Vector2, int>();
        var lightUvs = new List<double>();
        var lightIndices = new List<int>();

        for (int quad = part.FirstQuad; quad < part.FirstQuad + part.QuadCount; quad++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                int i = (quad * 4) + corner;
                Vector3 position = mesh.Positions[i];
                if (!corners.TryGetValue(position, out int index))
                {
                    index = corners.Count;
                    corners[position] = index;
                    vertices.AddRange([position.X, position.Y, position.Z]);
                }

                // The last corner of a face is written as its bitwise complement: that ends the face.
                polygons.Add(corner == 3 ? ~index : index);

                Vector3 normal = mesh.Normals[i];
                normals.AddRange([normal.X, normal.Y, normal.Z]);

                var uv = new Vector2(mesh.Uvs[i].X, 1f - mesh.Uvs[i].Y);
                if (!uvIndexOf.TryGetValue(uv, out int uvIndex))
                {
                    uvIndex = uvIndexOf.Count;
                    uvIndexOf[uv] = uvIndex;
                    uvs.AddRange([uv.X, uv.Y]);
                }

                uvIndices.Add(uvIndex);

                if (lightmapped)
                {
                    var light = new Vector2(mesh.LightmapUvs[i].X, 1f - mesh.LightmapUvs[i].Y);
                    if (!lightIndexOf.TryGetValue(light, out int lightIndex))
                    {
                        lightIndex = lightIndexOf.Count;
                        lightIndexOf[light] = lightIndex;
                        lightUvs.AddRange([light.X, light.Y]);
                    }

                    lightIndices.Add(lightIndex);
                }
            }
        }

        var geometry = new FbxNode("Geometry", id, FbxNode.ObjectName(part.Name, "Geometry"), "Mesh");
        geometry.Add("Vertices", vertices.ToArray());
        geometry.Add("PolygonVertexIndex", polygons.ToArray());
        geometry.Add("GeometryVersion", 124);

        FbxNode normalLayer = geometry.Add("LayerElementNormal", 0);
        normalLayer.Add("Version", 101);
        normalLayer.Add("Name", string.Empty);
        normalLayer.Add("MappingInformationType", "ByPolygonVertex");
        normalLayer.Add("ReferenceInformationType", "Direct");
        normalLayer.Add("Normals", normals.ToArray());

        FbxNode uvLayer = geometry.Add("LayerElementUV", 0);
        uvLayer.Add("Version", 101);
        uvLayer.Add("Name", "UVMap");
        uvLayer.Add("MappingInformationType", "ByPolygonVertex");
        uvLayer.Add("ReferenceInformationType", "IndexToDirect");
        uvLayer.Add("UV", uvs.ToArray());
        uvLayer.Add("UVIndex", uvIndices.ToArray());

        FbxNode materialLayer = geometry.Add("LayerElementMaterial", 0);
        materialLayer.Add("Version", 101);
        materialLayer.Add("Name", string.Empty);
        materialLayer.Add("MappingInformationType", "AllSame");
        materialLayer.Add("ReferenceInformationType", "IndexToDirect");
        materialLayer.Add("Materials", new[] { 0 });

        if (lightmapped)
        {
            FbxNode lightLayer = geometry.Add("LayerElementUV", 1);
            lightLayer.Add("Version", 101);
            lightLayer.Add("Name", "Lightmap");
            lightLayer.Add("MappingInformationType", "ByPolygonVertex");
            lightLayer.Add("ReferenceInformationType", "IndexToDirect");
            lightLayer.Add("UV", lightUvs.ToArray());
            lightLayer.Add("UVIndex", lightIndices.ToArray());
        }

        FbxNode layer = geometry.Add("Layer", 0);
        layer.Add("Version", 100);
        foreach (string element in new[] { "LayerElementNormal", "LayerElementUV", "LayerElementMaterial" })
        {
            FbxNode entry = layer.Add("LayerElement");
            entry.Add("Type", element);
            entry.Add("TypedIndex", 0);
        }

        // A second set of coordinates lives in a layer of its own, as FBX keeps them.
        if (lightmapped)
        {
            FbxNode second = geometry.Add("Layer", 1);
            second.Add("Version", 100);
            FbxNode entry = second.Add("LayerElement");
            entry.Add("Type", "LayerElementUV");
            entry.Add("TypedIndex", 1);
        }

        return geometry;
    }

    private static FbxNode Model(long id, string name, Matrix4x4 local, bool isMesh, JsonObject? extras)
    {
        var model = new FbxNode("Model", id, FbxNode.ObjectName(name, "Model"), isMesh ? "Mesh" : "Null");
        model.Add("Version", 232);

        (Vector3 translation, Vector3 rotation, Vector3 scale) = Placement(local);
        FbxNode properties = model.Add("Properties70");
        properties.Property("Lcl Translation", "Lcl Translation", string.Empty, "A", (double)translation.X, (double)translation.Y, (double)translation.Z);
        properties.Property("Lcl Rotation", "Lcl Rotation", string.Empty, "A", (double)rotation.X, (double)rotation.Y, (double)rotation.Z);
        properties.Property("Lcl Scaling", "Lcl Scaling", string.Empty, "A", (double)scale.X, (double)scale.Y, (double)scale.Z);
        properties.Property("DefaultAttributeIndex", "int", "Integer", string.Empty, 0);
        properties.Property("InheritType", "enum", string.Empty, string.Empty, 1);

        // What the game is told about it, as user properties.
        foreach ((string key, JsonNode? value) in extras ?? [])
        {
            switch (value)
            {
                case JsonValue flag when flag.TryGetValue(out bool on):
                    properties.Property(key, "bool", string.Empty, "U", on ? 1 : 0);
                    break;
                case JsonValue number when number.TryGetValue(out double real):
                    properties.Property(key, "double", "Number", "U", real);
                    break;
                case JsonValue text when text.TryGetValue(out string? words):
                    properties.Property(key, "KString", string.Empty, "U", words ?? string.Empty);
                    break;
                case not null:
                    properties.Property(key, "KString", string.Empty, "U", value.ToJsonString());
                    break;
            }
        }

        model.Add("Shading", true);
        model.Add("Culling", "CullingOff");
        return model;
    }

    /// <summary>
    /// A local placement as FBX writes one: a move, a turn in degrees about X, then Y, then Z — its
    /// default order — and a scale.
    /// </summary>
    public static (Vector3 Translation, Vector3 Rotation, Vector3 Scale) Placement(Matrix4x4 local)
    {
        if (!Matrix4x4.Decompose(local, out Vector3 scale, out Quaternion turn, out Vector3 translation))
        {
            return (local.Translation, Vector3.Zero, Vector3.One);
        }

        return (translation, EulerXyz(turn), scale);
    }

    /// <summary>A turn as the three angles FBX gives it, in degrees: X first, then Y, then Z.</summary>
    public static Vector3 EulerXyz(Quaternion turn)
    {
        // The turn as a matrix on column vectors: R = Rz · Ry · Rx. System.Numerics keeps rows.
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(turn));
        float r20 = m.M13, r21 = m.M23, r22 = m.M33, r10 = m.M12, r00 = m.M11, r12 = m.M32, r11 = m.M22;

        float y = MathF.Asin(Math.Clamp(-r20, -1f, 1f));
        float x, z;
        if (MathF.Abs(r20) < 0.99999f)
        {
            x = MathF.Atan2(r21, r22);
            z = MathF.Atan2(r10, r00);
        }
        else
        {
            // Straight up or down: X and Z turn about the same axis, and Z is taken as none.
            x = MathF.Atan2(-r12, r11);
            z = 0f;
        }

        const float Degrees = 180f / MathF.PI;
        return new Vector3(x * Degrees, y * Degrees, z * Degrees);
    }

    private static FbxNode Material(long id)
    {
        var material = new FbxNode("Material", id, FbxNode.ObjectName(MaterialName, "Material"), string.Empty);
        material.Add("Version", 102);
        material.Add("ShadingModel", "lambert");
        material.Add("MultiLayer", 0);
        FbxNode properties = material.Add("Properties70");
        properties.Property("DiffuseColor", "Color", string.Empty, "A", 1d, 1d, 1d);
        properties.Property("DiffuseFactor", "Number", string.Empty, "A", 1d);
        properties.Property("EmissiveColor", "Color", string.Empty, "A", 0d, 0d, 0d);
        properties.Property("AmbientColor", "Color", string.Empty, "A", 0d, 0d, 0d);
        return material;
    }

    private static FbxNode Video(long id, string fileName, byte[] png)
    {
        var video = new FbxNode("Video", id, FbxNode.ObjectName(MaterialName, "Video"), "Clip");
        video.Add("Type", "Clip");
        FbxNode properties = video.Add("Properties70");
        properties.Property("Path", "KString", "XRefUrl", string.Empty, fileName);
        properties.Property("RelPath", "KString", "XRefUrl", string.Empty, fileName);
        video.Add("UseMipMap", 0);
        video.Add("Filename", fileName);
        video.Add("RelativeFilename", fileName);
        video.Add("Content", png);
        return video;
    }

    private static FbxNode Texture(long id, string fileName)
    {
        var texture = new FbxNode("Texture", id, FbxNode.ObjectName(MaterialName, "Texture"), string.Empty);
        texture.Add("Type", "TextureVideoClip");
        texture.Add("Version", 202);
        texture.Add("TextureName", FbxNode.ObjectName(MaterialName, "Texture"));
        FbxNode properties = texture.Add("Properties70");
        properties.Property("UseMaterial", "bool", string.Empty, string.Empty, 1);
        properties.Property("UVSet", "KString", string.Empty, string.Empty, "UVMap");
        texture.Add("Media", FbxNode.ObjectName(MaterialName, "Video"));
        texture.Add("FileName", fileName);
        texture.Add("RelativeFilename", fileName);
        texture.Add("ModelUVTranslation", 0d, 0d);
        texture.Add("ModelUVScaling", 1d, 1d);
        texture.Add("Texture_Alpha_Source", "None");
        texture.Add("Cropping", 0, 0, 0, 0);
        return texture;
    }

    private static FbxNode Header()
    {
        var header = new FbxNode("FBXHeaderExtension");
        header.Add("FBXHeaderVersion", 1003);
        header.Add("FBXVersion", FbxWriter.Version);
        header.Add("EncryptionType", 0);
        FbxNode stamp = header.Add("CreationTimeStamp");
        stamp.Add("Version", 1000);
        foreach ((string part, int value) in new[] { ("Year", 1970), ("Month", 1), ("Day", 1), ("Hour", 10), ("Minute", 0), ("Second", 0), ("Millisecond", 0) })
        {
            stamp.Add(part, value);
        }

        header.Add("Creator", "Blockage");
        FbxNode info = header.Add("SceneInfo", FbxNode.ObjectName("GlobalInfo", "SceneInfo"), "UserData");
        info.Add("Type", "UserData");
        info.Add("Version", 100);
        FbxNode meta = info.Add("MetaData");
        meta.Add("Version", 100);
        foreach (string field in new[] { "Title", "Subject", "Author", "Keywords", "Revision", "Comment" })
        {
            meta.Add(field, string.Empty);
        }

        FbxNode properties = info.Add("Properties70");
        properties.Property("Original|ApplicationVendor", "KString", string.Empty, string.Empty, "Blockage");
        properties.Property("Original|ApplicationName", "KString", string.Empty, string.Empty, "Blockage");
        return header;
    }

    private static FbxNode GlobalSettings()
    {
        var settings = new FbxNode("GlobalSettings");
        settings.Add("Version", 1000);
        FbxNode properties = settings.Add("Properties70");
        properties.Property("UpAxis", "int", "Integer", string.Empty, 1);
        properties.Property("UpAxisSign", "int", "Integer", string.Empty, 1);
        properties.Property("FrontAxis", "int", "Integer", string.Empty, 2);
        properties.Property("FrontAxisSign", "int", "Integer", string.Empty, 1);
        properties.Property("CoordAxis", "int", "Integer", string.Empty, 0);
        properties.Property("CoordAxisSign", "int", "Integer", string.Empty, 1);
        properties.Property("OriginalUpAxis", "int", "Integer", string.Empty, 1);
        properties.Property("OriginalUpAxisSign", "int", "Integer", string.Empty, 1);
        properties.Property("UnitScaleFactor", "double", "Number", string.Empty, 100d);
        properties.Property("OriginalUnitScaleFactor", "double", "Number", string.Empty, 100d);
        properties.Property("AmbientColor", "ColorRGB", "Color", string.Empty, 0d, 0d, 0d);
        properties.Property("DefaultCamera", "KString", string.Empty, string.Empty, "Producer Perspective");
        properties.Property("TimeMode", "enum", string.Empty, string.Empty, 11);
        properties.Property("TimeSpanStart", "KTime", "Time", string.Empty, 0L);
        properties.Property("TimeSpanStop", "KTime", "Time", string.Empty, 46_186_158_000L);
        properties.Property("CustomFrameRate", "double", "Number", string.Empty, 24d);
        return settings;
    }

    private static FbxNode Documents(long id)
    {
        var documents = new FbxNode("Documents");
        documents.Add("Count", 1);
        FbxNode document = documents.Add("Document", id, "Scene", "Scene");
        FbxNode properties = document.Add("Properties70");
        properties.Property("SourceObject", "object", string.Empty, string.Empty);
        properties.Property("ActiveAnimStackName", "KString", string.Empty, string.Empty, string.Empty);
        document.Add("RootNode", 0L);
        return documents;
    }

    private static FbxNode Definitions(int models, int geometries, int empties)
    {
        var definitions = new FbxNode("Definitions");
        (string Type, int Count)[] kinds =
        [
            ("GlobalSettings", 1), ("Model", models), ("Geometry", geometries), ("Material", 1), ("Texture", 1), ("Video", 1), ("NodeAttribute", empties),
        ];

        definitions.Add("Version", 100);
        definitions.Add("Count", kinds.Sum(kind => kind.Count));
        foreach ((string type, int count) in kinds.Where(kind => kind.Count > 0))
        {
            definitions.Add("ObjectType", type).Add("Count", count);
        }

        return definitions;
    }

    private static FbxNode Takes()
    {
        var takes = new FbxNode("Takes");
        takes.Add("Current", string.Empty);
        return takes;
    }
}
