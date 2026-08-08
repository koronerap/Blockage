using EditorApp.Rendering;

namespace EditorApp.Ui;

/// <summary>
/// What the View menu can do. The commands live in the application, which owns the camera; this is
/// only the wiring, so the menu does not need to know about cameras or scenes.
/// </summary>
public sealed class ViewActions
{
    public required Action FrameLevel { get; init; }

    public required Action FrameFocused { get; init; }

    /// <summary>Points the camera at the origin without changing where it stands.</summary>
    public required Action LookAtCenter { get; init; }

    /// <summary>Back to the opening view.</summary>
    public required Action ResetCamera { get; init; }

    public required Func<bool> GridVisible { get; init; }

    public required Action ToggleGrid { get; init; }

    public required Func<bool> MeasurementsVisible { get; init; }

    public required Action ToggleMeasurements { get; init; }

    /// <summary>
    /// How the viewport shades the level. Handed over as the object rather than as get/set pairs:
    /// it is a small bag of settings that several controls edit directly, and wrapping each field
    /// in a delegate would only add a layer to step through.
    /// </summary>
    public required SceneLighting Lighting { get; init; }
}
