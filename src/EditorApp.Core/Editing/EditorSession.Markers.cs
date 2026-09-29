using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Markers (Fullreleaseplan 6.4) — objects with no voxels, marking a place for the game: a spawn, a
/// trigger box, a sound — and custom properties (6.5) on any object. Each change is an undo step; a
/// dragged or typed value is one when it is let go.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>A marker of <paramref name="kind"/> standing at <paramref name="point"/>, selected. One undo step.</summary>
    public VoxelObject AddMarker(MarkerKind kind, Vector3 point)
    {
        ExitEditMode();
        EndStroke();
        CancelExtrude();
        Selection = null;

        string name = ObjectMarker.NameOf(kind);
        if (Scene.Objects.Any(o => o.Name == name))
        {
            name = DuplicateName(name, Scene.Objects.Select(o => o.Name));
        }

        Vector3 snapped = new(MathF.Round(point.X), MathF.Round(point.Y), MathF.Round(point.Z));
        var command = new CreateObjectCommand(Scene, new VoxelWorld(), ObjectTransform.At(snapped), name, $"Add {name}");
        command.Redo();
        VoxelObject marker = command.Created!;
        marker.Marker = ObjectMarker.Default(kind);

        History.Push(command);
        SelectOnly([marker.Id]);
        HasUnsavedChanges = true;
        return marker;
    }

    /// <summary>A marker's settings changed as a slider is dragged; <see cref="PushObjectDataEdit"/> records the drag once let go.</summary>
    public void SetMarkerLive(VoxelObject target, ObjectMarker? marker)
    {
        target.Marker = marker?.Clamped();
        HasUnsavedChanges = true;
    }

    /// <summary>An object's custom properties changed as they are typed; <see cref="PushObjectDataEdit"/> records the edit once done.</summary>
    public void SetPropertiesLive(VoxelObject target, IReadOnlyList<CustomProperty> properties)
    {
        target.Properties = [.. properties.Select(p => p.Clamped())];
        HasUnsavedChanges = true;
    }

    /// <summary>Records a finished edit of an object's marker and properties, from how they were, as one undo step.</summary>
    public bool PushObjectDataEdit(VoxelObject target, ObjectMarker? markerBefore, IReadOnlyList<CustomProperty> propertiesBefore, string name)
    {
        if (target.Marker == markerBefore && target.Properties.SequenceEqual(propertiesBefore))
        {
            return false;
        }

        History.Push(new ObjectDataCommand(target, markerBefore, propertiesBefore, target.Marker, target.Properties, name));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>An object's properties set outright — one added, taken away or changed in kind. One undo step.</summary>
    public bool SetProperties(int objectId, IReadOnlyList<CustomProperty> properties, string name)
    {
        if (Scene.Find(objectId) is not { } target)
        {
            return false;
        }

        IReadOnlyList<CustomProperty> before = target.Properties;
        SetPropertiesLive(target, properties);
        return PushObjectDataEdit(target, target.Marker, before, name);
    }
}
