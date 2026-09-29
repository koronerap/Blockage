using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>
/// What each palette entry puts into each of glTF's material textures (Fullreleaseplan 4.1): the
/// colour with its opacity in alpha, the metallic-roughness pair where glTF looks for them — roughness
/// in green, metal in blue — and the glow. Laid out the way the colour is, palette sheet or atlas, so
/// one set of UVs finds all three.
/// </summary>
public static class MaterialTextures
{
    /// <summary>The colour, and how much of it light cannot pass: alpha is the opacity.</summary>
    public static Color32 BaseColour(Palette palette, int index)
    {
        Color32 colour = palette[index];
        return colour with { A = (byte)MathF.Round(colour.A * palette.Material(index).Opacity) };
    }

    public static Color32 MetallicRoughness(Palette palette, int index)
    {
        VoxelMaterial material = palette.Material(index);
        return new Color32(0, (byte)MathF.Round(material.Roughness * 255f), (byte)MathF.Round(material.Metallic * 255f));
    }

    public static Color32 Emissive(Palette palette, int index)
    {
        Color32 colour = palette[index];
        float glow = palette.Material(index).Emission;
        return new Color32((byte)MathF.Round(colour.R * glow), (byte)MathF.Round(colour.G * glow), (byte)MathF.Round(colour.B * glow));
    }
}
