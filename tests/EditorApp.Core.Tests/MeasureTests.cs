using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// The measure tool's rulers and the notes left in a level (Fullreleaseplan 7.9): where a ruler's
/// ends land, what its label says, and a note's words kept with the level but sent to no game.
/// </summary>
public class MeasureTests
{
    /// <summary>A block four wide, two high and four deep, placed as asked.</summary>
    private static VoxelScene Block(ObjectTransform placed)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 4; z++)
        {
            grid.SetVoxel(x, y, z, Palette.WhiteIndex);
        }

        var scene = new VoxelScene();
        scene.Add(grid, placed, "Block");
        return scene;
    }

    private static readonly Vector3 Down = -Vector3.UnitY;

    // ---- Where the ends land ------------------------------------------------------------------------

    [Fact]
    public void AnEndLandsOnTheVoxelCornerNearestWhereTheRayMetTheFace()
    {
        VoxelScene scene = Block(ObjectTransform.Identity);
        var ray = new Ray(new Vector3(1.2f, 10f, 2.7f), Down);
        Assert.True(scene.TryPick(ray, out ScenePick pick));

        Assert.Equal(new Vector3(1f, 2f, 3f), Measuring.PointOn(pick, ray, free: false));
    }

    [Fact]
    public void FreeTheEndIsWhereTheRayMetTheFace()
    {
        VoxelScene scene = Block(ObjectTransform.Identity);
        var ray = new Ray(new Vector3(1.2f, 10f, 2.7f), Down);
        Assert.True(scene.TryPick(ray, out ScenePick pick));

        Vector3 met = Measuring.PointOn(pick, ray, free: true);

        Assert.Equal(1.2f, met.X, 3);
        Assert.Equal(2f, met.Y, 3);
        Assert.Equal(2.7f, met.Z, 3);
    }

    /// <summary>The corners are the object's own, whatever size its voxels and wherever it stands.</summary>
    [Fact]
    public void CornersAreThoseOfTheObjectsOwnVoxels()
    {
        VoxelScene scene = Block(new ObjectTransform(new Vector3(10f, 0f, 0f), Quaternion.Identity, 0.5f));
        var ray = new Ray(new Vector3(10.6f, 10f, 0.3f), Down);
        Assert.True(scene.TryPick(ray, out ScenePick pick));

        Vector3 corner = Measuring.PointOn(pick, ray, free: false);

        Assert.Equal(10.5f, corner.X, 3);
        Assert.Equal(1f, corner.Y, 3);
        Assert.Equal(0.5f, corner.Z, 3);
    }

    [Fact]
    public void WithNothingThereTheEndIsOnTheGround()
    {
        var ray = new Ray(new Vector3(2.3f, 10f, 4.6f), Down);

        Assert.Equal(new Vector3(2f, 0f, 5f), Measuring.OnGround(ray, free: false));
        Assert.Equal(new Vector3(2.3f, 0f, 4.6f), Measuring.OnGround(ray, free: true)!.Value, new Vector3Comparer(0.001f));
    }

    [Fact]
    public void LookingAwayFromTheGroundFindsNoPlaceOnIt()
    {
        Assert.Null(Measuring.OnGround(new Ray(new Vector3(0f, 5f, 0f), Vector3.UnitY), free: false));
        Assert.Null(Measuring.OnGround(new Ray(new Vector3(0f, 5f, 0f), Vector3.UnitX), free: false));
    }

    // ---- What the label says ------------------------------------------------------------------------

    [Fact]
    public void ARulerAlongOneAxisSaysItsLengthInVoxelsAndMetres() =>
        Assert.Equal("12 vx  -  1.2 m", new Ruler(Vector3.Zero, new Vector3(12f, 0f, 0f)).Describe(voxelsPerMetre: 10f));

    [Fact]
    public void ARulerAcrossAxesSaysHowFarAlongEach() =>
        Assert.Equal("5 vx  -  50 cm\nX 3   Y 4   Z 0", new Ruler(new Vector3(1f, 1f, 0f), new Vector3(4f, 5f, 0f)).Describe(voxelsPerMetre: 10f));

    [Theory]
    [InlineData(0.5f, 10f, "0.5 vx  -  5 cm")]
    [InlineData(25_000f, 10f, "25000 vx  -  2.5 km")]
    [InlineData(3f, 1f, "3 vx  -  3 m")]
    public void TheMetresComeInAUnitThatReads(float length, float perMetre, string expected) =>
        Assert.Equal(expected, new Ruler(Vector3.Zero, new Vector3(0f, 0f, length)).Describe(perMetre));

    [Fact]
    public void AnotherLevelStartsWithNoRulers()
    {
        var session = new EditorSession();
        session.Rulers.Add(new Ruler(Vector3.Zero, Vector3.One));

        session.ReplaceScene(new VoxelScene(), projectPath: null);

        Assert.Empty(session.Rulers);
    }

    // ---- Notes --------------------------------------------------------------------------------------

    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceScene(Block(ObjectTransform.Identity), projectPath: null);
        return session;
    }

    [Fact]
    public void ANoteIsAMarkerThatStartsWithNothingWritten()
    {
        VoxelObject note = Session().AddMarker(MarkerKind.Note, new Vector3(1f, 2f, 3f));

        Assert.Equal("Note", note.Name);
        Assert.True(note.Marker!.IsNote);
        Assert.Equal(string.Empty, note.Marker.Text);
    }

    [Fact]
    public void ANotesWordsAreSavedWithTheLevel()
    {
        EditorSession session = Session();
        VoxelObject note = session.AddMarker(MarkerKind.Note, new Vector3(1f, 2f, 3f));
        session.SetMarkerLive(note, note.Marker! with { Text = "The door goes here.\nTwo wide." });

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "notes");
        stream.Position = 0;
        VoxelObject loaded = VxLevelFile.LoadScene(stream).Objects.Single(o => o.Name == note.Name);

        Assert.Equal(new ObjectMarker(MarkerKind.Note, new Vector3(1f), "The door goes here.\nTwo wide."), loaded.Marker);
        Assert.Equal(new Vector3(1f, 2f, 3f), loaded.Transform.Position);
    }

    [Fact]
    public void WritingANoteIsUndoneAsOneStep()
    {
        EditorSession session = Session();
        VoxelObject note = session.AddMarker(MarkerKind.Note, Vector3.Zero);
        ObjectMarker before = note.Marker!;

        session.SetMarkerLive(note, before with { Text = "W" });
        session.SetMarkerLive(note, before with { Text = "Wall" });
        session.PushObjectDataEdit(note, before, note.Properties, "Edit Note");
        session.Undo();

        Assert.Equal(string.Empty, note.Marker!.Text);
        Assert.Single(session.Scene.Objects, o => o.IsMarker);
    }

    [Fact]
    public void ANotesWordsAreKeptToTheirLimit()
    {
        var marker = new ObjectMarker(MarkerKind.Note, Vector3.One, new string('a', ObjectMarker.MaxTextLength + 50));

        Assert.Equal(ObjectMarker.MaxTextLength, marker.Clamped().Text.Length);
    }

    /// <summary>A spawn is for the game and goes out as a node; a note is for the people and goes nowhere.</summary>
    [Fact]
    public void ANoteGoesToNoGame()
    {
        EditorSession session = Session();
        VoxelObject note = session.AddMarker(MarkerKind.Note, Vector3.Zero);
        VoxelObject spawn = session.AddMarker(MarkerKind.Spawn, Vector3.One);

        ExportMesh mesh = GreedyMesher.BuildScene(session.Scene, instanceLinked: true);

        Assert.Contains(mesh.Instances, instance => instance.Name == spawn.Name);
        Assert.DoesNotContain(mesh.Instances, instance => instance.Name == note.Name);
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 value) => 0;
    }
}
