using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>Linked copies (Fullreleaseplan 6.2): copies that share one grid of voxels, until one is made a single user.</summary>
public class LinkedCopyTests
{
    private static (EditorSession Session, VoxelObject Original, VoxelObject Copy) Linked()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, Palette.WhiteIndex);
        grid.SetVoxel(1, 0, 0, Palette.WhiteIndex);

        var session = new EditorSession();
        session.ReplaceWorld(grid, projectPath: null);
        VoxelObject original = session.Scene.Objects[0];
        session.ChooseObject(original.Id);

        VoxelObject copy = (VoxelObject)Assert.Single(session.DuplicateSelectedLinked(Vector3.UnitX));
        return (session, original, copy);
    }

    [Fact]
    public void ALinkedCopySharesTheVoxels()
    {
        (EditorSession session, VoxelObject original, VoxelObject copy) = Linked();

        Assert.Same(original.Grid, copy.Grid);
        Assert.Equal(2, session.UsersOf(copy));
        Assert.NotEqual(original.Transform.Position, copy.Transform.Position);

        copy.Grid.SetVoxel(5, 0, 0, 3);
        Assert.Equal(3, original.Grid.GetVoxel(5, 0, 0));
    }

    [Fact]
    public void MakingASingleUserGivesItVoxelsOfItsOwnUntilUndone()
    {
        (EditorSession session, VoxelObject original, VoxelObject copy) = Linked();

        Assert.True(session.MakeSingleUser(copy.Id));
        Assert.NotSame(original.Grid, copy.Grid);
        Assert.Equal(original.Grid.ContentHash(), copy.Grid.ContentHash());
        Assert.False(session.MakeSingleUser(copy.Id));

        copy.Grid.SetVoxel(5, 0, 0, 3);
        Assert.Equal(Palette.EmptyIndex, original.Grid.GetVoxel(5, 0, 0));

        session.Undo();
        Assert.Same(original.Grid, copy.Grid);
    }

    [Fact]
    public void EachCopysModifiersSeeEveryEditToTheSharedVoxels()
    {
        (_, VoxelObject original, VoxelObject copy) = Linked();
        original.SetModifiers([new VoxelModifier(ModifierKind.Mirror, Axis.X, Plane: 0)]);
        copy.SetModifiers([new VoxelModifier(ModifierKind.Mirror, Axis.Z, Plane: 0)]);
        _ = original.Shown;
        _ = copy.Shown;

        original.Grid.SetVoxel(0, 4, 0, 7);

        Assert.Equal(7, original.Shown.GetVoxel(0, 4, 0));
        Assert.Equal(7, copy.Shown.GetVoxel(0, 4, 0));
    }

    [Fact]
    public void JoiningIntoALinkedCopyLeavesTheOtherCopiesAlone()
    {
        (EditorSession session, VoxelObject original, VoxelObject copy) = Linked();
        var extra = new VoxelWorld();
        extra.SetVoxel(0, 3, 0, 9);
        VoxelObject joined = session.Scene.Add(extra, copy.Transform, "Extra");

        Assert.True(session.JoinInto(joined.Id, copy.Id));

        Assert.NotSame(original.Grid, copy.Grid);
        Assert.Equal(9, copy.Grid.GetVoxel(0, 3, 0));
        Assert.Equal(Palette.EmptyIndex, original.Grid.GetVoxel(0, 3, 0));

        session.Undo();
        Assert.Same(original.Grid, copy.Grid);
        Assert.Equal(Palette.EmptyIndex, original.Grid.GetVoxel(0, 3, 0));
    }

    [Fact]
    public void LinkedCopiesAreSavedOnceAndLoadStillLinked()
    {
        (EditorSession session, VoxelObject original, VoxelObject copy) = Linked();

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "linked");
        stream.Position = 0;
        VoxelScene loaded = VxLevelFile.LoadScene(stream);

        VoxelObject loadedOriginal = loaded.Objects.Single(o => o.Name == original.Name);
        VoxelObject loadedCopy = loaded.Objects.Single(o => o.Name == copy.Name);
        Assert.Same(loadedOriginal.Grid, loadedCopy.Grid);
        Assert.Equal(original.Grid.ContentHash(), loadedCopy.Grid.ContentHash());

        VoxelScene snapshot = session.Scene.Snapshot();
        Assert.Same(snapshot.Objects[0].Grid, snapshot.Objects[1].Grid);
        Assert.NotSame(original.Grid, snapshot.Objects[0].Grid);
    }

    [Fact]
    public void GltfGetsOneMeshPlacedOncePerCopy()
    {
        (EditorSession session, VoxelObject original, VoxelObject copy) = Linked();

        ExportMesh baked = GreedyMesher.BuildScene(session.Scene);
        ExportMesh instanced = GreedyMesher.BuildScene(session.Scene, instanceLinked: true);

        Assert.Equal(2, baked.Parts.Count);
        Assert.Empty(baked.Instances);

        MeshPart part = Assert.Single(instanced.Parts);
        Assert.Equal(baked.QuadCount / 2, instanced.QuadCount);
        Assert.Equal(2, instanced.Instances.Count);
        Assert.All(instanced.Instances, instance => Assert.Equal(0, instance.Part));
        Assert.Equal(copy.Transform.ToMatrix(), instanced.Instances.Single(i => i.Name == copy.Name).Transform);
        Assert.Equal(original.Name, part.Name);
    }

    [Fact]
    public void TheGltfFileHasOneMeshAndANodeForEachCopy()
    {
        (EditorSession session, VoxelObject original, VoxelObject copy) = Linked();
        string path = Path.Combine(Path.GetTempPath(), $"linked-{Guid.NewGuid():N}.glb");
        try
        {
            ExportMesh mesh = GreedyMesher.BuildScene(session.Scene, instanceLinked: true);
            new GltfExporter(binary: true).Export(mesh, session.Scene.Palette, path, new ExportOptions());

            SharpGLTF.Schema2.ModelRoot model = SharpGLTF.Schema2.ModelRoot.Load(path);
            Assert.Single(model.LogicalMeshes);
            var nodes = model.LogicalNodes.Where(n => n.Mesh is not null).ToList();
            Assert.Equal(2, nodes.Count);
            Assert.Contains(nodes, n => n.Name == copy.Name);
            Assert.Contains(nodes, n => n.Name == original.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
