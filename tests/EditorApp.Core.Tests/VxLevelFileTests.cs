using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EditorApp.Core.Project;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class RleTests
{
    [Fact]
    public void UniformChunkCollapsesToASingleRun()
    {
        var data = new byte[Chunk.VoxelCount];
        Array.Fill(data, (byte)7);

        byte[] encoded = Rle.Encode(data);

        Assert.Equal(3, encoded.Length);
        Assert.Equal(data, Rle.Decode(encoded, data.Length));
    }

    [Fact]
    public void EmptyChunkCollapsesToASingleRun()
    {
        var data = new byte[Chunk.VoxelCount];
        Assert.Equal(3, Rle.Encode(data).Length);
    }

    [Fact]
    public void RandomDataRoundTrips()
    {
        var random = new Random(1234);
        var data = new byte[Chunk.VoxelCount];

        // Runs of random length and value: the shape real chunk data actually has.
        int index = 0;
        while (index < data.Length)
        {
            int run = Math.Min(random.Next(1, 400), data.Length - index);
            byte value = (byte)random.Next(0, 256);
            data.AsSpan(index, run).Fill(value);
            index += run;
        }

        byte[] encoded = Rle.Encode(data);
        Assert.Equal(data, Rle.Decode(encoded, data.Length));
    }

    [Fact]
    public void AlternatingDataStillRoundTrips()
    {
        var data = new byte[Chunk.VoxelCount];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 1);
        }

        Assert.Equal(data, Rle.Decode(Rle.Encode(data), data.Length));
    }

    [Fact]
    public void TruncatedStreamIsRejected()
    {
        var data = new byte[Chunk.VoxelCount];
        Array.Fill(data, (byte)3);
        byte[] encoded = Rle.Encode(data);

        Assert.Throws<VxLevelFormatException>(() => Rle.Decode(encoded.AsSpan(0, 0), data.Length));
    }

    [Fact]
    public void OverlongStreamIsRejected()
    {
        byte[] encoded = [0x10, 0x00, 5, 0x10, 0x00, 6];   // two runs of 16
        Assert.Throws<VxLevelFormatException>(() => Rle.Decode(encoded, 16));
    }
}

