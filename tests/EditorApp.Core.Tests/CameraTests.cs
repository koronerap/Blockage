using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Tests;

/// <summary>Cameras kept with the level (Fullreleaseplan 5.4): made from the view, moved, chosen to render from, undone and saved.</summary>
public class CameraTests
{
    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        return session;
    }

    private static SceneCamera View(Vector3 position, float yawDegrees = 30f, float pitchDegrees = -20f) =>
        new(0, "ignored", position, yawDegrees * (MathF.PI / 180f), pitchDegrees * (MathF.PI / 180f), 50f, CameraKind.Perspective, 12f, 25f);

    [Fact]
    public void TheFirstCameraIsTheOneRendersAreSeenFrom()
    {
        EditorSession session = Session();

        SceneCamera first = session.AddCamera(View(new Vector3(10f, 5f, 10f)));
        SceneCamera second = session.AddCamera(View(new Vector3(-10f, 5f, 10f)));

        Assert.Equal("Camera", first.Name);
        Assert.NotEqual(first.Name, second.Name);
        Assert.Equal(first.Id, session.Scene.ActiveCameraId);
        Assert.Equal(second.Id, session.PickedCameraId);
        Assert.Equal(new Vector3(10f, 5f, 10f), session.Scene.ActiveCamera!.Position);

        session.Undo();
        Assert.Single(session.Scene.Cameras);
        session.Undo();
        Assert.Empty(session.Scene.Cameras);
        Assert.Null(session.Scene.ActiveCamera);
    }

    [Fact]
    public void MovingACameraToTheViewKeepsItsNameKindAndLens()
    {
        EditorSession session = Session();
        SceneCamera camera = session.AddCamera(View(new Vector3(10f, 5f, 10f)));
        session.RenameCamera(camera.Id, "Hero shot");

        Assert.True(session.SetCameraToView(camera.Id, View(new Vector3(1f, 2f, 3f), 90f, 0f) with { FieldOfView = 80f }));

        SceneCamera moved = session.Scene.FindCamera(camera.Id)!;
        Assert.Equal("Hero shot", moved.Name);
        Assert.Equal(new Vector3(1f, 2f, 3f), moved.Position);
        Assert.Equal(50f, moved.FieldOfView);

        session.Undo();
        Assert.Equal(new Vector3(10f, 5f, 10f), session.Scene.FindCamera(camera.Id)!.Position);
    }

    [Fact]
    public void AnIsometricCameraLooksAtTheSamePointFromTheGameAngle()
    {
        SceneCamera camera = View(new Vector3(0f, 10f, -20f), 20f, -45f);
        SceneCamera iso = camera.Isometric();

        Assert.Equal(CameraKind.Isometric, iso.Kind);
        Assert.Equal(-30f, iso.Pitch * (180f / MathF.PI), 3);
        Assert.Equal(45f, iso.Yaw * (180f / MathF.PI), 3);
        Assert.True(Vector3.Distance(camera.Pivot, iso.Pivot) < 1e-3f);
        Assert.True(iso.ToRenderCamera().Orthographic);
    }

    [Fact]
    public void DeletingTheRenderCameraRendersTheViewUntilItIsUndone()
    {
        EditorSession session = Session();
        SceneCamera camera = session.AddCamera(View(Vector3.One));

        session.DeleteCamera(camera.Id);
        Assert.Equal(0, session.Scene.ActiveCameraId);

        session.Undo();
        Assert.Equal(camera.Id, session.Scene.ActiveCameraId);
    }

    [Fact]
    public void PickingAnObjectLetsThePickedCameraGo()
    {
        EditorSession session = Session();
        SceneCamera camera = session.AddCamera(View(Vector3.One));
        Assert.Equal(camera.Id, session.PickedCameraId);

        session.ChooseObject(session.Scene.Objects[0].Id);

        Assert.Equal(0, session.PickedCameraId);
    }

    [Fact]
    public void ADraggedSettingIsOneUndoStep()
    {
        EditorSession session = Session();
        SceneCamera camera = session.AddCamera(View(Vector3.One));
        int steps = session.History.UndoCount;

        session.SetCameraLive(camera with { FieldOfView = 60f });
        session.SetCameraLive(camera with { FieldOfView = 70f });
        Assert.True(session.PushCameraEdit(camera, "Edit"));

        Assert.Equal(steps + 1, session.History.UndoCount);
        session.Undo();
        Assert.Equal(50f, session.Scene.FindCamera(camera.Id)!.FieldOfView);
    }

    [Fact]
    public void CamerasAreSavedWithTheLevel()
    {
        EditorSession session = Session();
        session.AddCamera(View(new Vector3(4f, 5f, 6f)));
        SceneCamera second = session.AddCamera(View(new Vector3(-4f, 5f, 6f), 135f, -30f));
        session.SetCameraKind(second.Id, CameraKind.Isometric);
        session.SetActiveCamera(second.Id);

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "cameras");
        stream.Position = 0;
        VoxelScene loaded = VxLevelFile.LoadScene(stream);

        Assert.Equal(2, loaded.Cameras.Count);
        SceneCamera active = loaded.ActiveCamera!;
        SceneCamera saved = session.Scene.FindCamera(second.Id)!;
        Assert.Equal(saved.Name, active.Name);
        Assert.Equal(CameraKind.Isometric, active.Kind);
        Assert.True(Vector3.Distance(saved.Position, active.Position) < 1e-3f);
        Assert.Equal(saved.Yaw, active.Yaw, 4);
        Assert.Equal(saved.Pitch, active.Pitch, 4);

        // A new object in the loaded level takes an id no camera has.
        int objectId = loaded.Add(new Voxels.VoxelWorld(), ObjectTransform.Identity, "New").Id;
        Assert.DoesNotContain(loaded.Cameras, c => c.Id == objectId);
    }
}
