using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>
/// Reads and writes <c>.vxlevel</c> project files (EditorApp.md, "Proje dosyası formatı"). An
/// exported mesh is one-way — it cannot be turned back into voxels — so the editor needs a format
/// of its own.
///
/// <code>
/// .vxlevel  (zip container)
/// +-- manifest.json          version, name, chunk size, palette, objects
/// +-- objects/1/chunks/
///     +-- 0_0_0.bin          32768 palette indices, run-length coded
/// </code>
///
/// Version 2 introduced objects. Version 1 files hold a single grid at <c>chunks/</c> and still
/// load — being able to open old files is exactly what the version field was written for.
/// </summary>
public static class VxLevelFile
{
    /// <summary>
    /// 1: a single grid. 2: objects with transforms. 3: per-face colours. 4: voxel size.
    /// 5: hidden objects. 6: voxel size per object, positions in world units; lights and ambient.
    ///
    /// Version 4 is a bump for a field an older build would simply not see. That is exactly why it
    /// is one: it sets the scale of everything exported from the file, so a build that ignored it
    /// would write a correct-looking mesh at the wrong size rather than fail. Refusing to open the
    /// file says so out loud. Version 5 is the same case: hidden objects are left out of an export,
    /// and a build that did not know about hiding would quietly put them back in. Version 6 changes
    /// what a position means, which an older build would read as a level flown apart.
    /// </summary>
    public const int CurrentVersion = 6;

    /// <summary>The first version with a voxel size on every object and positions in world units.</summary>
    private const int PerObjectVoxelSizeVersion = 6;

    public const string Extension = ".vxlevel";

    private const string ManifestEntry = "manifest.json";
    private const string LegacyChunkPrefix = "chunks/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes the level. The file is built beside the target and moved into place, so a failure
    /// part way through cannot destroy the previous save.
    /// </summary>
    public static void Save(VoxelScene scene, string path, string? name = null)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = path + ".saving";

