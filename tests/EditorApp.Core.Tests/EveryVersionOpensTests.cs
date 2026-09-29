using System.Numerics;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// The .vxlevel format is frozen at version 7 for 1.0 (docs/vxlevel-format.md), and every version
/// before it keeps opening. Each file in Fixtures was saved by the writer of the version it is
/// named for, built from the commit that brought that version in, and v7 by the build that froze
/// the format: so these check the reader against what the builds really wrote, not against what
/// this one thinks they wrote. The same small level every time, as far as each version could hold
/// it: a floor of six colours with a voxel in a second chunk, and a tower turned a quarter.
/// </summary>
public class EveryVersionOpensTests
{
    private static readonly Quaternion QuarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

    private static VoxelScene Open(string name) =>
        VxLevelFile.LoadScene(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static void AssertFloor(VoxelWorld grid)
    {
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                Assert.Equal((byte)(1 + (x * 2) + y), grid.GetVoxel(x, y, 0));
            }
        }

        Assert.Equal(200, grid.GetVoxel(40, 5, -3));
        Assert.Equal(7, grid.SolidCount);
    }

    private static void AssertTower(VoxelWorld grid)
    {
        for (int y = 0; y < 4; y++)
        {
            Assert.Equal(17, grid.GetVoxel(0, y, 0));
        }

        Assert.Equal(4, grid.SolidCount);
    }

    private static void AssertPlaced(VoxelObject o, Vector3 position, float voxelSize, bool turned)
    {
        Assert.Equal(position.X, o.Transform.Position.X, 4);
        Assert.Equal(position.Y, o.Transform.Position.Y, 4);
        Assert.Equal(position.Z, o.Transform.Position.Z, 4);
        Assert.Equal(voxelSize, o.VoxelSize, 5);
        Assert.True(MathF.Abs(Quaternion.Dot(turned ? QuarterTurn : Quaternion.Identity, o.Transform.Rotation)) > 0.9999f);
    }

    /// <summary>The floor and the tower, placed as a version holds them, and what it knew of painting and hiding.</summary>
    private static (VoxelObject Floor, VoxelObject Tower) FloorAndTower(VoxelScene scene, float scale, float towerSize, bool painted, bool hidden)
    {
        Assert.Equal(["Floor", "Tower"], scene.Objects.Select(o => o.Name));
        VoxelObject floor = scene.Objects[0];
        VoxelObject tower = scene.Objects[1];

        AssertFloor(floor.Grid);
        AssertTower(tower.Grid);
        AssertPlaced(floor, Vector3.Zero, scale, turned: false);
        AssertPlaced(tower, new Vector3(10, 0, -4) * (towerSize == scale ? scale : 1f), towerSize, turned: true);
        Assert.Equal(painted ? 42 : 1, floor.Grid.GetFaceColor(0, 0, 0, Face.PosY));
        Assert.Equal(!hidden, tower.OwnVisible);

        // Nothing before 7 had lights of its own but 6's; a level from before them gets the sun the viewport always had.
        Assert.Single(scene.Lights);
        return (floor, tower);
    }

    [Fact]
    public void Version1IsOneGridAtTheOrigin()
    {
        VoxelScene scene = Open("v1.vxlevel");

        VoxelObject only = Assert.Single(scene.Objects);
        Assert.Equal("Object 1", only.Name);
        AssertPlaced(only, Vector3.Zero, 1f, turned: false);
        AssertFloor(only.Grid);
        Assert.Single(scene.Lights);
    }

    [Fact]
    public void Version2HasObjectsWhereTheyStood() =>
        FloorAndTower(Open("v2.vxlevel"), scale: 1f, towerSize: 1f, painted: false, hidden: false);

    [Fact]
    public void Version3PaintsFaces() =>
        FloorAndTower(Open("v3.vxlevel"), scale: 1f, towerSize: 1f, painted: true, hidden: false);

    /// <summary>Half-unit voxels for the whole level, and positions counted in them: each object takes the size, the positions are made world units.</summary>
    [Fact]
    public void Version4GivesTheLevelsVoxelSizeToEveryObject() =>
        FloorAndTower(Open("v4.vxlevel"), scale: 0.5f, towerSize: 0.5f, painted: true, hidden: false);

    [Fact]
    public void Version5HidesObjects() =>
        FloorAndTower(Open("v5.vxlevel"), scale: 0.5f, towerSize: 0.5f, painted: true, hidden: true);

    [Fact]
    public void Version6GivesEachObjectItsOwnVoxelSize() =>
        FloorAndTower(Open("v6.vxlevel"), scale: 1f, towerSize: 0.25f, painted: true, hidden: true);

    /// <summary>The frozen format, every part of it.</summary>
    [Fact]
    public void Version7HoldsEverythingOnePointZeroSaves()
    {
        VoxelScene scene = Open("v7.vxlevel");

        Assert.Equal(["Floor", "Tower", "Tower copy", "Spawn", "Note"], scene.Objects.Select(o => o.Name));
        VoxelObject floor = scene.Objects[0];
        VoxelObject tower = scene.Objects[1];
        VoxelObject copy = scene.Objects[2];
        VoxelObject spawn = scene.Objects[3];
        VoxelObject note = scene.Objects[4];

        AssertFloor(floor.Grid);
        Assert.Equal(42, floor.Grid.GetFaceColor(0, 0, 0, Face.PosY));
        AssertPlaced(floor, Vector3.Zero, 1f, turned: false);
        Assert.Same(floor, scene.Focus);

        // Collections, nested, with their switches.
        SceneCollection ground = Assert.IsType<SceneCollection>(scene.FindCollection(floor.CollectionId));
        SceneCollection level = Assert.IsType<SceneCollection>(scene.FindCollection(ground.ParentId));
        Assert.Equal(("Ground", true, false), (ground.Name, ground.Locked, ground.Export));
        Assert.Equal(("Level", false, true), (level.Name, level.Locked, level.Export));

        // The tower: hidden, locked, a child of the floor, with two modifiers.
        AssertTower(tower.Grid);
        AssertPlaced(tower, new Vector3(10, 0, -4), 0.25f, turned: true);
        Assert.False(tower.OwnVisible);
        Assert.True(tower.OwnLocked);
        Assert.Same(floor, scene.ParentOf(tower));
        Assert.Equal(
            [
                new VoxelModifier(ModifierKind.Mirror, Axis.X, 2, 3, 8, true),
                new VoxelModifier(ModifierKind.Array, Axis.Z, 0, 3, 4, false),
            ],
            tower.Modifiers);

        // A linked copy has the tower's very voxels.
        Assert.Same(tower.Grid, copy.Grid);
        AssertPlaced(copy, new Vector3(12, 0, -4), 0.25f, turned: false);
        Assert.Same(level, scene.FindCollection(copy.CollectionId));

        // Markers, with properties and a note's text.
        Assert.Equal(new ObjectMarker(MarkerKind.Spawn, new Vector3(1, 2, 1)), spawn.Marker);
        Assert.Equal(
            [
                new CustomProperty("team", PropertyKind.Text, "red"),
                new CustomProperty("lives", PropertyKind.Number, "3"),
                new CustomProperty("boss", PropertyKind.Toggle, "true"),
            ],
            spawn.Properties);
        Assert.True(scene.IsSelected(spawn.Id));
        Assert.Equal(MarkerKind.Note, note.Marker?.Kind);
        Assert.Equal("Mind the gap", note.Marker?.Text);

        // What palette entries are made of.
        Assert.Equal(1f, scene.Palette.Material(200).Emission, 3);
        Assert.Equal(0.3f, scene.Palette.Material(17).Opacity, 3);

        // The level's own light, and nothing added beside it.
        SceneLight lamp = Assert.Single(scene.Lights);
        Assert.Equal(("Lamp", LightKind.Point), (lamp.Name, lamp.Kind));
        Assert.Equal(new Vector3(2, 3, 1), lamp.Position);
        Assert.Equal(new Vector3(1f, 0.8f, 136f / 255f).X, lamp.Colour.X, 3);
        Assert.Equal(0.8f, lamp.Colour.Y, 2);
        Assert.Equal(2f, lamp.Intensity, 3);
        Assert.Equal(10f, lamp.Range, 3);
        Assert.Same(floor, scene.ParentOf(lamp));
        Assert.Equal(0.3f, scene.Ambient, 3);

        // Cameras, and the one renders are seen from.
        Assert.Equal(["Front", "Top"], scene.Cameras.Select(c => c.Name));
        Assert.Equal(("Top", CameraKind.Orthographic, 30f), (scene.ActiveCamera?.Name, scene.ActiveCamera?.Kind, scene.ActiveCamera?.OrthographicHeight));

        Assert.Equal((32, 7, true), (scene.RenderSettings.Samples, scene.RenderSettings.Seed, scene.RenderSettings.TransparentBackground));
        Assert.Equal(0.05f, scene.RenderSettings.Fog, 3);
        Assert.Equal(0.2f, scene.RenderSettings.Bloom, 3);

        ReferenceImage image = Assert.Single(scene.ReferenceImages);
        Assert.Equal(("sketch.png", ImagePlane.Side, false), (image.Path, image.Plane, image.Behind));
        Assert.Equal(16f, image.Width, 3);
        Assert.Equal(0.4f, image.Opacity, 3);
    }

    /// <summary>Saved again, the 1.0 file opens to the same level: nothing is lost on the way through this build.</summary>
    [Fact]
    public void Version7SurvivesBeingSavedAgain()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resaved-{Guid.NewGuid():N}.vxlevel");
        try
        {
            VxLevelFile.Save(Open("v7.vxlevel"), path);
            VoxelScene again = VxLevelFile.LoadScene(path);

            Assert.Equal(["Floor", "Tower", "Tower copy", "Spawn", "Note"], again.Objects.Select(o => o.Name));
            Assert.Same(again.Objects[1].Grid, again.Objects[2].Grid);
            Assert.Equal(2, again.Objects[1].Modifiers.Count);
            Assert.Equal(2, again.Collections.Count);
            Assert.Equal(2, again.Cameras.Count);
            Assert.Equal("Mind the gap", again.Objects[4].Marker?.Text);
            Assert.Equal(VxLevelFile.CurrentVersion, VxLevelFile.ReadManifest(path).Version);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
