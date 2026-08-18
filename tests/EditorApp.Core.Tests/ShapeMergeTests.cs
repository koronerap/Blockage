using System.Globalization;
using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Merging on shape alone, which an unwrapped mesh makes possible: colour only had to match because
/// a merged quad's four corners sampled a single texel of the palette image. With a real sheet the
/// quad owns a rectangle big enough for every cell, so paint goes into the texture instead of into
/// the triangle count.
/// </summary>
public class ShapeMergeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-shape-merge", Guid.NewGuid().ToString("N"));

    public ShapeMergeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>A slab whose top face is painted in stripes, so colour-keyed merging has to give up.</summary>
    private static VoxelWorld StripedSlab(int side = 8)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int z = 0; z < side; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
                grid.SetFaceColor(new Int3(x, 0, z), Face.PosY, (byte)(30 + ((x + z) % 5)));
            }
        }

        return grid;
    }

    private static VoxelScene SceneOf(VoxelWorld grid, string name = "slab")
    {
        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, name);
        return scene;
    }

    [Fact]
    public void PaintNoLongerCutsUpTheGeometry()
    {
        VoxelScene scene = SceneOf(StripedSlab());

        ExportMesh colourKeyed = GreedyMesher.BuildScene(scene);
        ExportMesh shapeOnly = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);

        // Six sides, and every one of them a single quad, however it was painted.
        Assert.Equal(6, shapeOnly.QuadCount);
        Assert.True(
            colourKeyed.QuadCount > 30,
            $"The stripes should have split the colour-keyed mesh; it has {colourKeyed.QuadCount} quads.");
    }

    [Fact]
    public void MergingOnShapeStillDoesNotChangeTheSurface()
    {
        // EditorApp.md §4b, and the reason it is the primary gate: merging may only ever change how
        // the surface is written down. Loosening the rule must not loosen that.
        VoxelScene scene = SceneOf(StripedSlab(10));

        var naive = new MeshBuilder();
        EditMesher.BuildWorldNaive(scene.Objects[0].Grid, naive);

        ExportMesh shapeOnly = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);

        Assert.Equal(naive.TotalArea(), shapeOnly.TotalArea(), 4);
        Assert.True(shapeOnly.VertexCount < naive.VertexCount);
    }

    [Fact]
    public void EveryVoxelKeepsItsOwnColourInTheTexture()
    {
        // The whole trade. A quad now spans several colours, so the sheet has to carry each cell
        // separately — otherwise the geometry got cheaper by losing the paint.
        VoxelScene scene = SceneOf(StripedSlab());
        ExportMesh mesh = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);
        UvAtlas atlas = UvUnwrap.Apply(mesh, texelsPerVoxel: 4);

        byte[] pixels = AtlasTexture.CreateRgba(atlas, mesh, scene.Palette);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            QuadColors cells = mesh.QuadCells[quad];
            Vector2 origin = mesh.Uvs[quad * 4];
            Vector2 alongU = mesh.Uvs[(quad * 4) + 1] - origin;
            Vector2 alongV = mesh.Uvs[(quad * 4) + 3] - origin;

            for (int j = 0; j < cells.Height; j++)
            {
                for (int i = 0; i < cells.Width; i++)
                {
                    // Follow the quad's own parameters to the middle of this cell, the way a shader
                    // would, rather than reading the island's coordinates back out.
                    float u = (i + 0.5f) / cells.Width;
                    float v = (j + 0.5f) / cells.Height;
                    Vector2 uv = origin + (alongU * u) + (alongV * v);

                    int x = (int)(uv.X * atlas.Width);
                    int y = (int)(uv.Y * atlas.Height);
                    int offset = (((y * atlas.Width) + x) * 4);

                    Color32 expected = scene.Palette[cells.At(i, j)];
                    Assert.Equal(
                        (expected.R, expected.G, expected.B),
                        (pixels[offset], pixels[offset + 1], pixels[offset + 2]));
                }
            }
        }
    }

    [Fact]
    public void PaletteExportsStillMergeOnColour()
    {
        // Palette blocks put all four corners of a quad on one texel, so a merged quad there can
        // only ever be one colour. That mode has to keep the old rule.
        VoxelScene scene = SceneOf(StripedSlab());
        ExportMesh mesh = GreedyMesher.BuildScene(scene);

        Assert.All(mesh.QuadCells, cells => Assert.Equal((1, 1), (cells.Width, cells.Height)));

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector2 first = mesh.Uvs[quad * 4];
            for (int corner = 1; corner < 4; corner++)
            {
                Assert.Equal(first, mesh.Uvs[(quad * 4) + corner]);
            }
        }
    }

    [Fact]
    public void EachObjectComesOutAsItsOwnObject()
    {
        var scene = new VoxelScene();
        foreach (string name in new[] { "barrel", "grip", "stock" })
        {
            var grid = new VoxelWorld();
            grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
            scene.Add(grid, new ObjectTransform(new Vector3(scene.Objects.Count * 10f, 0f, 0f), Quaternion.Identity), name);
        }

        ExportMesh mesh = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);

        Assert.Equal(3, mesh.Parts.Count);
        Assert.Equal(["barrel", "grip", "stock"], mesh.Parts.Select(p => p.Name));
        Assert.Equal(mesh.QuadCount, mesh.Parts.Sum(p => p.QuadCount));

        string path = Path.Combine(_directory, "level.obj");
        new ObjExporter().Export(mesh, scene.Palette, path, new ExportOptions());

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(
            ["o barrel", "o grip", "o stock"],
            lines.Where(l => l.StartsWith("o ", StringComparison.Ordinal)));

        // Every face has to land under one of them, or an object would come in empty.
        int faces = lines.Count(l => l.StartsWith("f ", StringComparison.Ordinal));
        Assert.Equal(mesh.QuadCount, faces);
    }

    [Fact]
    public void ObjectFacesFollowTheirOwnObjectMarker()
    {
        // Not just that the markers are present — that the faces after each one belong to it. Six
        // faces per cube, in the order the parts were recorded.
        var scene = new VoxelScene();
        for (int i = 0; i < 3; i++)
        {
            var grid = new VoxelWorld();
            grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
            scene.Add(grid, new ObjectTransform(new Vector3(i * 10f, 0f, 0f), Quaternion.Identity), $"piece{i}");
        }

        ExportMesh mesh = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);
        string path = Path.Combine(_directory, "three.obj");
        new ObjExporter().Export(mesh, scene.Palette, path, new ExportOptions());

        var positions = new List<float[]>();
        string current = string.Empty;
        var xByObject = new Dictionary<string, List<float>>();

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0] == "v")
            {
                positions.Add([.. parts[1..].Select(s => float.Parse(s, CultureInfo.InvariantCulture))]);
            }
            else if (parts[0] == "o")
            {
                current = parts[1];
                xByObject[current] = [];
            }
            else if (parts[0] == "f")
            {
                foreach (string corner in parts[1..])
                {
                    xByObject[current].Add(positions[int.Parse(corner.Split('/')[0], CultureInfo.InvariantCulture) - 1][0]);
                }
            }
        }

        // Each cube sits ten apart, so its faces can only reference its own corner of the world.
        Assert.Equal(3, xByObject.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.All(xByObject[$"piece{i}"], x => Assert.InRange(x, (i * 10f) - 0.001f, (i * 10f) + 1.001f));
        }
    }
}
