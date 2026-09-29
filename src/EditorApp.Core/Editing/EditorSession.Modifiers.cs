using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>A change to an object's modifiers, kept as the whole list before and after.</summary>
public sealed class ModifierCommand(VoxelObject target, IReadOnlyList<VoxelModifier> before, IReadOnlyList<VoxelModifier> after, string name) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 1;

    public void Redo() => target.SetModifiers(after);

    public void Undo() => target.SetModifiers(before);
}

/// <summary>Non-destructive modifiers on objects (Fullreleaseplan 3.10).</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// A new modifier as it starts on an object: a mirror across the object's origin — so a half
    /// built on the positive side is completed — or a row of three, one object's width and a voxel
    /// apart along X.
    /// </summary>
    public static VoxelModifier NewModifier(ModifierKind kind, VoxelObject target)
    {
        if (kind == ModifierKind.Mirror)
        {
            return new VoxelModifier(ModifierKind.Mirror, Axis.X, Plane: 0);
        }

        int width = target.Grid.TryGetBounds(out Int3 min, out Int3 max) ? max.X - min.X + 1 : 1;
        return new VoxelModifier(ModifierKind.Array, Axis.X, Count: 3, Step: width + 1);
    }

    private bool ChangeModifiers(VoxelObject target, IReadOnlyList<VoxelModifier> after, string name)
    {
        IReadOnlyList<VoxelModifier> before = target.Modifiers;
        if (before.SequenceEqual(after))
        {
            return false;
        }

        var command = new ModifierCommand(target, before, after, name);
        command.Redo();
        History.Push(command);
        HasUnsavedChanges = true;
        return true;
    }

    public bool AddModifier(int objectId, ModifierKind kind) =>
        Scene.Find(objectId) is { } target
        && ChangeModifiers(target, [.. target.Modifiers, NewModifier(kind, target)], $"Add {kind} to {target.Name}");

    public bool RemoveModifier(int objectId, int index) =>
        Scene.Find(objectId) is { } target
        && index >= 0 && index < target.Modifiers.Count
        && ChangeModifiers(target, [.. target.Modifiers.Where((_, i) => i != index)], $"Remove {target.Modifiers[index].Label}");

    /// <summary>A modifier's settings changed live — a drag in Properties — without an undo step until <see cref="PushModifierEdit"/>.</summary>
    public void PreviewModifier(int objectId, int index, VoxelModifier changed)
    {
        if (Scene.Find(objectId) is not { } target || index < 0 || index >= target.Modifiers.Count)
        {
            return;
        }

        VoxelModifier[] list = [.. target.Modifiers];
        list[index] = changed.Clamped();
        if (!list.SequenceEqual(target.Modifiers))
        {
            target.SetModifiers(list);
            HasUnsavedChanges = true;
        }
    }

    /// <summary>Records a finished live edit of an object's modifiers as one undo step.</summary>
    public bool PushModifierEdit(int objectId, IReadOnlyList<VoxelModifier> before, string name)
    {
        if (Scene.Find(objectId) is not { } target || before.SequenceEqual(target.Modifiers))
        {
            return false;
        }

        History.Push(new ModifierCommand(target, before, target.Modifiers, name));
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Makes a modifier — and every one before it, which it works on — voxels for good: the object's
    /// voxels become what those modifiers show, and they leave the list. One undo step.
    /// </summary>
    public bool ApplyModifier(int objectId, int index)
    {
        if (Scene.Find(objectId) is not { Locked: false } target || index < 0 || index >= target.Modifiers.Count)
        {
            return false;
        }

        ExitEditMode();
        EndStroke();
        CancelExtrude();
        Selection = null;

        IReadOnlyList<VoxelModifier> before = target.Modifiers;
        VoxelWorld baked = new ModifierEvaluator(target.Grid, [.. before.Take(index + 1)]).Shown;

        // Applied to this object alone: a linked copy is given voxels of its own first.
        ICommand? single = SingleUserFirst(target);
        var grid = new ReplaceGridCommand(target, baked, target.Transform, "Apply modifier");
        grid.Redo();
        var list = new ModifierCommand(target, before, [.. before.Skip(index + 1)], "Apply modifier");
        list.Redo();

        List<ICommand> steps = single is null ? [grid, list] : [single, grid, list];
        History.Push(new CompositeCommand(index == 0 ? $"Apply {before[0].Label}" : $"Apply {index + 1} modifiers", steps));
        HasUnsavedChanges = true;
        return true;
    }
}
