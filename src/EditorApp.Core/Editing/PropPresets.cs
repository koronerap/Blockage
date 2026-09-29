using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>A ready-made thing the Add menu makes, to start a prop from rather than a bare block.</summary>
public enum PropKind
{
    Crate,
    Barrel,
    Table,
    Chair,
    Tree,
    Fence,
}

/// <summary>
/// Props, coarse and simple: a start to refine — subdivide, extrude, paint — rather than a finished
/// model. Each stands on its origin like the shapes do, and is coloured from the level's own
/// palette: the nearest it has to the wood, bark and leaves wanted, so a prop never adds colours of
/// its own.
/// </summary>
public static class PropPresets
{
    public static readonly PropKind[] All = [PropKind.Crate, PropKind.Barrel, PropKind.Table, PropKind.Chair, PropKind.Tree, PropKind.Fence];

    // The default palette's own browns, grey and green, so a prop in a new level is in exactly these
    // — and a colour picked by eye between its steps can land on the wrong hue: a brown that is
    // nearest to the palette's olive is not a brown any more.
    private static readonly Color32 Wood = Color32.FromHsv(2 * (360f / 22f), 0.55f, 0.6f);
    private static readonly Color32 DarkWood = Color32.FromHsv(2 * (360f / 22f), 0.55f, 0.4f);
    private static readonly Color32 Metal = new(91, 91, 91);
    private static readonly Color32 Bark = DarkWood;
    private static readonly Color32 Leaves = Color32.FromHsv(7 * (360f / 22f), 0.55f, 0.6f);

    public static string NameOf(PropKind kind) => kind switch
    {
        PropKind.Barrel => "Barrel",
        PropKind.Table => "Table",
        PropKind.Chair => "Chair",
        PropKind.Tree => "Tree",
        PropKind.Fence => "Fence",
        _ => "Crate",
    };

    public static VoxelWorld Build(PropKind kind, Palette palette)
    {
        var world = new VoxelWorld();
        world.ReplacePalette(palette);
        byte Colour(Color32 colour) => palette.Nearest(colour);

        switch (kind)
        {
            case PropKind.Barrel:
                Barrel(world, Colour(Wood), Colour(Metal));
                break;

            case PropKind.Table:
                Table(world, Colour(Wood), Colour(DarkWood));
                break;

            case PropKind.Chair:
                Chair(world, Colour(Wood), Colour(DarkWood));
                break;

            case PropKind.Tree:
                Tree(world, Colour(Bark), Colour(Leaves));
                break;

            case PropKind.Fence:
                Fence(world, Colour(Wood), Colour(DarkWood));
                break;

            default:
                Crate(world, Colour(Wood), Colour(DarkWood));
                break;
        }

        return world;
    }

    /// <summary>Six a side: planks, with a frame of darker wood along every edge.</summary>
    private static void Crate(VoxelWorld world, byte planks, byte frame)
    {
        const int Low = -3, High = 2;

        for (int x = Low; x <= High; x++)
        {
            for (int y = 0; y <= High - Low; y++)
            {
                for (int z = Low; z <= High; z++)
                {
                    int edges = (x is Low or High ? 1 : 0) + (y is 0 or High - Low ? 1 : 0) + (z is Low or High ? 1 : 0);
                    world.SetVoxel(x, y, z, edges >= 2 ? frame : planks);
                }
            }
        }
    }

    /// <summary>Eight high, a little narrower at the ends, with two metal hoops.</summary>
    private static void Barrel(VoxelWorld world, byte staves, byte hoops)
    {
        const int Height = 8;

        for (int y = 0; y < Height; y++)
        {
            float radius = y is 0 or Height - 1 ? 2.6f : 3f;
            byte colour = y is 1 or Height - 2 ? hoops : staves;

            for (int x = -3; x < 3; x++)
            {
                for (int z = -3; z < 3; z++)
                {
                    if (((x + 0.5f) * (x + 0.5f)) + ((z + 0.5f) * (z + 0.5f)) <= radius * radius)
                    {
                        world.SetVoxel(x, y, z, colour);
                    }
                }
            }
        }
    }

    /// <summary>A top eight by five on four legs, four high.</summary>
    private static void Table(VoxelWorld world, byte top, byte legs)
    {
        for (int x = -4; x <= 3; x++)
        {
            for (int z = -3; z <= 1; z++)
            {
                world.SetVoxel(x, 4, z, top);
            }
        }

        foreach ((int x, int z) in new[] { (-4, -3), (3, -3), (-4, 1), (3, 1) })
        {
            for (int y = 0; y < 4; y++)
            {
                world.SetVoxel(x, y, z, legs);
            }
        }
    }

    /// <summary>A seat four across on four legs, and a back standing up from its rear edge.</summary>
    private static void Chair(VoxelWorld world, byte seat, byte legs)
    {
        for (int x = -2; x <= 1; x++)
        {
            for (int z = -2; z <= 1; z++)
            {
                world.SetVoxel(x, 3, z, seat);
            }

            for (int y = 4; y <= 7; y++)
            {
                world.SetVoxel(x, y, -2, seat);
            }
        }

        foreach ((int x, int z) in new[] { (-2, -2), (1, -2), (-2, 1), (1, 1) })
        {
            for (int y = 0; y < 3; y++)
            {
                world.SetVoxel(x, y, z, legs);
            }
        }
    }

    /// <summary>A trunk under a round crown of leaves.</summary>
    private static void Tree(VoxelWorld world, byte bark, byte leaves)
    {
        const int Crown = 3;
        const int CrownHeight = 7;

        for (int y = 0; y < CrownHeight - 1; y++)
        {
            world.SetVoxel(0, y, 0, bark);
        }

        for (int x = -Crown; x <= Crown; x++)
        {
            for (int y = -Crown; y <= Crown; y++)
            {
                for (int z = -Crown; z <= Crown; z++)
                {
                    if ((x * x) + (y * y) + (z * z) <= (Crown * Crown) + 1)
                    {
                        world.SetVoxel(x, CrownHeight + y, z, leaves);
                    }
                }
            }
        }
    }

    /// <summary>Three posts and two rails between them, nine long.</summary>
    private static void Fence(VoxelWorld world, byte rails, byte posts)
    {
        for (int x = -4; x <= 4; x++)
        {
            world.SetVoxel(x, 1, 0, rails);
            world.SetVoxel(x, 3, 0, rails);
        }

        foreach (int x in new[] { -4, 0, 4 })
        {
            for (int y = 0; y < 5; y++)
            {
                world.SetVoxel(x, y, 0, posts);
            }
        }
    }
}
