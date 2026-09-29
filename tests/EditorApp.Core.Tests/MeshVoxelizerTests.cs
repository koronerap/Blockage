using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Import;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Meshes made into voxels (Fullreleaseplan 8.3): where the surface passes, what it closes in, and in what colours.</summary>
public sealed class MeshVoxelizerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"voxelize-{Guid.NewGuid():N}");

    public MeshVoxelizerTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly Color32 Red = new(220, 40, 40);
    private static readonly Color32 Blue = new(40, 80, 220);

    /// <summary>A box as twelve triangles, its top in one colour and the rest in another.</summary>
    private static ColouredMesh Box(Vector3 size, Color32 sides, Color32 top)
    {
        Vector3 Corner(int i) => new((i & 1) == 0 ? 0f : size.X, (i & 2) == 0 ? 0f : size.Y, (i & 4) == 0 ? 0f : size.Z);
        (int, int, int, int, bool)[] faces =
        [
            (0, 2, 6, 4, false), (1, 5, 7, 3, false), // −X, +X
            (0, 4, 5, 1, false), (2, 3, 7, 6, true),  // −Y, +Y
            (0, 1, 3, 2, false), (4, 6, 7, 5, false), // −Z, +Z
        ];

        var triangles = new List<MeshTriangle>();
        foreach ((int a, int b, int c, int d, bool isTop) in faces)
        {
            Color32 colour = isTop ? top : sides;
            triangles.Add(new MeshTriangle(Corner(a), Corner(b), Corner(c)) { Colour = colour });
            triangles.Add(new MeshTriangle(Corner(a), Corner(c), Corner(d)) { Colour = colour });
        }

        return new ColouredMesh("box", triangles, []);
    }

    private static VoxelWorld GridOf(VoxelScene scene) => scene.Objects.Single().Grid;

    private static Color32 ColourAt(VoxelScene scene, int x, int y, int z) => scene.Palette[GridOf(scene).GetVoxel(x, y, z)];

    // ---- Where the voxels go ------------------------------------------------------------------------

    [Fact]
    public void ABoxsSurfaceIsItsShellOfVoxels()
    {
        VoxelScene shell = MeshVoxelizer.Voxelize(Box(new Vector3(8f), Red, Blue), resolution: 8, solid: false, "box");

        Assert.Equal((8 * 8 * 8) - (6 * 6 * 6), GridOf(shell).SolidCount);
    }

    [Fact]
    public void FilledTheBoxIsSolidThrough()
    {
        VoxelScene solid = MeshVoxelizer.Voxelize(Box(new Vector3(8f), Red, Blue), resolution: 8, solid: true, "box");

        Assert.Equal(8 * 8 * 8, GridOf(solid).SolidCount);
    }

    [Fact]
    public void TheResolutionIsAlongTheLongestSide()
    {
        VoxelScene made = MeshVoxelizer.Voxelize(Box(new Vector3(16f, 4f, 8f), Red, Red), resolution: 8, solid: true, "slab");

        Assert.True(GridOf(made).TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(8, 2, 4), new Int3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1));
    }

    [Fact]
    public void ItStandsOnTheGroundWithItsMiddleOnTheSpot()
    {
        VoxelScene made = MeshVoxelizer.Voxelize(Box(new Vector3(8f), Red, Red), resolution: 8, solid: true, "box");

        Assert.True(GridOf(made).TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(-4, 0, -4), min);
        Assert.Equal(new Int3(3, 7, 3), max);
    }

    // ---- Colours ------------------------------------------------------------------------------------

    [Fact]
    public void EachVoxelIsTheColourOfTheSurfaceNearestIt()
    {
        VoxelScene made = MeshVoxelizer.Voxelize(Box(new Vector3(8f), Red, Blue), resolution: 8, solid: false, "box");

        Assert.Equal(Blue, ColourAt(made, 0, 7, 0));
        Assert.Equal(Red, ColourAt(made, -4, 3, 0));
        Assert.Equal(Red, ColourAt(made, 0, 0, 0));
    }

    [Fact]
    public void TheInsideTakesTheColourOfTheSurfaceNearestIt()
    {
        VoxelScene made = MeshVoxelizer.Voxelize(Box(new Vector3(8f), Red, Blue), resolution: 8, solid: true, "box");

        Assert.Equal(Blue, ColourAt(made, 0, 6, 0));
        Assert.Equal(Red, ColourAt(made, 0, 1, 0));
    }

    // ---- The test through the cube ------------------------------------------------------------------

    [Fact]
    public void ATriangleThroughTheCubeOverlapsIt() =>
        Assert.True(MeshVoxelizer.Overlaps(Vector3.Zero, 0.5f, new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, -2f), new Vector3(0f, 0f, 2f)));

    /// <summary>Its bounds and its plane meet the cube's; only the axis across an edge tells them apart.</summary>
    [Fact]
    public void ATriangleJustPastACornerDoesNot() =>
        Assert.False(MeshVoxelizer.Overlaps(Vector3.Zero, 0.5f, new Vector3(1.1f, 0f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(1.1f, 1.1f, 0f)));

    [Fact]
    public void TheNearestPointOfATriangleIsWeighedByItsCorners()
    {
        (float a, float b, float c, float distance) = MeshVoxelizer.Closest(new Vector3(0.25f, 1f, 0.25f), Vector3.Zero, Vector3.UnitX, Vector3.UnitZ);

        Assert.Equal(0.5f, a, 3);
        Assert.Equal(0.25f, b, 3);
        Assert.Equal(0.25f, c, 3);
        Assert.Equal(1f, distance, 3);
    }

    // ---- Reading models -----------------------------------------------------------------------------

    [Fact]
    public void AnObjTakesItsMaterialsColourAndItsCornersColours()
    {
        string obj = Path.Combine(_directory, "tri.obj");
        File.WriteAllLines(Path.Combine(_directory, "tri.mtl"), ["newmtl red", "Kd 1 0 0"]);
        File.WriteAllLines(obj,
        [
            "mtllib tri.mtl",
            "v 0 0 0 1 1 1",
            "v 1 0 0 0 1 0",
            "v 0 1 0 1 1 1",
            "usemtl red",
            "f 1 2 3",
        ]);

        ColouredMesh mesh = ColouredMesh.Load(obj);

        MeshTriangle triangle = Assert.Single(mesh.Triangles);
        Assert.Equal(new Color32(255, 0, 0), triangle.At(1f, 0f, 0f));
        Assert.Equal(new Color32(0, 0, 0), triangle.At(0f, 1f, 0f));
    }

    [Fact]
    public void AnObjTextureIsReadFromThePngBesideIt()
    {
        byte[] pixels = [255, 0, 0, 255, 0, 0, 255, 255];
        PngWriter.WriteRgba(Path.Combine(_directory, "two.png"), pixels, 2, 1);
        File.WriteAllLines(Path.Combine(_directory, "quad.mtl"), ["newmtl painted", "map_Kd two.png"]);
        string obj = Path.Combine(_directory, "quad.obj");
        File.WriteAllLines(obj,
        [
            "mtllib quad.mtl",
            "v 0 0 0", "v 1 0 0", "v 0 1 0",
            "vt 0.25 0.5", "vt 0.75 0.5", "vt 0.25 0.5",
            "usemtl painted",
            "f 1/1 2/2 3/3",
        ]);

        MeshTriangle triangle = Assert.Single(ColouredMesh.Load(obj).Triangles);

        Assert.Equal(new Color32(255, 0, 0), triangle.At(1f, 0f, 0f));
        Assert.Equal(new Color32(0, 0, 255), triangle.At(0f, 1f, 0f));
    }

    [Fact]
    public void AMissingMaterialFileIsSaidAndTheModelIsWhite()
    {
        string obj = Path.Combine(_directory, "bare.obj");
        File.WriteAllLines(obj, ["mtllib gone.mtl", "v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3"]);

        ColouredMesh mesh = ColouredMesh.Load(obj);

        Assert.Contains(mesh.Warnings, warning => warning.Contains("gone.mtl", StringComparison.Ordinal));
        Assert.Equal(new Color32(255, 255, 255), mesh.Triangles[0].At(1f, 0f, 0f));
    }

    /// <summary>Blockage's own GLB, made back into voxels at its size, comes back in its colours.</summary>
    [Fact]
    public void ItsOwnGlbComesBackInItsColours()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            grid.SetVoxel(x, y, z, y < 2 ? (byte)20 : (byte)90);
        }

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "Block");
        string glb = Path.Combine(_directory, "block.glb");
        new GltfExporter(binary: true).Export(GreedyMesher.BuildScene(scene), scene.Palette, glb, new ExportOptions());

        VoxelScene back = MeshVoxelizer.Voxelize(ColouredMesh.Load(glb), resolution: 4, solid: true, "block");

        Assert.Equal(64, GridOf(back).SolidCount);
        Assert.Equal(scene.Palette[20], ColourAt(back, 0, 0, 0));
        Assert.Equal(scene.Palette[90], ColourAt(back, 0, 3, 0));
    }
}