        try
        {
            using (FileStream stream = File.Create(temporaryPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteArchive(scene, archive, name ?? Path.GetFileNameWithoutExtension(path));
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Writes the level into an already-open stream, leaving it open.
    ///
    /// The counterpart to <see cref="LoadScene(Stream)"/>, and the only way to save on a platform
    /// that hands out a stream for the document the user picked rather than a path — Android's
    /// storage has no path a process may simply write to.
    ///
    /// The path overload's safety net does not apply here: it builds beside the target and moves
    /// into place, and a stream cannot be moved. Whoever opened it decides what a failure means.
    /// </summary>
    public static void Save(VoxelScene scene, Stream stream, string name)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteArchive(scene, archive, name);
    }

    /// <summary>Convenience for a single-grid level.</summary>
    public static void Save(VoxelWorld world, string path, string? name = null)
    {
        var scene = new VoxelScene();
        scene.ReplacePalette(world.Palette);
        scene.Add(world, ObjectTransform.Identity, "Object 1");
        Save(scene, path, name);
    }

    private static void WriteArchive(VoxelScene scene, ZipArchive archive, string name)
    {
        var manifest = new LevelManifest
        {
            Version = CurrentVersion,
            Name = name,
            ChunkSize = Chunk.Size,
            Palette = LevelManifest.EncodePalette(scene.Palette),
            SavedCustomSlots = [.. scene.Palette.SavedCustomSlots()],
            Lights = [.. scene.Lights.Select(light => WriteLight(scene, light))],
            Ambient = scene.Ambient,
            Active = scene.Focus?.Id,
            Render = WriteRender(scene.RenderSettings),
            Materials = scene.Palette.Materials().Any()
                ? [.. scene.Palette.Materials().Select(m => new LevelManifest.MaterialEntry
                {
                    Index = m.Index,
                    Emission = m.Material.Emission,
                    Metallic = m.Material.Metallic,
                    Roughness = m.Material.Roughness,
                    Opacity = m.Material.Opacity,
                })]
                : null,
            SavedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        var entries = new List<LevelManifest.ObjectEntry>();

        foreach (VoxelObject o in scene.Objects)
        {
            List<ChunkCoord> coordinates = SortedCoordinates(o.Grid);

            entries.Add(new LevelManifest.ObjectEntry
            {
                Id = o.Id,
                Name = o.Name,
                Position = [o.Transform.Position.X, o.Transform.Position.Y, o.Transform.Position.Z],
                Rotation =
                [
                    o.Transform.Rotation.X,
                    o.Transform.Rotation.Y,
                    o.Transform.Rotation.Z,
                    o.Transform.Rotation.W,
                ],
                Chunks = [.. coordinates.Select(c => new[] { c.X, c.Y, c.Z })],
                Visible = o.Visible,
                VoxelSize = o.VoxelSize,
                Locked = o.Locked,
                Parent = scene.ParentOf(o)?.Id,
                Selected = scene.IsSelected(o.Id),
                Modifiers = o.Modifiers.Count == 0 ? null : [.. o.Modifiers.Select(m => new LevelManifest.ModifierEntry
                {
                    Kind = m.Kind.ToString().ToLowerInvariant(),
                    Axis = m.Axis.ToString().ToLowerInvariant(),
                    Plane = m.Plane,
                    Count = m.Count,
                    Step = m.Step,
                    Enabled = m.Enabled,
                })],
            });

            foreach (ChunkCoord coord in coordinates)
            {
                Chunk chunk = o.Grid.Chunks[coord];

                ZipArchiveEntry entry = archive.CreateEntry(ChunkEntryName(o.Id, coord), CompressionLevel.Optimal);
                using (Stream chunkStream = entry.Open())
                {
                    byte[] encoded = Rle.Encode(chunk.Indices);
                    chunkStream.Write(encoded, 0, encoded.Length);
                }

                WriteFaceOverrides(archive, o.Id, coord, chunk);
            }
        }

        manifest.Objects = [.. entries];

        if (scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            manifest.BoundsMin = [(int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Floor(min.Z)];
            manifest.BoundsMax = [(int)MathF.Ceiling(max.X), (int)MathF.Ceiling(max.Y), (int)MathF.Ceiling(max.Z)];
        }

        ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        using Stream manifestStream = manifestEntry.Open();
        JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
    }

    /// <summary>
    /// Painted faces go in their own entry, written only when a chunk has any. Keeping them out of
    /// the index blob means the run-length stream stays exactly what version 1 and 2 wrote, and a
    /// chunk with no painted faces costs nothing at all.
    ///
    /// Each record is five bytes: the voxel's linear index, the face, the palette index.
    /// </summary>
    private static void WriteFaceOverrides(ZipArchive archive, int objectId, ChunkCoord coord, Chunk chunk)
    {
        if (chunk.FaceOverrideCount == 0)
        {
            return;
        }

        var records = new List<(int Linear, Face Face, byte PaletteIndex)>(chunk.FaceOverrides());

        // Stable order, so saving the same level twice produces identical bytes.
        records.Sort(static (a, b) =>
        {
            int compare = a.Linear.CompareTo(b.Linear);
            return compare != 0 ? compare : ((int)a.Face).CompareTo((int)b.Face);
        });

        ZipArchiveEntry entry = archive.CreateEntry(FaceEntryName(objectId, coord), CompressionLevel.Optimal);
        using Stream stream = entry.Open();

        Span<byte> record = stackalloc byte[5];
        foreach ((int linear, Face face, byte paletteIndex) in records)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record[..4], (linear * FaceInfo.Count) + (int)face);
            record[4] = paletteIndex;
            stream.Write(record);
        }
    }

    private static void ReadFaceOverrides(ZipArchive archive, int objectId, ChunkCoord coord, Chunk chunk)
    {
        ZipArchiveEntry? entry = archive.GetEntry(FaceEntryName(objectId, coord));
        if (entry is null)
        {
            return;
        }

        byte[] data = ReadAll(entry);
        if (data.Length % 5 != 0)
        {
            throw new VxLevelFormatException("Corrupt face colour data: the record length does not divide evenly.");
        }

        for (int offset = 0; offset < data.Length; offset += 5)
        {
            int key = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
            chunk.LoadFaceOverride(key / FaceInfo.Count, (Face)(key % FaceInfo.Count), data[offset + 4]);
        }
    }

    /// <summary>Stable order keeps two saves of the same level byte-comparable.</summary>
    private static List<ChunkCoord> SortedCoordinates(VoxelWorld grid)
    {
        var coordinates = new List<ChunkCoord>();
        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (!chunk.IsEmpty)
            {
                coordinates.Add(coord);
            }
        }

        coordinates.Sort(static (a, b) =>
        {
            int compare = a.X.CompareTo(b.X);
            if (compare != 0) return compare;
            compare = a.Y.CompareTo(b.Y);
            return compare != 0 ? compare : a.Z.CompareTo(b.Z);
        });

        return coordinates;
    }

    public static VoxelScene LoadScene(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return LoadScene(stream);
    }

    public static VoxelScene LoadScene(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntry)
            ?? throw new VxLevelFormatException("Not a .vxlevel file: manifest.json is missing.");

        LevelManifest manifest;
        using (Stream manifestStream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<LevelManifest>(manifestStream, JsonOptions)
                ?? throw new VxLevelFormatException("manifest.json is empty.");
        }

        if (manifest.Version > CurrentVersion)
        {
            throw new VxLevelFormatException(
                $"This file was written by a newer version of EditorApp (file version {manifest.Version}, "
                + $"this build reads up to {CurrentVersion}).");
        }

        if (manifest.ChunkSize != Chunk.Size)
        {
            throw new VxLevelFormatException(
                $"Chunk size {manifest.ChunkSize} does not match this build's {Chunk.Size}.");
        }

        var scene = new VoxelScene();
        scene.ReplacePalette(LevelManifest.DecodePalette(manifest.Palette, manifest.SavedCustomSlots));
        foreach (LevelManifest.MaterialEntry material in manifest.Materials ?? [])
        {
            if (material.Index > Palette.EmptyIndex && material.Index < Palette.Size)
            {
                scene.Palette.SetMaterial(material.Index, VoxelMaterial.Of(material.Emission, material.Metallic, material.Roughness, material.Opacity));
            }
        }

        // Before version 6 the whole level had one voxel size — absent before version 4, where one
        // voxel was always one unit — and positions were counted in those voxels. Both move onto
        // each object: the size as its own, the position multiplied out into world units.
        float levelVoxelSize = ObjectTransform.ValidVoxelSize(manifest.VoxelSize ?? 1f) ?? 1f;
        bool perObject = manifest.Version >= PerObjectVoxelSizeVersion;

        // Ids are the file's own and the scene hands out new ones, so parents are matched up once
        // everything is in: a child may well be listed before its parent.
        var byFileId = new Dictionary<int, VoxelObject>();
        var parented = new List<(IPlaceable Child, int ParentFileId)>();

        // An empty list is a level with nothing in it; only a missing one is version 1's single grid.
        if (manifest.Objects is { } objects)
        {
            foreach (LevelManifest.ObjectEntry entry in objects)
            {
                ObjectTransform placed = ReadTransform(entry);
                placed = perObject
                    ? placed with { VoxelSize = ObjectTransform.ValidVoxelSize(entry.VoxelSize ?? 1f) ?? 1f }
                    : placed with { Position = placed.Position * levelVoxelSize, VoxelSize = levelVoxelSize };

                VoxelObject added = scene.Add(
                    ReadGrid(archive, entry.Chunks, coord => ChunkEntryName(entry.Id, coord), entry.Id),
                    placed,
                    entry.Name);

                added.Visible = entry.Visible;
                added.Locked = entry.Locked;
                if (entry.Modifiers is { Length: > 0 } modifiers)
                {
                    added.SetModifiers(modifiers.Select(ReadModifier));
                }

                if (entry.Selected)
                {
                    scene.Select(added.Id);
                }

                byFileId.TryAdd(entry.Id, added);
                if (entry.Parent is { } parent)
                {
                    parented.Add((added, parent));
                }
            }
        }
        else
        {
            // Version 1: one grid at the root, sitting at the origin.
            scene.Add(
                ReadGrid(archive, manifest.Chunks, coord => LegacyChunkPrefix + $"{coord.X}_{coord.Y}_{coord.Z}.bin"),
                ObjectTransform.Identity with { VoxelSize = levelVoxelSize },
                "Object 1");
        }

        // After the objects, so a sun added for an older file can be placed clear of them.
        if (manifest.Lights is { } lights)
        {
            foreach (LevelManifest.LightEntry entry in lights)
            {
                SceneLight light = ReadLight(scene, entry);
                if (entry.Selected)
                {
                    scene.Select(light.Id);
                }

                if (entry.Parent is { } parent)
                {
                    parented.Add((light, parent));
                }
            }
        }
        else
        {
            scene.AddDefaultSun();
        }

        // A parent that is not there, or that would close a loop, is simply no parent.
        foreach ((IPlaceable child, int parentFileId) in parented)
        {
            if (byFileId.TryGetValue(parentFileId, out VoxelObject? parent))
            {
                scene.SetParent(child.Id, parent.Id);
            }
        }

        scene.Ambient = manifest.Ambient ?? VoxelScene.DefaultAmbient;
        if (manifest.Render is { } render)
        {
            scene.RenderSettings = ReadRender(render);
        }

        if (manifest.Active is { } active && byFileId.TryGetValue(active, out VoxelObject? focused))
        {
            scene.SetFocus(focused.Id);
        }

        scene.MarkAllDirty();
        return scene;
    }

