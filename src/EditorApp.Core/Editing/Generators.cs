using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// The Add menu's Generate list (Fullreleaseplan 6.8): ground from noise, a cave, a tree, a rock and
/// a simple building, each made from a handful of numbers and a seed — the same numbers and seed make
/// the same thing, voxel for voxel. They are shapes like the rest: set down on a surface, adjusted
/// straight after in the Adjust panel. Their colours are the nearest the level's palette has to
/// grass, earth, stone, bark, leaves, plaster and the rest.
/// </summary>
public static class Generators
{
    public static readonly ShapeKind[] All = [ShapeKind.Terrain, ShapeKind.Cave, ShapeKind.Tree, ShapeKind.Rock, ShapeKind.Building];

    private static ShapeField Seed => new("Seed", 1, 0, 99_999, "The same seed makes the same thing again; another makes another.");

    public static readonly ShapeField[] TerrainFields =
    [
        new("Width", 64, 8, 256, "Along X, in voxels."),
        new("Depth", 64, 8, 256, "Along Z, in voxels."),
        new("Height", 16, 2, 64, "How high the hills rise above the lowest ground, in voxels."),
        new("Roughness", 50, 0, 100, "0 is gently rolling; 100 is broken, rugged ground."),
        new("Water", 0, 0, 64, "Low ground under this height is under water; 0 for none."),
        Seed,
    ];

    public static readonly ShapeField[] CaveFields =
    [
        new("Size", 40, 12, 128, "Each side of the block of rock the cave is in, in voxels."),
        new("Openness", 40, 10, 80, "How much of the rock is hollowed out, in hundredths."),
        Seed,
    ];

    public static readonly ShapeField[] TreeFields =
    [
        new("Height", 12, 4, 48, "Ground to the top of the trunk, in voxels."),
        new("Crown", 5, 2, 16, "How far the leaves spread from the trunk, in voxels."),
        Seed,
    ];

    public static readonly ShapeField[] RockFields =
    [
        new("Size", 5, 2, 32, "Half its width, in voxels."),
        new("Roughness", 50, 0, 100, "0 is a smooth boulder; 100 is jagged."),
        Seed,
    ];

    public static readonly ShapeField[] BuildingFields =
    [
        new("Width", 12, 5, 64, "Along X, in voxels."),
        new("Depth", 10, 5, 64, "Along Z, in voxels."),
        new("Floors", 2, 1, 10, "Storeys, each four voxels high."),
        new("Roof", 1, 0, 1, "0 for a flat roof with a low wall round it, 1 for a pitched one."),
        Seed,
    ];

    public static string NameOf(ShapeKind kind) => kind switch
    {
        ShapeKind.Terrain => "Terrain",
        ShapeKind.Cave => "Cave",
        ShapeKind.Tree => "Tree",
        ShapeKind.Rock => "Rock",
        _ => "Building",
    };

    public static VoxelWorld Build(ShapeSettings s, Palette palette)
    {
        var cells = new Dictionary<Int3, byte>();
        var colours = new Colours(palette);
        switch (s.Kind)
        {
            case ShapeKind.Terrain:
                Terrain(cells, colours, s[0], s[1], s[2], s[3], s[4], s[5]);
                break;
            case ShapeKind.Cave:
                Cave(cells, colours, s[0], s[1], s[2]);
                break;
            case ShapeKind.Tree:
                Tree(cells, colours, s[0], s[1], s[2]);
                break;
            case ShapeKind.Rock:
                Rock(cells, colours, s[0], s[1], s[2]);
                break;
            default:
                Building(cells, colours, s[0], s[1], s[2], s[3], s[4]);
                break;
        }

        var world = new VoxelWorld();
        foreach ((Int3 cell, byte colour) in cells)
        {
            world.SetVoxel(cell, colour);
        }

        return world;
    }

