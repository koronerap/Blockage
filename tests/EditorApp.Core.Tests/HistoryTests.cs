using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The undo history as a list (Fullreleaseplan 7.6): a click on a step goes back or forward to it.</summary>
public class HistoryTests
{
    [Fact]
    public void GoingToAStepUndoesOrRedoesUpToIt()
    {
        var session = new EditorSession();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        session.ReplaceWorld(grid, projectPath: null);

        session.AddMarker(MarkerKind.Empty, Vector3.Zero);
        session.AddMarker(MarkerKind.Spawn, Vector3.One);
        session.AddMarker(MarkerKind.Sound, Vector3.UnitX * 4f);
        Assert.Equal(["Add Empty", "Add Spawn point", "Add Sound"], session.History.DoneNames.ToArray());

        Assert.Equal(3, session.GoToHistory(0));
        Assert.Single(session.Scene.Objects);
        Assert.Equal(["Add Empty", "Add Spawn point", "Add Sound"], session.History.UndoneNames.ToArray());

        Assert.Equal(2, session.GoToHistory(2));
        Assert.Equal(3, session.Scene.Objects.Count);
        Assert.Equal(["Add Sound"], session.History.UndoneNames.ToArray());

        Assert.Equal(0, session.GoToHistory(2));
    }
}
