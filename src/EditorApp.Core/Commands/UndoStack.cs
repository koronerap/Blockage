using EditorApp.Core.Voxels;

namespace EditorApp.Core.Commands;

/// <summary>
/// Undo history trimmed by total retained cells rather than command count (EditorApp.md §8). A
/// single large extrude can cost more memory than a hundred brush dabs, and a "keep the last 50
/// operations" policy cannot see that.
/// </summary>
public sealed class UndoStack
{
    private readonly List<ICommand> _undo = [];
    private readonly List<ICommand> _redo = [];

    /// <summary>Roughly 8M cells, about 64 MB of change records.</summary>
    public int CellBudget { get; set; } = 8_000_000;

    public int RetainedCells { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? NextUndoName => _undo.Count > 0 ? _undo[^1].Name : null;

    /// <summary>The step Undo would take back, or null — for asking whether anything has been done since a given one.</summary>
    public ICommand? LastDone => _undo.Count > 0 ? _undo[^1] : null;

    public string? NextRedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>What has been done, oldest first — what Undo takes back from the end of.</summary>
    public IEnumerable<string> DoneNames => _undo.Select(command => command.Name);

    /// <summary>What was undone and can be done again, the next one to redo first.</summary>
    public IEnumerable<string> UndoneNames => Enumerable.Reverse(_redo).Select(command => command.Name);

    /// <summary>
    /// Records a command that has already been applied to the world. Doing a new action discards
    /// the redo branch.
    /// </summary>
    public void Push(ICommand command)
    {
        if (command is VoxelEditCommand { IsEmpty: true })
        {
            return;
        }

        ClearRedo();

        _undo.Add(command);
        RetainedCells += command.RetainedCells;
        TrimToBudget();
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        ICommand command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();

        _redo.Add(command);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        ICommand command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Redo();

        _undo.Add(command);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        RetainedCells = 0;
    }

    private void ClearRedo()
    {
        // Redo entries are still counted in RetainedCells; drop their share as they go.
        foreach (ICommand command in _redo)
        {
            RetainedCells -= command.RetainedCells;
        }

        _redo.Clear();
        RetainedCells = Math.Max(RetainedCells, 0);
    }

    // Always keeps at least one command: a single edit bigger than the whole budget must still be
    // undoable, otherwise the user loses the action they just performed with no way back.
    private void TrimToBudget()
    {
        while (RetainedCells > CellBudget && _undo.Count > 1)
        {
            RetainedCells -= _undo[0].RetainedCells;
            _undo.RemoveAt(0);
        }
    }
}