    private static LevelManifest.LightEntry WriteLight(VoxelScene scene, SceneLight light)
    {
        Vector3 colour = light.Colour * 255f;

        return new LevelManifest.LightEntry
        {
            Name = light.Name,
            Kind = light.Kind.ToString().ToLowerInvariant(),
            Position = [light.Position.X, light.Position.Y, light.Position.Z],
            Rotation =
            [
                light.Transform.Rotation.X,
                light.Transform.Rotation.Y,
                light.Transform.Rotation.Z,
                light.Transform.Rotation.W,
            ],
            Colour = $"#{(int)MathF.Round(colour.X):X2}{(int)MathF.Round(colour.Y):X2}{(int)MathF.Round(colour.Z):X2}",
            Intensity = light.Intensity,
            Range = light.Range,
            SpotAngle = light.SpotAngle,
            SpotBlend = light.SpotBlend,
            Visible = light.Visible,
            Locked = light.Locked,
            Parent = scene.ParentOf(light)?.Id,
            Selected = scene.IsSelected(light.Id),
        };
    }

    private static LevelManifest.RenderEntry WriteRender(Rendering.RenderSettings settings) => new()
    {
        Engine = settings.Engine == Rendering.RenderEngine.Gpu ? "gpu" : null,
        Width = settings.Width,
        Height = settings.Height,
        Samples = settings.Samples,
        Bounces = settings.Bounces,
        Seed = settings.Seed,
        SkyStrength = settings.SkyStrength,
        SkyTop = [settings.SkyTop.X, settings.SkyTop.Y, settings.SkyTop.Z],
        SkyHorizon = [settings.SkyHorizon.X, settings.SkyHorizon.Y, settings.SkyHorizon.Z],
        TransparentBackground = settings.TransparentBackground,
        Exposure = settings.Exposure,
        EmissionStrength = settings.EmissionStrength,
        Fog = settings.Fog,
        Aperture = settings.Aperture,
        FocusDistance = settings.FocusDistance,
        Bloom = settings.Bloom,
        SunSize = settings.SunSize,
    };

