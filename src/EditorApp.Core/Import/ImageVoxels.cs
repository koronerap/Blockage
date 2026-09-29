using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Import;

/// <summary>
/// Images made into voxels (Fullreleaseplan 8.2): a pixel-art sprite stood up with a thickness, and a
/// heightmap raised into terrain. Each comes as a small level with its own palette, for the level it
/// goes into to find its colours in, as a prop's are found.
/// </summary>
public static class ImageVoxels
{
    public const int MaxThickness = 64;

    public const int MaxHeight = 256;

    public const int MaxWidth = 1024;

    /// <summary>
    /// A sprite standing up, facing the front: each pixel more solid than <paramref name="alphaCutoff"/>
    /// a column <paramref name="thickness"/> voxels deep, in its own colour — the image's top row at the
    /// top, its bottom row on the ground, its middle on the spot it is put.
    /// </summary>
    public static VoxelScene Sprite(DecodedImage image, int thickness, byte alphaCutoff = 128, string name = "Sprite")
    {
        thickness = Math.Clamp(thickness, 1, MaxThickness);
        var palette = new Palette();
        var slots = new Dictionary<Color32, byte>();
        var grid = new VoxelWorld();
        int left = image.Width / 2;
        int front = thickness / 2;

        for (int v = 0; v < image.Height; v++)
        {
            for (int u = 0; u < image.Width; u++)
            {
                Color32 pixel = image[u, v];
                if (pixel.A < Math.Max(alphaCutoff, (byte)1))
                {
                    continue;
                }

                byte colour = SlotFor(pixel with { A = 255 }, palette, slots);
                int y = image.Height - 1 - v;
                for (int z = 0; z < thickness; z++)
                {
                    grid.SetVoxel(u - left, y, front - z, colour);
                }
            }
        }

        if (grid.SolidCount == 0)
        {
            throw new InvalidDataException("Every pixel of the image is see-through: there is nothing to make voxels of.");
        }

        return SceneOf(grid, palette, name);
    }

    /// <summary>
    /// Terrain from a heightmap: each pixel a column as high as it is bright — black one voxel, white
    /// <paramref name="height"/> — and <paramref name="width"/> columns across, the image's top row at
    /// the back. Dressed as the terrain generator dresses its own, with water up to <paramref name="water"/>.
    /// </summary>
    public static VoxelScene Heightmap(DecodedImage image, int width, int height, int water = 0, string name = "Terrain")
    {
        width = Math.Clamp(width, 1, MaxWidth);
        height = Math.Clamp(height, 1, MaxHeight);
        water = Math.Clamp(water, 0, height);
        int depth = Math.Clamp((int)MathF.Round(width * image.Height / (float)image.Width), 1, MaxWidth);

        Palette palette = Palette.CreateDefault();
        var colours = new Generators.Colours(palette);
        var cells = new Dictionary<Int3, byte>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                // The pixel under the middle of the column.
                int u = Math.Min(image.Width - 1, (int)((x + 0.5f) * image.Width / width));
                int v = Math.Min(image.Height - 1, (int)((z + 0.5f) * image.Height / depth));
                int top = 1 + (int)MathF.Round(Brightness(image[u, v]) * (height - 1));
                Generators.Ground(cells, colours, x - (width / 2), z - (depth / 2), top, height, water, seed: 0);
            }
        }

        var grid = new VoxelWorld();
        foreach ((Int3 cell, byte colour) in cells)
        {
            grid.SetVoxel(cell, colour);
        }

        return SceneOf(grid, palette, name);
    }

    /// <summary>How bright a pixel looks, 0 to 1, green counting most as it does to the eye.</summary>
    public static float Brightness(Color32 pixel) =>
        ((0.2126f * pixel.R) + (0.7152f * pixel.G) + (0.0722f * pixel.B)) / 255f;

    /// <summary>The sprite's own palette slot for a colour: a new one while there are slots, else the nearest already in.</summary>
    private static byte SlotFor(Color32 colour, Palette palette, Dictionary<Color32, byte> slots)
    {
        if (slots.TryGetValue(colour, out byte found))
        {
            return found;
        }

        if (slots.Count >= Palette.Size - 1)
        {
            return palette.Nearest(colour);
        }

        byte slot = (byte)(slots.Count + 1);
        palette[slot] = colour;
        slots[colour] = slot;
        return slot;
    }

    private static VoxelScene SceneOf(VoxelWorld grid, Palette palette, string name)
    {
        var scene = new VoxelScene();
        scene.ReplacePalette(palette);
        scene.Add(grid, ObjectTransform.Identity, name);
        return scene;
    }
}
