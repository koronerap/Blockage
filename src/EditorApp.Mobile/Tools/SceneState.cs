using System.Numerics;
using EditorApp.Rendering;

namespace EditorApp.Mobile.Tools;

/// <param name="Voxels">How many solid voxels this object holds, for the list.</param>
public readonly record struct ObjectState(int Id, string Name, bool Visible, bool Focused, int Voxels, bool Locked = false);

/// <summary>
/// Everything the level page shows, read out of the session in one go while the scene lock is held.
///
/// A snapshot rather than a reference, for the same reason <see cref="ToolState"/> is: the page is
/// built on the UI thread and the session belongs to whichever thread holds the lock.
/// </summary>
/// <param name="Lighting">A copy — the live one belongs to the renderer, on the GL thread.</param>
/// <param name="VoxelSize">The focused object's voxel size — each object has its own.</param>
/// <param name="Extent">The level's size in world units, or null when there is nothing in it.</param>
public readonly record struct SceneState(
    bool GridVisible,
    LightingState Lighting,
    float VoxelSize,
    Vector3? Extent,
    int SolidCount,
    IReadOnlyList<ObjectState> Objects);

/// <param name="Azimuth">Compass bearing of the light in degrees.</param>
/// <param name="Elevation">Height of the light in degrees: 90 is straight overhead.</param>
public readonly record struct LightingState(
    bool IsLit,
    float Azimuth,
    float Elevation,
    float Intensity,
    float Ambient)
{
    public static LightingState From(SceneLighting lighting) => new(
        lighting.IsLit,
        lighting.Azimuth,
        lighting.Elevation,
        lighting.Intensity,
        lighting.Ambient);
}