    private static Rendering.RenderSettings ReadRender(LevelManifest.RenderEntry entry)
    {
        static Vector3 Colour(float[] rgb, Vector3 fallback) =>
            rgb.Length == 3 ? new Vector3(rgb[0], rgb[1], rgb[2]) : fallback;

        var defaults = new Rendering.RenderSettings();
        return new Rendering.RenderSettings
        {
            Engine = entry.Engine == "gpu" ? Rendering.RenderEngine.Gpu : Rendering.RenderEngine.Cpu,
            Width = entry.Width,
            Height = entry.Height,
            Samples = entry.Samples,
            Bounces = entry.Bounces,
            Seed = entry.Seed,
            SkyStrength = entry.SkyStrength,
            SkyTop = Colour(entry.SkyTop, defaults.SkyTop),
            SkyHorizon = Colour(entry.SkyHorizon, defaults.SkyHorizon),
            TransparentBackground = entry.TransparentBackground,
            Exposure = entry.Exposure,
            EmissionStrength = entry.EmissionStrength,
            Fog = entry.Fog,
            Aperture = entry.Aperture,
            FocusDistance = entry.FocusDistance,
            Bloom = entry.Bloom,
            SunSize = entry.SunSize,
        }.Clamped();
    }

    private static VoxelModifier ReadModifier(LevelManifest.ModifierEntry entry)
    {
        ModifierKind kind = entry.Kind.ToLowerInvariant() switch
        {
            "mirror" => ModifierKind.Mirror,
            "array" => ModifierKind.Array,
            _ => throw new VxLevelFormatException($"A modifier is of an unknown kind '{entry.Kind}'."),
        };

        Axis axis = entry.Axis.ToLowerInvariant() switch
        {
            "y" => Axis.Y,
            "z" => Axis.Z,
            _ => Axis.X,
        };

        return new VoxelModifier(kind, axis, entry.Plane, entry.Count, entry.Step, entry.Enabled);
    }

