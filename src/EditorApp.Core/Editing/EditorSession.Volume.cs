using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>Booleans, volume filters and resampling on the selection (Fullreleaseplan 3.4–3.6).</summary>
public sealed partial class EditorSession
{
    /// <summary>Whether a boolean leaves the objects it used in the level; by default they go, as a join's do.</summary>
    public bool BooleanKeepsOthers { get; set; }

    /// <summary>How thick Hollow leaves the walls, in voxels.</summary>
    public int HollowThickness
    {
        get => _hollowThickness;
        set => _hollowThickness = Math.Clamp(value, 1, 64);
    }

    private int _hollowThickness = 1;

    /// <summary>Pieces smaller than this many voxels are what Remove Loose Pieces clears.</summary>
    public int LooseMinimum
    {
        get => _looseMinimum;
        set => _looseMinimum = Math.Clamp(value, 1, 1_000_000);
    }

    private int _looseMinimum = 8;

    /// <summary>What Scale multiplies a model's size in voxels by.</summary>
    public float ScaleFactor
    {
        get => _scaleFactor;
        set => _scaleFactor = float.IsFinite(value) ? Math.Clamp(value, 0.1f, 8f) : _scaleFactor;
    }

    private float _scaleFactor = 2f;

    /// <summary>
    /// A boolean of the other selected objects into the active one: their volume added, cut out,
    /// or all that is outside them taken away. The others go unless <see cref="BooleanKeepsOthers"/>.
    /// One undo step; how many objects were used, and in <paramref name="problem"/> why when none.
    /// </summary>
    public int BooleanSelected(BooleanOperation operation, out string? problem)
    {
        problem = null;
        if (SelectedLightId != 0 || Scene.Focus is not { Locked: false } target)
        {
            problem = "The active one has to be an unlocked object - click the one to change last.";
            return 0;
        }

        List<VoxelObject> operands = [.. Scene.SelectedObjects.Where(o => o.Id != target.Id && !o.IsEmpty)];
        if (operands.Count == 0)
        {
            problem = "Select the objects to use, then the one to change last.";
            return 0;
        }

        ExitEditMode();
        EndStroke();
        CancelExtrude();
        Selection = null;

        string name = operation switch
        {
            BooleanOperation.Union => $"Union into {target.Name}",
            BooleanOperation.Difference => $"Cut from {target.Name}",
            _ => $"Intersect {target.Name}",
        };

        var edit = new VoxelEditCommand(name, target.Grid);
        foreach (VoxelObject operand in operands)
        {
            VolumeOperations.Boolean(operation, target, operand, edit);
        }

        var steps = new List<ICommand> { edit };
        if (!BooleanKeepsOthers)
        {
            foreach (VoxelObject operand in operands)
            {
                var delete = new DeleteObjectCommand(Scene, operand);
                delete.Redo();
                steps.Add(delete);
            }
        }

        if (steps.Count == 1 && edit.IsEmpty)
        {
            problem = "It changed nothing.";
            return 0;
        }

        Scene.SetFocus(target.Id);
        History.Push(new CompositeCommand(name, steps));
        HasUnsavedChanges = true;
        return operands.Count;
    }

    /// <summary>What the volume filters work on: the object in Edit Mode, else every selected object.</summary>
    private List<VoxelObject> FilterTargets() =>
        EditObject is { } edited ? [edited] : [.. Scene.SelectedObjects.Where(o => !o.Locked && !o.IsEmpty)];

    /// <summary>A filter run over each of <see cref="FilterTargets"/>, as one undo step. How many it changed.</summary>
    private int FilterSelected(string name, Func<VoxelEditCommand, int> filter)
    {
        List<VoxelObject> targets = FilterTargets();
        if (targets.Count == 0)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();
        CancelVoxelTransform();

        var steps = new List<ICommand>();
        foreach (VoxelObject target in targets)
        {
            var command = new VoxelEditCommand(name, target.Grid);
            filter(command);
            if (!command.IsEmpty)
            {
                steps.Add(command);
            }
        }

        if (steps.Count == 0)
        {
            return 0;
        }

        Selection = null;
        History.Push(CompositeCommand.Of(targets.Count == 1 ? $"{name} {targets[0].Name}" : $"{name} {targets.Count} objects", steps));
        KeepEditModeHonest();
        HasUnsavedChanges = true;
        return steps.Count;
    }

    public int HollowSelected() => FilterSelected("Hollow", command => VolumeOperations.Hollow(HollowThickness, command));

    public int ThickenSelected() => FilterSelected("Thicken", VolumeOperations.Thicken);

    public int ThinSelected() => FilterSelected("Thin", VolumeOperations.Thin);

    public int SmoothSelected() => FilterSelected("Smooth", VolumeOperations.Smooth);

    public int RemoveLooseSelected() => FilterSelected("Remove loose pieces", command => VolumeOperations.RemoveLoose(LooseMinimum, command));

    /// <summary>Why an object cannot be resampled by this much, or null when it can.</summary>
    public static string? ResampleProblem(VoxelObject target, float scale, float voxelSize)
    {
        if (target.IsEmpty)
        {
            return "There are no voxels to resample.";
        }

        if (voxelSize > ObjectTransform.MaxVoxelSize || voxelSize < ObjectTransform.MinVoxelSize)
        {
            return "Its voxels would be too big or too small.";
        }

        long after = (long)(target.Grid.SolidCount * (double)scale * scale * scale);
        return after > Subdivide.MaxVoxels ? $"It would hold about {after:N0} voxels, more than an object can." : null;
    }

    /// <summary>A resampling of each selected object that can take it, as one undo step.</summary>
    private int ResampleSelected(string name, float scale, Func<VoxelObject, (VoxelWorld Grid, ObjectTransform Transform)> resample)
    {
        List<VoxelObject> targets = [.. FilterTargets().Where(o => ResampleProblem(o, scale, ResampledSize(o, name)) is null)];
        if (targets.Count == 0)
        {
            return 0;
        }

        EndStroke();
        CancelExtrude();
        CancelVoxelTransform();
        VoxelSelection.Clear();
        Selection = null;

        var steps = new List<ICommand>();
        foreach (VoxelObject target in targets)
        {
            (VoxelWorld grid, ObjectTransform transform) = resample(target);
            var command = new ReplaceGridCommand(target, grid, transform, $"{name} {target.Name}");
            command.Redo();
            steps.Add(command);
        }

        History.Push(CompositeCommand.Of(targets.Count == 1 ? $"{name} {targets[0].Name}" : $"{name} {targets.Count} objects", steps));
        HasUnsavedChanges = true;
        return steps.Count;
    }

    private static float ResampledSize(VoxelObject target, string name) =>
        name == HalveName ? target.VoxelSize * 2f : target.VoxelSize;

    private const string HalveName = "Halve";

    /// <summary>
    /// Subdivide's reverse, on every selected object: half the voxels across, each twice the size,
    /// the object where it was and as big.
    /// </summary>
    public int HalveSelected() =>
        ResampleSelected(HalveName, 0.5f, o => (VolumeOperations.Halve(o.Grid), o.Transform with { VoxelSize = o.VoxelSize * 2f }));

    /// <summary>Every selected model <see cref="ScaleFactor"/> times the size, in voxels of the size they are.</summary>
    public int ScaleSelected() =>
        ResampleSelected($"Scale x{ScaleFactor:0.##}", ScaleFactor, o => (VolumeOperations.Scale(o.Grid, ScaleFactor), o.Transform));
}
