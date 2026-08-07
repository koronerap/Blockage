using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>
/// Reads and writes <c>.vxlevel</c> project files (EditorApp.md §7). An exported mesh is one-way —
/// it cannot be turned back into voxels — so the editor needs a format of its own.
///
/// <code>
/// .vxlevel  (zip container)
/// +-- manifest.json     version, name, chunk size, palette, bounds, chunk list
/// +-- chunks/
///     +-- 0_0_0.bin     32768 palette indices, run-length coded
/// </code>
/// </summary>
public static class VxLevelFile
{
    public const int CurrentVersion = 1;

    public const string Extension = ".vxlevel";

    private const string ManifestEntry = "manifest.json";
    private const string ChunkPrefix = "chunks/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes the world. The file is built beside the target and moved into place, so a failure
    /// part way through cannot destroy the previous save.
    /// </summary>
    public static void Save(VoxelWorld world, string path, string? name = null)
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
                WriteArchive(world, archive, name ?? Path.GetFileNameWithoutExtension(path));
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void WriteArchive(VoxelWorld world, ZipArchive archive, string name)
    {
        var coordinates = new List<ChunkCoord>();
        foreach ((ChunkCoord coord, Chunk chunk) in world.Chunks)
        {
            if (!chunk.IsEmpty)
            {
                coordinates.Add(coord);
            }
        }

        // Stable order keeps two saves of the same level byte-comparable.
        coordinates.Sort(static (a, b) =>
        {
            int compare = a.X.CompareTo(b.X);
            if (compare != 0) return compare;
            compare = a.Y.CompareTo(b.Y);
            return compare != 0 ? compare : a.Z.CompareTo(b.Z);
        });

        var manifest = new LevelManifest
        {
            Version = CurrentVersion,
            Name = name,
            ChunkSize = Chunk.Size,
            Palette = LevelManifest.EncodePalette(world.Palette),
            Chunks = [.. coordinates.Select(c => new[] { c.X, c.Y, c.Z })],
            SavedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        if (world.TryGetBounds(out Int3 min, out Int3 max))
        {
            manifest.BoundsMin = [min.X, min.Y, min.Z];
            manifest.BoundsMax = [max.X, max.Y, max.Z];
        }

        ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        using (Stream manifestStream = manifestEntry.Open())
        {
            JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
        }

        foreach (ChunkCoord coord in coordinates)
        {
            Chunk chunk = world.Chunks[coord];
            byte[] encoded = Rle.Encode(chunk.Indices);

            ZipArchiveEntry entry = archive.CreateEntry(ChunkEntryName(coord), CompressionLevel.Optimal);
            using Stream chunkStream = entry.Open();
            chunkStream.Write(encoded, 0, encoded.Length);
        }
    }

    public static VoxelWorld Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }

    public static VoxelWorld Load(Stream stream)
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

        var world = new VoxelWorld();
        world.ReplacePalette(LevelManifest.DecodePalette(manifest.Palette));

        var indices = new byte[Chunk.VoxelCount];
        foreach (int[] triple in manifest.Chunks)
        {
            if (triple.Length != 3)
            {
                throw new VxLevelFormatException("A chunk coordinate in the manifest is not an [x, y, z] triple.");
            }

            var coord = new ChunkCoord(triple[0], triple[1], triple[2]);
            string entryName = ChunkEntryName(coord);

            ZipArchiveEntry entry = archive.GetEntry(entryName)
                ?? throw new VxLevelFormatException($"Manifest lists {entryName}, but the file does not contain it.");

            byte[] encoded = ReadAll(entry);
            Rle.Decode(encoded, indices);

            world.GetOrCreateChunk(coord).LoadIndices(indices);
        }

        world.MarkAllDirty();
        return world;
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

    private static string ChunkEntryName(ChunkCoord coord) =>
        $"{ChunkPrefix}{coord.X}_{coord.Y}_{coord.Z}.bin";

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
