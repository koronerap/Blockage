using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Project;

/// <summary>What a new level can start as.</summary>
public enum LevelTemplate
{
    /// <summary>The 8 × 8 × 8 white cube a new level has always started as.</summary>
    Cube,

    /// <summary>A floor, one voxel thick, to build a level on.</summary>
    Ground,

    /// <summary>A floor with walls round it and no roof: an inside to furnish.</summary>
    Room,

    /// <summary>One voxel, to build up from next to nothing.</summary>
    Voxel,

    /// <summary>Nothing but the sun: everything comes from Shift+A.</summary>
    Empty,
}

/// <summary>
/// The levels New can start from — Blender's templates, for a voxel editor. Each gets the sun a new
/// level has, and all but Empty something to extrude from; Empty is filled from Shift+A. Everything
/// sits on the ground plane, centred on the origin, as the cube always has.
/// </summary>
public static class LevelTemplates
{
    public const int GroundSize = 32;

    public const int RoomSize = 16;

    /// <summary>How high a room's walls stand above its floor.</summary>
    public const int RoomWallHeight = 6;

    /// <summary>A light grey: set apart from the white a model is built in, so what stands on it shows.</summary>
    public const byte FloorIndex = 12;

    /// <summary>In the order New lists them, the one Ctrl+N makes first.</summary>
    public static readonly LevelTemplate[] All = [LevelTemplate.Cube, LevelTemplate.Ground, LevelTemplate.Room, LevelTemplate.Voxel, LevelTemplate.Empty];

    public static string NameOf(LevelTemplate template) => template switch
    {
        LevelTemplate.Ground => "Ground",
        LevelTemplate.Room => "Room",
        LevelTemplate.Voxel => "Single voxel",
        LevelTemplate.Empty => "Empty",
        _ => "Cube",
    };

    /// <summary>What it starts with, in a phrase: for a tooltip, and for the status bar once made.</summary>
    public static string DescriptionOf(LevelTemplate template) => template switch
    {
        LevelTemplate.Ground => $"{GroundSize}x{GroundSize} floor, one voxel thick, to build a level on",
        LevelTemplate.Room => $"{RoomSize}x{RoomSize} floor walled {RoomWallHeight} high, open at the top",
        LevelTemplate.Voxel => "one white voxel to build up from",
        LevelTemplate.Empty => "nothing but the sun - Shift+A adds shapes, props and lights",
        _ => $"{EditorSession.StarterCubeSize}x{EditorSession.StarterCubeSize}x{EditorSession.StarterCubeSize} white cube to extrude from",
    };

    public static VoxelScene Build(LevelTemplate template)
    {
        var scene = new VoxelScene();

        switch (template)
        {
            case LevelTemplate.Ground:
                scene.Add(Floor(GroundSize), ObjectTransform.Identity, "Ground");
                break;

            case LevelTemplate.Room:
                // Two objects, so the floor can be locked while the walls are worked on.
                scene.Add(Floor(RoomSize), ObjectTransform.Identity, "Floor");
                scene.Add(Walls(RoomSize, RoomWallHeight), ObjectTransform.Identity, "Walls");
                break;

            case LevelTemplate.Voxel:
                var one = new VoxelWorld();
                one.SetVoxel(0, 0, 0, Palette.WhiteIndex);
                scene.Add(one, ObjectTransform.Identity, "Voxel");
                break;

            case LevelTemplate.Empty:
                break;

            default:
                scene.Add(EditorSession.CreateStarterWorld(), ObjectTransform.Identity, "Object 1");
                break;
        }

        scene.AddDefaultSun();
        return scene;
    }

    /// <summary>A square slab one voxel thick, its top on the ground plane's first layer.</summary>
    private static VoxelWorld Floor(int size)
    {
        var world = new VoxelWorld();
        int half = size / 2;

        for (int x = -half; x < size - half; x++)
        {
            for (int z = -half; z < size - half; z++)
            {
                world.SetVoxel(x, 0, z, FloorIndex);
            }
        }

        return world;
    }

    /// <summary>The four walls round a floor of <paramref name="size"/>, standing on it, one voxel thick.</summary>
    private static VoxelWorld Walls(int size, int height)
    {
        var world = new VoxelWorld();
        int half = size / 2;
        int last = size - half - 1;

        for (int y = 1; y <= height; y++)
        {
            for (int i = -half; i <= last; i++)
            {
                world.SetVoxel(i, y, -half, Palette.WhiteIndex);
                world.SetVoxel(i, y, last, Palette.WhiteIndex);
                world.SetVoxel(-half, y, i, Palette.WhiteIndex);
                world.SetVoxel(last, y, i, Palette.WhiteIndex);
            }
        }

        return world;
    }
}
