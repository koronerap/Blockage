namespace EditorApp.Core.Commands;

/// <summary>
/// Several steps kept as one: what an operation on a whole selection records, so that one Ctrl+Z
/// takes all of it back. Each step has already been done when it is handed over; undo takes them
/// back last first, so each finds the level as it left it.
/// </summary>
public sealed class CompositeCommand(string name, IReadOnlyList<ICommand> steps) : ICommand
{
    public string Name { get; } = name;

    public IReadOnlyList<ICommand> Steps => steps;

    public int RetainedCells { get; } = steps.Sum(step => step.RetainedCells);

    public void Redo()
    {
        foreach (ICommand step in steps)
        {
            step.Redo();
        }
    }

    public void Undo()
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            steps[i].Undo();
        }
    }

    /// <summary>The one step itself when there is only one, so the history names it as it is.</summary>
    public static ICommand Of(string name, IReadOnlyList<ICommand> steps) =>
        steps.Count == 1 ? steps[0] : new CompositeCommand(name, steps);
}
