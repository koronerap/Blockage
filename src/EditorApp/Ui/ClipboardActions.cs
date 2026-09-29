using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;

namespace EditorApp.Ui;

/// <summary>
/// Copy, cut, paste and join, from the keys and the menus alike, each saying in the status bar what
/// it did — a copy has nothing to show for itself on screen, and without a word it looks as though
/// the key did nothing.
/// </summary>
public static class ClipboardActions
{
    public static void Copy(EditorSession session, ReportLog reports)
    {
        bool region = session.CopiesSelection;
        string? name = session.Scene.Focus?.Name;
        int copied = session.Copy();

        if (copied == 0)
        {
            reports.Post("Nothing to copy.", ReportKind.Warning);
            return;
        }

        reports.Post(region
            ? $"Copied {copied:N0} voxels behind the selection."
            : $"Copied all of {name}, {copied:N0} voxels.");
    }

    public static void Cut(EditorSession session, ReportLog reports)
    {
        if (session.Scene.Focus is { Locked: true } locked)
        {
            reports.Post($"{locked.Name} is locked - unlock it in the Outliner to cut from it.", ReportKind.Warning);
            return;
        }

        bool region = session.CopiesSelection;
        bool last = session.Scene.Objects.Count <= 1;
        string? name = session.Scene.Focus?.Name;
        int cut = session.Cut();

        if (cut == 0)
        {
            reports.Post("Nothing to cut.", ReportKind.Warning);
        }
        else if (!region && last)
        {
            reports.Post($"Copied {name} - the last object stays, so nothing was cut.", ReportKind.Warning);
        }
        else
        {
            reports.Post(region ? $"Cut {cut:N0} voxels." : $"Cut {name}, {cut:N0} voxels.");
        }
    }

    public static void Paste(EditorSession session, FlyCamera camera, ReportLog reports)
    {
        if (session.Clipboard is null)
        {
            reports.Post("Nothing to paste - copy something first.", ReportKind.Warning);
            return;
        }

        if (ObjectMenu.Paste(session, camera) is { } pasted)
        {
            reports.Post($"Pasted as {pasted.Name} - drag it where it goes.");
        }
    }

    public static void Join(EditorSession session, VoxelObject source, VoxelObject target, ReportLog reports)
    {
        if (session.JoinInto(source.Id, target.Id))
        {
            reports.Post($"Joined {source.Name} into {target.Name}.");
        }
        else
        {
            reports.Post(session.JoinProblem(source.Id, target.Id) ?? "Could not join.", ReportKind.Warning);
        }
    }
}
