using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using EditorApp.Core.Editing;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Lights as things in the level: how they are aimed, what a new level starts with, how they are
/// kept in a file and undone in the editor — and that they never reach an export.
/// </summary>
public class LightTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-light-tests", Guid.NewGuid().ToString("N"));

    public LightTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        return session;
    }

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"{actual} is not {expected}.");

    // ---- Aiming ---------------------------------------------------------------------------------

    [Fact]
    public void AnUnturnedLightShinesStraightDown() =>
        Near(-Vector3.UnitY, new SceneLight(1, LightKind.Spot, "s").Direction);

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.3f, -0.8f, 0.52f)]
    [InlineData(0f, 1f, 0f)]      // straight up, where the shortest turn has no axis of its own
    [InlineData(0f, -1f, 0f)]
    public void AimingPointsTheLightWhereItWasAimed(float x, float y, float z)
    {
        Vector3 direction = Vector3.Normalize(new Vector3(x, y, z));
        var light = new SceneLight(1, LightKind.Spot, "s")
        {
            Transform = new ObjectTransform(Vector3.Zero, SceneLight.Aiming(direction)),
        };

        Near(direction, light.Direction);
    }

    [Theory]
    [InlineData(200f, 50f)]
    [InlineData(10f, 5f)]
    [InlineData(300f, -20f)]
    public void BearingAndHeightComeBackFromTheDirection(float azimuth, float elevation)
    {
        (float a, float e) = SceneLight.AnglesOf(SceneLight.ShiningFrom(azimuth, elevation));

        Assert.Equal(azimuth, a, 2);
        Assert.Equal(elevation, e, 2);
    }

    // ---- What a level starts with -----------------------------------------------------------------

    /// <summary>
    /// The viewport had one light, from a bearing of 200 and a height of 50, before lights were in the
    /// level. A new level's sun is that light, so nothing built before looks any different.
    /// </summary>
    [Fact]
    public void ANewLevelStartsWithTheSunTheViewportAlwaysHad()
    {
        EditorSession session = Session();

        SceneLight sun = Assert.Single(session.Scene.Lights);
        Assert.Equal(LightKind.Directional, sun.Kind);
        Assert.Equal(SceneLight.SunIntensity, sun.Intensity);
        Assert.Equal(Vector3.One, sun.Colour);
        Near(SceneLight.ShiningFrom(200f, 50f), sun.Direction);
        Assert.Equal(VoxelScene.DefaultAmbient, session.Scene.Ambient);
    }

    [Fact]
    public void ObjectsAndLightsNeverShareAnId()
    {
        EditorSession session = Session();
        session.AddLight(LightKind.Point, Vector3.One, -Vector3.UnitY);
        session.DuplicateFocus(Vector3.UnitX);
        session.AddLight(LightKind.Spot, Vector3.One, -Vector3.UnitY);

        int[] ids = [.. session.Scene.Objects.Select(o => o.Id), .. session.Scene.Lights.Select(l => l.Id)];

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    // ---- The file -----------------------------------------------------------------------------------

    [Fact]
    public void EveryLightSettingSurvivesAFile()
    {
        EditorSession session = Session();
        SceneLight spot = session.AddLight(LightKind.Spot, new Vector3(3f, 9f, -2f), Vector3.Normalize(new Vector3(0.2f, -1f, 0.1f)));
        spot.Apply(spot.State with
        {
            Name = "Torch",
            Colour = new Vector3(1f, 0.5f, 0.25f),
            Intensity = 2.5f,
            Range = 18f,
            SpotAngle = 60f,
            SpotBlend = 0.4f,
            Visible = false,
        });
        session.SetAmbient(0.2f);

        string path = Path.Combine(_directory, "lit" + VxLevelFile.Extension);
        VxLevelFile.Save(session.Scene, path);
        VoxelScene loaded = VxLevelFile.LoadScene(path);

        Assert.Equal(2, loaded.Lights.Count);
        SceneLight back = loaded.Lights[1];

        Assert.Equal("Torch", back.Name);
        Assert.Equal(LightKind.Spot, back.Kind);
        Near(spot.Position, back.Position);
        Near(spot.Direction, back.Direction);
        Near(spot.Colour, back.Colour, 1f / 255f);
        Assert.Equal(2.5f, back.Intensity);
        Assert.Equal(18f, back.Range);
        Assert.Equal(60f, back.SpotAngle);
        Assert.Equal(0.4f, back.SpotBlend);
        Assert.False(back.Visible);
        Assert.Equal(0.2f, loaded.Ambient, 5);
    }

    /// <summary>A file from before lights were saved gets the sun the viewport lit it with then.</summary>
    [Fact]
    public void AFileWithoutLightsGetsTheSunItWasSeenBy()
    {
        EditorSession session = Session();
        string path = Path.Combine(_directory, "old" + VxLevelFile.Extension);
        VxLevelFile.Save(session.Scene, path);
        RewriteManifest(path, node =>
        {
            node.AsObject().Remove("lights");
            node.AsObject().Remove("ambient");
        });

        VoxelScene loaded = VxLevelFile.LoadScene(path);

        SceneLight sun = Assert.Single(loaded.Lights);
        Near(SceneLight.ShiningFrom(SceneLight.SunAzimuth, SceneLight.SunElevation), sun.Direction);
        Assert.Equal(VoxelScene.DefaultAmbient, loaded.Ambient);
    }

    /// <summary>An empty list is a level someone chose to leave unlit, and it stays that way.</summary>
    [Fact]
    public void ALevelLeftWithoutLightsStaysWithout()
    {
        EditorSession session = Session();
        session.DeleteLight(session.Scene.Lights[0].Id);

        string path = Path.Combine(_directory, "dark" + VxLevelFile.Extension);
        VxLevelFile.Save(session.Scene, path);

        Assert.Empty(VxLevelFile.LoadScene(path).Lights);
    }

    [Fact]
    public void ALightOfAnUnknownKindIsReported()
    {
        string path = Path.Combine(_directory, "odd" + VxLevelFile.Extension);
        VxLevelFile.Save(Session().Scene, path);
        RewriteManifest(path, node => node["lights"]![0]!["kind"] = "laser");

        VxLevelFormatException error = Assert.Throws<VxLevelFormatException>(() => VxLevelFile.LoadScene(path));
        Assert.Contains("laser", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Lights are for seeing by while building. The engine lights the level its own way.</summary>
    [Fact]
    public void LightsNeverReachAnExport()
    {
        EditorSession session = Session();
        ExportMesh before = GreedyMesher.BuildScene(session.Scene);

        session.AddLight(LightKind.Point, new Vector3(4f, 10f, 4f), -Vector3.UnitY);
        session.AddLight(LightKind.Spot, new Vector3(4f, 12f, 4f), -Vector3.UnitY);
        ExportMesh after = GreedyMesher.BuildScene(session.Scene);

        Assert.Equal(before.QuadCount, after.QuadCount);
        Assert.Equal(before.Parts.Count, after.Parts.Count);
        Assert.Equal(before.Positions, after.Positions);
    }

    // ---- The editor -----------------------------------------------------------------------------

    [Fact]
    public void AddingALightPicksItAndUndoingTakesItAway()
    {
        EditorSession session = Session();

        SceneLight point = session.AddLight(LightKind.Point, new Vector3(0f, 5f, 0f), -Vector3.UnitY);

        Assert.Equal(point.Id, session.SelectedLightId);
        Assert.Same(point, session.TransformTarget);
        Assert.Equal("Point", point.Name);

        session.Undo();
        Assert.DoesNotContain(point, session.Scene.Lights);
        Assert.Null(session.SelectedLight);
        Assert.Same(session.Scene.Focus, session.TransformTarget);

        session.Redo();
        Assert.Contains(point, session.Scene.Lights);
    }

    [Fact]
    public void ASecondLightOfAKindIsNumbered()
    {
        EditorSession session = Session();

        SceneLight sun = session.AddLight(LightKind.Directional, Vector3.Zero, -Vector3.UnitY);

        Assert.Equal("Sun.001", sun.Name);
    }

    [Fact]
    public void UndoingADeletePutsTheLightBackInItsPlace()
    {
        EditorSession session = Session();
        session.AddLight(LightKind.Point, Vector3.One, -Vector3.UnitY);
        session.AddLight(LightKind.Spot, Vector3.One, -Vector3.UnitY);
        string[] names = [.. session.Scene.Lights.Select(l => l.Name)];

        session.DeleteLight(session.Scene.Lights[1].Id);
        session.Undo();

        Assert.Equal(names, session.Scene.Lights.Select(l => l.Name));
    }

    [Fact]
    public void AnEditIsOneUndoStepHoweverManyChangesItHad()
    {
        EditorSession session = Session();
        SceneLight light = session.Scene.Lights[0];
        LightState before = light.State;

        light.Apply(light.State with { Intensity = 1.2f });
        light.Apply(light.State with { Intensity = 1.6f, Colour = new Vector3(1f, 0.8f, 0.6f) });
        Assert.True(session.PushLightEdit(light, before, "Edit Sun"));

        session.Undo();
        Assert.Equal(before, light.State);

        session.Redo();
        Assert.Equal(1.6f, light.Intensity);
    }

    /// <summary>Settings outside what a light can be are clamped, not stored.</summary>
    [Fact]
    public void ImpossibleSettingsAreClamped()
    {
        var light = new SceneLight(1, LightKind.Spot, "s");

        light.Apply(light.State with { Intensity = -4f, Range = 0f, SpotAngle = 400f, SpotBlend = float.NaN, Colour = new Vector3(3f, -1f, 0.5f) });

        Assert.Equal(0f, light.Intensity);
        Assert.Equal(SceneLight.MinRange, light.Range);
        Assert.Equal(SceneLight.MaxSpotAngle, light.SpotAngle);
        Assert.Equal(0.15f, light.SpotBlend);
        Assert.Equal(new Vector3(1f, 0f, 0.5f), light.Colour);
    }

    [Fact]
    public void ChoosingAnObjectLetsGoOfTheLight()
    {
        EditorSession session = Session();
        session.SelectLight(session.Scene.Lights[0].Id);

        session.ChooseObject(session.Scene.Objects[0].Id);

        Assert.Null(session.SelectedLight);
        Assert.Same(session.Scene.Objects[0], session.TransformTarget);
    }

    [Fact]
    public void MovingAPickedLightIsAnUndoStepAndLeavesTheObjectAlone()
    {
        EditorSession session = Session();
        SceneLight sun = session.Scene.Lights[0];
        ObjectTransform objectBefore = session.Scene.Objects[0].Transform;
        session.SelectLight(sun.Id);

        IPlaceable target = session.TransformTarget!;
        ObjectTransform before = target.Transform;
        session.ApplyTransform(target, before.Translated(new Vector3(0f, 3f, 0f)));
        session.PushTransformEdit(target, before, "Move light");

        Assert.Equal(before.Position + new Vector3(0f, 3f, 0f), sun.Position);
        Assert.Equal(objectBefore, session.Scene.Objects[0].Transform);

        session.Undo();
        Assert.Equal(before, sun.Transform);
    }

    [Fact]
    public void SwitchingALightOffIsSavedButNotAnUndoStep()
    {
        EditorSession session = Session();
        SceneLight sun = session.Scene.Lights[0];

        Assert.True(session.SetLightVisible(sun.Id, false));

        Assert.False(sun.Visible);
        Assert.True(session.HasUnsavedChanges);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void ADuplicatedLightIsACopyBesideItPicked()
    {
        EditorSession session = Session();
        SceneLight sun = session.Scene.Lights[0];

        SceneLight copy = session.DuplicateLight(sun.Id, new Vector3(2f, 0f, 0f))!;

        Assert.Equal("Sun.001", copy.Name);
        Assert.Equal(sun.Position + new Vector3(2f, 0f, 0f), copy.Position);
        Near(sun.Direction, copy.Direction);
        Assert.Equal(copy.Id, session.SelectedLightId);
    }

    /// <summary>Autosave writes a snapshot on another thread; a light edited after it must not show up in it.</summary>
    [Fact]
    public void ASnapshotsLightsAreItsOwn()
    {
        EditorSession session = Session();
        VoxelScene snapshot = session.Scene.Snapshot();

        session.Scene.Lights[0].Apply(session.Scene.Lights[0].State with { Intensity = 5f });
        session.SetAmbient(0.9f);

        Assert.Equal(SceneLight.SunIntensity, snapshot.Lights[0].Intensity);
        Assert.Equal(VoxelScene.DefaultAmbient, snapshot.Ambient);
    }

    private static void RewriteManifest(string path, Action<JsonNode> change)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        ZipArchiveEntry entry = archive.GetEntry("manifest.json")!;

        JsonNode node;
        using (var reader = new StreamReader(entry.Open()))
        {
            node = JsonNode.Parse(reader.ReadToEnd())!;
        }

        change(node);

        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
        writer.Write(node.ToJsonString());
    }
}
