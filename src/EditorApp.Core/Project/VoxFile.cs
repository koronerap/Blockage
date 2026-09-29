using System.Globalization;
using System.Numerics;
using System.Text;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>What writing a .vox came to: how many models and placed copies of them, and what could not go across as it was.</summary>
public sealed record VoxReport(int Models, int Instances, IReadOnlyList<string> Warnings);

/// <summary>
/// MagicaVoxel's .vox, in and out (Fullreleaseplan 8.1): its models, its scene of transforms, groups
/// and layers, its palette and its materials.
///
/// MagicaVoxel stands its models on Z; here Y is up. The turn between them, (x, y, z) to (x, z, −y),
/// is the one Blender's glTF export makes: it keeps handedness, so nothing comes out mirrored, and
/// MagicaVoxel's front, −Y, is the front here, +Z. A model sits with its middle voxel,
/// floor(size / 2), where its transform puts it; a transform turns only by quarter turns and flips.
///
/// Copies of one model come in as linked copies of one grid. A copy flipped rather than turned has
/// no rotation to be here, and comes in as a grid of its own, flipped. Going out, an object on the
/// voxel lattice keeps its model and its turn; one off it — turned between quarter turns, or of
/// another voxel size — is baked into the world's voxels.
/// </summary>
public static class VoxFile
{
    public const string Extension = ".vox";

    /// <summary>The largest a model is along any axis; bigger objects go out in pieces this size.</summary>
    public const int MaxModelSize = 256;

    private const int WrittenVersion = 200;

    // Where MagicaVoxel's cells go here, as a matrix and an offset: (a, b, c) to (a, c, −b − 1).
    private static readonly int[,] Axes = { { 1, 0, 0 }, { 0, 0, 1 }, { 0, -1, 0 } };
    private static readonly int[,] AxesBack = Transpose(Axes);
    private static readonly Int3 AxesOffset = new(0, 0, -1);

    // ---- Reading ------------------------------------------------------------------------------------

