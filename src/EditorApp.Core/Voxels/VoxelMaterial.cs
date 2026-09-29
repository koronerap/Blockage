namespace EditorApp.Core.Voxels;

/// <summary>
/// What a palette colour is made of besides its colour (Fullreleaseplan 4.1): how much it glows,
/// how metallic and how smooth it is, and how much light goes through it. Belongs to the palette
/// entry, as MagicaVoxel's materials do — painting a colour paints its material too — and goes to the
/// game as glTF's metallic-roughness material.
///
/// Stored so that all-zero is the plain material — matte, opaque, dull — which is what every entry
/// starts as and what an array of them is before anything is set. The settings people think in,
/// roughness and opacity, are the other way round, and are read off it.
/// </summary>
/// <param name="Emission">How much it glows, 0 to 1: light of its own colour, whatever lights it.</param>
/// <param name="Metallic">0 a plain surface, 1 a metal: reflections tinted by its colour.</param>
/// <param name="Smoothness">1 − roughness: 0 matte, 1 a mirror-sharp highlight.</param>
/// <param name="Transparency">1 − opacity: 0 solid, towards 1 glass.</param>
public readonly record struct VoxelMaterial(float Emission, float Metallic, float Smoothness, float Transparency)
{
    /// <summary>Glass goes no clearer than this: a face that vanished entirely could not be found again.</summary>
    public const float MaxTransparency = 0.95f;

    public static VoxelMaterial Plain => default;

    public float Roughness => 1f - Smoothness;

    public float Opacity => 1f - Transparency;

    public bool IsPlain => this == Plain;

    public bool IsTransparent => Transparency > 0.001f;

    /// <summary>One made from the settings people think in.</summary>
    public static VoxelMaterial Of(float emission, float metallic, float roughness, float opacity) =>
        new VoxelMaterial(emission, metallic, 1f - roughness, 1f - opacity).Clamped();

    public VoxelMaterial Clamped() => new(
        Clamp01(Emission),
        Clamp01(Metallic),
        Clamp01(Smoothness),
        Math.Clamp(float.IsFinite(Transparency) ? Transparency : 0f, 0f, MaxTransparency));

    private static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
}
