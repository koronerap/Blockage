using System.Numerics;

namespace EditorApp.Core.Voxels;

/// <summary>An 8-bit-per-channel RGBA color.</summary>
public readonly record struct Color32(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Color32 Transparent = new(0, 0, 0, 0);
    public static readonly Color32 White = new(255, 255, 255);

    /// <summary>Packed as 0xAABBGGRR — the byte order OpenGL expects for a normalized RGBA attribute.</summary>
    public uint Rgba => (uint)(R | (G << 8) | (B << 16) | (A << 24));

    public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    public static Color32 FromVector4(Vector4 v) => new(
        (byte)Math.Clamp(MathF.Round(v.X * 255f), 0, 255),
        (byte)Math.Clamp(MathF.Round(v.Y * 255f), 0, 255),
        (byte)Math.Clamp(MathF.Round(v.Z * 255f), 0, 255),
        (byte)Math.Clamp(MathF.Round(v.W * 255f), 0, 255));

    /// <summary>Hue in [0,360), saturation and value in [0,1].</summary>
    public static Color32 FromHsv(float hue, float saturation, float value)
    {
        float c = value * saturation;
        float h = (hue % 360f + 360f) % 360f / 60f;
        float x = c * (1f - MathF.Abs(h % 2f - 1f));
        float m = value - c;

        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new Color32(
            (byte)MathF.Round((r + m) * 255f),
            (byte)MathF.Round((g + m) * 255f),
            (byte)MathF.Round((b + m) * 255f));
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
