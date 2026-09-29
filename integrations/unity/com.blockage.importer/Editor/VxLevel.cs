using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Blockage.Importer
{
    /// <summary>
    /// An object's voxels as Blockage keeps them: 32³ chunks of palette indices, 0 for empty, and the
    /// faces painted a colour of their own. Coordinates are the object's own cells, Blockage's axes.
    /// </summary>
    internal sealed class VoxelGrid
    {
        public const int Size = 32;

        public readonly Dictionary<Vector3Int, byte[]> Chunks = new Dictionary<Vector3Int, byte[]>();

        /// <summary>Per chunk, a painted face's colour by its voxel's index times six plus its face.</summary>
        public readonly Dictionary<Vector3Int, Dictionary<int, byte>> Faces = new Dictionary<Vector3Int, Dictionary<int, byte>>();

        public static int Index(int x, int y, int z) => (y << 10) | (z << 5) | x;

        public static Vector3Int CellOf(Vector3Int chunk, int index) =>
            new Vector3Int((chunk.x * Size) + (index & 31), (chunk.y * Size) + (index >> 10), (chunk.z * Size) + ((index >> 5) & 31));

        public bool IsEmpty
        {
            get
            {
                foreach (byte[] cells in Chunks.Values)
                {
                    foreach (byte cell in cells)
                    {
                        if (cell != 0)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        public byte Get(int x, int y, int z) =>
            Chunks.TryGetValue(new Vector3Int(x >> 5, y >> 5, z >> 5), out byte[] cells) ? cells[Index(x & 31, y & 31, z & 31)] : (byte)0;

        /// <summary>The colour a face shows: its own paint, or its voxel's.</summary>
        public byte FaceColour(int x, int y, int z, int face)
        {
            var chunk = new Vector3Int(x >> 5, y >> 5, z >> 5);
            int index = Index(x & 31, y & 31, z & 31);
            if (Faces.TryGetValue(chunk, out Dictionary<int, byte> painted) && painted.TryGetValue((index * 6) + face, out byte colour))
            {
                return colour;
            }

            return Chunks.TryGetValue(chunk, out byte[] cells) ? cells[index] : (byte)0;
        }

        public void Set(int x, int y, int z, byte colour)
        {
            var chunk = new Vector3Int(x >> 5, y >> 5, z >> 5);
            if (!Chunks.TryGetValue(chunk, out byte[] cells))
            {
                cells = new byte[Size * Size * Size];
                Chunks[chunk] = cells;
            }

            cells[Index(x & 31, y & 31, z & 31)] = colour;
        }

        public void Paint(int x, int y, int z, int face, byte colour)
        {
            var chunk = new Vector3Int(x >> 5, y >> 5, z >> 5);
            if (!Faces.TryGetValue(chunk, out Dictionary<int, byte> painted))
            {
                painted = new Dictionary<int, byte>();
                Faces[chunk] = painted;
            }

            painted[(Index(x & 31, y & 31, z & 31) * 6) + face] = colour;
        }
    }

    /// <summary>A Mirror or an Array over an object's voxels, as Blockage shows and exports it.</summary>
    internal struct Modifier
    {
        public bool Mirror;
        public int Axis;
        public int Plane;
        public int Count;
        public int Step;
    }

    internal sealed class LevelObject
    {
        public int Id;
        public string Name = string.Empty;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public float VoxelSize = 1f;
        public bool Visible = true;
        public int Parent;
        public int LinkedTo;
        public int Collection;
        public string Marker;
        public Vector3 MarkerSize = Vector3.one;
        public readonly List<BlockageProperties.Entry> Properties = new List<BlockageProperties.Entry>();
        public readonly List<Modifier> Modifiers = new List<Modifier>();
        public VoxelGrid Grid = new VoxelGrid();
    }

    internal sealed class LevelLight
    {
        public string Name = "Light";
        public string Kind = "point";
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Color Colour = Color.white;
        public float Intensity = 1f;
        public float Range = 15f;
        public float SpotAngle = 45f;
        public float SpotBlend = 0.15f;
        public bool Visible = true;
        public int Parent;
    }

    internal struct LevelMaterial
    {
        public float Emission;
        public float Metallic;
        public float Roughness;
        public float Opacity;
    }

    internal sealed class Level
    {
        public string Name = "level";
        public readonly Color32[] Palette = new Color32[256];
        public readonly Dictionary<int, LevelMaterial> Materials = new Dictionary<int, LevelMaterial>();
        public readonly List<LevelObject> Objects = new List<LevelObject>();
        public readonly List<LevelLight> Lights = new List<LevelLight>();

        /// <summary>Each collection's parent, whether it is shown, and whether it goes out in exports.</summary>
        public readonly Dictionary<int, (int Parent, bool Visible, bool Export)> Collections = new Dictionary<int, (int, bool, bool)>();

        /// <summary>What Blockage's own exports take: shown, and in no collection kept out of them.</summary>
        public bool IsExported(int collection, bool visible)
        {
            for (int depth = 0; collection != 0 && depth < 64; depth++)
            {
                if (!Collections.TryGetValue(collection, out (int Parent, bool Visible, bool Export) entry))
                {
                    break;
                }

                if (!entry.Visible || !entry.Export)
                {
                    return false;
                }

                collection = entry.Parent;
            }

            return visible;
        }
    }

    /// <summary>
    /// Reads a .vxlevel: a zip of a JSON manifest and each object's run-length coded chunks — the
    /// same file Blockage saves, read without Blockage (Fullreleaseplan 8.7).
    /// </summary>
    internal static class VxLevel
    {
        /// <summary>The newest version this importer knows; a newer file wants a newer package.</summary>
        public const int SupportedVersion = 6;

        public static Level Read(string path)
        {
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("This is not a .vxlevel: manifest.json is missing.");
                string text;
                using (var reader = new StreamReader(manifestEntry.Open()))
                {
                    text = reader.ReadToEnd();
                }

                var manifest = Json.Parse(text) as Dictionary<string, object> ?? throw new InvalidDataException("manifest.json is not a JSON object.");
                int version = manifest.Int("version") ?? 1;
                if (version > SupportedVersion)
                {
                    throw new InvalidDataException($"The level was saved by a newer Blockage (version {version}); this importer reads up to {SupportedVersion}. Update the package.");
                }

                var level = new Level { Name = manifest.Text("name", "level") };
                ReadPalette(manifest, level);
                float levelVoxelSize = (float)manifest.Number("voxelSize", 1);

                foreach (object item in manifest.List("collections"))
                {
                    if (item is Dictionary<string, object> collection && collection.Int("id") is int id)
                    {
                        level.Collections[id] = (collection.Int("parent") ?? 0, collection.Bool("visible", true), collection.Bool("export", true));
                    }
                }

                List<object> objects = manifest.List("objects");
                if (objects.Count == 0 && manifest.List("chunks").Count > 0)
                {
                    // Version 1: one grid at the root.
                    var only = new LevelObject { Id = 1, Name = "Object 1", VoxelSize = levelVoxelSize };
                    ReadChunks(zip, manifest.List("chunks"), coord => $"chunks/{coord.x}_{coord.y}_{coord.z}.bin", null, only.Grid);
                    level.Objects.Add(only);
                }

                var byId = new Dictionary<int, LevelObject>();
                foreach (object item in objects)
                {
                    if (!(item is Dictionary<string, object> entry))
                    {
                        continue;
                    }

                    LevelObject o = ReadObject(entry, levelVoxelSize);
                    ReadChunks(zip, entry.List("chunks"), coord => $"objects/{o.Id}/chunks/{coord.x}_{coord.y}_{coord.z}.bin", $"objects/{o.Id}/faces/", o.Grid);
                    level.Objects.Add(o);
                    byId[o.Id] = o;
                }

                // Linked copies share the grid of the one that was written.
                foreach (LevelObject o in level.Objects)
                {
                    if (o.LinkedTo != 0 && byId.TryGetValue(o.LinkedTo, out LevelObject source) && source != o)
                    {
                        o.Grid = source.Grid;
                    }
                }

                foreach (object item in manifest.List("lights"))
                {
                    if (item is Dictionary<string, object> light)
                    {
                        level.Lights.Add(ReadLight(light));
                    }
                }

                return level;
            }
        }

        private static void ReadPalette(Dictionary<string, object> manifest, Level level)
        {
            List<object> colours = manifest.List("palette");
            for (int i = 1; i < Math.Min(colours.Count, 256); i++)
            {
                if (colours[i] is string hex)
                {
                    level.Palette[i] = Colour(hex);
                }
            }

            foreach (object item in manifest.List("materials"))
            {
                if (item is Dictionary<string, object> material && material.Int("index") is int index && index > 0 && index < 256)
                {
                    level.Materials[index] = new LevelMaterial
                    {
                        Emission = (float)material.Number("emission"),
                        Metallic = (float)material.Number("metallic"),
                        Roughness = (float)material.Number("roughness", 1),
                        Opacity = (float)material.Number("opacity", 1),
                    };
                }
            }
        }

        private static LevelObject ReadObject(Dictionary<string, object> entry, float levelVoxelSize)
        {
            var o = new LevelObject
            {
                Id = entry.Int("id") ?? 0,
                Name = entry.Text("name", "Object"),
                Visible = entry.Bool("visible", true),
                Parent = entry.Int("parent") ?? 0,
                LinkedTo = entry.Int("linkedTo") ?? 0,
                Collection = entry.Int("collection") ?? 0,
                VoxelSize = (float)entry.Number("voxelSize", levelVoxelSize),
                Marker = entry.Text("marker"),
            };

            float[] position = entry.Floats("position");
            if (position != null && position.Length == 3)
            {
                o.Position = new Vector3(position[0], position[1], position[2]);
            }

            float[] rotation = entry.Floats("rotation");
            if (rotation != null && rotation.Length == 4 && (rotation[0] * rotation[0]) + (rotation[1] * rotation[1]) + (rotation[2] * rotation[2]) + (rotation[3] * rotation[3]) > 1e-6f)
            {
                o.Rotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]).normalized;
            }

            float[] markerSize = entry.Floats("markerSize");
            if (markerSize != null && markerSize.Length == 3)
            {
                o.MarkerSize = new Vector3(markerSize[0], markerSize[1], markerSize[2]);
            }

            foreach (object item in entry.List("properties"))
            {
                if (item is Dictionary<string, object> property && property.Text("key") is string key && key.Length > 0)
                {
                    o.Properties.Add(new BlockageProperties.Entry { key = key, type = property.Text("type", "text"), value = property.Text("value", string.Empty) });
                }
            }

            foreach (object item in entry.List("modifiers"))
            {
                if (item is Dictionary<string, object> modifier && modifier.Bool("enabled", true))
                {
                    o.Modifiers.Add(new Modifier
                    {
                        Mirror = modifier.Text("kind", "mirror") == "mirror",
                        Axis = modifier.Text("axis", "x") == "y" ? 1 : modifier.Text("axis", "x") == "z" ? 2 : 0,
                        Plane = modifier.Int("plane") ?? 0,
                        Count = Mathf.Clamp(modifier.Int("count") ?? 3, 1, 64),
                        Step = modifier.Int("step") ?? 8,
                    });
                }
            }

            return o;
        }

        private static LevelLight ReadLight(Dictionary<string, object> entry)
        {
            var light = new LevelLight
            {
                Name = entry.Text("name", "Light"),
                Kind = entry.Text("kind", "point"),
                Colour = Colour(entry.Text("colour", "#FFFFFF")),
                Intensity = (float)entry.Number("intensity", 1),
                Range = (float)entry.Number("range", 15),
                SpotAngle = (float)entry.Number("spotAngle", 45),
                SpotBlend = (float)entry.Number("spotBlend", 0.15),
                Visible = entry.Bool("visible", true),
                Parent = entry.Int("parent") ?? 0,
            };

            float[] position = entry.Floats("position");
            if (position != null && position.Length == 3)
            {
                light.Position = new Vector3(position[0], position[1], position[2]);
            }

            float[] rotation = entry.Floats("rotation");
            if (rotation != null && rotation.Length == 4)
            {
                light.Rotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]).normalized;
            }

            return light;
        }

        private static void ReadChunks(ZipArchive zip, List<object> chunks, Func<Vector3Int, string> name, string faces, VoxelGrid grid)
        {
            foreach (object item in chunks)
            {
                if (!(item is List<object> triple) || triple.Count != 3)
                {
                    continue;
                }

                var coord = new Vector3Int((int)(double)triple[0], (int)(double)triple[1], (int)(double)triple[2]);
                ZipArchiveEntry entry = zip.GetEntry(name(coord)) ?? throw new InvalidDataException($"The manifest lists {name(coord)}, but the file does not contain it.");
                byte[] cells = Decode(ReadAll(entry), VoxelGrid.Size * VoxelGrid.Size * VoxelGrid.Size);
                grid.Chunks[coord] = cells;

                if (faces != null && zip.GetEntry($"{faces}{coord.x}_{coord.y}_{coord.z}.bin") is ZipArchiveEntry painted)
                {
                    byte[] records = ReadAll(painted);
                    var map = new Dictionary<int, byte>();
                    for (int offset = 0; offset + 5 <= records.Length; offset += 5)
                    {
                        map[BitConverter.ToInt32(records, offset)] = records[offset + 4];
                    }

                    grid.Faces[coord] = map;
                }
            }
        }

        /// <summary>Runs of [ushort count][byte value].</summary>
        private static byte[] Decode(byte[] encoded, int length)
        {
            var cells = new byte[length];
            int written = 0;
            for (int offset = 0; offset + 3 <= encoded.Length; offset += 3)
            {
                int run = encoded[offset] | (encoded[offset + 1] << 8);
                byte value = encoded[offset + 2];
                if (run == 0 || written + run > length)
                {
                    throw new InvalidDataException("A chunk's run-length data is corrupt.");
                }

                for (int i = 0; i < run; i++)
                {
                    cells[written + i] = value;
                }

                written += run;
            }

            if (written != length)
            {
                throw new InvalidDataException("A chunk's run-length data does not fill it.");
            }

            return cells;
        }

        private static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using (Stream source = entry.Open())
            using (var buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        private static Color32 Colour(string hex)
        {
            string digits = hex.TrimStart('#');
            byte Part(int at) => byte.Parse(digits.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return digits.Length >= 6
                ? new Color32(Part(0), Part(2), Part(4), digits.Length >= 8 ? Part(6) : (byte)255)
                : new Color32(255, 255, 255, 255);
        }
    }

    /// <summary>
    /// What an object shows with its modifiers: its own voxels, then each copy a mirror or an array
    /// makes of them — a cell the colour of the first of its images to reach it, the original first.
    /// </summary>
    internal static class ModifierStack
    {
        private struct Map
        {
            public Vector3Int Origin, X, Y, Z;

            public Vector3Int Cell(Vector3Int c) => Origin + (X * c.x) + (Y * c.y) + (Z * c.z);

            public int Face(int face)
            {
                Vector3Int offset = Offsets[face];
                Vector3Int turned = (X * offset.x) + (Y * offset.y) + (Z * offset.z);
                for (int f = 0; f < 6; f++)
                {
                    if (Offsets[f] == turned)
                    {
                        return f;
                    }
                }

                return face;
            }
        }

        /// <summary>Blockage's faces in order: +X, −X, +Y, −Y, +Z, −Z.</summary>
        public static readonly Vector3Int[] Offsets =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        private static readonly Map Identity = new Map { Origin = Vector3Int.zero, X = new Vector3Int(1, 0, 0), Y = new Vector3Int(0, 1, 0), Z = new Vector3Int(0, 0, 1) };

        public static VoxelGrid Apply(VoxelGrid source, List<Modifier> modifiers)
        {
            var maps = new List<Map> { Identity };
            foreach (Modifier modifier in modifiers)
            {
                var next = new List<Map>(maps);
                if (modifier.Mirror)
                {
                    Map mirror = Identity;
                    int across = (2 * modifier.Plane) - 1;
                    switch (modifier.Axis)
                    {
                        case 0: mirror.Origin = new Vector3Int(across, 0, 0); mirror.X = new Vector3Int(-1, 0, 0); break;
                        case 1: mirror.Origin = new Vector3Int(0, across, 0); mirror.Y = new Vector3Int(0, -1, 0); break;
                        default: mirror.Origin = new Vector3Int(0, 0, across); mirror.Z = new Vector3Int(0, 0, -1); break;
                    }

                    foreach (Map map in maps)
                    {
                        next.Add(Compose(mirror, map));
                    }
                }
                else
                {
                    for (int k = 1; k < modifier.Count; k++)
                    {
                        Map shift = Identity;
                        int along = k * modifier.Step;
                        shift.Origin = modifier.Axis == 0 ? new Vector3Int(along, 0, 0) : modifier.Axis == 1 ? new Vector3Int(0, along, 0) : new Vector3Int(0, 0, along);
                        foreach (Map map in maps)
                        {
                            next.Add(Compose(shift, map));
                        }
                    }
                }

                if (next.Count > 512)
                {
                    break;
                }

                maps = next;
            }

            if (maps.Count == 1)
            {
                return source;
            }

            var shown = new VoxelGrid();
            foreach (Map map in maps)
            {
                foreach (KeyValuePair<Vector3Int, byte[]> chunk in source.Chunks)
                {
                    source.Faces.TryGetValue(chunk.Key, out Dictionary<int, byte> painted);
                    for (int index = 0; index < chunk.Value.Length; index++)
                    {
                        byte colour = chunk.Value[index];
                        if (colour == 0)
                        {
                            continue;
                        }

                        Vector3Int cell = VoxelGrid.CellOf(chunk.Key, index);
                        Vector3Int to = map.Cell(cell);
                        if (shown.Get(to.x, to.y, to.z) != 0)
                        {
                            continue;
                        }

                        shown.Set(to.x, to.y, to.z, colour);
                        if (painted == null)
                        {
                            continue;
                        }

                        for (int face = 0; face < 6; face++)
                        {
                            if (painted.TryGetValue((index * 6) + face, out byte paint))
                            {
                                shown.Paint(to.x, to.y, to.z, map.Face(face), paint);
                            }
                        }
                    }
                }
            }

            return shown;
        }

        /// <summary>First <paramref name="inner"/>, then <paramref name="outer"/>.</summary>
        private static Map Compose(Map outer, Map inner) => new Map
        {
            Origin = outer.Cell(inner.Origin),
            X = outer.Cell(inner.X) - outer.Origin,
            Y = outer.Cell(inner.Y) - outer.Origin,
            Z = outer.Cell(inner.Z) - outer.Origin,
        };
    }
}
