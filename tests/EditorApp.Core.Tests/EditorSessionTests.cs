using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class EditorSessionTests
{
    private static EditorSession SessionWithVoxel(Int3 position, byte index = 5)
    {
        var session = new EditorSession();
        session.World.SetVoxel(position, index);
        return session;
    }

    [Fact]
    public void PlaceWritesInFrontOfThePickedFace()
    {
        EditorSession session = SessionWithVoxel(new Int3(0, 0, 0));
        session.ActiveTool = EditorTool.Place;
        session.ActiveColorIndex = 12;

        var hit = new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f);
        Assert.True(session.ApplyTool(hit));
        session.EndStroke();

        Assert.Equal(12, session.World.GetVoxel(0, 1, 0));
        Assert.Equal(5, session.World.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void EraseRemovesThePickedVoxel()
    {
        EditorSession session = SessionWithVoxel(new Int3(2, 3, 4));
        session.ActiveTool = EditorTool.Erase;

        Assert.True(session.ApplyTool(new RaycastHit(new Int3(2, 3, 4), Face.PosX, 1f)));
        session.EndStroke();

        Assert.False(session.World.IsSolid(2, 3, 4));
    }

    [Fact]
    public void PaintRecolorsWithoutChangingShape()
    {
        EditorSession session = SessionWithVoxel(new Int3(0, 0, 0), 5);
        session.ActiveTool = EditorTool.Paint;
        session.ActiveColorIndex = 30;

        session.ApplyTool(new RaycastHit(new Int3(0, 0, 0), Face.PosZ, 1f));
        session.EndStroke();

        Assert.Equal(30, session.World.GetVoxel(0, 0, 0));
        Assert.Equal(1, session.World.SolidCount);
    }

    [Fact]
    public void PickAdoptsTheColorUnderTheCursor()
    {
        EditorSession session = SessionWithVoxel(new Int3(0, 0, 0), 77);
        session.ActiveTool = EditorTool.Pick;
        session.ActiveColorIndex = 1;

        session.ApplyTool(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f));

        Assert.Equal(77, session.ActiveColorIndex);
        // The eyedropper changes no voxels, so it must not create an undo step.
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void OneStrokeIsOneUndoStep()
    {
        var session = new EditorSession();
        session.ActiveTool = EditorTool.Place;
        session.ActiveColorIndex = 4;

        session.BeginStroke();
        for (int x = 0; x < 10; x++)
        {
            session.PlaceAt(new Int3(x, 0, 0));
        }

        session.EndStroke();

        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(10, session.World.SolidCount);

        session.Undo();
        Assert.Equal(0, session.World.SolidCount);
    }

    [Fact]
    public void BrushRadiusWritesACube()
    {
        var session = new EditorSession();
        session.ActiveColorIndex = 9;
        session.BrushRadius = 1;

        session.BeginStroke();
        session.PlaceAt(new Int3(0, 0, 0));
        session.EndStroke();

        Assert.Equal(27, session.World.SolidCount);
        Assert.Equal(9, session.World.GetVoxel(-1, -1, -1));
        Assert.Equal(9, session.World.GetVoxel(1, 1, 1));
        Assert.False(session.World.IsSolid(2, 0, 0));
    }

    [Fact]
    public void FillToolRecolorsTheConnectedRegionAsOneStep()
    {
        var session = new EditorSession();
        for (int x = 0; x < 6; x++)
        {
            session.World.SetVoxel(x, 0, 0, 3);
        }

        session.ActiveTool = EditorTool.Fill;
        session.ActiveColorIndex = 11;

        Assert.True(session.ApplyTool(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f)));
        session.EndStroke();

        Assert.Equal(1, session.History.UndoCount);
        for (int x = 0; x < 6; x++)
        {
            Assert.Equal(11, session.World.GetVoxel(x, 0, 0));
        }

        session.Undo();
        Assert.Equal(3, session.World.GetVoxel(0, 0, 0));
    }

    [Fact]
    public void EditingAcrossAChunkSeamDirtiesBothChunks()
    {
        var session = new EditorSession();
        session.World.SetVoxel(31, 0, 0, 1);
        session.World.SetVoxel(32, 0, 0, 1);
        session.World.ConsumeDirtyChunks();

        session.ActiveTool = EditorTool.Erase;
        session.ApplyTool(new RaycastHit(new Int3(31, 0, 0), Face.PosY, 1f));
        session.EndStroke();

        IReadOnlySet<ChunkCoord> dirty = session.World.DirtyChunks;
        Assert.Contains(new ChunkCoord(0, 0, 0), dirty);
        Assert.Contains(new ChunkCoord(1, 0, 0), dirty);
    }

    [Fact]
    public void ReplaceWorldResetsHistoryAndDirtyFlag()
    {
        var session = new EditorSession();
        session.BeginStroke();
        session.PlaceAt(new Int3(0, 0, 0));
        session.EndStroke();
        Assert.True(session.HasUnsavedChanges);

        var replacement = new VoxelWorld();
        replacement.SetVoxel(9, 9, 9, 2);
        session.ReplaceWorld(replacement, "C:/levels/demo.vxlevel");

        Assert.False(session.HasUnsavedChanges);
        Assert.False(session.History.CanUndo);
        Assert.Equal("demo", session.ProjectName);
        Assert.Equal(2, session.World.GetVoxel(9, 9, 9));
    }
}
