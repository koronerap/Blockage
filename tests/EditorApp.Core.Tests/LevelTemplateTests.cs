using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>What each template a new level can start from holds.</summary>
public class LevelTemplateTests
{
    public static TheoryData<LevelTemplate> Templates() => [.. LevelTemplates.All];

    /// <summary>With no Place tool, a level without a voxel in it is a dead end.</summary>
    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateHasSomethingToExtrudeAndTheSun(LevelTemplate template)
    {
        VoxelScene scene = LevelTemplates.Build(template);

        Assert.NotEmpty(scene.Objects);
        Assert.All(scene.Objects, o => Assert.False(o.IsEmpty));
        Assert.Single(scene.Lights, l => l.Kind == LightKind.Directional);
        Assert.NotEqual(0, scene.FocusId);
    }

    [Fact]
    public void EveryTemplateIsListedOnceWithAName()
    {
        Assert.Equal(Enum.GetValues<LevelTemplate>().Length, LevelTemplates.All.Distinct().Count());
        Assert.Equal(LevelTemplates.All.Length, LevelTemplates.All.Select(LevelTemplates.NameOf).Distinct().Count());
        Assert.Equal(LevelTemplate.Cube, LevelTemplates.All[0]);
    }

    /// <summary>The cube is the level New has always made, voxel for voxel.</summary>
    [Fact]
    public void TheCubeIsTheStarterCube()
    {
        VoxelObject cube = Assert.Single(LevelTemplates.Build(LevelTemplate.Cube).Objects);
        VoxelWorld starter = EditorSession.CreateStarterWorld();

        Assert.Equal(starter.SolidCount, cube.Grid.SolidCount);
        Assert.True(cube.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.True(starter.TryGetBounds(out Int3 starterMin, out Int3 starterMax));
        Assert.Equal((starterMin, starterMax), (min, max));
    }

    [Fact]
    public void TheGroundIsOneLayerCentredOnTheOrigin()
    {
        VoxelObject ground = Assert.Single(LevelTemplates.Build(LevelTemplate.Ground).Objects);

        Assert.Equal(LevelTemplates.GroundSize * LevelTemplates.GroundSize, ground.Grid.SolidCount);
        Assert.True(ground.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(-16, 0, -16), min);
        Assert.Equal(new Int3(15, 0, 15), max);
        Assert.Equal(LevelTemplates.FloorIndex, ground.Grid.GetVoxel(new Int3(3, 0, -7)));
    }

    /// <summary>A floor, and walls one voxel thick round its edge, standing on it with nothing over the middle.</summary>
    [Fact]
    public void TheRoomIsAFloorWithWallsAndNoRoof()
    {
        VoxelScene scene = LevelTemplates.Build(LevelTemplate.Room);
        VoxelObject floor = scene.Objects.Single(o => o.Name == "Floor");
        VoxelObject walls = scene.Objects.Single(o => o.Name == "Walls");
        int size = LevelTemplates.RoomSize;
        int height = LevelTemplates.RoomWallHeight;

        Assert.Equal(size * size, floor.Grid.SolidCount);
        Assert.Equal(((size * 4) - 4) * height, walls.Grid.SolidCount);

        Assert.True(walls.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(new Int3(-8, 1, -8), min);
        Assert.Equal(new Int3(7, height, 7), max);

        // Inside the walls, above the floor: empty.
        Assert.False(walls.Grid.IsSolid(new Int3(0, 1, 0)));
        Assert.False(walls.Grid.IsSolid(new Int3(-7, 3, 6)));
        Assert.True(walls.Grid.IsSolid(new Int3(-8, 3, 6)));
    }

    [Fact]
    public void TheVoxelIsOneVoxel()
    {
        VoxelObject voxel = Assert.Single(LevelTemplates.Build(LevelTemplate.Voxel).Objects);

        Assert.Equal(1, voxel.Grid.SolidCount);
        Assert.True(voxel.Grid.IsSolid(new Int3(0, 0, 0)));
    }

    [Fact]
    public void EveryTemplateSaysWhatItIs()
    {
        Assert.All(LevelTemplates.All, t => Assert.False(string.IsNullOrWhiteSpace(LevelTemplates.DescriptionOf(t))));
        Assert.Contains("8x8x8", LevelTemplates.DescriptionOf(LevelTemplate.Cube), StringComparison.Ordinal);
    }
}
