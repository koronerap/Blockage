using System.Globalization;
using System.Numerics;
using System.Text;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// MagicaVoxel's .vox (Fullreleaseplan 8.1): what goes out comes back where it was and as it was;
/// files built by hand, the way MagicaVoxel writes them, come in standing up, facing forward and
/// turned the way their scene says.
/// </summary>
public class VoxFileTests
{
    private const byte Red = 20;
    private const byte Blue = 90;

    /// <summary>Every voxel of every object as the world's cell it fills, and its colour.</summary>
    private static HashSet<(Int3 Cell, byte Colour)> World(VoxelScene scene)
    {
        var cells = new HashSet<(Int3, byte)>();
        foreach (VoxelObject o in scene.Objects.Where(o => !o.IsEmpty))
        {
            foreach ((ChunkCoord coord, Chunk chunk) in o.Grid.Chunks)
            {
                for (int i = 0; i < Chunk.VoxelCount; i++)
                {
                    Int3 local = Chunk.FromLinearIndex(i);
                    byte colour = chunk.Get(local.X, local.Y, local.Z);
                    if (colour == Palette.EmptyIndex)
                    {
                        continue;
                    }

                    var cell = new Vector3((coord.X * Chunk.Size) + local.X + 0.5f, (coord.Y * Chunk.Size) + local.Y + 0.5f, (coord.Z * Chunk.Size) + local.Z + 0.5f);
                    Vector3 world = o.Transform.TransformPoint(cell);
                    cells.Add((new Int3((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y), (int)MathF.Floor(world.Z)), colour));
                }
            }
        }

        return cells;
    }

    /// <summary>An L of voxels, a different colour at its foot: turned or mirrored, it would not match.</summary>
    private static VoxelWorld Ell()
    {
        var grid = new VoxelWorld();
        for (int y = 0; y < 4; y++)
        {
            grid.SetVoxel(0, y, 0, Red);
        }

        grid.SetVoxel(1, 0, 0, Blue);
        grid.SetVoxel(2, 0, 0, Blue);
        grid.SetVoxel(0, 0, 1, Blue);
        return grid;
    }

    private static VoxelScene RoundTrip(VoxelScene scene, out VoxReport report)
    {
        using var stream = new MemoryStream();
        report = VoxFile.Save(scene, stream);
        stream.Position = 0;
        return VoxFile.Load(stream);
    }

    // ---- Out and back -------------------------------------------------------------------------------

    [Fact]
    public void AnObjectComesBackWhereItWasAndAsItWas()
    {
        var scene = new VoxelScene();
        scene.Add(Ell(), ObjectTransform.Identity with { Position = new Vector3(5f, -2f, 7f) }, "Ell");

        VoxelScene back = RoundTrip(scene, out VoxReport report);

        Assert.Equal(World(scene), World(back));
        Assert.Equal("Ell", back.Objects.Single(o => !o.IsEmpty).Name);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void TheColoursAndTheirMaterialsComeBack()
    {
        var scene = new VoxelScene();
        scene.Add(Ell(), ObjectTransform.Identity, "Ell");
        scene.Palette[Red] = new Color32(200, 30, 40);
        scene.Palette.SetMaterial(Blue, VoxelMaterial.Of(0f, 1f, 0.25f, 1f));

        VoxelScene back = RoundTrip(scene, out _);

        Assert.Equal(new Color32(200, 30, 40), back.Palette[Red]);
        VoxelMaterial metal = back.Palette.Material(Blue);
        Assert.Equal(1f, metal.Metallic, 3);
        Assert.Equal(0.25f, metal.Roughness, 3);
        Assert.True(back.Palette.Material(Red).IsPlain);
    }

    /// <summary>Linked copies go out as one model placed twice, and come back linked.</summary>
    [Fact]
    public void LinkedCopiesTurnedByQuarterTurnsComeBackLinked()
    {
        var scene = new VoxelScene();
        VoxelWorld grid = Ell();
        scene.Add(grid, ObjectTransform.Identity, "Ell");
        scene.Add(grid, new ObjectTransform(new Vector3(10f, 0f, 3f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f), 1f), "Ell turned");
        scene.Add(grid, new ObjectTransform(new Vector3(-6f, 4f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), 1f), "Ell upside down");

        VoxelScene back = RoundTrip(scene, out VoxReport report);

        Assert.Equal(1, report.Models);
        Assert.Equal(3, report.Instances);
        Assert.Equal(World(scene), World(back));
        List<VoxelObject> copies = [.. back.Objects.Where(o => !o.IsEmpty)];
        Assert.Equal(3, copies.Count);
        Assert.All(copies, copy => Assert.Same(copies[0].Grid, copy.Grid));
    }

    [Fact]
    public void AnObjectOffTheLatticeIsBakedIntoPlaceAndSaysSo()
    {
        var scene = new VoxelScene();
        scene.Add(Ell(), new ObjectTransform(new Vector3(0.5f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f), 1f), "Leaning");

        VoxelScene back = RoundTrip(scene, out VoxReport report);

        Assert.Contains(report.Warnings, warning => warning.Contains("Leaning", StringComparison.Ordinal));
        Assert.NotEmpty(World(back));
        Assert.Equal(ObjectTransform.Identity.Rotation, back.Objects.Single(o => !o.IsEmpty).Transform.Rotation);
    }

    [Fact]
    public void AnObjectLargerThanAModelGoesOutInPieces()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 300; x++)
        {
            grid.SetVoxel(x, 0, 0, Red);
        }