    public static VoxelScene Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }

    public static VoxelScene Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (ReadId(reader) != "VOX ")
        {
            throw new InvalidDataException("This is not a MagicaVoxel file: it does not start with VOX.");
        }

        reader.ReadInt32();
        if (ReadId(reader) != "MAIN")
        {
            throw new InvalidDataException("The file has no MAIN chunk.");
        }

        int mainContent = reader.ReadInt32();
        int mainChildren = reader.ReadInt32();
        reader.ReadBytes(mainContent);

        var file = new ParsedFile();
        long read = 0;
        while (read < mainChildren)
        {
            string id = ReadId(reader);
            int size = reader.ReadInt32();
            int children = reader.ReadInt32();
            byte[] body = reader.ReadBytes(size);
            reader.ReadBytes(children);
            if (body.Length < size)
            {
                throw new InvalidDataException($"The {id} chunk ends early.");
            }

            ReadChunk(id, body, file);
            read += 12 + size + children;
        }

        return Build(file);
    }

    private sealed class ParsedFile
    {
        public List<(Int3 Size, byte[] Voxels)> Models { get; } = [];

        public Int3? PendingSize { get; set; }

        public Color32[]? Palette { get; set; }

        public Dictionary<int, Dictionary<string, string>> Materials { get; } = [];

        public Dictionary<int, ParsedNode> Nodes { get; } = [];

        public Dictionary<int, (string? Name, bool Hidden)> Layers { get; } = [];
    }

    private sealed record ParsedNode(char Kind, Dictionary<string, string> Attributes, int[] Children, int Layer, Dictionary<string, string> Frame);

    private static void ReadChunk(string id, byte[] body, ParsedFile file)
    {
        using var chunk = new BinaryReader(new MemoryStream(body));
        switch (id)
        {
            case "SIZE":
                file.PendingSize = new Int3(chunk.ReadInt32(), chunk.ReadInt32(), chunk.ReadInt32());
                break;

            case "XYZI":
            {
                int count = chunk.ReadInt32();
                byte[] voxels = chunk.ReadBytes(count * 4);
                file.Models.Add((file.PendingSize ?? new Int3(MaxModelSize, MaxModelSize, MaxModelSize), voxels));
                file.PendingSize = null;
                break;
            }

            case "RGBA":
            {
                // The chunk's first colour is colour 1: colour 0 is empty, and has none.
                var palette = new Color32[Palette.Size];
                for (int i = 0; i < Palette.Size - 1; i++)
                {
                    byte[] rgba = chunk.ReadBytes(4);
                    palette[i + 1] = new Color32(rgba[0], rgba[1], rgba[2], 255);
                }

                file.Palette = palette;
                break;
            }

            case "MATL":
                file.Materials[chunk.ReadInt32()] = ReadDictionary(chunk);
                break;

            case "nTRN":
            {
                int node = chunk.ReadInt32();
                Dictionary<string, string> attributes = ReadDictionary(chunk);
                int child = chunk.ReadInt32();
                chunk.ReadInt32();
                int layer = chunk.ReadInt32();
                int frames = chunk.ReadInt32();
                Dictionary<string, string> frame = frames > 0 ? ReadDictionary(chunk) : [];
                file.Nodes[node] = new ParsedNode('T', attributes, [child], layer, frame);
                break;
            }

            case "nGRP":
            {
                int node = chunk.ReadInt32();
                Dictionary<string, string> attributes = ReadDictionary(chunk);
                int count = chunk.ReadInt32();
                int[] children = new int[Math.Max(count, 0)];
                for (int i = 0; i < children.Length; i++)
                {
                    children[i] = chunk.ReadInt32();
                }

                file.Nodes[node] = new ParsedNode('G', attributes, children, -1, []);
                break;
            }

            case "nSHP":
            {
                int node = chunk.ReadInt32();
                Dictionary<string, string> attributes = ReadDictionary(chunk);
                int count = chunk.ReadInt32();
                int[] models = new int[Math.Max(count, 0)];
                for (int i = 0; i < models.Length; i++)
                {
                    models[i] = chunk.ReadInt32();
                    ReadDictionary(chunk);
                }

                file.Nodes[node] = new ParsedNode('S', attributes, models, -1, []);
                break;
            }

            case "LAYR":
            {
                int layer = chunk.ReadInt32();
                Dictionary<string, string> attributes = ReadDictionary(chunk);
                file.Layers[layer] = (attributes.GetValueOrDefault("_name"), attributes.GetValueOrDefault("_hidden") == "1");
                break;
            }
        }
    }

    /// <summary>A copy of a model where the scene puts it: turned and moved, named, on a layer, maybe hidden.</summary>
    private readonly record struct Placement(int Model, int[,] Rotation, Int3 Translation, string? Name, int Layer, bool Hidden);

    private static VoxelScene Build(ParsedFile file)
    {
        var scene = new VoxelScene();
        var palette = new Palette();
        Color32[] colours = file.Palette ?? DefaultPalette();
        for (int i = 1; i < Palette.Size; i++)
        {
            palette[i] = colours[i];
            if (Palette.IsCustomIndex(i))
            {
                palette.SetCustomSaved(i, true);
            }
        }

        foreach ((int index, Dictionary<string, string> material) in file.Materials)
        {
            if (index > Palette.EmptyIndex && index < Palette.Size && MaterialOf(material) is { IsPlain: false } made)
            {
                palette.SetMaterial(index, made);
            }
        }

        scene.ReplacePalette(palette);

        List<Placement> placements = [];
        if (file.Nodes.ContainsKey(0))
        {
            Walk(file, 0, Identity(), new Int3(0, 0, 0), name: null, layer: 0, hidden: false, placements, depth: 0);
        }
        else
        {
            // An older file, before the scene: each model in the middle, standing on the ground.
            for (int i = 0; i < file.Models.Count; i++)
            {
                placements.Add(new Placement(i, Identity(), new Int3(0, 0, file.Models[i].Size.Z / 2), null, 0, false));
            }
        }

        var grids = new Dictionary<int, VoxelWorld>();
        var collections = new Dictionary<int, int>();
        bool layered = placements.Select(p => p.Layer).Distinct().Count() > 1
            || placements.Any(p => file.Layers.TryGetValue(p.Layer, out var layer) && layer.Name is { Length: > 0 });

        int count = 0;
        foreach (Placement placed in placements)
        {
            if (placed.Model < 0 || placed.Model >= file.Models.Count)
            {
                continue;
            }

            (Int3 size, byte[] voxels) = file.Models[placed.Model];
            Int3 half = new(size.X / 2, size.Y / 2, size.Z / 2);
            string name = placed.Name is { Length: > 0 } given ? given : $"Model {placed.Model + 1}";
            VoxelObject made;

            // The object's origin is the model's pivot, its middle voxel, as MagicaVoxel turns it about.
            Int3 pivot = Apply(Axes, placed.Translation);
            if (Determinant(placed.Rotation) > 0)
            {
                // Turned, not flipped: a linked copy of the model's one grid, placed by its turn.
                if (!grids.TryGetValue(placed.Model, out VoxelWorld? grid))
                {
                    grid = new VoxelWorld();
                    for (int i = 0; i + 3 < voxels.Length; i += 4)
                    {
                        grid.SetVoxel(Here(Subtract(new Int3(voxels[i], voxels[i + 1], voxels[i + 2]), half)), voxels[i + 3]);
                    }

                    grids[placed.Model] = grid;
                }

                int[,] turn = Multiply(Multiply(Axes, placed.Rotation), AxesBack);
                Int3 position = Subtract(Subtract(Add(pivot, AxesOffset), Apply(turn, AxesOffset)), FlipOffset(turn));
                made = scene.Add(grid, new ObjectTransform(position.ToVector3(), RotationOf(turn), 1f), name);
            }
            else
            {
                // Flipped: baked as it stands, on a grid of its own.
                var grid = new VoxelWorld();
                for (int i = 0; i + 3 < voxels.Length; i += 4)
                {
                    var voxel = new Int3(voxels[i], voxels[i + 1], voxels[i + 2]);
                    grid.SetVoxel(Here(Apply(placed.Rotation, Subtract(voxel, half))), voxels[i + 3]);
                }

                made = scene.Add(grid, ObjectTransform.Identity with { Position = pivot.ToVector3() }, name);
            }

            made.Visible = !placed.Hidden;
            count++;

            if (layered)
            {
                if (!collections.TryGetValue(placed.Layer, out int collectionId))
                {
                    (string? layerName, bool layerHidden) = file.Layers.GetValueOrDefault(placed.Layer);
                    SceneCollection collection = scene.AddCollection(layerName is { Length: > 0 } ? layerName : $"Layer {placed.Layer}", parentId: 0);
                    collection.Visible = !layerHidden;
                    collectionId = collection.Id;
                    collections[placed.Layer] = collectionId;
                }

                scene.SetCollection(made.Id, collectionId);
            }
        }

        if (count == 0)
        {
            throw new InvalidDataException("The file has no models in it.");
        }

        scene.RefreshCollections();
        scene.AddDefaultSun();
        return scene;
    }

    private static void Walk(ParsedFile file, int id, int[,] rotation, Int3 translation, string? name, int layer, bool hidden, List<Placement> placements, int depth)
    {
        if (depth > 64 || !file.Nodes.TryGetValue(id, out ParsedNode? node))
        {
            return;
        }

        switch (node.Kind)
        {
            case 'T':
            {
                // A transform: its frame's turn and move, after those above it.
                int[,] own = node.Frame.TryGetValue("_r", out string? r) && int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out int packed)
                    ? Unpack(packed)
                    : Identity();
                Int3 move = node.Frame.TryGetValue("_t", out string? t) ? ParseTranslation(t) : new Int3(0, 0, 0);
                Walk(
                    file,
                    node.Children[0],
                    Multiply(rotation, own),
                    Add(Apply(rotation, move), translation),
                    node.Attributes.GetValueOrDefault("_name") ?? name,
                    node.Layer >= 0 ? node.Layer : layer,
                    hidden || node.Attributes.GetValueOrDefault("_hidden") == "1",
                    placements,
                    depth + 1);
                break;
            }

            case 'G':
                foreach (int child in node.Children)
                {
                    Walk(file, child, rotation, translation, name: null, layer, hidden, placements, depth + 1);
                }

                break;

            case 'S':
                foreach (int model in node.Children)
                {
                    placements.Add(new Placement(model, rotation, translation, name, layer, hidden));
                }

                break;
        }
    }

    /// <summary>MagicaVoxel's material as one here: glow, metal, roughness and glass.</summary>
    private static VoxelMaterial MaterialOf(Dictionary<string, string> material)
    {
        float Value(string key, float fallback) =>
            material.TryGetValue(key, out string? text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

        string type = material.GetValueOrDefault("_type") ?? "_diffuse";
        float roughness = Value("_rough", 0.1f);
        return type switch
        {
            "_emit" => VoxelMaterial.Of(Value("_emit", 1f), 0f, roughness, 1f),
            "_metal" => VoxelMaterial.Of(0f, Value("_metal", 1f), roughness, 1f),
            "_glass" => VoxelMaterial.Of(0f, 0f, roughness, 1f - Value("_trans", Value("_alpha", 0.5f))),
            "_blend" => VoxelMaterial.Of(0f, Value("_metal", 0f), roughness, 1f - Value("_trans", Value("_alpha", 0f))),
            _ => VoxelMaterial.Plain,
        };
    }

    // ---- Writing ------------------------------------------------------------------------------------

    public static VoxReport Save(VoxelScene scene, string path)
    {
        string temporary = path + ".saving";
        VoxReport report;
        using (FileStream stream = File.Create(temporary))
        {
            report = Save(scene, stream);
        }

        File.Move(temporary, path, overwrite: true);
        return report;
    }

    public static VoxReport Save(VoxelScene scene, Stream stream)
    {
        var warnings = new List<string>();
        var models = new List<(Int3 Size, List<(Int3 Voxel, byte Colour)> Voxels)>();
        var instances = new List<(string Name, int Model, int[,] Rotation, Int3 Translation, int Layer, bool Hidden)>();
        var shared = new Dictionary<VoxelWorld, List<(int Model, Int3 Min)>>(ReferenceEqualityComparer.Instance);
        var layers = new Dictionary<int, (int Layer, string Name, bool Hidden)>();
        bool painted = false;

        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.IsExported || o.IsEmpty)
            {
                continue;
            }

            VoxelWorld grid = o.Shown;
            painted |= grid.Chunks.Values.Any(chunk => chunk.FaceOverrides().Any());
            int layer = LayerOf(scene, o, layers);

            if (LatticeTurn(o.Transform) is { } turn)
            {
                // On the lattice: the object's own voxels as its model, with its turn and place.
                if (o.HasModifiers || !shared.TryGetValue(grid, out List<(int Model, Int3 Min)>? pieces))
                {
                    pieces = [];
                    foreach ((Int3 min, List<(Int3 Voxel, byte Colour)> voxels) in Pieces(Cells(grid).Select(cell => (Apply(AxesBack, Subtract(cell.Cell, AxesOffset)), cell.Colour))))
                    {
                        models.Add((SizeOf(voxels), voxels));
                        pieces.Add((models.Count - 1, min));
                    }

                    if (!o.HasModifiers)
                    {
                        shared[grid] = pieces;
                    }
                }

                Int3 position = Round(o.Transform.Position);
                int[,] rotation = Multiply(Multiply(AxesBack, turn), Axes);
                foreach ((int model, Int3 min) in pieces)
                {
                    Int3 size = models[model].Size;
                    Int3 half = new(size.X / 2, size.Y / 2, size.Z / 2);
                    Int3 inWorld = Add(Add(Apply(turn, Apply(Axes, min)), Apply(turn, AxesOffset)), Add(position, FlipOffset(turn)));
                    Int3 translation = Add(Apply(rotation, half), Apply(AxesBack, Subtract(inWorld, AxesOffset)));
                    instances.Add((o.Name, model, rotation, translation, layer, !o.Visible));
                }
            }
            else
            {
                // Off the lattice: baked into the world's voxels where it stands.
                warnings.Add($"{o.Name} is turned or sized off the voxel lattice, so it is written baked into its place.");
                foreach ((Int3 min, List<(Int3 Voxel, byte Colour)> voxels) in Pieces(Baked(o, grid).Select(cell => (Apply(AxesBack, Subtract(cell.Cell, AxesOffset)), cell.Colour))))
                {
                    models.Add((SizeOf(voxels), voxels));
                    Int3 size = models[^1].Size;
                    instances.Add((o.Name, models.Count - 1, Identity(), Add(min, new Int3(size.X / 2, size.Y / 2, size.Z / 2)), layer, !o.Visible));
                }
            }
        }

        if (painted)
        {
            warnings.Add("Painted faces are written in their voxel's colour: MagicaVoxel colours whole voxels.");
        }

        if (models.Count == 0)
        {
            throw new InvalidOperationException("There is nothing to write: no object with voxels is exported.");
        }

        var children = new MemoryStream();
        using (var body = new BinaryWriter(children, Encoding.UTF8, leaveOpen: true))
        {
            foreach ((Int3 size, List<(Int3 Voxel, byte Colour)> voxels) in models)
            {
                WriteChunk(body, "SIZE", chunk =>
                {
                    chunk.Write(size.X);
                    chunk.Write(size.Y);
                    chunk.Write(size.Z);
                });
                WriteChunk(body, "XYZI", chunk =>
                {
                    chunk.Write(voxels.Count);
                    foreach ((Int3 voxel, byte colour) in voxels)
                    {
                        chunk.Write((byte)voxel.X);
                        chunk.Write((byte)voxel.Y);
                        chunk.Write((byte)voxel.Z);
                        chunk.Write(colour);
                    }
                });
            }

            // The scene: a root transform over one group, and a transform and a shape for each copy.
            WriteTransform(body, 0, [], child: 1, layer: -1, []);
            WriteChunk(body, "nGRP", chunk =>
            {
                chunk.Write(1);
                WriteDictionary(chunk, []);
                chunk.Write(instances.Count);
                for (int i = 0; i < instances.Count; i++)
                {
                    chunk.Write(2 + (i * 2));
                }
            });

            for (int i = 0; i < instances.Count; i++)
            {
                (string name, int model, int[,] rotation, Int3 translation, int layer, bool hidden) = instances[i];
                var attributes = new Dictionary<string, string> { ["_name"] = name };
                if (hidden)
                {
                    attributes["_hidden"] = "1";
                }

                var frame = new Dictionary<string, string>
                {
                    ["_t"] = string.Create(CultureInfo.InvariantCulture, $"{translation.X} {translation.Y} {translation.Z}"),
                };
                if (!IsIdentity(rotation))
                {
                    frame["_r"] = Pack(rotation).ToString(CultureInfo.InvariantCulture);
                }

                WriteTransform(body, 2 + (i * 2), attributes, child: 3 + (i * 2), layer, frame);
                int shape = 3 + (i * 2);
                WriteChunk(body, "nSHP", chunk =>
                {
                    chunk.Write(shape);
                    WriteDictionary(chunk, []);
                    chunk.Write(1);
                    chunk.Write(model);
                    WriteDictionary(chunk, []);
                });
            }

            foreach ((int layer, string name, bool hidden) in layers.Values.OrderBy(l => l.Layer))
            {
                WriteChunk(body, "LAYR", chunk =>
                {
                    chunk.Write(layer);
                    var attributes = new Dictionary<string, string> { ["_name"] = name };
                    if (hidden)
                    {
                        attributes["_hidden"] = "1";
                    }

                    WriteDictionary(chunk, attributes);
                    chunk.Write(-1);
                });
            }

            WriteChunk(body, "RGBA", chunk =>
            {
                for (int i = 1; i <= Palette.Size; i++)
                {
                    Color32 colour = i < Palette.Size ? scene.Palette[i] : default;
                    chunk.Write(colour.R);
                    chunk.Write(colour.G);
                    chunk.Write(colour.B);
                    chunk.Write(i < Palette.Size ? (byte)255 : (byte)0);
                }
            });

            foreach ((int index, VoxelMaterial material) in scene.Palette.Materials())
            {
                WriteChunk(body, "MATL", chunk =>
                {
                    chunk.Write(index);
                    WriteDictionary(chunk, MaterialDictionary(material));
                });
            }
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("VOX "));
        writer.Write(WrittenVersion);
        writer.Write(Encoding.ASCII.GetBytes("MAIN"));
        writer.Write(0);
        writer.Write((int)children.Length);
        children.Position = 0;
        children.CopyTo(stream);

        return new VoxReport(models.Count, instances.Count, warnings);
    }

    /// <summary>The layer an object goes on: one for each collection at the top, as far as MagicaVoxel's sixteen go.</summary>
    private static int LayerOf(VoxelScene scene, VoxelObject o, Dictionary<int, (int Layer, string Name, bool Hidden)> layers)
    {
        SceneCollection? top = scene.FindCollection(o.CollectionId);
        for (int depth = 0; top is { ParentId: not 0 } && depth < 64; depth++)
        {
            top = scene.FindCollection(top.ParentId);
        }

        if (top is null)
        {
            return 0;
        }

        if (!layers.TryGetValue(top.Id, out var layer))
        {
            if (layers.Count >= 15)
            {
                return 0;
            }

            layer = (layers.Count + 1, top.Name, !top.Visible);
            layers[top.Id] = layer;
        }

        return layer.Layer;
    }

    private static Dictionary<string, string> MaterialDictionary(VoxelMaterial material)
    {
        static string Text(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        string type = material.Emission > 0f ? "_emit"
            : material.IsTransparent ? "_glass"
            : material.Metallic > 0f ? "_metal"
            : "_diffuse";

        var values = new Dictionary<string, string>
        {
            ["_type"] = type,
            ["_rough"] = Text(material.Roughness),
        };

        switch (type)
        {
            case "_emit":
                values["_emit"] = Text(material.Emission);
                values["_flux"] = "1";
                break;
            case "_glass":
                values["_trans"] = Text(material.Transparency);
                values["_alpha"] = Text(material.Transparency);
                values["_ior"] = "0.3";
                break;
            case "_metal":
                values["_metal"] = Text(material.Metallic);
                break;
        }

        return values;
    }

    /// <summary>
    /// The turn of a transform that keeps an object on the voxel lattice — voxels of size 1, moved by
    /// whole voxels, turned by quarter turns — as a matrix; null for any other.
    /// </summary>
    private static int[,]? LatticeTurn(ObjectTransform placed)
    {
        Vector3 position = placed.Position;
        if (MathF.Abs(placed.VoxelSize - 1f) > 1e-4f
            || Vector3.Distance(position, Round(position).ToVector3()) > 1e-3f)
        {
            return null;
        }

        var turn = new int[3, 3];
        Vector3[] columns = [Vector3.Transform(Vector3.UnitX, placed.Rotation), Vector3.Transform(Vector3.UnitY, placed.Rotation), Vector3.Transform(Vector3.UnitZ, placed.Rotation)];
        for (int column = 0; column < 3; column++)
        {
            Vector3 axis = columns[column];
            float[] parts = [axis.X, axis.Y, axis.Z];
            for (int row = 0; row < 3; row++)
            {
                float rounded = MathF.Round(parts[row]);
                if (MathF.Abs(parts[row] - rounded) > 1e-3f)
                {
                    return null;
                }

                turn[row, column] = (int)rounded;
            }
        }

        return Determinant(turn) == 1 ? turn : null;
    }

    /// <summary>An object's voxels as the world's cells, for one that is off the lattice: each cell takes the voxel its middle falls in.</summary>
    private static IEnumerable<(Int3 Cell, byte Colour)> Baked(VoxelObject o, VoxelWorld grid)
    {
        if (!grid.TryGetBounds(out Int3 low, out Int3 high))
        {
            yield break;
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector3((corner & 1) == 0 ? low.X : high.X + 1, (corner & 2) == 0 ? low.Y : high.Y + 1, (corner & 4) == 0 ? low.Z : high.Z + 1);
            Vector3 world = o.Transform.TransformPoint(local);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }

        for (int x = (int)MathF.Floor(min.X); x < (int)MathF.Ceiling(max.X); x++)
        for (int y = (int)MathF.Floor(min.Y); y < (int)MathF.Ceiling(max.Y); y++)
        for (int z = (int)MathF.Floor(min.Z); z < (int)MathF.Ceiling(max.Z); z++)
        {
            Vector3 local = o.Transform.InverseTransformPoint(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
            byte colour = grid.GetVoxel((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), (int)MathF.Floor(local.Z));
            if (colour != Palette.EmptyIndex)
            {
                yield return (new Int3(x, y, z), colour);
            }
        }
    }

    private static IEnumerable<(Int3 Cell, byte Colour)> Cells(VoxelWorld grid)
    {
        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            for (int i = 0; i < Chunk.VoxelCount; i++)
            {
                Int3 local = Chunk.FromLinearIndex(i);
                byte colour = chunk.Get(local.X, local.Y, local.Z);
                if (colour != Palette.EmptyIndex)
                {
                    yield return (new Int3((coord.X << Chunk.SizeShift) + local.X, (coord.Y << Chunk.SizeShift) + local.Y, (coord.Z << Chunk.SizeShift) + local.Z), colour);
                }
            }
        }
    }

    /// <summary>Voxels cut into models no bigger than MagicaVoxel takes, each with where its corner is, and moved to start there.</summary>
    private static IEnumerable<(Int3 Min, List<(Int3 Voxel, byte Colour)> Voxels)> Pieces(IEnumerable<(Int3 Voxel, byte Colour)> voxels)
    {
        List<(Int3 Voxel, byte Colour)> all = [.. voxels];
        if (all.Count == 0)
        {
            yield break;
        }

        Int3 min = all.Aggregate(all[0].Voxel, (m, v) => new Int3(Math.Min(m.X, v.Voxel.X), Math.Min(m.Y, v.Voxel.Y), Math.Min(m.Z, v.Voxel.Z)));
        foreach (var piece in all.GroupBy(v => new Int3(
                     (v.Voxel.X - min.X) / MaxModelSize, (v.Voxel.Y - min.Y) / MaxModelSize, (v.Voxel.Z - min.Z) / MaxModelSize)))
        {
            Int3 corner = Add(min, new Int3(piece.Key.X * MaxModelSize, piece.Key.Y * MaxModelSize, piece.Key.Z * MaxModelSize));
            Int3 low = piece.Aggregate(piece.First().Voxel, (m, v) => new Int3(Math.Min(m.X, v.Voxel.X), Math.Min(m.Y, v.Voxel.Y), Math.Min(m.Z, v.Voxel.Z)));
            low = new Int3(Math.Max(low.X, corner.X), Math.Max(low.Y, corner.Y), Math.Max(low.Z, corner.Z));
            yield return (low, [.. piece.Select(v => (Subtract(v.Voxel, low), v.Colour))]);
        }
    }

    private static Int3 SizeOf(List<(Int3 Voxel, byte Colour)> voxels) =>
        voxels.Aggregate(new Int3(1, 1, 1), (s, v) => new Int3(Math.Max(s.X, v.Voxel.X + 1), Math.Max(s.Y, v.Voxel.Y + 1), Math.Max(s.Z, v.Voxel.Z + 1)));

    // ---- The chunks ---------------------------------------------------------------------------------

    private static string ReadId(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static Dictionary<string, string> ReadDictionary(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        var values = new Dictionary<string, string>();
        for (int i = 0; i < count; i++)
        {
            string key = ReadString(reader);
            values[key] = ReadString(reader);
        }

        return values;
    }

    private static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));

    private static void WriteChunk(BinaryWriter writer, string id, Action<BinaryWriter> content)
    {
        using var body = new MemoryStream();
        using (var chunk = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            content(chunk);
        }

        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write((int)body.Length);
        writer.Write(0);
        writer.Write(body.ToArray());
    }

    private static void WriteTransform(BinaryWriter writer, int node, Dictionary<string, string> attributes, int child, int layer, Dictionary<string, string> frame) =>
        WriteChunk(writer, "nTRN", chunk =>
        {
            chunk.Write(node);
            WriteDictionary(chunk, attributes);
            chunk.Write(child);
            chunk.Write(-1);
            chunk.Write(layer);
            chunk.Write(1);
            WriteDictionary(chunk, frame);
        });

    private static void WriteDictionary(BinaryWriter writer, Dictionary<string, string> values)
    {
        writer.Write(values.Count);
        foreach ((string key, string value) in values)
        {
            WriteString(writer, key);
            WriteString(writer, value);
        }
    }

    private static void WriteString(BinaryWriter writer, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    // ---- Turns as MagicaVoxel packs them ------------------------------------------------------------

    /// <summary>
    /// A turn from its byte: bits 0–1 say which column the first row's one is in, bits 2–3 the
    /// second row's, and bits 4–6 whether each row's is negative. The third row takes the column left.
    /// </summary>
    public static int[,] Unpack(int packed)
    {
        int first = packed & 3;
        int second = (packed >> 2) & 3;
        if (first > 2 || second > 2 || first == second)
        {
            return Identity();
        }

        int third = 3 - first - second;
        var turn = new int[3, 3];
        turn[0, first] = (packed & 16) != 0 ? -1 : 1;
        turn[1, second] = (packed & 32) != 0 ? -1 : 1;
        turn[2, third] = (packed & 64) != 0 ? -1 : 1;
        return turn;
    }

    public static int Pack(int[,] turn)
    {
        int packed = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                if (turn[row, column] == 0)
                {
                    continue;
                }

                if (row < 2)
                {
                    packed |= column << (row * 2);
                }

                if (turn[row, column] < 0)
                {
                    packed |= 16 << row;
                }
            }
        }

        return packed;
    }

    // ---- Small integer geometry ---------------------------------------------------------------------

    /// <summary>A MagicaVoxel model's voxel as a cell here.</summary>
    private static Int3 Here(Int3 voxel) => Add(Apply(Axes, voxel), AxesOffset);

    private static int[,] Identity() => new[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    private static bool IsIdentity(int[,] m) => m[0, 0] == 1 && m[1, 1] == 1 && m[2, 2] == 1;

    private static int[,] Transpose(int[,] m)
    {
        var t = new int[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                t[i, j] = m[j, i];
            }
        }

        return t;
    }

    private static int[,] Multiply(int[,] a, int[,] b)
    {
        var m = new int[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                m[i, j] = (a[i, 0] * b[0, j]) + (a[i, 1] * b[1, j]) + (a[i, 2] * b[2, j]);
            }
        }

        return m;
    }

    private static Int3 Apply(int[,] m, Int3 v) => new(
        (m[0, 0] * v.X) + (m[0, 1] * v.Y) + (m[0, 2] * v.Z),
        (m[1, 0] * v.X) + (m[1, 1] * v.Y) + (m[1, 2] * v.Z),
        (m[2, 0] * v.X) + (m[2, 1] * v.Y) + (m[2, 2] * v.Z));

    private static int Determinant(int[,] m) =>
        (m[0, 0] * ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1])))
        - (m[0, 1] * ((m[1, 0] * m[2, 2]) - (m[1, 2] * m[2, 0])))
        + (m[0, 2] * ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])));

    /// <summary>How far a turned cell's corner moves back along each axis the turn sends negative.</summary>
    private static Int3 FlipOffset(int[,] turn) => new(
        Math.Min(0, turn[0, 0] + turn[0, 1] + turn[0, 2]),
        Math.Min(0, turn[1, 0] + turn[1, 1] + turn[1, 2]),
        Math.Min(0, turn[2, 0] + turn[2, 1] + turn[2, 2]));

    /// <summary>A turn as a rotation, for a matrix that is one — no flip in it.</summary>
    private static Quaternion RotationOf(int[,] turn) => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
        turn[0, 0], turn[1, 0], turn[2, 0], 0f,
        turn[0, 1], turn[1, 1], turn[2, 1], 0f,
        turn[0, 2], turn[1, 2], turn[2, 2], 0f,
        0f, 0f, 0f, 1f)));

    private static Int3 Add(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static Int3 Subtract(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static Int3 Round(Vector3 v) => new((int)MathF.Round(v.X), (int)MathF.Round(v.Y), (int)MathF.Round(v.Z));

    private static Int3 ParseTranslation(string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int Part(int i) => i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        return new Int3(Part(0), Part(1), Part(2));
    }

    /// <summary>
    /// The palette MagicaVoxel gives a file that brings none: a cube of six steps a channel, brightest
    /// first and without its black, then ten steps each of red, green, blue and grey.
    /// </summary>
    public static Color32[] DefaultPalette()
    {
        var palette = new Color32[Palette.Size];
        byte[] cube = [0xff, 0xcc, 0x99, 0x66, 0x33, 0x00];
        byte[] ramp = [0xee, 0xdd, 0xbb, 0xaa, 0x88, 0x77, 0x55, 0x44, 0x22, 0x11];
        int index = 1;
        foreach (byte r in cube)
        foreach (byte g in cube)
        foreach (byte b in cube)
        {
            if (r != 0 || g != 0 || b != 0)
            {
                palette[index++] = new Color32(r, g, b);
            }
        }

        foreach (byte step in ramp)
        {
            palette[index++] = new Color32(step, 0, 0);
        }

        foreach (byte step in ramp)
        {
            palette[index++] = new Color32(0, step, 0);
        }

        foreach (byte step in ramp)
        {
            palette[index++] = new Color32(0, 0, step);
        }

        foreach (byte step in ramp)
        {
            palette[index++] = new Color32(step, step, step);
        }

        return palette;
    }
}
