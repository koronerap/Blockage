using System.Numerics;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class LevelSamplesTests
{
    /// <summary>Whether some voxel of an object's lowest layer sits right on a voxel of the ground.</summary>
    private static bool StandsOn(VoxelObject thing, VoxelObject ground)
    {
        Assert.True(thing.Grid.TryGetBounds(out Int3 min, out Int3 max));
        for (int x = min.X; x <= max.X; x++)
        {
            for (int z = min.Z; z <= max.Z; z++)
            {
                if (!thing.Grid.IsSolid(x, min.Y, z))
                {
                    continue;
                }

                Vector3 below = thing.Transform.Position + new Vector3(x, min.Y - 1, z) - ground.Transform.Position;
                if (ground.Grid.IsSolid((int)MathF.Round(below.X), (int)MathF.Round(below.Y), (int)MathF.Round(below.Z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Theory]
    [InlineData(LevelSample.Island)]
    [InlineData(LevelSample.Village)]
    [InlineData(LevelSample.Cave)]
    public void EverySampleIsALevelThatSavesAndOpensAgain(LevelSample sample)
    {
        VoxelScene scene = LevelSamples.Build(sample);

        Assert.True(scene.SolidCount > 1000);
        Assert.Contains(scene.Lights, light => light.Kind == LightKind.Directional);
        Assert.Equal(scene.SolidCount, LevelSamples.Build(sample).SolidCount);

        string path = Path.Combine(Path.GetTempPath(), $"sample-{Guid.NewGuid():N}.vxlevel");
        try
        {
            VxLevelFile.Save(scene, path);
            VoxelScene again = VxLevelFile.LoadScene(path);
            Assert.Equal(scene.Objects.Select(o => o.Name), again.Objects.Select(o => o.Name));
            Assert.Equal(scene.SolidCount, again.SolidCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheIslandsTreesAndRocksStandOnIt()
    {
        VoxelScene scene = LevelSamples.Build(LevelSample.Island);
        VoxelObject island = scene.Objects.Single(o => o.Name == "Island");
        VoxelObject[] standing = [.. scene.Objects.Where(o => o != island)];

        Assert.Equal(6, standing.Count(o => o.Name.StartsWith("Tree", StringComparison.Ordinal)));
        Assert.Equal(4, standing.Count(o => o.Name.StartsWith("Rock", StringComparison.Ordinal)));
        Assert.All(standing, o => Assert.True(StandsOn(o, island), o.Name));
    }

    [Fact]
    public void TheVillageHasHousesPropsASpawnAndALamp()
    {
        VoxelScene scene = LevelSamples.Build(LevelSample.Village);
        VoxelObject green = scene.Objects.Single(o => o.Name == "Green");

        foreach (string name in new[] { "House", "Shop", "Tree", "Crate", "Barrel", "Fence" })
        {
            Assert.True(StandsOn(scene.Objects.Single(o => o.Name == name), green), name);
        }

        Assert.Equal(MarkerKind.Spawn, scene.Objects.Single(o => o.Name == "Spawn").Marker?.Kind);
        Assert.Contains(scene.Lights, light => light.Kind == LightKind.Point);
    }

    [Fact]
    public void TheCaveIsLitFromInsideAndItsSpawnStandsOnItsFloor()
    {
        VoxelScene scene = LevelSamples.Build(LevelSample.Cave);
        VoxelObject cave = scene.Objects.Single(o => o.Name == "Cave");
        SceneLight lamp = scene.Lights.Single(light => light.Kind == LightKind.Point);

        Vector3 inside = lamp.Position - cave.Transform.Position;
        Assert.False(cave.Grid.IsSolid((int)MathF.Floor(inside.X), (int)MathF.Floor(inside.Y), (int)MathF.Floor(inside.Z)));
        Assert.True(cave.Grid.TryGetBounds(out Int3 min, out Int3 max));
        Assert.InRange(inside.Y, min.Y, max.Y);

        Vector3 feet = scene.Objects.Single(o => o.Name == "Spawn").Transform.Position - cave.Transform.Position;
        Assert.True(cave.Grid.IsSolid((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Y) - 1, (int)MathF.Floor(feet.Z)));
    }
}