    private static SceneLight ReadLight(VoxelScene scene, LevelManifest.LightEntry entry)
    {
        LightKind kind = entry.Kind.ToLowerInvariant() switch
        {
            "directional" => LightKind.Directional,
            "spot" => LightKind.Spot,
            "point" => LightKind.Point,
            _ => throw new VxLevelFormatException($"Light '{entry.Name}' is of an unknown kind '{entry.Kind}'."),
        };

        if (entry.Position.Length != 3 || entry.Rotation.Length != 4)
        {
            throw new VxLevelFormatException($"Light '{entry.Name}' has a malformed placement.");
        }

        var rotation = new Quaternion(entry.Rotation[0], entry.Rotation[1], entry.Rotation[2], entry.Rotation[3]);
        rotation = rotation.LengthSquared() < 1e-6f ? Quaternion.Identity : Quaternion.Normalize(rotation);

        SceneLight light = scene.AddLight(kind, entry.Name);
        light.Apply(light.State with
        {
            Transform = new ObjectTransform(new Vector3(entry.Position[0], entry.Position[1], entry.Position[2]), rotation),
            Colour = ParseLightColour(entry),
            Intensity = entry.Intensity,
            Range = entry.Range,
            SpotAngle = entry.SpotAngle,
            SpotBlend = entry.SpotBlend,
            Visible = entry.Visible,
        });

        light.Locked = entry.Locked;
        return light;
    }

    private static Vector3 ParseLightColour(LevelManifest.LightEntry entry)
    {
        ReadOnlySpan<char> digits = entry.Colour.AsSpan().TrimStart('#');
        if (digits.Length != 6
            || !int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            throw new VxLevelFormatException($"Light '{entry.Name}' has a colour that is not #RRGGBB: '{entry.Colour}'.");
        }

        return new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255f;
    }

    private static ObjectTransform ReadTransform(LevelManifest.ObjectEntry entry)
    {
        if (entry.Position.Length != 3 || entry.Rotation.Length != 4)
        {
            throw new VxLevelFormatException($"Object '{entry.Name}' has a malformed transform.");
        }

        var rotation = new Quaternion(entry.Rotation[0], entry.Rotation[1], entry.Rotation[2], entry.Rotation[3]);
        if (rotation.LengthSquared() < 1e-6f)
        {
            rotation = Quaternion.Identity;
        }

        return new ObjectTransform(
            new Vector3(entry.Position[0], entry.Position[1], entry.Position[2]),
            Quaternion.Normalize(rotation));
    }

    private static VoxelWorld ReadGrid(
        ZipArchive archive,
        int[][] chunks,
        Func<ChunkCoord, string> entryName,
        int? objectId = null)
    {
        var grid = new VoxelWorld();
        var indices = new byte[Chunk.VoxelCount];

        foreach (int[] triple in chunks)
        {
            if (triple.Length != 3)
            {
                throw new VxLevelFormatException("A chunk coordinate in the manifest is not an [x, y, z] triple.");
            }

            var coord = new ChunkCoord(triple[0], triple[1], triple[2]);
            string name = entryName(coord);

            ZipArchiveEntry entry = archive.GetEntry(name)
                ?? throw new VxLevelFormatException($"Manifest lists {name}, but the file does not contain it.");

            Rle.Decode(ReadAll(entry), indices);
            Chunk chunk = grid.GetOrCreateChunk(coord);
            chunk.LoadIndices(indices);

            // Absent for versions 1 and 2, and for any chunk whose faces are all its base colour.
            if (objectId is { } id)
            {
                ReadFaceOverrides(archive, id, coord, chunk);
            }
        }

        return grid;
    }

    /// <summary>Convenience for callers that only want a single grid, such as the benchmarks.</summary>
    public static VoxelWorld Load(string path)
    {
        VoxelScene scene = LoadScene(path);
        return scene.Objects.Count > 0 ? scene.Objects[0].Grid : new VoxelWorld();
    }

    /// <summary>Reads just the manifest — used to show details without loading every chunk.</summary>
    public static LevelManifest ReadManifest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        ZipArchiveEntry entry = archive.GetEntry(ManifestEntry)
            ?? throw new VxLevelFormatException("Not a .vxlevel file: manifest.json is missing.");

        using Stream manifestStream = entry.Open();
        return JsonSerializer.Deserialize<LevelManifest>(manifestStream, JsonOptions)
            ?? throw new VxLevelFormatException("manifest.json is empty.");
    }

    private static string ChunkEntryName(int objectId, ChunkCoord coord) =>
        $"objects/{objectId}/chunks/{coord.X}_{coord.Y}_{coord.Z}.bin";

    private static string FaceEntryName(int objectId, ChunkCoord coord) =>
        $"objects/{objectId}/faces/{coord.X}_{coord.Y}_{coord.Z}.bin";

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using Stream source = entry.Open();
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Leaving a stray .saving file behind is better than masking the original failure.
        }
    }
}
