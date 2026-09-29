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
    /// <summary>What a whole-object copy or cut takes, in words: the one name, or how many.</summary>
    private static string What(EditorSession session) =>
        session.SelectedObjects.Take(2).ToList() switch
        {
            [var only] => only.Name,
            _ => $"{session.SelectedObjects.Count()} objects",
        };

    public static void Copy(EditorSession session, ReportLog reports)
    {
        bool region = session.CopiesSelection;
        string what = What(session);
        int copied = session.Copy();

        if (copied == 0)
        {
            reports.Post("Nothing to copy - select something first.", ReportKind.Warning);
            return;
        }

        reports.Post(region
            ? $"Copied {copied:N0} voxels behind the selection."
            : $"Copied all of {what}, {copied:N0} voxels.");
    }

    public static void Cut(EditorSession session, ReportLog reports)
    {
        if (session.CopiesSelection && session.Scene.Focus is { Locked: true } locked)
        {
            reports.Post($"{locked.Name} is locked - unlock it in the Outliner to cut from it.", ReportKind.Warning);
            return;
        }

        bool region = session.CopiesSelection;
        string what = What(session);
        int cut = session.Cut();

        if (cut == 0)
        {
            reports.Post("Nothing to cut - select something first.", ReportKind.Warning);
        }
        else
        {
            reports.Post(region ? $"Cut {cut:N0} voxels." : $"Cut {what}, {cut:N0} voxels.");
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
            int pieces = session.ClipboardPieces.Count;
            reports.Post(pieces == 1 ? $"Pasted as {pasted.Name} - drag it where it goes." : $"Pasted {pieces} objects - drag them where they go.");
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
