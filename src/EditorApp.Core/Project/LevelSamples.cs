using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>A finished little level to look round and take apart, from the welcome screen.</summary>
public enum LevelSample
{
    Island,
    Village,
    Cave,
}

/// <summary>
/// The sample levels (Fullreleaseplan 9.8), made rather than shipped: built from the generators and
/// the props the Add menu has, so they are in the format of the build that opens them, cost nothing
/// to download, and show what the Add menu can make. The same every time: every seed is fixed.
/// </summary>
public static class LevelSamples
{
    public static readonly LevelSample[] All = [LevelSample.Island, LevelSample.Village, LevelSample.Cave];

    public static string NameOf(LevelSample sample) => sample switch
    {
        LevelSample.Island => "Island",
        LevelSample.Village => "Village",
        _ => "Cave",
    };

    /// <summary>What it is, in a phrase, for a tooltip.</summary>
    public static string DescriptionOf(LevelSample sample) => sample switch
    {
        LevelSample.Island => "hills in the sea, with trees and rocks on them",
        LevelSample.Village => "two houses on a green, with crates, barrels, a fence, a spawn point and a lamp",
        _ => "a cave in a block of rock, lit from inside",
    };

    public static VoxelScene Build(LevelSample sample)
    {
        var scene = new VoxelScene();
        switch (sample)
        {
            case LevelSample.Island:
                BuildIsland(scene);
                break;
            case LevelSample.Village:
                BuildVillage(scene);
                break;
            default:
                BuildCave(scene);
                break;
        }

        scene.AddDefaultSun();
        return scene;
    }

    private static void BuildIsland(VoxelScene scene)
    {
        const int Water = 5;
        var colours = new Generators.Colours(scene.Palette);
        VoxelWorld land = Generators.Build(new ShapeSettings(ShapeKind.Terrain, [96, 96, 18, 45, Water, 21]), scene.Palette);
        VoxelObject island = scene.Add(land, ObjectTransform.At(Centred(land)), "Island");

        // On dry ground only, well clear of the water.
        bool Dry(int x, int z) => TopOf(land, x, z) is { } top && top > Water + 1 && land.GetVoxel(x, top, z) != colours.Water;

        int placed = 0;
        for (int attempt = 0; attempt < 400 && placed < 10; attempt++)
        {
            (int x, int z) = Column(land, attempt, seed: 21);
            if (!Dry(x, z))
            {
                continue;
            }

            bool tree = placed < 6;
            VoxelWorld grid = tree
                ? Generators.Build(new ShapeSettings(ShapeKind.Tree, [10 + (placed % 3 * 2), 4 + (placed % 2), placed + 1]), scene.Palette)
                : Generators.Build(new ShapeSettings(ShapeKind.Rock, [3 + (placed % 2), 55, placed + 1]), scene.Palette);
            Stand(scene, grid, tree ? $"Tree {placed + 1}" : $"Rock {placed - 5}", island, x, z);
            placed++;
        }
    }

