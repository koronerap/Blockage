using System.IO.Compression;
using System.Numerics;
using System.Text;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Binary FBX (Fullreleaseplan 8.5), read back node by node: its framing, its models under their
/// parents, linked copies sharing a geometry, markers as empty models with their properties, and the
/// turns in FBX's own angles. Checked by hand against Blender 5.0's importer as well.
/// </summary>
public sealed class FbxExporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fbx-{Guid.NewGuid():N}");

    public FbxExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // ---- A reader, enough to check what was written -------------------------------------------------

    private sealed record Read(string Name, List<object> Values, List<Read> Children)
    {
        public Read Child(string name) => Children.Single(c => c.Name == name);

        public IEnumerable<Read> All(string name) => Children.Where(c => c.Name == name);

        /// <summary>A Properties70 entry's values after its name, type, label and flags.</summary>
        public object[]? Property(string name) =>
            Child("Properties70").All("P").FirstOrDefault(p => (string)p.Values[0] == name)?.Values.Skip(4).ToArray();
    }

    private static List<Read> Parse(byte[] file)
    {
        Assert.Equal("Kaydara FBX Binary  \0", Encoding.ASCII.GetString(file, 0, 21));
        Assert.Equal(7400, BitConverter.ToInt32(file, 23));
        int at = 27;
        return ReadList(file, ref at);
    }

    private static List<Read> ReadList(byte[] file, ref int at)
    {
        var nodes = new List<Read>();
        while (true)
        {
            uint end = BitConverter.ToUInt32(file, at);
            uint count = BitConverter.ToUInt32(file, at + 4);
            byte nameLength = file[at + 12];
            if (end == 0)
            {
                at += 13;
                return nodes;
            }

            string name = Encoding.ASCII.GetString(file, at + 13, nameLength);
            at += 13 + nameLength;
            var values = new List<object>();
            for (int i = 0; i < count; i++)
            {
                values.Add(ReadValue(file, ref at));
            }

            var children = at < end ? ReadList(file, ref at) : [];
            at = (int)end;
            nodes.Add(new Read(name, values, children));
        }
    }

    private static object ReadValue(byte[] file, ref int at)
    {
        char code = (char)file[at++];
        switch (code)
        {
            case 'C': return file[at++] != 0;
            case 'I': at += 4; return BitConverter.ToInt32(file, at - 4);
            case 'L': at += 8; return BitConverter.ToInt64(file, at - 8);
            case 'D': at += 8; return BitConverter.ToDouble(file, at - 8);
            case 'F': at += 4; return BitConverter.ToSingle(file, at - 4);
            case 'Y': at += 2; return BitConverter.ToInt16(file, at - 2);
            case 'S' or 'R':
            {
                int length = BitConverter.ToInt32(file, at);
                byte[] bytes = file[(at + 4)..(at + 4 + length)];
                at += 4 + length;
                return code == 'S' ? Encoding.UTF8.GetString(bytes) : bytes;
            }

            default:
            {
                int length = BitConverter.ToInt32(file, at);
                int encoding = BitConverter.ToInt32(file, at + 4);
                int size = BitConverter.ToInt32(file, at + 8);
                byte[] data = file[(at + 12)..(at + 12 + size)];
                at += 12 + size;
                if (encoding == 1)
                {
                    using var packed = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                    using var raw = new MemoryStream();
                    packed.CopyTo(raw);
                    data = raw.ToArray();
                }

                return code switch
                {
                    'd' => Enumerable.Range(0, length).Select(i => BitConverter.ToDouble(data, i * 8)).ToArray(),
                    'i' => Enumerable.Range(0, length).Select(i => BitConverter.ToInt32(data, i * 4)).ToArray(),
                    _ => data,
                };
            }
        }
    }

    // ---- The level ----------------------------------------------------------------------------------

    private static VoxelWorld Block()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        return grid;
    }

    private (List<Read> File, EditorSession Session) Export(Action<EditorSession> build)
    {
        var session = new EditorSession();
        session.ReplaceScene(new VoxelScene(), projectPath: null);
        build(session);
        string path = Path.Combine(_directory, "level.fbx");
        new FbxExporter().Export(GreedyMesher.BuildScene(session.Scene, instanceLinked: true, lights: false), session.Scene.Palette, path, new ExportOptions());
        return (Parse(File.ReadAllBytes(path)), session);
    }

    private static IEnumerable<Read> Models(List<Read> file) => file.Single(n => n.Name == "Objects").All("Model");

    private static Read Model(List<Read> file, string name) =>
        Models(file).Single(m => ((string)m.Values[1]).StartsWith(name + "\0\u0001", StringComparison.Ordinal));

    private static List<(long Child, long Parent)> Links(List<Read> file) =>
        [.. file.Single(n => n.Name == "Connections").All("C").Select(c => ((long)c.Values[1], (long)c.Values[2]))];

    [Fact]
    public void TheFileIsFramedAsFbxFramesIt()
    {
        (List<Read> file, _) = Export(session => session.Scene.Add(Block(), ObjectTransform.Identity, "Crate"));

        Assert.Equal(
            ["FBXHeaderExtension", "FileId", "CreationTime", "Creator", "GlobalSettings", "Documents", "References", "Definitions", "Objects", "Connections", "Takes"],
            file.Select(n => n.Name));
        object[] up = file.Single(n => n.Name == "GlobalSettings").Property("UpAxis")!;
        Assert.Equal(1, up[0]);
    }

    [Fact]
    public void AnObjectIsAModelWithItsMeshWhereItStands()
    {
        (List<Read> file, _) = Export(session => session.Scene.Add(Block(), ObjectTransform.Identity with { Position = new Vector3(4f, 1f, -2f) }, "Crate"));

        Read crate = Model(file, "Crate");
        Assert.Equal("Mesh", crate.Values[2]);
        Assert.Equal([4d, 1d, -2d], crate.Property("Lcl Translation")!);

        Read geometry = file.Single(n => n.Name == "Objects").All("Geometry").Single();
        int[] faces = (int[])geometry.Child("PolygonVertexIndex").Values[0];
        Assert.Equal(6 * 4, faces.Length);
        Assert.Equal(6, faces.Count(i => i < 0));
        Assert.Contains(((long)geometry.Values[0], (long)crate.Values[0]), Links(file));
    }

    [Fact]
    public void AChildHangsUnderItsParentPlacedRelativeToIt()
    {
        (List<Read> file, _) = Export(session =>
        {
            VoxelObject table = session.Scene.Add(Block(), ObjectTransform.Identity with { Position = new Vector3(5f, 0f, 0f) }, "Table");
            VoxelObject cup = session.Scene.Add(Block(), ObjectTransform.Identity with { Position = new Vector3(5f, 2f, 1f) }, "Cup");
            session.Scene.SetParent(cup.Id, table.Id);
        });

        Read table = Model(file, "Table");
        Read cup = Model(file, "Cup");
        Assert.Contains(((long)cup.Values[0], (long)table.Values[0]), Links(file));
        Assert.Contains(((long)table.Values[0], 0L), Links(file));
        Assert.Equal([0d, 2d, 1d], cup.Property("Lcl Translation")!);
    }

    [Fact]
    public void LinkedCopiesShareOneGeometry()
    {
        (List<Read> file, _) = Export(session =>
        {
            VoxelWorld grid = Block();
            session.Scene.Add(grid, ObjectTransform.Identity, "Crate");
            session.Scene.Add(grid, ObjectTransform.Identity with { Position = new Vector3(8f, 0f, 0f) }, "Crate copy");
        });

        Read geometry = file.Single(n => n.Name == "Objects").All("Geometry").Single();
        Assert.Equal(2, Links(file).Count(link => link.Child == (long)geometry.Values[0]));
    }

    [Fact]
    public void AMarkerIsAnEmptyModelWithItsPropertiesForTheGame()
    {
        (List<Read> file, _) = Export(session =>
        {
            session.Scene.Add(Block(), ObjectTransform.Identity, "Crate");
            VoxelObject spawn = session.AddMarker(MarkerKind.Spawn, new Vector3(1f, 0f, 3f));
            session.SetProperties(spawn.Id, [new CustomProperty("team", PropertyKind.Text, "red"), new CustomProperty("lives", PropertyKind.Number, "3")], "Add properties");
        });

        Read spawn = Model(file, "Spawn point");
        Assert.Equal("Null", spawn.Values[2]);
        Assert.Equal("red", spawn.Property("team")![0]);
        Assert.Equal(3d, spawn.Property("lives")![0]);
        Assert.Equal("spawn", spawn.Property("marker")![0]);
    }

    [Fact]
    public void ThePaletteIsEmbeddedAndWrittenBeside()
    {
        (List<Read> file, _) = Export(session => session.Scene.Add(Block(), ObjectTransform.Identity, "Crate"));

        Read video = file.Single(n => n.Name == "Objects").All("Video").Single();
        byte[] png = (byte[])video.Child("Content").Values[0];
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        Assert.True(File.Exists(Path.Combine(_directory, (string)video.Child("RelativeFilename").Values[0])));
    }

    // ---- Turns --------------------------------------------------------------------------------------

    /// <summary>The angles, turned about X, then Y, then Z, give back the turn they came from.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(1f, 0f, 0f, 90f)]
    [InlineData(0f, 1f, 0f, 30f)]
    [InlineData(1f, 2f, 3f, 70f)]
    [InlineData(-2f, 1f, 0.5f, 200f)]
    [InlineData(0f, 1f, 0f, 90f)]
    public void TheAnglesGiveBackTheTurn(float ax, float ay, float az, float degrees)
    {
        Vector3 axis = ax == 0f && ay == 0f && az == 0f ? Vector3.UnitY : Vector3.Normalize(new Vector3(ax, ay, az));
        Quaternion turn = Quaternion.CreateFromAxisAngle(axis, degrees * (MathF.PI / 180f));

        Vector3 angles = FbxExporter.EulerXyz(turn) * (MathF.PI / 180f);
        Quaternion back = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angles.Z)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, angles.Y)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angles.X);

        foreach (Vector3 probe in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Assert.True(Vector3.Distance(Vector3.Transform(probe, turn), Vector3.Transform(probe, back)) < 1e-3f);
        }
    }
}
