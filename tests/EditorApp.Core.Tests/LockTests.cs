using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Locking: a locked object or light is drawn, exported and saved like any other, but the viewport
/// passes over it — it cannot be picked, take focus or be changed from there.
/// </summary>
public class LockTests
{
    private static VoxelWorld Block(int side)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, 5);
                }
            }
        }

        return grid;
    }

    /// <summary>A floor, and a block standing on it, seen from above.</summary>
    private static (EditorSession Session, VoxelObject Floor, VoxelObject Block) FloorAndBlock()
    {
        var floor = new VoxelWorld();
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                floor.SetVoxel(x, 0, z, 5);
            }
        }

        var scene = new VoxelScene();
        VoxelObject floorObject = scene.Add(floor, ObjectTransform.Identity, "Floor");
        VoxelObject blockObject = scene.Add(Block(2), ObjectTransform.At(new Vector3(3f, 1f, 3f)), "Block");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return (session, floorObject, blockObject);
    }

    private static Ray Down(float x, float z) => new(new Vector3(x, 20f, z), -Vector3.UnitY);

    [Fact]
    public void ALockedObjectIsNotPicked()
    {
        (EditorSession session, VoxelObject floor, _) = FloorAndBlock();

        Assert.True(session.Scene.TryPick(Down(1.5f, 1.5f), out ScenePick before));
        Assert.Equal(floor.Id, before.Object.Id);

        session.SetObjectLocked(floor.Id, true);

        Assert.False(session.Scene.TryPick(Down(1.5f, 1.5f), out _));
    }

    /// <summary>What stands on a locked floor is still there to be picked, and so is what is behind it.</summary>
    [Fact]
    public void PicksPassThroughALockedObject()
    {
        (EditorSession session, VoxelObject floor, VoxelObject block) = FloorAndBlock();
        session.SetObjectLocked(floor.Id, true);

        Assert.True(session.Scene.TryPick(Down(3.5f, 3.5f), out ScenePick pick));
        Assert.Equal(block.Id, pick.Object.Id);

        // Asked for, the floor is still there — where a light gets aimed.
        Assert.True(session.Scene.TryPick(Down(1.5f, 1.5f), out ScenePick floorPick, includeLocked: true));
        Assert.Equal(floor.Id, floorPick.Object.Id);
    }

    [Fact]
    public void ALockedObjectCannotTakeFocus()
    {
        (EditorSession session, VoxelObject floor, VoxelObject block) = FloorAndBlock();
        session.ChooseObject(block.Id);
        session.SetObjectLocked(floor.Id, true);

        Assert.False(session.TryFocus(floor.Id));
        Assert.False(session.ChooseObject(floor.Id));
        Assert.Equal(block.Id, session.Scene.FocusId);
    }

    /// <summary>Choosing a locked row changes nothing — not even the extrude selection on what has focus.</summary>
    [Fact]
    public void ChoosingALockedObjectLetsNothingGo()
    {
        (EditorSession session, VoxelObject floor, VoxelObject block) = FloorAndBlock();
        session.ChooseObject(block.Id);
        session.SetObjectLocked(floor.Id, true);
        session.SelectPatch(new RaycastHit(new Int3(0, 1, 0), Face.PosY, 1f));

        session.ChooseObject(floor.Id);

        Assert.True(session.HasSelection);
    }

    [Fact]
    public void LockingTheFocusedObjectMovesFocusOn()
    {
        (EditorSession session, VoxelObject floor, VoxelObject block) = FloorAndBlock();
        session.ChooseObject(floor.Id);
        session.SelectPatch(new RaycastHit(new Int3(0, 0, 0), Face.PosY, 1f));

        session.SetObjectLocked(floor.Id, true);

        Assert.Equal(block.Id, session.Scene.FocusId);
        Assert.False(session.HasSelection);
        Assert.True(session.HasUnsavedChanges);
    }

    /// <summary>Focus moves on past a locked neighbour to the next object that can hold it.</summary>
    [Fact]
    public void FocusSkipsLockedNeighboursWhenItMovesOn()
    {
        var scene = new VoxelScene();
        VoxelObject first = scene.Add(Block(1), ObjectTransform.Identity, "First");
        VoxelObject second = scene.Add(Block(1), ObjectTransform.At(new Vector3(3f, 0f, 0f)), "Second");
        VoxelObject third = scene.Add(Block(1), ObjectTransform.At(new Vector3(6f, 0f, 0f)), "Third");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);

        session.ChooseObject(first.Id);
        session.SetObjectLocked(second.Id, true);
        session.SetObjectLocked(first.Id, true);

        Assert.Equal(third.Id, session.Scene.FocusId);
    }

    /// <summary>With every object locked, focus has nowhere to go — and then the gizmo has nothing to act on.</summary>
    [Fact]
    public void WithEverythingLockedTheGizmoHasNoTarget()
    {
        (EditorSession session, VoxelObject floor, VoxelObject block) = FloorAndBlock();
        session.SetObjectLocked(floor.Id, true);
        session.SetObjectLocked(block.Id, true);

        Assert.Null(session.TransformTarget);
        Assert.False(session.FlipFocus(Axis.X));
        Assert.False(session.RotateFocus(RotateDirection.Left));
        Assert.False(session.SubdivideFocus());
        Assert.NotNull(session.SubdivideProblem(session.Scene.Focus));
        Assert.Equal(0, session.Cut());
    }

    [Fact]
    public void ALockedLightCannotBePickedAndLockingOneLetsItGo()
    {
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        SceneLight lamp = session.AddLight(LightKind.Point, new Vector3(0f, 5f, 0f), -Vector3.UnitY);
        Assert.Equal(lamp.Id, session.SelectedLightId);

        session.SetLightLocked(lamp.Id, true);

        Assert.Equal(0, session.SelectedLightId);
        Assert.False(session.SelectLight(lamp.Id));

        session.SetLightLocked(lamp.Id, false);
        Assert.True(session.SelectLight(lamp.Id));
    }

    [Fact]
    public void UnlockAllUnlocksObjectsAndLights()
    {
        (EditorSession session, VoxelObject floor, VoxelObject block) = FloorAndBlock();
        SceneLight lamp = session.AddLight(LightKind.Spot, new Vector3(0f, 5f, 0f), -Vector3.UnitY);
        session.SetObjectLocked(floor.Id, true);
        session.SetLightLocked(lamp.Id, true);

        Assert.Equal(2, session.UnlockAll());

        Assert.False(floor.Locked);
        Assert.False(block.Locked);
        Assert.False(lamp.Locked);
    }

    [Fact]
    public void LocksAreSavedWithTheLevel()
    {
        (EditorSession session, VoxelObject floor, _) = FloorAndBlock();
        SceneLight lamp = session.AddLight(LightKind.Point, new Vector3(0f, 5f, 0f), -Vector3.UnitY);
        session.SetObjectLocked(floor.Id, true);
        session.SetLightLocked(lamp.Id, true);

        string path = Path.Combine(Path.GetTempPath(), $"locks-{Guid.NewGuid():N}.vxlevel");
        try
        {
            VxLevelFile.Save(session.Scene, path);
            VoxelScene loaded = VxLevelFile.LoadScene(path);

            Assert.True(loaded.Objects.Single(o => o.Name == "Floor").Locked);
            Assert.False(loaded.Objects.Single(o => o.Name == "Block").Locked);
            Assert.True(loaded.Lights.Single(l => l.Name == lamp.Name).Locked);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A level whose first object is locked opens with focus on the first one that is not.</summary>
    [Fact]
    public void ALevelOpensWithFocusOffItsLockedObjects()
    {
        var scene = new VoxelScene();
        VoxelObject floor = scene.Add(Block(2), ObjectTransform.Identity, "Floor");
        VoxelObject keep = scene.Add(Block(2), ObjectTransform.At(new Vector3(4f, 0f, 0f)), "Keep");
        floor.Locked = true;
        scene.SetFocus(floor.Id);

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);

        Assert.Equal(keep.Id, session.Scene.FocusId);
    }

    /// <summary>The snapshot an autosave writes keeps the locks, so a recovered level comes back as it was left.</summary>
    [Fact]
    public void AnAutosaveSnapshotKeepsTheLocks()
    {
        (EditorSession session, VoxelObject floor, _) = FloorAndBlock();
        session.SetObjectLocked(floor.Id, true);

        VoxelScene copy = session.Scene.Snapshot();

        Assert.True(copy.Find(floor.Id)!.Locked);
    }
}
