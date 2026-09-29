using System.Numerics;
using System.Text.Json;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Levels of detail and lightmap UVs (Fullreleaseplan 8.8), as the exports carry them.</summary>
public sealed class LodAndLightmapTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lods-{Guid.NewGuid():N}");

    public LodAndLightmapTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static VoxelWorld Box(int width, int height, int depth, byte colour = 20)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        for (int z = 0; z < depth; z++)
        {
            grid.SetVoxel(x, y, z, colour);
        }

        return grid;
    }

    // ---- Coarser copies -----------------------------------------------------------------------------

    [Fact]
    public void ABlockIsOneVoxelWhenAtLeastHalfOfItIsSolid()
    {
        var grid = new VoxelWorld();
        // Four of a block's eight, a whole layer: half, so it stays.
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        {
            grid.SetVoxel(x, 0, z, 20);
        }

        // Three of the next block's eight: less than half, so it goes.
        grid.SetVoxel(2, 0, 0, 20);
        grid.SetVoxel(3, 0, 0, 20);
        grid.SetVoxel(2, 1, 0, 20);

        VoxelWorld coarse = VoxelLod.Downsample(grid, 2);

        Assert.Equal(1, coarse.SolidCount);
        Assert.True(coarse.IsSolid(new Int3(0, 0, 0)));
    }

    [Fact]
    public void ABlockTakesTheColourMostOfItsVoxelsHave()
    {
        VoxelWorld grid = Box(2, 2, 2, colour: 20);
        grid.SetVoxel(0, 0, 0, 90);
        grid.SetVoxel(1, 0, 0, 90);

        Assert.Equal(20, VoxelLod.Downsample(grid, 2).GetVoxel(0, 0, 0));
    }

    [Fact]
    public void BlocksBelowZeroAreCountedFromBelowZero()
    {
        var shifted = new VoxelWorld();
        for (int x = -2; x < 0; x++)
        for (int y = -2; y < 0; y++)
        for (int z = -2; z < 0; z++)
        {
            shifted.SetVoxel(x, y, z, 20);
        }

        VoxelWorld coarse = VoxelLod.Downsample(shifted, 2);

        Assert.Equal(1, coarse.SolidCount);
        Assert.True(coarse.IsSolid(new Int3(-1, -1, -1)));
    }

    [Fact]
    public void WithLevelsOfDetailEachObjectHasItsCoarserParts()
    {
        var scene = new VoxelScene();
        scene.Add(Box(8, 8, 8), ObjectTransform.Identity, "Block");

        ExportMesh mesh = GreedyMesher.BuildScene(scene, instanceLinked: true, lods: true);

        MeshInstance block = Assert.Single(mesh.Instances);
        Assert.Equal(2, block.Lods!.Count);
        Assert.Equal(["Block", "Block_LOD1", "Block_LOD2"], mesh.Parts.Select(p => p.Name));

        // Each covers the block: eight voxels a side, in blocks of two and then of four.
        foreach (int part in block.Lods)
        {
            MeshPart lod = mesh.Parts[part];
            IEnumerable<Vector3> corners = Enumerable.Range(lod.FirstQuad * 4, lod.QuadCount * 4).Select(i => mesh.Positions[i]);
            Assert.Equal(new Vector3(8f), corners.Aggregate(Vector3.Max));
        }
    }

    // ---- Lightmap UVs -------------------------------------------------------------------------------

    /// <summary>Each part's faces apart, within its own square — as a lightmapper packs a mesh at a time.</summary>
    [Fact]
    public void LightmapUvsKeepEachPartsFacesApartInItsOwnSquare()
    {
        var scene = new VoxelScene();
        scene.Add(Box(3, 2, 2), ObjectTransform.Identity, "Crate");
        scene.Add(Box(1, 4, 1), ObjectTransform.Identity with { Position = new Vector3(8f, 0f, 0f) }, "Post");
        ExportMesh mesh = GreedyMesher.BuildScene(scene, instanceLinked: true);

        UvUnwrap.AddLightmapUvs(mesh);

        Assert.Equal(mesh.VertexCount, mesh.LightmapUvs.Count);
        foreach (MeshPart part in mesh.Parts)
        {
            var rectangles = new List<(Vector2 Min, Vector2 Max)>();
            for (int quad = part.FirstQuad; quad < part.FirstQuad + part.QuadCount; quad++)
            {
                IEnumerable<Vector2> uvs = Enumerable.Range(quad * 4, 4).Select(i => mesh.LightmapUvs[i]);
                Vector2 low = uvs.Aggregate(Vector2.Min), high = uvs.Aggregate(Vector2.Max);
                Assert.True(low.X >= 0f && low.Y >= 0f && high.X <= 1f && high.Y <= 1f, $"{part.Name}: {low} to {high} is outside its square");
                foreach ((Vector2 otherLow, Vector2 otherHigh) in rectangles)
                {
                    bool apart = high.X <= otherLow.X + 1e-5f || otherHigh.X <= low.X + 1e-5f || high.Y <= otherLow.Y + 1e-5f || otherHigh.Y <= low.Y + 1e-5f;
                    Assert.True(apart, $"{part.Name}: two faces share lightmap texels");
                }

                rectangles.Add((low, high));
            }
        }
    }

    [Fact]
    public void TheTexturesUvsAreLeftAsTheyWere()
    {
        var scene = new VoxelScene();
        scene.Add(Box(3, 2, 2), ObjectTransform.Identity, "Crate");
        ExportMesh mesh = GreedyMesher.BuildScene(scene, instanceLinked: true);
        List<Vector2> before = [.. mesh.Uvs];

        UvUnwrap.AddLightmapUvs(mesh);

        Assert.Equal(before, mesh.Uvs);
    }

    [Fact]
    public void AGltfCarriesTheLightmapUvsAsItsSecondSet()
    {
        var scene = new VoxelScene();
        scene.Add(Box(3, 2, 2), ObjectTransform.Identity, "Crate");
        ExportMesh mesh = GreedyMesher.BuildScene(scene, instanceLinked: true);
        UvUnwrap.AddLightmapUvs(mesh);
        string path = Path.Combine(_directory, "crate.gltf");

        new GltfExporter(binary: false).Export(mesh, scene.Palette, path, new ExportOptions());

        using JsonDocument gltf = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement attributes = gltf.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
        Assert.True(attributes.TryGetProperty("TEXCOORD_1", out _));
    }

    [Fact]
    public void WithoutLightmapUvsTheGltfHasOneSet()
    {
        var scene = new VoxelScene();
        scene.Add(Box(3, 2, 2), ObjectTransform.Identity, "Crate");
        string path = Path.Combine(_directory, "plain.gltf");

        new GltfExporter(binary: false).Export(GreedyMesher.BuildScene(scene, instanceLinked: true), scene.Palette, path, new ExportOptions());

        using JsonDocument gltf = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement attributes = gltf.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
        Assert.False(attributes.TryGetProperty("TEXCOORD_1", out _));
    }
}
