using EditorApp.Core.Voxels;

namespace EditorApp.Mobile.Tools;

/// <summary>
/// Everything the palette page shows, read out of the session in one go while the scene lock is
/// held. A snapshot, for the same reason the other page states are.
/// </summary>
/// <param name="Saved">The custom slots that were kept, in the order they were kept.</param>
/// <param name="ActiveIsSavedSwatch">
/// Whether the active colour is one of those. Only a saved swatch can be given back.
/// </param>
/// <param name="ActiveIsWorking">
/// Whether the active colour is the picker's working slot — chosen but not yet kept, which is what
/// makes "Save as swatch" mean anything.
/// </param>
public readonly record struct PaletteState(
    Color32[] Colors,
    byte ActiveIndex,
    int[] Saved,
    bool ActiveIsSavedSwatch,
    bool ActiveIsWorking,
    int FreeSlots)
{
    public Color32 Active => Colors[ActiveIndex];
}