    /// <summary>What each kind of thing is painted in: the nearest the palette has.</summary>
    internal sealed class Colours(Palette palette)
    {
        public byte Grass { get; } = palette.Nearest(new Color32(92, 158, 58, 255));
        public byte DarkGrass { get; } = palette.Nearest(new Color32(70, 128, 46, 255));
        public byte Earth { get; } = palette.Nearest(new Color32(122, 86, 56, 255));
        public byte Stone { get; } = palette.Nearest(new Color32(128, 128, 132, 255));
        public byte DarkStone { get; } = palette.Nearest(new Color32(92, 92, 98, 255));
        public byte Sand { get; } = palette.Nearest(new Color32(214, 196, 140, 255));
        public byte Snow { get; } = palette.Nearest(new Color32(242, 244, 248, 255));
        public byte Water { get; } = palette.Nearest(new Color32(58, 110, 196, 255));
        public byte Bark { get; } = palette.Nearest(new Color32(106, 74, 46, 255));
        public byte Leaves { get; } = palette.Nearest(new Color32(66, 138, 52, 255));
        public byte DarkLeaves { get; } = palette.Nearest(new Color32(44, 104, 40, 255));
        public byte Glass { get; } = palette.Nearest(new Color32(126, 176, 226, 255));
        public byte Door { get; } = palette.Nearest(new Color32(96, 64, 40, 255));
        public byte Trim { get; } = palette.Nearest(new Color32(150, 146, 140, 255));

        public byte Plaster(int choice) => palette.Nearest(choice switch
        {
            0 => new Color32(222, 212, 190, 255),
            1 => new Color32(200, 170, 140, 255),
            _ => new Color32(190, 196, 204, 255),
        });

        public byte Roof(int choice) => palette.Nearest(choice switch
        {
            0 => new Color32(150, 62, 48, 255),
            1 => new Color32(80, 84, 96, 255),
            _ => new Color32(110, 76, 52, 255),
        });
    }

    // ---- Ground --------------------------------------------------------------------------------

