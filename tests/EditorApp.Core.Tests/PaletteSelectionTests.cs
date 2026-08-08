using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class PaletteSelectionTests
{
    private static EditorSession SessionWithCube(byte index = Palette.WhiteIndex)
    {
        var session = new EditorSession();
        var scene = new VoxelScene();

        var grid = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                for (int z = 0; z < 3; z++)
                {
                    grid.SetVoxel(x, y, z, index);
                }
            }
        }

        scene.Add(grid, ObjectTransform.Identity, "cube");
        session.ReplaceScene(scene, projectPath: null);
        return session;
    }

    [Fact]
    public void TheLibraryStopsBeforeTheCustomRange()
    {
        Palette palette = Palette.CreateDefault();

        Assert.NotEqual(Color32.Transparent, palette[Palette.CustomStart - 1]);
        Assert.Equal(Palette.CustomCount, palette.FreeCustomSlots);

        for (int i = Palette.CustomStart; i < Palette.Size; i++)
        {
            Assert.True(palette.IsCustomSlotFree(i));
        }
    }

    [Fact]
    public void PickingAColourAlreadyInTheLibrarySelectsItAndChangesNothing()
    {
        EditorSession session = SessionWithCube();
        Palette palette = session.Scene.Palette;

        Color32 existing = palette[40];
        int freeBefore = palette.FreeCustomSlots;

        byte chosen = session.SelectColor(existing);

        Assert.Equal(40, chosen);
        Assert.Equal(40, session.ActiveColorIndex);
        Assert.Equal(freeBefore, palette.FreeCustomSlots);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void PickingANewColourClaimsACustomSlotAndLeavesTheLibraryAlone()
    {
        EditorSession session = SessionWithCube();
        Palette palette = session.Scene.Palette;

        Color32 libraryBefore = palette[40];
        var picked = new Color32(3, 250, 137);

        byte chosen = session.SelectColor(picked);

        Assert.True(Palette.IsCustomIndex(chosen));
        Assert.Equal(picked, palette[chosen]);
        Assert.Equal(chosen, session.ActiveColorIndex);
        Assert.Equal(libraryBefore, palette[40]);
        Assert.Equal(Palette.CustomCount - 1, palette.FreeCustomSlots);
    }

    [Fact]
    public void PickingAColourDoesNotRepaintVoxelsOnTheOldIndex()
    {
        // The whole point of the split: choosing a colour is not editing the palette.
        EditorSession session = SessionWithCube(Palette.WhiteIndex);
        ulong before = session.World.ContentHash();

        session.SelectColor(new Color32(200, 20, 20));

        Assert.Equal(before, session.World.ContentHash());
        Assert.Equal(new Color32(255, 255, 255), session.Scene.Palette[Palette.WhiteIndex]);
    }

    [Fact]
    public void PickingTheSameNewColourTwiceReusesOneSlot()
    {
        EditorSession session = SessionWithCube();
        var picked = new Color32(11, 22, 33);

        byte first = session.SelectColor(picked);
        byte second = session.SelectColor(picked);

        Assert.Equal(first, second);
        Assert.Equal(Palette.CustomCount - 1, session.Scene.Palette.FreeCustomSlots);
    }

    [Fact]
    public void ChoosingAColourIsNotAnUndoStep()
    {
        // Nothing has been painted with it yet, so nothing visible changed.
        EditorSession session = SessionWithCube();

        session.SelectColor(new Color32(9, 9, 200));

        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void DraggingThePickerReusesOneSlotInsteadOfHoardingThem()
    {
        EditorSession session = SessionWithCube();

        byte first = session.SelectColor(new Color32(10, 0, 0));
        for (int i = 1; i < 40; i++)
        {
            session.SelectColor(new Color32((byte)(10 + i), 0, 0));
        }

        Assert.Equal(first, session.ActiveColorIndex);
        Assert.Equal(Palette.CustomCount - 1, session.Scene.Palette.FreeCustomSlots);
    }

    [Fact]
    public void OnlySavedColoursBecomeSwatches()
    {
        EditorSession session = SessionWithCube();

        session.SelectColor(new Color32(10, 20, 30));
        Assert.Equal(0, session.Scene.Palette.SavedCustomCount);

        Assert.True(session.SaveActiveColor());
        Assert.Equal(1, session.Scene.Palette.SavedCustomCount);
        Assert.Contains(session.ActiveColorIndex, session.Scene.Palette.SavedCustomSlots());
    }

    [Fact]
    public void SavingAColourProtectsItFromTheNextPick()
    {
        EditorSession session = SessionWithCube();

        byte saved = session.SelectColor(new Color32(10, 20, 30));
        session.SaveActiveColor();

        byte next = session.SelectColor(new Color32(200, 100, 50));

        Assert.NotEqual(saved, next);
        Assert.Equal(new Color32(10, 20, 30), session.Scene.Palette[saved]);
    }

    [Fact]
    public void PaintingWithAWorkingColourAnchorsIt()
    {
        // Once voxels carry the colour, choosing the next one must not repaint them.
        EditorSession session = SessionWithCube();
        session.ActiveTool = EditorTool.Paint;

        byte used = session.SelectColor(new Color32(7, 180, 90));
        session.BeginStroke();
        session.Paint(new Core.Raycast.RaycastHit(new Int3(0, 2, 0), Face.PosY, 1f));
        session.EndStroke();

        Assert.Null(session.WorkingSlot);

        byte next = session.SelectColor(new Color32(250, 10, 10));

        Assert.NotEqual(used, next);
        Assert.Equal(new Color32(7, 180, 90), session.Scene.Palette[used]);
        Assert.Equal(used, session.World.GetFaceColor(new Int3(0, 2, 0), Face.PosY));
    }

    [Fact]
    public void SavedSwatchesSurviveASaveAndLoad()
    {
        EditorSession session = SessionWithCube();
        session.SelectColor(new Color32(12, 34, 56));
        session.SaveActiveColor();
        byte saved = session.ActiveColorIndex;

        // An unsaved working colour alongside it must not come back as a swatch.
        session.SelectColor(new Color32(99, 99, 99));

        string path = Path.Combine(Path.GetTempPath(), $"palette-{Guid.NewGuid():N}.vxlevel");
        try
        {
            Core.Project.VxLevelFile.Save(session.Scene, path);
            VoxelScene loaded = Core.Project.VxLevelFile.LoadScene(path);

            Assert.Equal(1, loaded.Palette.SavedCustomCount);
            Assert.True(loaded.Palette.IsCustomSaved(saved));
            Assert.Equal(new Color32(12, 34, 56), loaded.Palette[saved]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AClearedSlotGoesBackIntoThePool()
    {
        EditorSession session = SessionWithCube();
        byte slot = session.SelectColor(new Color32(5, 5, 5));

        Assert.True(session.ClearCustomColor(slot));
        Assert.True(session.Scene.Palette.IsCustomSlotFree(slot));
        Assert.Equal(Palette.CustomCount, session.Scene.Palette.FreeCustomSlots);
    }

    [Fact]
    public void LibraryEntriesCannotBeCleared()
    {
        EditorSession session = SessionWithCube();
        Assert.False(session.ClearCustomColor(40));
        Assert.NotEqual(Color32.Transparent, session.Scene.Palette[40]);
    }

    [Fact]
    public void WhenEveryCustomSlotIsSavedOneIsStillHandedOut()
    {
        EditorSession session = SessionWithCube();

        // Save every custom slot, so nothing is free and nothing is scratch.
        for (int i = 0; i < Palette.CustomCount; i++)
        {
            session.SelectColor(new Color32((byte)(i + 1), 7, 200));
            session.SaveActiveColor();
        }

        Assert.Equal(0, session.Scene.Palette.FreeCustomSlots);
        Assert.Equal(Palette.CustomCount, session.Scene.Palette.SavedCustomCount);

        // Refusing to pick a colour would be worse than dropping the oldest swatch.
        byte reused = session.SelectColor(new Color32(1, 1, 1));

        Assert.True(Palette.IsCustomIndex(reused));
        Assert.Equal(new Color32(1, 1, 1), session.Scene.Palette[reused]);
    }

    [Fact]
    public void AnUnsavedSlotIsPreferredOverASavedOneWhenReusing()
    {
        EditorSession session = SessionWithCube();

        // One saved swatch, then fill the rest with anchored working colours.
        session.SelectColor(new Color32(1, 2, 3));
        session.SaveActiveColor();
        byte saved = session.ActiveColorIndex;

        for (int i = 0; i < Palette.CustomCount; i++)
        {
            session.SelectColor(new Color32((byte)(50 + i), 9, 9));
        }

        Assert.Equal(new Color32(1, 2, 3), session.Scene.Palette[saved]);
        Assert.True(session.Scene.Palette.IsCustomSaved(saved));
    }

    [Fact]
    public void UsedIndicesSeesEveryObject()
    {
        EditorSession session = SessionWithCube(40);
        var second = new VoxelWorld();
        second.SetVoxel(0, 0, 0, 77);
        session.Scene.Add(second, ObjectTransform.Identity, "other");

        HashSet<byte> used = session.UsedPaletteIndices();

        Assert.Contains((byte)40, used);
        Assert.Contains((byte)77, used);
        Assert.DoesNotContain((byte)0, used);
    }

    [Fact]
    public void EditingALibraryEntryStillRepaintsEveryVoxelUsingIt()
    {
        // The spec's reason for storing indices at all, kept intact behind an explicit action.
        EditorSession session = SessionWithCube(40);
        var replacement = new Color32(1, 2, 3);

        session.ApplyPaletteColor(40, replacement);

        Assert.Equal(replacement, session.Scene.Palette[40]);
        Assert.Equal(40, session.World.GetVoxel(0, 0, 0));
    }
}
