using EditorApp.Core.Voxels;

namespace EditorApp;

/// <summary>
/// Placeholder content so the editor opens with something to look at and navigate. Replaced by
/// real project loading at M3.
/// </summary>
public static class DemoScene
{
    public static void Fill(VoxelWorld world, int halfExtent = 48)
    {
        for (int x = -halfExtent; x < halfExtent; x++)
        {
            for (int z = -halfExtent; z < halfExtent; z++)
            {
                int height = 4
                    + (int)(6f * Wave(x * 0.06f, z * 0.06f))
                    + (int)(3f * Wave(x * 0.17f + 11f, z * 0.17f - 7f));

                for (int y = 0; y <= height; y++)
                {
                    world.SetVoxel(x, y, z, ColorForHeight(y, height));
                }
            }
        }
    }

    private static float Wave(float x, float z) =>
        (MathF.Sin(x) * MathF.Cos(z) + MathF.Sin(x * 1.7f + z * 0.9f)) * 0.5f + 1f;

    // Palette layout from Palette.CreateDefault: 1..15 grayscale, 16.. hue ramps.
    private static byte ColorForHeight(int y, int height)
    {
        if (y == height)
        {
            return 96;   // green band
        }

        return y > height - 3 ? (byte)78 : (byte)8;
    }
}
