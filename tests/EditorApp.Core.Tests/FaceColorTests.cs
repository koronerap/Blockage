using EditorApp.Core.Commands;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class FaceColorStorageTests
{
    [Fact]
    public void AVoxelWithNothingPaintedCostsNoOverrides()
    {
        // The whole design: only painted faces are paid for, so a plain model stores one byte per
        // voxel exactly as it did before per-face colour existed.
        var chunk = new Chunk();
        for (int i = 0; i < 100; i++)
        {
            chunk.Set(i % Chunk.Size, 0, 0, 7);
        }

        Assert.Equal(0, chunk.FaceOverrideCount);
        Assert.Equal(7, chunk.GetFace(0, 0, 0, Face.PosY));
    }

    [Fact]
    public void PaintingBackToTheBaseColourDropsTheOverride()
    {
        var chunk = new Chunk();
        chunk.Set(1, 1, 1, 7);

        Assert.True(chunk.SetFace(1, 1, 1, Face.PosY, 9));
        Assert.Equal(1, chunk.FaceOverrideCount);

        Assert.True(chunk.SetFace(1, 1, 1, Face.PosY, 7));
        Assert.Equal(0, chunk.FaceOverrideCount);
        Assert.Equal(7, chunk.GetFace(1, 1, 1, Face.PosY));
    }

    [Fact]
    public void OnlyTheNamedFaceChanges()
    {
        var chunk = new Chunk();
        chunk.Set(2, 2, 2, 5);
        chunk.SetFace(2, 2, 2, Face.PosX, 60);

        Assert.Equal(60, chunk.GetFace(2, 2, 2, Face.PosX));

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            if ((Face)f != Face.PosX)
            {
                Assert.Equal(5, chunk.GetFace(2, 2, 2, (Face)f));
            }
        }
    }

    [Fact]
    public void AnEmptyCellHasNoFacesToPaint()
    {
        var chunk = new Chunk();
        Assert.False(chunk.SetFace(0, 0, 0, Face.PosY, 9));
        Assert.Equal(0, chunk.FaceOverrideCount);
    }

    [Fact]
    public void ClearingAVoxelForgetsItsPaintedFaces()
    {
        var chunk = new Chunk();
        chunk.Set(3, 3, 3, 5);
        chunk.SetFace(3, 3, 3, Face.NegZ, 40);

        chunk.Set(3, 3, 3, Palette.EmptyIndex);
        chunk.Set(3, 3, 3, 5);

        Assert.Equal(0, chunk.FaceOverrideCount);
        Assert.Equal(5, chunk.GetFace(3, 3, 3, Face.NegZ));
    }
}

public class FaceColorPipelineTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-face-tests", Guid.NewGuid().ToString("N"));

    public FaceColorPipelineTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>A single voxel with all six faces painted differently — the hardest case to preserve.</summary>
    private static VoxelScene RainbowCube()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            grid.SetFaceColor(Int3.Zero, (Face)f, (byte)(100 + f));
        }

        scene.Add(grid, ObjectTransform.Identity, "rainbow");
        return scene;
    }

    [Fact]
    public void EachFaceKeepsItsOwnColourThroughASaveAndLoad()
    {
        VoxelScene original = RainbowCube();
        string path = Path.Combine(_directory, "faces.vxlevel");

        VxLevelFile.Save(original, path);
        VoxelScene loaded = VxLevelFile.LoadScene(path);

        VoxelWorld grid = loaded.Objects[0].Grid;
        for (int f = 0; f < FaceInfo.Count; f++)
        {
            Assert.Equal(100 + f, grid.GetFaceColor(Int3.Zero, (Face)f));
        }
    }

    [Fact]
    public void AFileWithNoPaintedFacesCarriesNoFaceData()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        scene.Add(grid, ObjectTransform.Identity, "plain");

        string path = Path.Combine(_directory, "plain.vxlevel");
        VxLevelFile.Save(scene, path);

        using FileStream stream = File.OpenRead(path);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("/faces/", StringComparison.Ordinal));
    }

    [Fact]
    public void TheEditMeshGivesEachFaceItsOwnColour()
    {
        VoxelScene scene = RainbowCube();

        var builder = new MeshBuilder();
        EditMesher.BuildChunk(scene.Objects[0].Grid, new ChunkCoord(0, 0, 0), builder);

        Assert.Equal(6, builder.QuadCount);

        // Six quads, six distinct colours.
        var colours = new HashSet<uint>();
        for (int quad = 0; quad < builder.QuadCount; quad++)
        {
            colours.Add(builder.Vertices[quad * 4].Rgba);
        }

        Assert.Equal(6, colours.Count);
    }

    [Fact]
    public void GreedyMeshingStillMergesFacesThatSharePaintedColour()
    {
        // Merging keys on the face's colour, so painting a whole side one colour must still come
        // out as a single quad.
        var scene = new VoxelScene();
        var grid = new VoxelWorld();

        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 4; z++)
            {
                grid.SetVoxel(x, 0, z, 5);
                grid.SetFaceColor(new Int3(x, 0, z), Face.PosY, 60);
            }
        }

        scene.Add(grid, ObjectTransform.Identity, "plate");
        ExportMesh mesh = GreedyMesher.BuildScene(scene);

        Assert.Equal(6, mesh.QuadCount);
        Assert.Contains((byte)60, mesh.UsedPaletteIndices());
        Assert.Contains((byte)5, mesh.UsedPaletteIndices());
    }

    [Fact]
    public void PaintingOneFaceSplitsAQuadThatWouldOtherwiseMerge()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();

        for (int x = 0; x < 4; x++)
        {
            grid.SetVoxel(x, 0, 0, 5);
        }

        scene.Add(grid, ObjectTransform.Identity, "bar");
        int before = GreedyMesher.BuildScene(scene).QuadCount;

        grid.SetFaceColor(new Int3(1, 0, 0), Face.PosY, 60);
        int after = GreedyMesher.BuildScene(scene).QuadCount;

        Assert.True(after > before, $"Expected the run to split; {before} quads became {after}.");
    }

    [Fact]
    public void TheExportedSurfaceIsUnchangedByPainting()
    {
        // Colour must not move geometry: the §4b area invariant still has to hold.
        VoxelScene scene = RainbowCube();

        var naive = new MeshBuilder();
        EditMesher.BuildWorldNaive(scene.Objects[0].Grid, naive);

        Assert.Equal(naive.TotalArea(), GreedyMesher.BuildScene(scene).TotalArea(), 4);
    }

    [Fact]
    public void UndoRestoresEveryPaintedFace()
    {
        VoxelScene scene = RainbowCube();
        VoxelWorld grid = scene.Objects[0].Grid;

        var command = new VoxelEditCommand("repaint", grid);
        for (int f = 0; f < FaceInfo.Count; f++)
        {
            command.ApplyFace(Int3.Zero, (Face)f, 200);
        }

        command.Undo();

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            Assert.Equal(100 + f, grid.GetFaceColor(Int3.Zero, (Face)f));
        }
    }
}
