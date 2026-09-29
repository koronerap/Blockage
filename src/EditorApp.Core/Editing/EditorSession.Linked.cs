using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Editing;

/// <summary>
/// Linked copies (Fullreleaseplan 6.2, Blender's Alt+D): copies that share one grid of voxels, so an
/// edit to any of them is an edit to all — a prop repeated down a street, changed once. Each keeps
/// its own place, name, collection and modifiers; Make Single User gives one voxels of its own.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>How many objects share this one's voxels, itself included: 1 for an object on its own.</summary>
    public int UsersOf(VoxelObject o) => Scene.Objects.Count(other => ReferenceEquals(other.Grid, o.Grid));

    /// <summary>Everything selected, copied beside where it is as linked copies, and the copies selected. One undo step.</summary>
    public IReadOnlyList<IPlaceable> DuplicateSelectedLinked(Vector3 towards) => DuplicateSelected(towards, linked: true);

    /// <summary>Gives a linked copy voxels of its own. One undo step; false for an object that is on its own already.</summary>
    public bool MakeSingleUser(int objectId)
    {
        if (Scene.Find(objectId) is not { } target || UsersOf(target) < 2)
        {
            return false;
        }

        EndStroke();
        CancelExtrude();
        Selection = null;

        var command = new MakeSingleUserCommand(target);
        command.Redo();
        History.Push(command);
        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>
    /// Before an edit that must reach one object alone — joining into it, applying its modifiers —
    /// a linked copy is given voxels of its own; the command that did it, for the edit's undo step,
    /// or null when it had them already.
    /// </summary>
    private ICommand? SingleUserFirst(VoxelObject target)
    {
        if (UsersOf(target) < 2)
        {
            return null;
        }

        var command = new MakeSingleUserCommand(target);
        command.Redo();
        return command;
    }
}