public class VxLevelFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-tests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_directory, name + VxLevelFile.Extension);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static VoxelWorld BuildWorld()
    {
        var world = new VoxelWorld();

        // Spans several chunks, including negative coordinates.
        for (int x = -40; x < 40; x += 3)
        {
            for (int z = -5; z < 5; z++)
            {
                world.SetVoxel(x, Math.Abs(x) % 7, z, (byte)(1 + ((x + 64) % 200)));
            }
        }

        world.Palette[1] = new Color32(10, 20, 30);
        world.Palette[255] = new Color32(1, 2, 3, 200);
        return world;
    }

    [Fact]
    public void RoundTripPreservesVoxelsAndPalette()
    {
        VoxelWorld original = BuildWorld();
        string path = PathFor("roundtrip");

        VxLevelFile.Save(original, path);
        VoxelWorld loaded = VxLevelFile.Load(path);

        Assert.Equal(original.ContentHash(), loaded.ContentHash());
        Assert.Equal(original.SolidCount, loaded.SolidCount);
        Assert.Equal(original.Chunks.Count, loaded.Chunks.Count);

        for (int i = 0; i < Palette.Size; i++)
        {
            Assert.Equal(original.Palette[i], loaded.Palette[i]);
        }

        Assert.True(original.TryGetBounds(out Int3 min, out Int3 max));
        Assert.True(loaded.TryGetBounds(out Int3 loadedMin, out Int3 loadedMax));
        Assert.Equal(min, loadedMin);
        Assert.Equal(max, loadedMax);
    }

    [Fact]
    public void SavingTwiceProducesIdenticalChunkData()
    {
        VoxelWorld world = BuildWorld();
        string first = PathFor("stable-a");
        string second = PathFor("stable-b");

        VxLevelFile.Save(world, first, name: "stable");
        VxLevelFile.Save(world, second, name: "stable");

        Assert.Equal(ChunkEntries(first), ChunkEntries(second));
    }

    private static Dictionary<string, string> ChunkEntries(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = new Dictionary<string, string>();
        foreach (ZipArchiveEntry entry in archive.Entries.Where(e => e.FullName.StartsWith("chunks/", StringComparison.Ordinal)))
        {
            using Stream content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            entries[entry.FullName] = Convert.ToBase64String(buffer.ToArray());
        }

        return entries;
    }

    [Fact]
    public void EmptyLevelRoundTrips()
    {
        var world = new VoxelWorld();
        string path = PathFor("empty");

        VxLevelFile.Save(world, path);
        VoxelWorld loaded = VxLevelFile.Load(path);

        Assert.Equal(0, loaded.SolidCount);
        Assert.False(loaded.TryGetBounds(out _, out _));
    }

    [Fact]
    public void ManifestIsReadableJsonWithAVersion()
    {
        string path = PathFor("manifest");
        VxLevelFile.Save(BuildWorld(), path, name: "My Level");

        LevelManifest manifest = VxLevelFile.ReadManifest(path);

        Assert.Equal(VxLevelFile.CurrentVersion, manifest.Version);
        Assert.Equal("My Level", manifest.Name);
        Assert.Equal(Chunk.Size, manifest.ChunkSize);
        Assert.Equal(Palette.Size, manifest.Palette.Length);
        Assert.NotEmpty(manifest.Chunks);
        Assert.NotNull(manifest.BoundsMin);
    }

    [Fact]
    public void AFutureVersionIsRejectedWithAClearMessage()
    {
        string path = PathFor("future");
        VxLevelFile.Save(BuildWorld(), path);
        RewriteManifest(path, manifest => manifest.Version = VxLevelFile.CurrentVersion + 1);

        VxLevelFormatException error = Assert.Throws<VxLevelFormatException>(() => VxLevelFile.Load(path));
        Assert.Contains("newer version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMismatchedChunkSizeIsRejected()
    {
        string path = PathFor("chunksize");
        VxLevelFile.Save(BuildWorld(), path);
        RewriteManifest(path, manifest => manifest.ChunkSize = 16);

        Assert.Throws<VxLevelFormatException>(() => VxLevelFile.Load(path));
    }

    [Fact]
    public void AMissingChunkEntryIsReported()
    {
        string path = PathFor("missing-chunk");
        VxLevelFile.Save(BuildWorld(), path);

        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.Entries.First(e => e.FullName.StartsWith("chunks/", StringComparison.Ordinal)).Delete();
        }

        VxLevelFormatException error = Assert.Throws<VxLevelFormatException>(() => VxLevelFile.Load(path));
        Assert.Contains("does not contain", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANonLevelZipIsReported()
    {
        string path = PathFor("not-a-level");
        Directory.CreateDirectory(_directory);

        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using Stream entry = archive.CreateEntry("readme.txt").Open();
            entry.Write("hello"u8);
        }

        Assert.Throws<VxLevelFormatException>(() => VxLevelFile.Load(path));
    }

    [Fact]
    public void AFailedSaveLeavesThePreviousFileIntact()
    {
        string path = PathFor("overwrite");
        VoxelWorld first = BuildWorld();
        VxLevelFile.Save(first, path);
        long originalLength = new FileInfo(path).Length;

        var second = new VoxelWorld();
        second.SetVoxel(0, 0, 0, 1);
        VxLevelFile.Save(second, path);

        Assert.NotEqual(originalLength, new FileInfo(path).Length);
        Assert.Equal(1, VxLevelFile.Load(path).SolidCount);
        Assert.False(File.Exists(path + ".saving"));
    }

    private static void RewriteManifest(string path, Action<LevelManifest> mutate)
    {
        LevelManifest manifest = VxLevelFile.ReadManifest(path);
        mutate(manifest);

        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update);

        archive.GetEntry("manifest.json")!.Delete();
        using Stream entry = archive.CreateEntry("manifest.json").Open();
        entry.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
    }
}
