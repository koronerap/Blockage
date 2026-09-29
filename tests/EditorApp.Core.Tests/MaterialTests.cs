using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using SharpGLTF.Schema2;

namespace EditorApp.Core.Tests;

/// <summary>
/// Voxel materials (Fullreleaseplan 4.1): what a palette colour is made of besides its colour, kept
/// with the palette, saved, undone, carried by the mesh to the renderer, and exported to glTF.
/// </summary>
public class MaterialTests
{
    private const byte Glass = 120;
    private const byte Lamp = 60;

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "editorapp-material-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EveryEntryStartsPlainAndAClearedOneIsPlainAgain()
    {
        var palette = Palette.CreateDefault();

        Assert.True(palette.Material(Glass).IsPlain);
        Assert.Equal(1f, palette.Material(Glass).Roughness);
        Assert.Equal(1f, palette.Material(Glass).Opacity);
        Assert.False(palette.AnyTransparent);

        palette.SetMaterial(Glass, VoxelMaterial.Of(0f, 0f, 0.1f, 0.4f));
        Assert.True(palette.AnyTransparent);
        Assert.Equal(0.4f, palette.Material(Glass).Opacity, 3);

        palette.SetMaterial(Glass, VoxelMaterial.Plain);
        Assert.Empty(palette.Materials());
    }

    [Fact]
    public void AMaterialStaysInsideWhatItCanBe()
    {
        VoxelMaterial wild = VoxelMaterial.Of(5f, -2f, 3f, 0f);

        Assert.Equal(1f, wild.Emission);
        Assert.Equal(0f, wild.Metallic);
        Assert.Equal(1f, wild.Roughness);

        // Glass that vanished entirely could not be found again.
        Assert.Equal(1f - VoxelMaterial.MaxTransparency, wild.Opacity, 3);
    }

    [Fact]
    public void ACloneOfThePaletteHasItsMaterials()
    {
        var palette = Palette.CreateDefault();
        palette.SetMaterial(Lamp, VoxelMaterial.Of(1f, 0f, 1f, 1f));

        Assert.Equal(palette.Material(Lamp), palette.Clone().Material(Lamp));
    }

    [Fact]
    public void MaterialsAreSavedWithTheLevel()
    {
        var scene = VoxelScene.CreateStarter();
        scene.Palette.SetMaterial(Glass, VoxelMaterial.Of(0f, 0.5f, 0.25f, 0.6f));
        scene.Palette.SetMaterial(Lamp, VoxelMaterial.Of(0.8f, 0f, 1f, 1f));

        using var stream = new MemoryStream();
        VxLevelFile.Save(scene, stream, "materials");
        stream.Position = 0;
        Palette loaded = VxLevelFile.LoadScene(stream).Palette;

        Assert.Equal(scene.Palette.Material(Glass).Opacity, loaded.Material(Glass).Opacity, 3);
        Assert.Equal(scene.Palette.Material(Glass).Metallic, loaded.Material(Glass).Metallic, 3);
        Assert.Equal(scene.Palette.Material(Lamp).Emission, loaded.Material(Lamp).Emission, 3);
        Assert.True(loaded.Material(Palette.WhiteIndex).IsPlain);
    }

    [Fact]
    public void AMaterialDragIsOneUndoStep()
    {
        var session = new EditorSession();
        session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);
        VoxelMaterial before = session.Scene.Palette.Material(Glass);

        session.SetMaterial(Glass, VoxelMaterial.Of(0f, 0f, 1f, 0.8f));
        session.SetMaterial(Glass, VoxelMaterial.Of(0f, 0f, 1f, 0.5f));
        Assert.True(session.PushMaterialEdit(Glass, before));

        Assert.Equal(1, session.History.UndoCount);
        session.Undo();
        Assert.True(session.Scene.Palette.Material(Glass).IsPlain);
    }

    [Fact]
    public void TheMeshCarriesEachFacesPaletteEntry()
    {
        var world = new VoxelWorld();
        world.SetVoxel(0, 0, 0, Glass);
        world.SetFaceColor(new Int3(0, 0, 0), Face.PosY, Lamp);

        var mesh = new MeshBuilder();
        EditMesher.BuildChunk(world, new ChunkCoord(0, 0, 0), mesh);

        foreach (MeshVertex vertex in mesh.Vertices)
        {
            Assert.Equal(vertex.Face == Face.PosY ? Lamp : Glass, vertex.PaletteIndex);
        }
    }

    [Fact]
    public void TheColourTextureCarriesOpacityInItsAlpha()
    {
        var palette = Palette.CreateDefault();
        palette.SetMaterial(Glass, VoxelMaterial.Of(0f, 0f, 1f, 0.5f));

        byte[] rgba = PaletteTexture.CreateRgba(palette);
        Vector2Int texel = TexelOf(Glass);

        Assert.InRange(rgba[(((texel.Y * PaletteTexture.Width) + texel.X) * 4) + 3], 126, 129);
        Vector2Int white = TexelOf(Palette.WhiteIndex);
        Assert.Equal(255, rgba[(((white.Y * PaletteTexture.Width) + white.X) * 4) + 3]);
    }

    private readonly record struct Vector2Int(int X, int Y);

    private static Vector2Int TexelOf(byte index) =>
        new((index % PaletteTexture.Columns * PaletteTexture.BlockSize) + 1, (index / PaletteTexture.Columns * PaletteTexture.BlockSize) + 1);

    [Fact]
    public void GltfGetsTheMaterialsSeeThroughFacesApart()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        grid.SetVoxel(2, 0, 0, Glass);
        grid.SetVoxel(4, 0, 0, Lamp);
        scene.Add(grid, ObjectTransform.Identity, "Things");
        scene.Palette.SetMaterial(Glass, VoxelMaterial.Of(0f, 0.2f, 0.1f, 0.5f));
        scene.Palette.SetMaterial(Lamp, VoxelMaterial.Of(1f, 0f, 1f, 1f));

        string directory = TempDirectory();
        string path = Path.Combine(directory, "things.glb");
        try
        {
            ExportMesh mesh = GreedyMesher.BuildScene(scene);
            new GltfExporter(binary: true).Export(mesh, scene.Palette, path, new ExportOptions());

            ModelRoot model = ModelRoot.Load(path);
            Assert.Equal(2, model.LogicalMaterials.Count);
            Assert.Contains(model.LogicalMaterials, m => m.Alpha == AlphaMode.BLEND);
            Assert.Contains(model.LogicalMaterials, m => m.Alpha == AlphaMode.OPAQUE);
            Assert.All(model.LogicalMaterials, m => Assert.NotNull(m.FindChannel("Emissive")?.Texture));
            Assert.All(model.LogicalMaterials, m => Assert.NotNull(m.FindChannel("MetallicRoughness")?.Texture));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WithoutMaterialsGltfIsAsItAlwaysWas()
    {
        var scene = VoxelScene.CreateStarter();
        string directory = TempDirectory();
        string path = Path.Combine(directory, "plain.glb");
        try
        {
            new GltfExporter(binary: true).Export(GreedyMesher.BuildScene(scene), scene.Palette, path, new ExportOptions());

            ModelRoot model = ModelRoot.Load(path);
            Material only = Assert.Single(model.LogicalMaterials);
            Assert.Equal(AlphaMode.OPAQUE, only.Alpha);
            Assert.Null(only.FindChannel("Emissive")?.Texture);
            Assert.Single(model.LogicalImages);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
