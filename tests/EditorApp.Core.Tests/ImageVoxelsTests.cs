using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Import;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Images made into voxels (Fullreleaseplan 8.2): a sprite stood up with a thickness, a heightmap raised into terrain.</summary>
public class ImageVoxelsTests
{
    private static readonly Color32 Clear = new(0, 0, 0, 0);
    private static readonly Color32 Red = new(220, 40, 40);
    private static readonly Color32 Gold = new(240, 200, 60);

    /// <summary>An image from its rows, top row first.</summary>
    private static DecodedImage Image(params Color32[][] rows) =>
        new(rows[0].Length, rows.Length, [.. rows.SelectMany(row => row)]);

    private static VoxelWorld GridOf(VoxelScene scene) => scene.Objects.Single().Grid;

    // ---- Sprites ------------------------------------------------------------------------------------

    [Fact]
    public void ASpriteStandsUpWithItsTopRowAtTheTop()
    {
        // Gold over red, and a hole in the bottom right.
        DecodedImage image = Image(
            [Gold, Gold, Gold],
            [Red, Red, Clear]);

        VoxelScene sprite = ImageVoxels.Sprite(image, thickness: 1);
        VoxelWorld grid = GridOf(sprite);

        Assert.Equal(5, grid.SolidCount);
        Assert.Equal(Gold, sprite.Palette[grid.GetVoxel(-1, 1, 0)]);
        Assert.Equal(Red, sprite.Palette[grid.GetVoxel(-1, 0, 0)]);
        Assert.False(grid.IsSolid(new Int3(1, 0, 0)));
        Assert.True(grid.IsSolid(new Int3(1, 1, 0)));
    }

    [Fact]
    public void EachPixelGoesAsDeepAsTheThickness()
    {
        VoxelScene sprite = ImageVoxels.Sprite(Image([Red, Gold]), thickness: 4);
        VoxelWorld grid = GridOf(sprite);

        Assert.Equal(8, grid.SolidCount);
        Assert.True(grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(4, max.Z - min.Z + 1);
    }

    [Fact]
    public void PixelsMoreSeeThroughThanTheCutoffAreLeftOut()
    {
        DecodedImage image = Image([Red, Red with { A = 100 }, Red with { A = 200 }]);

        Assert.Equal(2, GridOf(ImageVoxels.Sprite(image, 1, alphaCutoff: 128)).SolidCount);
        Assert.Equal(3, GridOf(ImageVoxels.Sprite(image, 1, alphaCutoff: 50)).SolidCount);
    }

    [Fact]
    public void AnImageWithNothingSolidIsRefused() =>
        Assert.Throws<InvalidDataException>(() => ImageVoxels.Sprite(Image([Clear, Clear]), 1));

    /// <summary>Set down in a level, a sprite's colours are the colours it had.</summary>
    [Fact]
    public void ASpritesColoursAreFoundInTheLevel()
    {
        var session = new EditorSession();
        session.ReplaceScene(new VoxelScene(), projectPath: null);

        IReadOnlyList<VoxelObject> placed = session.PlaceProp(ImageVoxels.Sprite(Image([Red, Gold]), 1, name: "badge"), "badge", Vector3.Zero);

        VoxelObject badge = Assert.Single(placed);
        Assert.Equal("badge", badge.Name);
        List<Color32> colours = [.. new[] { -1, 0 }.Select(x => session.Scene.Palette[badge.Grid.GetVoxel(x, 0, 0)])];
        Assert.Equal([Red, Gold], colours);
    }

    // ---- Heightmaps ---------------------------------------------------------------------------------

    [Fact]
    public void ABrighterPixelStandsHigher()
    {
        var black = new Color32(0, 0, 0);
        var white = new Color32(255, 255, 255);

        VoxelWorld grid = GridOf(ImageVoxels.Heightmap(Image([black, white]), width: 2, height: 10));

        int ColumnHeight(int x)
        {
            int top = 0;
            for (int y = 0; y < 20; y++)
            {
                if (grid.IsSolid(new Int3(x, y, 0)))
                {
                    top = y + 1;
                }
            }

            return top;
        }

        Assert.Equal(1, ColumnHeight(-1));
        Assert.Equal(10, ColumnHeight(0));
    }

    [Fact]
    public void TheDepthFollowsTheImagesShape()
    {
        var grey = new Color32(128, 128, 128);
        DecodedImage tall = new(2, 4, [.. Enumerable.Repeat(grey, 8)]);

        VoxelWorld grid = GridOf(ImageVoxels.Heightmap(tall, width: 10, height: 4));

        Assert.True(grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(10, max.X - min.X + 1);
        Assert.Equal(20, max.Z - min.Z + 1);
    }

    [Fact]
    public void WaterFillsTheLowGround()
    {
        var black = new Color32(0, 0, 0);
        VoxelScene terrain = ImageVoxels.Heightmap(Image([black]), width: 1, height: 10, water: 4);
        VoxelWorld grid = GridOf(terrain);

        byte water = terrain.Palette.Nearest(new Color32(58, 110, 196));
        Assert.Equal(water, grid.GetVoxel(0, 3, 0));
        Assert.False(grid.IsSolid(new Int3(0, 4, 0)));
    }

    [Fact]
    public void BrightnessCountsGreenMost()
    {
        Assert.Equal(1f, ImageVoxels.Brightness(new Color32(255, 255, 255)), 3);
        Assert.Equal(0f, ImageVoxels.Brightness(new Color32(0, 0, 0)), 3);
        Assert.True(ImageVoxels.Brightness(new Color32(0, 255, 0)) > ImageVoxels.Brightness(new Color32(255, 0, 0)));
    }
}
