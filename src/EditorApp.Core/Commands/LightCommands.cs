using EditorApp.Core.Scene;

namespace EditorApp.Core.Commands;

/// <summary>Puts a light into the level, reversibly — the very same light each time it is redone.</summary>
public sealed class AddLightCommand(VoxelScene scene, SceneLight light, string name) : ICommand
{
    private int _index = -1;
    private bool _wasSelected;

    public string Name { get; } = name;

    public int RetainedCells => 1;

    public SceneLight Light => light;

    public void Redo()
    {
        scene.RestoreLight(light, _index >= 0 ? _index : null);
        if (_wasSelected)
        {
            scene.Select(light.Id);
        }
    }

    public void Undo()
    {
        _index = scene.IndexOfLight(light.Id);
        _wasSelected = scene.IsSelected(light.Id);
        scene.RemoveLight(light.Id);
    }
}

/// <summary>Takes a light out of the level, reversibly, and puts it back where it was in the list.</summary>
public sealed class DeleteLightCommand(VoxelScene scene, SceneLight light) : ICommand
{
    private int _index = -1;

    private bool _wasSelected;

    public string Name => $"Delete {light.Name}";

    public int RetainedCells => 1;

    public void Redo()
    {
        _index = scene.IndexOfLight(light.Id);
        _wasSelected = scene.IsSelected(light.Id);
        scene.RemoveLight(light.Id);
    }

    public void Undo()
    {
        scene.RestoreLight(light, _index >= 0 ? _index : null);
        if (_wasSelected)
        {
            scene.Select(light.Id);
        }
    }
}

/// <summary>Any change to a light's settings, kept as the whole before and after.</summary>
public sealed class LightEditCommand(SceneLight light, LightState before, LightState after, string name) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo() => light.Apply(after);

    public void Undo() => light.Apply(before);
}
