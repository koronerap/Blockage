using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>An object's marker and custom properties changed, as they were before and after.</summary>
public sealed class ObjectDataCommand(
    VoxelObject target,
    ObjectMarker? markerBefore,
    IReadOnlyList<CustomProperty> propertiesBefore,
    ObjectMarker? markerAfter,
    IReadOnlyList<CustomProperty> propertiesAfter,
    string name) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo()
    {
        target.Marker = markerAfter;
        target.Properties = propertiesAfter;
    }

    public void Undo()
    {
        target.Marker = markerBefore;
        target.Properties = propertiesBefore;
    }
}
