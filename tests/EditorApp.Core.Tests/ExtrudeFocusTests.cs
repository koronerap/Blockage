using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Focus follows the cursor, which is right until the cursor has somewhere to be. Reaching for the
/// extrude arrow means crossing whatever sits between it and the pointer, and a selection lost to an
/// object merely passed over makes the tool unusable in a crowded scene.
/// </summary>
public class ExtrudeFocusTests
{
    private static VoxelWorld Cube(int side)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        return grid;
    }

    private static (EditorSession Session, int First, int Second) TwoObjects()
    {
        var session = new EditorSession();
        var scene = new VoxelScene();

        VoxelObject first = scene.Add(Cube(3), ObjectTransform.Identity, "first");
        VoxelObject second = scene.Add(Cube(3), ObjectTransform.Identity, "second");
        session.ReplaceScene(scene, projectPath: null);

        return (session, first.Id, second.Id);
    }

    private static void SelectTopFace(EditorSession session) =>
        session.SelectPatch(new RaycastHit(new Int3(1, 2, 1), Face.PosY, 1f));

    [Fact]
    public void AHeldSelectionPinsFocus()
    {
        (EditorSession session, int first, int second) = TwoObjects();
        session.ActiveTool = EditorTool.Extrude;
        session.TryFocus(first);
        SelectTopFace(session);

        Assert.False(session.TryFocus(second));

        Assert.Equal(first, session.Scene.FocusId);
        Assert.True(session.HasSelection);
    }

    [Fact]
    public void ClearingTheSelectionLetsFocusMoveAgain()
    {
        // How a deliberate click gets through: ExtrudeInteraction.OnPress clears, then focuses.
        (EditorSession session, int first, int second) = TwoObjects();
        session.ActiveTool = EditorTool.Extrude;
        session.TryFocus(first);
        SelectTopFace(session);

        session.ClearSelection();

        Assert.True(session.TryFocus(second));
        Assert.Equal(second, session.Scene.FocusId);
    }

    [Fact]
    public void WithNothingSelectedFocusStillFollowsTheCursor()
    {
        (EditorSession session, int first, int second) = TwoObjects();
        session.ActiveTool = EditorTool.Extrude;
        session.TryFocus(first);

        Assert.True(session.TryFocus(second));
        Assert.Equal(second, session.Scene.FocusId);
    }

    [Theory]
    [InlineData(EditorTool.Paint)]
    [InlineData(EditorTool.Transform)]
    [InlineData(EditorTool.LoopCut)]
    public void OtherToolsAreNotPinnedByALeftoverSelection(EditorTool tool)
    {
        // The pin belongs to Extrude. A selection still lying around while painting must not stop
        // focus following the brush.
        (EditorSession session, int first, int second) = TwoObjects();
        session.ActiveTool = EditorTool.Extrude;
        session.TryFocus(first);
        SelectTopFace(session);

        session.ActiveTool = tool;

        Assert.True(session.TryFocus(second));
        Assert.Equal(second, session.Scene.FocusId);
    }

    [Fact]
    public void AFocusChangeStillDropsTheSelectionItLeavesBehind()
    {
        // A selection belongs to the object it was made on; once focus does move, it cannot survive.
        (EditorSession session, int first, int second) = TwoObjects();
        session.ActiveTool = EditorTool.Extrude;
        session.TryFocus(first);
        SelectTopFace(session);
        session.ClearSelection();
        session.TryFocus(second);

        Assert.False(session.HasSelection);
    }
}