        grid.SetVoxel(299, 1, 0, Blue);
        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "Wall");

        VoxelScene back = RoundTrip(scene, out VoxReport report);

        Assert.Equal(2, report.Models);
        Assert.Equal(World(scene), World(back));
    }

    [Fact]
    public void PaintedFacesAreSaidToBeLost()
    {
        var scene = new VoxelScene();
        VoxelWorld grid = Ell();
        grid.SetFaceColor(new Int3(0, 3, 0), Face.PosY, Blue);
        scene.Add(grid, ObjectTransform.Identity, "Ell");

        RoundTrip(scene, out VoxReport report);

        Assert.Contains(report.Warnings, warning => warning.Contains("Painted faces", StringComparison.Ordinal));
    }

    // ---- MagicaVoxel's own files --------------------------------------------------------------------

    /// <summary>A .vox put together chunk by chunk, as MagicaVoxel lays one out.</summary>
    private sealed class VoxBuilder
    {
        private readonly MemoryStream _children = new();

        public VoxBuilder Model(Int3 size, params (int X, int Y, int Z, byte Colour)[] voxels)
        {
            Chunk("SIZE", w =>
            {
                w.Write(size.X);
                w.Write(size.Y);
                w.Write(size.Z);
            });
            Chunk("XYZI", w =>
            {
                w.Write(voxels.Length);
                foreach ((int x, int y, int z, byte colour) in voxels)
                {
                    w.Write((byte)x);
                    w.Write((byte)y);
                    w.Write((byte)z);
                    w.Write(colour);
                }
            });
            return this;
        }

        public VoxBuilder Transform(int id, int child, int layer = -1, string? move = null, int? turn = null, string? name = null)
        {
            Chunk("nTRN", w =>
            {
                w.Write(id);
                Dictionary(w, name is null ? [] : [("_name", name)]);
                w.Write(child);
                w.Write(-1);
                w.Write(layer);
                w.Write(1);
                var frame = new List<(string, string)>();
                if (move is not null)
                {
                    frame.Add(("_t", move));
                }

                if (turn is { } packed)
                {
                    frame.Add(("_r", packed.ToString(CultureInfo.InvariantCulture)));
                }

                Dictionary(w, [.. frame]);
            });
            return this;
        }

        public VoxBuilder Group(int id, params int[] children)
        {
            Chunk("nGRP", w =>
            {
                w.Write(id);
                Dictionary(w, []);
                w.Write(children.Length);
                foreach (int child in children)
                {
                    w.Write(child);
                }
            });
            return this;
        }

        public VoxBuilder Shape(int id, int model)
        {
            Chunk("nSHP", w =>
            {
                w.Write(id);
                Dictionary(w, []);
                w.Write(1);
                w.Write(model);
                Dictionary(w, []);
            });
            return this;
        }

        public VoxBuilder Layer(int id, string name, bool hidden = false)
        {
            Chunk("LAYR", w =>
            {
                w.Write(id);
                Dictionary(w, hidden ? [("_name", name), ("_hidden", "1")] : [("_name", name)]);
                w.Write(-1);
            });
            return this;
        }

        public MemoryStream Build()
        {
            var file = new MemoryStream();
            using (var w = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Encoding.ASCII.GetBytes("VOX "));
                w.Write(150);
                w.Write(Encoding.ASCII.GetBytes("MAIN"));
                w.Write(0);
                w.Write((int)_children.Length);
                w.Write(_children.ToArray());
            }

            file.Position = 0;
            return file;
        }

        private void Chunk(string id, Action<BinaryWriter> content)
        {
            using var body = new MemoryStream();
            using (var w = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                content(w);
            }

            using var writer = new BinaryWriter(_children, Encoding.UTF8, leaveOpen: true);
            writer.Write(Encoding.ASCII.GetBytes(id));
            writer.Write((int)body.Length);
            writer.Write(0);
            writer.Write(body.ToArray());
        }

        private static void Dictionary(BinaryWriter w, (string Key, string Value)[] values)
        {
            w.Write(values.Length);
            foreach ((string key, string value) in values)
            {
                byte[] k = Encoding.UTF8.GetBytes(key);
                byte[] v = Encoding.UTF8.GetBytes(value);
                w.Write(k.Length);
                w.Write(k);
                w.Write(v.Length);
                w.Write(v);
            }
        }
    }

    /// <summary>MagicaVoxel's Z is up here, and its front, −Y, is the front here, +Z.</summary>
    [Fact]
    public void AModelStandsUpAndFacesForward()
    {
        // A column three high on MagicaVoxel's Z, and one voxel out in front of its foot on −Y.
        MemoryStream file = new VoxBuilder()
            .Model(new Int3(1, 2, 3), (0, 1, 0, Red), (0, 1, 1, Red), (0, 1, 2, Red), (0, 0, 0, Blue))
            .Transform(0, 1)
            .Group(1, 2)
            .Transform(2, 3, layer: 0, move: "0 0 0")
            .Shape(3, 0)
            .Build();

        VoxelScene scene = VoxFile.Load(file);
        HashSet<(Int3 Cell, byte Colour)> world = World(scene);

        int[] heights = [.. world.Where(c => c.Colour == Red).Select(c => c.Cell.Y).Order()];
        Assert.Equal([heights[0], heights[0] + 1, heights[0] + 2], heights);
        Assert.Single(world.Where(c => c.Colour == Red).Select(c => (c.Cell.X, c.Cell.Z)).Distinct());

        (Int3 foot, _) = world.Single(c => c.Colour == Blue);
        Int3 column = world.First(c => c.Colour == Red).Cell;
        Assert.Equal(heights[0], foot.Y);
        Assert.Equal(column.Z + 1, foot.Z);
    }

    /// <summary>A copy turned by its transform lands where MagicaVoxel's own arithmetic puts each voxel.</summary>
    [Theory]
    [InlineData(4, "0 0 0")]
    [InlineData(17, "3 -2 5")]
    [InlineData(81, "-4 6 1")]
    [InlineData(40, "2 2 2")]
    [InlineData(20, "0 7 -3")]
    public void ATurnedCopyLandsWhereMagicaVoxelPutsIt(int turn, string move)
    {
        (int X, int Y, int Z, byte Colour)[] voxels = [(0, 0, 0, Red), (1, 0, 0, Red), (2, 0, 0, Blue), (0, 1, 0, Blue), (0, 0, 1, Red), (0, 0, 2, Blue)];
        var size = new Int3(3, 2, 3);
        MemoryStream file = new VoxBuilder()
            .Model(size, voxels)
            .Transform(0, 1)
            .Group(1, 2)
            .Transform(2, 3, layer: 0, move: move, turn: turn)
            .Shape(3, 0)
            .Build();

        VoxelScene scene = VoxFile.Load(file);

        // MagicaVoxel: world = R (v − floor(size / 2)) + t, then (x, y, z) to (x, z, −y − 1) here.
        int[,] r = VoxFile.Unpack(turn);
        string[] t = move.Split(' ');
        var expected = new HashSet<(Int3, byte)>();
        foreach ((int x, int y, int z, byte colour) in voxels)
        {
            int[] v = [x - (size.X / 2), y - (size.Y / 2), z - (size.Z / 2)];
            int wx = (r[0, 0] * v[0]) + (r[0, 1] * v[1]) + (r[0, 2] * v[2]) + int.Parse(t[0], CultureInfo.InvariantCulture);
            int wy = (r[1, 0] * v[0]) + (r[1, 1] * v[1]) + (r[1, 2] * v[2]) + int.Parse(t[1], CultureInfo.InvariantCulture);
            int wz = (r[2, 0] * v[0]) + (r[2, 1] * v[1]) + (r[2, 2] * v[2]) + int.Parse(t[2], CultureInfo.InvariantCulture);
            expected.Add((new Int3(wx, wz, -wy - 1), colour));
        }

        Assert.Equal(expected, World(scene));
    }

    [Fact]
    public void CopiesOfOneModelComeInLinkedAndNamed()
    {
        MemoryStream file = new VoxBuilder()
            .Model(new Int3(2, 2, 2), (0, 0, 0, Red), (1, 1, 1, Blue))
            .Transform(0, 1)
            .Group(1, 2, 4)
            .Transform(2, 3, layer: 0, move: "0 0 0", name: "Crate")
            .Shape(3, 0)
            .Transform(4, 5, layer: 0, move: "8 0 0", turn: 17, name: "Crate turned")
            .Shape(5, 0)
            .Build();

        VoxelScene scene = VoxFile.Load(file);
        List<VoxelObject> crates = [.. scene.Objects.Where(o => !o.IsEmpty)];

        Assert.Equal(["Crate", "Crate turned"], crates.Select(o => o.Name));
        Assert.Same(crates[0].Grid, crates[1].Grid);
    }

    [Fact]
    public void LayersComeInAsCollections()
    {
        MemoryStream file = new VoxBuilder()
            .Model(new Int3(1, 1, 1), (0, 0, 0, Red))
            .Transform(0, 1)
            .Group(1, 2, 4)
            .Transform(2, 3, layer: 0, move: "0 0 0")
            .Shape(3, 0)
            .Transform(4, 5, layer: 1, move: "4 0 0")
            .Shape(5, 0)
            .Layer(0, "Ground")
            .Layer(1, "Props", hidden: true)
            .Build();

        VoxelScene scene = VoxFile.Load(file);

        Assert.Equal(["Ground", "Props"], scene.Collections.Select(c => c.Name));
        Assert.False(scene.Collections.Single(c => c.Name == "Props").Visible);
        Assert.Equal(1, scene.Objects.Count(o => !o.IsEmpty && !o.Visible));
    }

    [Fact]
    public void AFileWithoutAScenePutsItsModelInAnyway()
    {
        MemoryStream file = new VoxBuilder().Model(new Int3(2, 2, 2), (0, 0, 0, Red), (1, 0, 0, Red)).Build();

        VoxelScene scene = VoxFile.Load(file);

        Assert.Equal(2, World(scene).Count);
    }

    [Fact]
    public void WhatIsNotAVoxFileIsRefused() =>
        Assert.Throws<InvalidDataException>(() => VoxFile.Load(new MemoryStream(Encoding.ASCII.GetBytes("PK\u0003\u0004 not a vox file at all"))));

    // ---- The pieces of the format -------------------------------------------------------------------

    [Fact]
    public void TheTurnsPackAndUnpackAlike()
    {
        Assert.Equal(4, VoxFile.Pack(VoxFile.Unpack(4)));
        for (int first = 0; first < 3; first++)
        for (int second = 0; second < 3; second++)
        for (int signs = 0; signs < 8; signs++)
        {
            if (first == second)
            {
                continue;
            }

            int packed = first | (second << 2) | (signs << 4);
            Assert.Equal(packed, VoxFile.Pack(VoxFile.Unpack(packed)));
        }
    }

    [Fact]
    public void TheDefaultPaletteIsMagicaVoxels()
    {
        Color32[] palette = VoxFile.DefaultPalette();

        Assert.Equal(new Color32(255, 255, 255), palette[1]);
        Assert.Equal(new Color32(255, 255, 204), palette[2]);
        Assert.Equal(new Color32(0, 0, 0x33), palette[215]);
        Assert.Equal(new Color32(0xee, 0, 0), palette[216]);
        Assert.Equal(new Color32(0x11, 0x11, 0x11), palette[255]);
    }
}
