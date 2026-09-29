using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Generate (Fullreleaseplan 6.8): the same numbers and seed make the same thing, standing on its origin.</summary>
public class GeneratorTests
{
    public static TheoryData<ShapeKind> Kinds() => [.. Generators.All];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheSameSeedMakesTheSameThingAndAnotherAnother(ShapeKind kind)
    {
        ShapeSettings settings = Shapes.Defaults(kind);
        int seed = Shapes.FieldsOf(kind).ToList().FindIndex(f => f.Name == "Seed");
        Palette palette = Palette.CreateDefault();

        VoxelWorld first = Shapes.Build(settings, 1, palette);
        VoxelWorld again = Shapes.Build(settings, 1, palette);
        VoxelWorld other = Shapes.Build(settings.With(seed, 77), 1, palette);

        Assert.True(first.SolidCount > 0);
        Assert.Equal(first.ContentHash(), again.ContentHash());
        Assert.NotEqual(first.ContentHash(), other.ContentHash());

        // It stands on its origin, as every shape does.
        Assert.True(first.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(0, min.Y);
        Assert.True(Math.Abs(min.X + max.X) <= 2, $"{kind} is not centred across X: {min.X}..{max.X}");
    }

    [Fact]
    public void TerrainIsGrassOverEarthOverStoneAndWaterFillsTheLows()
    {
        Palette palette = Palette.CreateDefault();
        ShapeSettings settings = Shapes.Defaults(ShapeKind.Terrain).With(4, 8);
        VoxelWorld ground = Shapes.Build(settings, 1, palette);

        byte water = palette.Nearest(new Color32(58, 110, 196, 255));
        byte stone = palette.Nearest(new Color32(128, 128, 132, 255));
        byte grass = palette.Nearest(new Color32(92, 158, 58, 255));
        byte[] all = [.. ground.Chunks.Values.SelectMany(c => c.Indices.ToArray())];
        Assert.Contains(all, b => b == water);
        Assert.Contains(all, b => b == stone);
        Assert.Contains(all, b => b == grass);

        // Nothing solid stands on water: it is the top of its column.
        foreach ((ChunkCoord coord, Chunk chunk) in ground.Chunks)
        {
            for (int i = 0; i < Chunk.VoxelCount; i++)
            {
                if (chunk.Indices[i] == water)
                {
                    Int3 cell = coord.Origin + Chunk.FromLinearIndex(i);
                    byte above = ground.GetVoxel(cell + new Int3(0, 1, 0));
                    Assert.True(above == Palette.EmptyIndex || above == water);
                }
            }
        }
    }

    [Fact]
    public void AGeneratedShapeIsAddedInItsOwnColoursAndAdjusted()
    {
        var session = new EditorSession();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 1);
        session.ReplaceWorld(grid, projectPath: null);
        session.ActiveColorIndex = 3;

        VoxelObject tree = session.AddShape(Shapes.Defaults(ShapeKind.Tree), Vector3.Zero, Vector3.UnitY, 1f);
        Assert.DoesNotContain(tree.Grid.Chunks.Values.SelectMany(c => c.Indices.ToArray()), b => b == 3);

        int solid = tree.Grid.SolidCount;
        Assert.True(session.ReshapeLast(Shapes.Defaults(ShapeKind.Tree).With(0, 30)));
        Assert.True(tree.Grid.SolidCount > solid);
    }

    /// <summary>Writes the five, rendered, when BLOCKAGE_RENDER_OUT names a file; nothing otherwise.</summary>
    [Fact]
    public void RenderToLookAt()
    {
        string? path = Environment.GetEnvironmentVariable("BLOCKAGE_RENDER_OUT");
        if (path is null)
        {
            return;
        }

        var scene = new VoxelScene();
        float x = -60f;
        foreach (ShapeKind kind in Generators.All)
        {
            ShapeSettings settings = kind == ShapeKind.Terrain ? Shapes.Defaults(kind).With(0, 40).With(1, 40).With(4, 6) : Shapes.Defaults(kind);
            VoxelWorld grid = Shapes.Build(settings, 1, scene.Palette);
            scene.Add(grid, ObjectTransform.At(new Vector3(x, 0f, 0f)), kind.ToString());
            x += kind == ShapeKind.Terrain ? 50f : 30f;
        }

        scene.AddDefaultSun();
        var camera = new RenderCamera(new Vector3(20f, 70f, 110f), Vector3.Normalize(new Vector3(-10f, -55f, -110f)), Vector3.UnitY, 45f, false, 0f);
        var tracer = new PathTracer(RenderScene.Capture(scene), camera, new RenderSettings { Width = 800, Height = 400, Samples = 24 });
        while (!tracer.IsFinished)
        {
            tracer.AddSample();
        }

        File.WriteAllBytes(path, PngWriter.EncodeRgba(tracer.ToRgba(), 800, 400));
    }
}