    private static void Terrain(Dictionary<Int3, byte> cells, Colours c, int width, int depth, int height, int roughness, int water, int seed)
    {
        float rough = roughness / 100f;
        float frequency = Lerp(1f / 56f, 1f / 14f, rough);
        float persistence = Lerp(0.35f, 0.62f, rough);
        (int x0, int x1) = Across(width);
        (int z0, int z1) = Across(depth);

        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            // Value noise gathers round the middle: spread it back over the whole height.
            float n = Math.Clamp((Noise.Fractal2(x * frequency, z * frequency, seed, 5, persistence) - 0.2f) / 0.6f, 0f, 1f);
            Ground(cells, c, x, z, 1 + (int)MathF.Round(n * height), height, water, seed);
        }
    }

    /// <summary>
    /// One column of ground, <paramref name="top"/> voxels high: grass on it — sand by the water, snow
    /// high up — earth under that and stone below, and water over it up to <paramref name="water"/>.
    /// </summary>
    internal static void Ground(Dictionary<Int3, byte> cells, Colours c, int x, int z, int top, int height, int water, int seed)
    {
        for (int y = 0; y < top; y++)
        {
            int below = top - 1 - y;
            byte colour = below switch
            {
                0 when top <= water + 1 && water > 0 => c.Sand,
                0 when height >= 12 && top > height * 0.85f => c.Snow,
                0 => Noise.Hash(x, 0, z, seed + 7) < 0.25f ? c.DarkGrass : c.Grass,
                < 3 => c.Earth,
                _ => c.Stone,
            };

            cells[new Int3(x, y, z)] = colour;
        }

        for (int y = top; y < water; y++)
        {
            cells[new Int3(x, y, z)] = c.Water;
        }
    }

    // ---- Underground ---------------------------------------------------------------------------

    private static void Cave(Dictionary<Int3, byte> cells, Colours c, int size, int openness, int seed)
    {
        (int a0, int a1) = Across(size);
        float frequency = 1f / 9f;

        // Hollowed where a winding band of the noise passes: tunnels rather than bubbles.
        float band = Lerp(0.03f, 0.16f, (openness - 10) / 70f);

        for (int x = a0; x <= a1; x++)
        for (int y = 0; y < size; y++)
        for (int z = a0; z <= a1; z++)
        {
            // A floor and a roof of whole rock; where the tunnels reach the sides, they open out.
            bool shell = y == 0 || y == size - 1;
            float n = Noise.Fractal3(x * frequency, y * frequency * 1.3f, z * frequency, seed, 3, 0.5f);
            bool hollow = !shell && MathF.Abs(n - 0.5f) < band;
            if (hollow)
            {
                continue;
            }

            cells[new Int3(x, y, z)] = Noise.Hash(x, y, z, seed + 3) < 0.3f ? c.DarkStone : c.Stone;
        }

        // Floors a cave's own colour: rock with open air above it.
        foreach (Int3 cell in cells.Keys.ToList())
        {
            if (!cells.ContainsKey(cell + new Int3(0, 1, 0)) && cell.Y < size - 1)
            {
                cells[cell] = c.Earth;
            }
        }
    }

    // ---- Growing things ------------------------------------------------------------------------

    private static void Tree(Dictionary<Int3, byte> cells, Colours c, int height, int crown, int seed)
    {
        var random = new Random(seed);
        bool thick = height >= 14;

        // A trunk that leans a little as it goes up.
        float leanX = (float)(random.NextDouble() - 0.5) * 0.25f;
        float leanZ = (float)(random.NextDouble() - 0.5) * 0.25f;
        for (int y = 0; y < height; y++)
        {
            int x = (int)MathF.Round(leanX * y);
            int z = (int)MathF.Round(leanZ * y);
            cells[new Int3(x, y, z)] = c.Bark;
            if (thick && y < height * 0.6f)
            {
                cells[new Int3(x + 1, y, z)] = c.Bark;
                cells[new Int3(x, y, z + 1)] = c.Bark;
                cells[new Int3(x + 1, y, z + 1)] = c.Bark;
            }
        }

        var top = new Int3((int)MathF.Round(leanX * height), height, (int)MathF.Round(leanZ * height));

        // Branches from the upper trunk out into the leaves.
        int branches = 2 + random.Next(3);
        for (int b = 0; b < branches; b++)
        {
            float angle = (float)(random.NextDouble() * MathF.Tau);
            int from = (int)(height * (0.55f + (0.3f * random.NextDouble())));
            Int3 start = new((int)MathF.Round(leanX * from), from, (int)MathF.Round(leanZ * from));
            for (int step = 1; step <= crown * 0.7f; step++)
            {
                cells.TryAdd(start + new Int3((int)MathF.Round(MathF.Cos(angle) * step), step / 2, (int)MathF.Round(MathF.Sin(angle) * step)), c.Bark);
            }
        }

        // The crown: a lumpy ball of leaves round the top.
        int reach = crown + 1;
        for (int x = -reach; x <= reach; x++)
        for (int y = -reach; y <= reach; y++)
        for (int z = -reach; z <= reach; z++)
        {
            float wobble = 0.75f + (0.5f * Noise.Value3(x * 0.35f, y * 0.35f, z * 0.35f, seed));
            float distance = MathF.Sqrt((x * x) + (y * y * 1.4f) + (z * z));
            if (distance <= crown * wobble)
            {
                Int3 cell = top + new Int3(x, y, z);
                if (cell.Y > 1)
                {
                    cells.TryAdd(cell, Noise.Hash(x, y, z, seed + 11) < 0.3f ? c.DarkLeaves : c.Leaves);
                }
            }
        }
    }

    private static void Rock(Dictionary<Int3, byte> cells, Colours c, int size, int roughness, int seed)
    {
        float rough = roughness / 100f;
        float frequency = Lerp(0.18f, 0.5f, rough);
        int reach = (int)MathF.Ceiling(size * 1.4f);

        // Sunk a little into the ground, as a stone lies, and flattened underneath.
        int sink = (int)MathF.Round(size * 0.3f);
        for (int x = -reach; x <= reach; x++)
        for (int y = -reach; y <= reach; y++)
        for (int z = -reach; z <= reach; z++)
        {
            float wobble = 1f + (Lerp(0.15f, 0.6f, rough) * (Noise.Fractal3(x * frequency, y * frequency, z * frequency, seed, 2, 0.5f) - 0.5f) * 2f);
            float distance = MathF.Sqrt((x * x) + (y * y * 1.6f) + (z * z));
            if (distance <= size * wobble && y + sink >= 0)
            {
                cells[new Int3(x, y + sink, z)] = Noise.Hash(x, y, z, seed + 5) < 0.35f ? c.DarkStone : c.Stone;
            }
        }
    }

    // ---- Buildings -----------------------------------------------------------------------------

    private const int Storey = 4;

    private static void Building(Dictionary<Int3, byte> cells, Colours c, int width, int depth, int floors, int roof, int seed)
    {
        var random = new Random(seed);
        byte wall = c.Plaster(random.Next(3));
        byte roofColour = c.Roof(random.Next(3));
        int windowEvery = 2 + random.Next(2);
        int height = (floors * Storey) + 1;
        (int x0, int x1) = Across(width);
        (int z0, int z1) = Across(depth);

        for (int y = 0; y < height; y++)
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            bool edgeX = x == x0 || x == x1;
            bool edgeZ = z == z0 || z == z1;
            bool floor = y % Storey == 0;
            if (!(edgeX || edgeZ || floor))
            {
                continue;
            }

            byte colour = edgeX && edgeZ ? c.Trim : wall;

            // Windows two voxels tall on every storey, along each wall, clear of the corners.
            int level = y % Storey;
            if ((edgeX ^ edgeZ) && level is 2 or 3)
            {
                int along = edgeX ? z - z0 : x - x0;
                int length = edgeX ? depth : width;
                if (along > 1 && along < length - 2 && along % windowEvery == 0)
                {
                    colour = c.Glass;
                }
            }

            cells[new Int3(x, y, z)] = colour;
        }

        // A door in the middle of the front, on the ground floor.
        int door = (x0 + x1) / 2;
        for (int y = 1; y <= 3; y++)
        {
            cells[new Int3(door, y, z0)] = c.Door;
        }

        if (roof == 0)
        {
            // Flat, with a low wall round its edge.
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (x == x0 || x == x1 || z == z0 || z == z1)
                {
                    cells[new Int3(x, height, z)] = c.Trim;
                }
            }

            return;
        }

        // Pitched along X: each course a step in from the front and the back, to the ridge.
        for (int step = 0; z0 - 1 + step <= z1 + 1 - step; step++)
        {
            int y = height + step;
            for (int x = x0 - 1; x <= x1 + 1; x++)
            {
                cells[new Int3(x, y, z0 - 1 + step)] = roofColour;
                cells[new Int3(x, y, z1 + 1 - step)] = roofColour;
            }

            // The gable ends, filled in under the roof.
            for (int z = z0 + step; z <= z1 - step; z++)
            {
                cells[new Int3(x0, y, z)] = wall;
                cells[new Int3(x1, y, z)] = wall;
            }
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>The cells a length spans centred on the origin, as the other shapes are.</summary>
    private static (int From, int To) Across(int length) => (-(length / 2), length - (length / 2) - 1);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}

