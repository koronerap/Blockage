using EditorApp.Core.Editing;

namespace EditorApp.Mobile.Tools;

/// <summary>
/// Everything the options bar needs to draw itself, read out of the session in one go.
///
/// A snapshot rather than a reference: the bar is built on the UI thread and the session belongs to
/// whichever thread holds the scene lock, so it is handed a copy taken while that lock was held.
/// </summary>
/// <param name="SelectedFaces">How many faces Extrude currently has, for the readout.</param>
/// <param name="HasCutPreview">Whether a cut plane is armed and waiting for a second tap.</param>
public readonly record struct ToolState(
    EditorTool Tool,
    TransformMode TransformMode,
    TransformSpace TransformSpace,
    bool ExtrudeCreatesObject,
    PaintMode PaintMode,
    float BrushRadius,
    int BucketThreshold,
    bool BucketWholeObject,
    int SelectedFaces,
    bool HasCutPreview);
