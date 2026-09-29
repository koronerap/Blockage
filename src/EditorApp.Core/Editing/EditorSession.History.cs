namespace EditorApp.Core.Editing;

/// <summary>The undo history as a list to go back and forth in (Fullreleaseplan 7.6).</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// Undoes or redoes until <paramref name="done"/> steps are done — 0 for the level as it was
    /// before any of them — one step at a time, as Ctrl+Z and Ctrl+Shift+Z would. Returns how many
    /// steps it went.
    /// </summary>
    public int GoToHistory(int done)
    {
        done = Math.Clamp(done, 0, History.UndoCount + History.RedoCount);
        int steps = 0;
        while (History.UndoCount > done && Undo())
        {
            steps++;
        }

        while (History.UndoCount < done && Redo())
        {
            steps++;
        }

        return steps;
    }
}
