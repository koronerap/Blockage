using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Collections (Fullreleaseplan 6.1): nested groups whose switches hold for everything in them.</summary>
public class CollectionTests
{
    private static VoxelWorld Block()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        return grid;
    }

    /// <summary>A level of three objects — the first two in a collection — and a sun.</summary>
    private static (EditorSession Session, VoxelObject A, VoxelObject B, VoxelObject C, SceneCollection Props) Level()
    {
        var session = new EditorSession();
        session.ReplaceWorld(Block(), projectPath: null);
        VoxelScene scene = session.Scene;
        VoxelObject a = scene.Objects[0];
        VoxelObject b = scene.Add(Block(), ObjectTransform.At(new Vector3(4f, 0f, 0f)), "B");
        VoxelObject c = scene.Add(Block(), ObjectTransform.At(new Vector3(8f, 0f, 0f)), "C");
        SceneCollection props = session.NewCollection(moving: [a.Id, b.Id]);
        return (session, a, b, c, props);
    }

    [Fact]
    public void AHiddenCollectionHidesWhatIsInItAndOnlyThat()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c, SceneCollection props) = Level();

        session.SetCollectionVisible(props.Id, false);

        Assert.False(a.Visible);
        Assert.False(b.Visible);
        Assert.True(c.Visible);
        Assert.True(a.OwnVisible);

        session.SetCollectionVisible(props.Id, true);
        Assert.True(a.Visible);
    }

    [Fact]
    public void ASwitchHoldsAllTheWayDownNestedCollections()
    {
        (EditorSession session, VoxelObject a, _, _, SceneCollection props) = Level();
        SceneCollection outer = session.NewCollection();
        session.MoveCollection(props.Id, outer.Id);

        session.SetCollectionLocked(outer.Id, true);
        Assert.True(a.Locked);
        Assert.False(a.OwnLocked);

        session.SetCollectionLocked(outer.Id, false);
        session.SetCollectionExported(outer.Id, false);
        Assert.True(a.Visible);
        Assert.False(a.IsExported);
    }

    [Fact]
    public void WhatIsKeptOutOfExportsIsStillDrawnButNotMeshed()
    {
        (EditorSession session, _, _, VoxelObject c, SceneCollection props) = Level();
        int all = GreedyMesher.BuildScene(session.Scene).Positions.Count;

        session.SetCollectionExported(props.Id, false);

        Assert.True(c.IsExported);
        Assert.True(GreedyMesher.BuildScene(session.Scene).Positions.Count < all);
        Assert.True(GreedyMesher.BuildScene(session.Scene).Positions.Count > 0);
    }

    [Fact]
    public void HidingTheCollectionOfTheFocusedObjectMovesFocusAndLetsTheSelectionGo()
    {
        (EditorSession session, VoxelObject a, _, VoxelObject c, SceneCollection props) = Level();
        session.ChooseObject(a.Id);

        session.SetCollectionVisible(props.Id, false);

        Assert.Equal(c.Id, session.Scene.FocusId);
        Assert.False(session.IsSelected(a.Id));
    }

    [Fact]
    public void DeletingACollectionLeavesWhatWasInItOneStepUp()
    {
        (EditorSession session, VoxelObject a, _, _, SceneCollection props) = Level();
        SceneCollection outer = session.NewCollection();
        session.MoveCollection(props.Id, outer.Id);

        session.DeleteCollection(props.Id);
        Assert.Equal(outer.Id, a.CollectionId);

        session.Undo();
        Assert.Equal(props.Id, a.CollectionId);
        Assert.Equal(outer.Id, session.Scene.FindCollection(props.Id)!.ParentId);
    }

    [Fact]
    public void ACollectionCannotGoInsideItself()
    {
        (EditorSession session, _, _, _, SceneCollection props) = Level();
        SceneCollection inner = session.NewCollection(props.Id);

        Assert.False(session.MoveCollection(props.Id, inner.Id));
        Assert.False(session.MoveCollection(props.Id, props.Id));
        Assert.Equal(0, props.ParentId);
    }

    [Fact]
    public void MovingThingsIsOneUndoStep()
    {
        (EditorSession session, VoxelObject a, VoxelObject b, VoxelObject c, SceneCollection props) = Level();

        Assert.True(session.MoveToCollection([c.Id], props.Id));
        Assert.Equal(props.Id, c.CollectionId);

        session.Undo();
        Assert.Equal(0, c.CollectionId);
        session.Undo();
        Assert.Empty(session.Scene.Collections);
        Assert.Equal(0, a.CollectionId);
        Assert.Equal(0, b.CollectionId);
    }

    [Fact]
    public void NewThingsGoIntoTheActiveCollectionAndCopiesStayWithTheirOriginal()
    {
        (EditorSession session, VoxelObject a, _, VoxelObject c, SceneCollection props) = Level();
        Assert.Equal(props.Id, session.Scene.ActiveCollectionId);

        VoxelObject added = session.Scene.Add(Block(), ObjectTransform.Identity, "New");
        Assert.Equal(props.Id, added.CollectionId);

        session.Scene.ActiveCollectionId = 0;
        session.ChooseObject(a.Id);
        VoxelObject copy = session.DuplicateFocus(Vector3.UnitX)!;
        Assert.Equal(props.Id, copy.CollectionId);

        session.ChooseObject(c.Id);
        Assert.Equal(0, session.DuplicateFocus(Vector3.UnitX)!.CollectionId);
    }

    [Fact]
    public void CollectionsAreSavedWithTheLevel()
    {
        (EditorSession session, VoxelObject a, _, _, SceneCollection props) = Level();
        SceneCollection outer = session.NewCollection();
        session.MoveCollection(props.Id, outer.Id);
        session.RenameCollection(outer.Id, "Buildings");
        session.SetCollectionExported(props.Id, false);
        session.SetCollectionVisible(outer.Id, false);
        SceneLight sun = session.Scene.Lights[0];
        session.MoveToCollection([sun.Id], outer.Id);

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "collections");
        stream.Position = 0;
        VoxelScene loaded = VxLevelFile.LoadScene(stream);

        SceneCollection buildings = Assert.Single(loaded.Collections, c => c.Name == "Buildings");
        SceneCollection inner = Assert.Single(loaded.Collections, c => c.ParentId == buildings.Id);
        Assert.False(buildings.Visible);
        Assert.False(inner.Export);

        VoxelObject loadedA = loaded.Objects.Single(o => o.Name == a.Name);
        Assert.Equal(inner.Id, loadedA.CollectionId);
        Assert.False(loadedA.Visible);
        Assert.True(loadedA.OwnVisible);
        Assert.Equal(buildings.Id, loaded.Lights[0].CollectionId);
        Assert.False(loaded.Lights[0].Visible);
    }
}
