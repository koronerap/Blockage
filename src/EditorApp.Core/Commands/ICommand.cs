namespace EditorApp.Core.Commands;

/// <summary>
/// A reversible edit (EditorApp.md, "Undo/redo"). Commands carry the before/after state of
/// everything they touched, and report how many cells that costs so the undo stack can be trimmed
/// by memory rather than by operation count.
///
/// A command also carries <i>what</i> it edited. Once a level is several objects, undo has to reach
/// the object the edit was made on — not whichever one happens to hold focus when Ctrl+Z is pressed.
/// </summary>
public interface ICommand
{
    /// <summary>Shown in the UI next to Undo/Redo.</summary>
    string Name { get; }

    /// <summary>How many cells of state this command holds onto.</summary>
    int RetainedCells { get; }

    void Redo();

    void Undo();
}