/// <summary>Value noise, smooth and repeatable: the same point and seed give the same number, 0 to 1.</summary>
public static class Noise
{
    /// <summary>A number from 0 to 1 for a lattice point and a seed, the same every time.</summary>
    public static float Hash(int x, int y, int z, int seed)
    {
        uint h = unchecked(((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u) ^ ((uint)z * 0xCB1AB31Fu) ^ ((uint)seed * 0x9E3779B9u));
        h ^= h >> 16;
        h = unchecked(h * 0x7FEB352Du);
        h ^= h >> 15;
        h = unchecked(h * 0x846CA68Bu);
        h ^= h >> 16;
        return (h >> 8) * (1f / 16777216f);
    }

    public static float Value3(float x, float y, float z, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        float fx = Smooth(x - ix), fy = Smooth(y - iy), fz = Smooth(z - iz);

        float Corner(int dx, int dy, int dz) => Hash(ix + dx, iy + dy, iz + dz, seed);

        float x00 = Mix(Corner(0, 0, 0), Corner(1, 0, 0), fx);
        float x10 = Mix(Corner(0, 1, 0), Corner(1, 1, 0), fx);
        float x01 = Mix(Corner(0, 0, 1), Corner(1, 0, 1), fx);
        float x11 = Mix(Corner(0, 1, 1), Corner(1, 1, 1), fx);
        return Mix(Mix(x00, x10, fy), Mix(x01, x11, fy), fz);
    }

    /// <summary>Several octaves of <see cref="Value3"/> on a plane, each finer and fainter, scaled back to 0 to 1.</summary>
    public static float Fractal2(float x, float z, int seed, int octaves, float persistence) => Fractal3(x, 0.5f, z, seed, octaves, persistence);

    public static float Fractal3(float x, float y, float z, int seed, int octaves, float persistence)
    {
        float sum = 0f, amplitude = 1f, total = 0f, frequency = 1f;
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += Value3(x * frequency, y * frequency, z * frequency, seed + (octave * 131)) * amplitude;
            total += amplitude;
            amplitude *= persistence;
            frequency *= 2f;
        }

        return sum / total;
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);
}
