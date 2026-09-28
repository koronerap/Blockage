using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// What the outliner does to the level: duplicating, renaming, hiding and showing, and the order
/// the list keeps through undo.
/// </summary>
public class ObjectOutlinerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-outliner-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Three 4x2x3 objects in a row, the middle one with a painted face, focus on the first.</summary>
    private static EditorSession ThreeObjects()
    {
        var scene = new VoxelScene();

        for (int i = 0; i < 3; i++)
        {
            var grid = new VoxelWorld();
            for (int x = 0; x < 4; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 3; z++)
            {
                grid.SetVoxel(x, y, z, (byte)(10 + i));
            }

            scene.Add(grid, ObjectTransform.At(new Vector3(i * 10f, 0f, 0f)), $"Block {i + 1}");
        }

        scene.Objects[1].Grid.SetFaceColor(3, 1, 2, Face.PosX, 40);

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return session;
    }

    private static string[] Names(EditorSession session) => [.. session.Scene.Objects.Select(o => o.Name)];

    // ---- Duplicate ------------------------------------------------------------------------------

    [Fact]
    public void ADuplicateCarriesEveryVoxelAndPaintedFace()
    {
        EditorSession session = ThreeObjects();
        VoxelObject source = session.Scene.Objects[1];
        session.ChooseObject(source.Id);

        VoxelObject copy = session.DuplicateFocus(Vector3.UnitX)!;

        Assert.Equal(source.Grid.ContentHash(), copy.Grid.ContentHash());
        Assert.Equal(40, copy.Grid.GetFaceColor(3, 1, 2, Face.PosX));
        Assert.Equal(source.Transform.Rotation, copy.Transform.Rotation);
    }

    /// <summary>A copy that shared storage with its original would be edited along with it.</summary>
    [Fact]
    public void EditingTheCopyLeavesTheOriginalAlone()
    {
        EditorSession session = ThreeObjects();
        VoxelObject source = session.Scene.Objects[0];
        ulong before = source.Grid.ContentHash();

        VoxelObject copy = session.DuplicateFocus(Vector3.UnitX)!;
        copy.Grid.SetVoxel(0, 0, 0, Palette.EmptyIndex);
        copy.Grid.SetFaceColor(1, 1, 1, Face.PosY, 50);

        Assert.Equal(before, source.Grid.ContentHash());
    }

    [Fact]
    public void TheCopyIsFocusedAndListedRightAfterItsOriginal()
    {
        EditorSession session = ThreeObjects();

        VoxelObject copy = session.DuplicateFocus(Vector3.UnitX)!;

        Assert.Equal(copy.Id, session.Scene.FocusId);
        Assert.Equal(["Block 1", "Block 1.001", "Block 2", "Block 3"], Names(session));
    }

    /// <summary>
    /// Beside the original, one voxel clear, on the side the camera's right points to — so it shows
    /// up to the right on screen, never overlapping.
    /// </summary>
    [Theory]
    [InlineData(1f, 0f, 5f, 0f)]      // 4 wide on X: 4 + 1
    [InlineData(-1f, 0f, -5f, 0f)]
    [InlineData(0.2f, 0.9f, 0f, 4f)]  // 3 deep on Z: 3 + 1
    [InlineData(0.2f, -0.9f, 0f, -4f)]
    public void TheCopyGoesBesideTheOriginalOneVoxelClear(float towardsX, float towardsZ, float offsetX, float offsetZ)
    {
        EditorSession session = ThreeObjects();
        Vector3 start = session.Scene.Objects[0].Transform.Position;

        VoxelObject copy = session.DuplicateFocus(new Vector3(towardsX, 0f, towardsZ))!;

        Assert.Equal(start + new Vector3(offsetX, 0f, offsetZ), copy.Transform.Position);
    }

    [Fact]
    public void UndoingADuplicateRemovesItAndGivesFocusBack()
    {
        EditorSession session = ThreeObjects();
        int original = session.Scene.FocusId;

        session.DuplicateFocus(Vector3.UnitX);
        Assert.True(session.HasUnsavedChanges);

        session.Undo();

        Assert.Equal(["Block 1", "Block 2", "Block 3"], Names(session));
        Assert.Equal(original, session.Scene.FocusId);

        session.Redo();
        Assert.Equal(["Block 1", "Block 1.001", "Block 2", "Block 3"], Names(session));
    }

    [Theory]
    [InlineData("Tree", new string[0], "Tree.001")]
    [InlineData("Tree", new[] { "Tree", "Tree.001" }, "Tree.002")]
    [InlineData("Tree.001", new[] { "Tree", "Tree.001" }, "Tree.002")]
    [InlineData("Tree.007", new[] { "Tree.007" }, "Tree.001")]
    [InlineData("v1.5", new[] { "v1.5" }, "v1.5.001")]      // not a counter: too short to be one
    [InlineData(".001", new[] { ".001" }, ".001.001")]      // nothing before the dot to keep
    public void DuplicatesAreNamedTheWayBlenderNamesThem(string name, string[] taken, string expected) =>
        Assert.Equal(expected, EditorSession.DuplicateName(name, taken));

    // ---- Delete keeps the order -----------------------------------------------------------------

    [Fact]
    public void UndoingADeletePutsTheObjectBackInItsPlace()
    {
        EditorSession session = ThreeObjects();

        session.DeleteObject(session.Scene.Objects[1].Id);
        Assert.Equal(["Block 1", "Block 3"], Names(session));

        session.Undo();

        Assert.Equal(["Block 1", "Block 2", "Block 3"], Names(session));
    }

    // ---- Rename -----------------------------------------------------------------------------------

    [Fact]
    public void RenamingTrimsAndCountsAsAChange()
    {
        EditorSession session = ThreeObjects();
        int id = session.Scene.Objects[2].Id;

        Assert.True(session.RenameObject(id, "  Tower  "));

        Assert.Equal("Tower", session.Scene.Find(id)!.Name);
        Assert.True(session.HasUnsavedChanges);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsRefused(string blank)
    {
        EditorSession session = ThreeObjects();
        int id = session.Scene.Objects[0].Id;

        Assert.False(session.RenameObject(id, blank));

        Assert.Equal("Block 1", session.Scene.Find(id)!.Name);
        Assert.False(session.HasUnsavedChanges);
    }

    // ---- Visibility --------------------------------------------------------------------------------

    [Fact]
    public void HidingTheFocusedObjectMovesFocusToTheNextVisibleOne()
    {
        EditorSession session = ThreeObjects();
        VoxelObject first = session.Scene.Objects[0];
        VoxelObject second = session.Scene.Objects[1];

        session.SetObjectVisible(first.Id, false);

        Assert.False(first.Visible);
        Assert.Equal(second.Id, session.Scene.FocusId);
        Assert.True(session.HasUnsavedChanges);
    }

    /// <summary>At the end of the list the nearest visible neighbour is the one above.</summary>
    [Fact]
    public void HidingTheLastObjectFocusesTheOneAbove()
    {
        EditorSession session = ThreeObjects();
        VoxelObject last = session.Scene.Objects[2];
        session.ChooseObject(last.Id);

        session.SetObjectVisible(last.Id, false);

        Assert.Equal(session.Scene.Objects[1].Id, session.Scene.FocusId);
    }

    /// <summary>With neighbours both ways, the one below takes over — where the eye goes next reading down.</summary>
    [Fact]
    public void HidingAMiddleObjectFocusesTheOneBelow()
    {
        EditorSession session = ThreeObjects();
        VoxelObject middle = session.Scene.Objects[1];
        session.ChooseObject(middle.Id);

        session.SetObjectVisible(middle.Id, false);

        Assert.Equal(session.Scene.Objects[2].Id, session.Scene.FocusId);
    }

    [Fact]
    public void HidingAnotherObjectLeavesFocusAlone()
    {
        EditorSession session = ThreeObjects();
        int focus = session.Scene.FocusId;

        session.SetObjectVisible(session.Scene.Objects[2].Id, false);

        Assert.Equal(focus, session.Scene.FocusId);
    }

    [Fact]
    public void ShowAllRevealsEveryHiddenObject()
    {
        EditorSession session = ThreeObjects();
        session.SetObjectVisible(session.Scene.Objects[0].Id, false);
        session.SetObjectVisible(session.Scene.Objects[2].Id, false);

        Assert.Equal(2, session.ShowAllObjects());

        Assert.All(session.Scene.Objects, o => Assert.True(o.Visible));
        Assert.Equal(0, session.ShowAllObjects());
    }

    /// <summary>A hidden object is left out of the export, so its being hidden has to survive a save.</summary>
    [Fact]
    public void AHiddenObjectStaysHiddenThroughAFile()
    {
        EditorSession session = ThreeObjects();
        session.SetObjectVisible(session.Scene.Objects[1].Id, false);
        string path = Path.Combine(_directory, "hidden.vxlevel");

        VxLevelFile.Save(session.Scene, path);
        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.Equal([true, false, true], loaded.Objects.Select(o => o.Visible));
    }

    /// <summary>Files from before hiding existed have no field for it: everything in them is shown.</summary>
    [Fact]
    public void AFileFromBeforeHidingShowsEverything()
    {
        EditorSession session = ThreeObjects();
        session.SetObjectVisible(session.Scene.Objects[1].Id, false);
        string path = Path.Combine(_directory, "old.vxlevel");
        VxLevelFile.Save(session.Scene, path);

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("manifest.json")!;
            JsonNode node;
            using (var reader = new StreamReader(entry.Open()))
            {
                node = JsonNode.Parse(reader.ReadToEnd())!;
            }

            node["version"] = 4;
            foreach (JsonNode? o in node["objects"]!.AsArray())
            {
                o!.AsObject().Remove("visible");
            }

            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(node.ToJsonString());
        }

        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.All(loaded.Objects, o => Assert.True(o.Visible));
    }

    // ---- Snapshot ----------------------------------------------------------------------------------

    /// <summary>
    /// Autosave writes a snapshot on another thread while editing goes on. Nothing done to the level
    /// after the snapshot may show up in it.
    /// </summary>
    [Fact]
    public void ASnapshotIsUntouchedByLaterEdits()
    {
        EditorSession session = ThreeObjects();
        session.SetObjectVisible(session.Scene.Objects[2].Id, false);
        VoxelScene snapshot = session.Scene.Snapshot();
        ulong taken = snapshot.ContentHash();

        VoxelObject first = session.Scene.Objects[0];
        first.Grid.SetVoxel(0, 0, 0, Palette.EmptyIndex);
        session.RenameObject(first.Id, "Changed");
        session.Scene.Palette[10] = new Color32(1, 2, 3, 255);
        session.ShowAllObjects();

        Assert.Equal(taken, snapshot.ContentHash());
        Assert.Equal("Block 1", snapshot.Objects[0].Name);
        Assert.NotEqual(new Color32(1, 2, 3, 255), snapshot.Palette[10]);
        Assert.False(snapshot.Objects[2].Visible);
        Assert.Equal(40, snapshot.Objects[1].Grid.GetFaceColor(3, 1, 2, Face.PosX));
    }

    [Fact]
    public void MarkingTheLevelChangedMovesTheRevisionOn()
    {
        EditorSession session = ThreeObjects();
        long before = session.Revision;

        session.RenameObject(session.Scene.Objects[0].Id, "Moved on");

        Assert.True(session.Revision > before);

        long after = session.Revision;
        session.HasUnsavedChanges = false;
        Assert.Equal(after, session.Revision);
    }
}