    private static void BuildVillage(VoxelScene scene)
    {
        const int Size = 48;
        var colours = new Generators.Colours(scene.Palette);
        var green = new VoxelWorld();
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                green.SetVoxel(x, 0, z, (x * 7 + z * 13) % 5 == 0 ? colours.DarkGrass : colours.Grass);
            }
        }

        // The houses at the back, as a new level's view looks from the corner at (0, 0); the yard
        // between them and the spawn in front.
        VoxelObject ground = scene.Add(green, ObjectTransform.At(Centred(green)), "Green");
        Stand(scene, Generators.Build(new ShapeSettings(ShapeKind.Building, [12, 10, 2, 1, 3]), scene.Palette), "House", ground, 34, 33);
        Stand(scene, Generators.Build(new ShapeSettings(ShapeKind.Building, [10, 8, 1, 0, 8]), scene.Palette), "Shop", ground, 36, 12);
        Stand(scene, PropPresets.Build(PropKind.Tree, scene.Palette), "Tree", ground, 13, 38);
        Stand(scene, PropPresets.Build(PropKind.Crate, scene.Palette), "Crate", ground, 23, 22);
        Stand(scene, PropPresets.Build(PropKind.Crate, scene.Palette), "Crate 2", ground, 25, 24);
        Stand(scene, PropPresets.Build(PropKind.Barrel, scene.Palette), "Barrel", ground, 21, 26);
        Stand(scene, PropPresets.Build(PropKind.Fence, scene.Palette), "Fence", ground, 6, 22);
        Stand(scene, PropPresets.Build(PropKind.Fence, scene.Palette), "Fence 2", ground, 6, 28);

        VoxelObject spawn = scene.Add(new VoxelWorld(), ObjectTransform.At(Above(ground, 14, 14)), "Spawn");
        spawn.Marker = ObjectMarker.Default(MarkerKind.Spawn);

        SceneLight lamp = scene.AddLight(LightKind.Point, "Lamp");
        lamp.Apply(lamp.State with
        {
            Transform = ObjectTransform.At(Above(ground, 23, 24) + new Vector3(0f, 4f, 0f)),
            Colour = new Vector3(1f, 0.82f, 0.6f),
            Range = 14f,
        });
    }

    private static void BuildCave(VoxelScene scene)
    {
        VoxelWorld rock = Generators.Build(new ShapeSettings(ShapeKind.Cave, [44, 45, 11]), scene.Palette);
        VoxelObject cave = scene.Add(rock, ObjectTransform.At(Centred(rock)), "Cave");

        // The open cell nearest the middle of the rock, and the floor under it.
        rock.TryGetBounds(out Int3 min, out Int3 max);
        var middle = new Vector3(min.X + max.X, min.Y + max.Y, min.Z + max.Z) * 0.5f;
        Int3 open = Enumerable.Range(min.X, max.X - min.X + 1)
            .SelectMany(x => Enumerable.Range(min.Y, max.Y - min.Y + 1).Select(y => (x, y)))
            .SelectMany(p => Enumerable.Range(min.Z, max.Z - min.Z + 1).Select(z => new Int3(p.x, p.y, z)))
            .Where(cell => !rock.IsSolid(cell) && FloorUnder(rock, cell) is not null)
            .MinBy(cell => Vector3.DistanceSquared(cell.ToVector3(), middle));

        Vector3 origin = cave.Transform.Position;
        int floor = FloorUnder(rock, open) ?? open.Y;

        SceneLight lamp = scene.AddLight(LightKind.Point, "Lamp");
        lamp.Apply(lamp.State with
        {
            Transform = ObjectTransform.At(origin + open.ToVector3() + new Vector3(0.5f)),
            Colour = new Vector3(1f, 0.7f, 0.45f),
            Intensity = 2f,
            Range = 18f,
        });

        VoxelObject spawn = scene.Add(new VoxelWorld(), ObjectTransform.At(origin + new Vector3(open.X + 0.5f, floor + 1, open.Z + 0.5f)), "Spawn");
        spawn.Marker = ObjectMarker.Default(MarkerKind.Spawn);
    }

    /// <summary>Where a grid goes for its footprint to be centred on the origin, its lowest voxels on the ground plane.</summary>
    private static Vector3 Centred(VoxelWorld grid)
    {
        grid.TryGetBounds(out Int3 min, out Int3 max);
        return new Vector3(-((min.X + max.X + 1) / 2), -min.Y, -((min.Z + max.Z + 1) / 2));
    }

    /// <summary>A grid stood on top of the ground's column (x, z), its footprint centred on the column.</summary>
    private static void Stand(VoxelScene scene, VoxelWorld grid, string name, VoxelObject ground, int x, int z)
    {
        grid.TryGetBounds(out Int3 min, out Int3 max);
        Vector3 bottom = new((min.X + max.X + 1) / 2, min.Y, (min.Z + max.Z + 1) / 2);
        scene.Add(grid, ObjectTransform.At(Above(ground, x, z) - bottom), name);
    }

    /// <summary>The world point on top of the ground's column (x, z).</summary>
    private static Vector3 Above(VoxelObject ground, int x, int z) =>
        ground.Transform.Position + new Vector3(x, (TopOf(ground.Grid, x, z) ?? -1) + 1, z);

    /// <summary>The highest solid voxel of a column, or null for none.</summary>
    private static int? TopOf(VoxelWorld grid, int x, int z)
    {
        grid.TryGetBounds(out Int3 min, out Int3 max);
        for (int y = max.Y; y >= min.Y; y--)
        {
            if (grid.IsSolid(x, y, z))
            {
                return y;
            }
        }

        return null;
    }

    /// <summary>The first solid voxel below an open cell, or null when it looks down on nothing.</summary>
    private static int? FloorUnder(VoxelWorld grid, Int3 cell)
    {
        grid.TryGetBounds(out Int3 min, out _);
        for (int y = cell.Y - 1; y >= min.Y; y--)
        {
            if (grid.IsSolid(cell.X, y, cell.Z))
            {
                return y;
            }
        }

        return null;
    }

    /// <summary>A column of a grid picked by a number, the same number always the same column.</summary>
    private static (int X, int Z) Column(VoxelWorld grid, int number, int seed)
    {
        grid.TryGetBounds(out Int3 min, out Int3 max);
        int x = min.X + 4 + (int)(Noise.Hash(number, 0, 0, seed) * (max.X - min.X - 8));
        int z = min.Z + 4 + (int)(Noise.Hash(number, 1, 0, seed) * (max.Z - min.Z - 8));
        return (x, z);
    }
}
