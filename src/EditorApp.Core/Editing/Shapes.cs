using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>A shape the Add menu makes: Blender's Add > Mesh, for a lattice.</summary>
public enum ShapeKind
{
    Voxel,
    Cube,
    Plane,
    Wall,
    Sphere,
    Cylinder,
    Cone,
    Pyramid,
    Torus,
    Stairs,
    Arch,

    /// <summary>Lettering in the built-in pixel font: a sign, a label, a number on a door.</summary>
    Text,
}

/// <summary>One number a shape is made from: what it is called, where it starts, and how far it may go.</summary>
public readonly record struct ShapeField(string Name, int Default, int Min, int Max, string Tooltip);

/// <summary>
/// A shape and what it is made with: its numbers, in the order its fields list them, whether it is
/// only a shell, and — for lettering — what it says.
/// </summary>
public sealed record ShapeSettings(ShapeKind Kind, IReadOnlyList<int> Values, bool Hollow = false, string Text = ShapeSettings.DefaultText)
{
    public const string DefaultText = "TEXT";

    /// <summary>Longer than this and a sign is a wall of letters no one set out to make.</summary>
    public const int MaxTextLength = 64;

    /// <summary>The value of a field, held inside the field's range.</summary>
    public int this[int field]
    {
        get
        {
            IReadOnlyList<ShapeField> fields = Shapes.FieldsOf(Kind);
            int value = field < Values.Count ? Values[field] : fields[field].Default;
            return Math.Clamp(value, fields[field].Min, fields[field].Max);
        }
    }

    /// <summary>The same shape with one number changed.</summary>
    public ShapeSettings With(int field, int value)
    {
        int[] values = [.. Shapes.FieldsOf(Kind).Select((_, i) => this[i])];
        values[field] = value;
        return this with { Values = values };
    }

    public bool Equals(ShapeSettings? other) =>
        other is not null
        && Kind == other.Kind
        && Hollow == other.Hollow
        && Text == other.Text
        && Enumerable.Range(0, Shapes.FieldsOf(Kind).Count).All(i => this[i] == other[i]);

    public override int GetHashCode() => HashCode.Combine(Kind, Hollow, Text, Enumerable.Range(0, Shapes.FieldsOf(Kind).Count).Aggregate(0, (h, i) => HashCode.Combine(h, this[i])));
}

/// <summary>
/// The shapes a new object can start as. Every one stands on its origin: the bottom at the ground
/// plane of its own grid, centred across it — so it is set down on a surface the way a block is put
/// on a table. Round shapes are the voxels whose centres fall inside the round, which is what a
/// voxel artist would lay by hand.
/// </summary>
public static class Shapes
{
    /// <summary>A preset is a start, not a level: a field that could reach a million voxels in one drag would only be a trap.</summary>
    public const int MaxSide = 64;

    /// <summary>In the order the menu lists them.</summary>
    public static readonly ShapeKind[] All =
    [
        ShapeKind.Voxel, ShapeKind.Cube, ShapeKind.Plane, ShapeKind.Wall, ShapeKind.Sphere, ShapeKind.Cylinder,
        ShapeKind.Cone, ShapeKind.Pyramid, ShapeKind.Torus, ShapeKind.Stairs, ShapeKind.Arch, ShapeKind.Text,
    ];

    private static readonly ShapeField[] Lettering =
    [
        new("Size", 1, 1, 8, "Voxels to one pixel of the font: a letter is five pixels wide and seven tall."),
        new("Depth", 2, 1, 16, "How thick the letters stand, in voxels."),
        new("Spacing", 1, 0, 8, "Pixels between letters."),
    ];

    private static readonly ShapeField[] None = [];

    private static readonly ShapeField[] Box =
    [
        new("Width", 4, 1, MaxSide, "Along X, in voxels."),
        new("Height", 4, 1, MaxSide, "Along Y, in voxels."),
        new("Depth", 4, 1, MaxSide, "Along Z, in voxels."),
    ];

    private static readonly ShapeField[] Slab =
    [
        new("Width", 8, 1, MaxSide, "Along X, in voxels."),
        new("Depth", 8, 1, MaxSide, "Along Z, in voxels."),
        new("Thickness", 1, 1, 16, "How thick it is."),
    ];

    private static readonly ShapeField[] Upright =
    [
        new("Length", 8, 1, MaxSide, "Along X, in voxels."),
        new("Height", 4, 1, MaxSide, "Up, in voxels."),
        new("Thickness", 1, 1, 16, "Along Z, in voxels."),
    ];

    private static readonly ShapeField[] Ball =
    [
        new("Radius", 4, 1, MaxSide / 2, "Half its width, in voxels."),
    ];

    private static readonly ShapeField[] Round =
    [
        new("Radius", 3, 1, MaxSide / 2, "Half its width, in voxels."),
        new("Height", 6, 1, MaxSide, "Up, in voxels."),
    ];

    private static readonly ShapeField[] Point =
    [
        new("Radius", 4, 1, MaxSide / 2, "Half the width of its base, in voxels."),
        new("Height", 6, 1, MaxSide, "Up to its point, in voxels."),
    ];

    private static readonly ShapeField[] Steps =
    [
        new("Base", 7, 1, MaxSide, "The width of the bottom step; each one up is a voxel in from every side."),
    ];

    private static readonly ShapeField[] Ring =
    [
        new("Ring", 5, 1, MaxSide / 4, "From the middle to the middle of the tube, in voxels."),
        new("Tube", 2, 1, MaxSide / 8, "Half the thickness of the tube, in voxels."),
    ];

    private static readonly ShapeField[] Flight =
    [
        new("Steps", 4, 1, 32, "How many; each is a voxel up and a voxel along."),
        new("Width", 3, 1, MaxSide, "Across, in voxels."),
    ];

    private static readonly ShapeField[] Doorway =
    [
        new("Width", 7, 3, MaxSide, "Across, pillars included, in voxels."),
        new("Height", 7, 2, MaxSide, "Up, the top included, in voxels."),
        new("Thickness", 1, 1, 16, "Through, in voxels."),
    ];

    public static string NameOf(ShapeKind kind) => kind switch
    {
        ShapeKind.Voxel => "Voxel",
        ShapeKind.Cube => "Cube",
        ShapeKind.Plane => "Plane",
        ShapeKind.Wall => "Wall",
        ShapeKind.Sphere => "Sphere",
        ShapeKind.Cylinder => "Cylinder",
        ShapeKind.Cone => "Cone",
        ShapeKind.Pyramid => "Pyramid",
        ShapeKind.Torus => "Torus",
        ShapeKind.Stairs => "Stairs",
        ShapeKind.Text => "Text",
        _ => "Arch",
    };

    public static IReadOnlyList<ShapeField> FieldsOf(ShapeKind kind) => kind switch
    {
        ShapeKind.Cube => Box,
        ShapeKind.Plane => Slab,
        ShapeKind.Wall => Upright,
        ShapeKind.Sphere => Ball,
        ShapeKind.Cylinder => Round,
        ShapeKind.Cone => Point,
        ShapeKind.Pyramid => Steps,
        ShapeKind.Torus => Ring,
        ShapeKind.Stairs => Flight,
        ShapeKind.Arch => Doorway,
        ShapeKind.Text => Lettering,
        _ => None,
    };

    /// <summary>The shapes that can be a shell one voxel thick instead of solid.</summary>
    public static bool CanBeHollow(ShapeKind kind) => kind is ShapeKind.Cube or ShapeKind.Sphere or ShapeKind.Cylinder;

    public static ShapeSettings Defaults(ShapeKind kind) => new(kind, [.. FieldsOf(kind).Select(f => f.Default)]);

    /// <summary>The shape's voxels, all of one colour.</summary>
    public static VoxelWorld Build(ShapeSettings settings, byte colour)
    {
        var world = new VoxelWorld();
        var cells = new HashSet<Int3>();
        ShapeSettings s = settings;

        switch (s.Kind)
        {
            case ShapeKind.Cube:
                Block(cells, s[0], s[1], s[2]);
                break;

            case ShapeKind.Plane:
                Block(cells, s[0], s[2], s[1]);
                break;

            case ShapeKind.Wall:
                Block(cells, s[0], s[1], s[2]);
                break;

            case ShapeKind.Sphere:
                Sphere(cells, s[0]);
                break;

            case ShapeKind.Cylinder:
                Cylinder(cells, s[0], s[1]);
                break;

            case ShapeKind.Cone:
                Cone(cells, s[0], s[1]);
                break;

            case ShapeKind.Pyramid:
                Pyramid(cells, s[0]);
                break;

            case ShapeKind.Torus:
                Torus(cells, s[0], s[1]);
                break;

            case ShapeKind.Stairs:
                Stairs(cells, s[0], s[1]);
                break;

            case ShapeKind.Arch:
                Arch(cells, s[0], s[1], s[2]);
                break;

            case ShapeKind.Text:
                string text = s.Text.Length > ShapeSettings.MaxTextLength ? s.Text[..ShapeSettings.MaxTextLength] : s.Text;
                cells.UnionWith(VoxelFont.Cells(text.Trim().Length == 0 ? ShapeSettings.DefaultText : text, s[0], s[1], s[2]));
                break;

            default:
                cells.Add(Int3.Zero);
                break;
        }

        bool hollow = s.Hollow && CanBeHollow(s.Kind);
        foreach (Int3 cell in cells)
        {
            if (!hollow || IsSurface(cells, cell))
            {
                world.SetVoxel(cell, colour);
            }
        }

        return world;
    }

    /// <summary>The cells a length covers, centred on zero: -2..1 for four, -2..2 for five.</summary>
    private static (int From, int To) Across(int length) => (-(length / 2), length - (length / 2) - 1);

    private static void Block(HashSet<Int3> cells, int width, int height, int depth)
    {
        (int x0, int x1) = Across(width);
        (int z0, int z1) = Across(depth);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    cells.Add(new Int3(x, y, z));
                }
            }
        }
    }

    private static void Sphere(HashSet<Int3> cells, int radius)
    {
        float limit = radius * radius;

        for (int x = -radius; x < radius; x++)
        {
            for (int y = -radius; y < radius; y++)
            {
                for (int z = -radius; z < radius; z++)
                {
                    if (Square(x + 0.5f) + Square(y + 0.5f) + Square(z + 0.5f) <= limit)
                    {
                        cells.Add(new Int3(x, y + radius, z));
                    }
                }
            }
        }
    }

    private static void Cylinder(HashSet<Int3> cells, int radius, int height)
    {
        for (int y = 0; y < height; y++)
        {
            Disc(cells, radius, radius, y);
        }
    }

    /// <summary>Narrowing layer by layer to its point, never to nothing: the top layer is the four middle voxels.</summary>
    private static void Cone(HashSet<Int3> cells, int radius, int height)
    {
        for (int y = 0; y < height; y++)
        {
            float layer = MathF.Max(radius * (1f - (y / (float)height)), 0.71f);
            Disc(cells, radius, layer, y);
        }
    }

    /// <summary>A round layer: the cells of a square <paramref name="extent"/> each way whose centres lie inside <paramref name="radius"/>.</summary>
    private static void Disc(HashSet<Int3> cells, int extent, float radius, int y)
    {
        float limit = radius * radius;

        for (int x = -extent; x < extent; x++)
        {
            for (int z = -extent; z < extent; z++)
            {
                if (Square(x + 0.5f) + Square(z + 0.5f) <= limit)
                {
                    cells.Add(new Int3(x, y, z));
                }
            }
        }
    }

    /// <summary>Stepped, as a voxel pyramid is: each layer a voxel in from every side of the one under it.</summary>
    private static void Pyramid(HashSet<Int3> cells, int width)
    {
        (int from, int to) = Across(width);

        for (int y = 0; from + y <= to - y; y++)
        {
            for (int x = from + y; x <= to - y; x++)
            {
                for (int z = from + y; z <= to - y; z++)
                {
                    cells.Add(new Int3(x, y, z));
                }
            }
        }
    }

    private static void Torus(HashSet<Int3> cells, int ring, int tube)
    {
        int extent = ring + tube;
        float limit = tube * tube;

        for (int x = -extent; x < extent; x++)
        {
            for (int y = -tube; y < tube; y++)
            {
                for (int z = -extent; z < extent; z++)
                {
                    float fromAxis = MathF.Sqrt(Square(x + 0.5f) + Square(z + 0.5f)) - ring;
                    if (Square(fromAxis) + Square(y + 0.5f) <= limit)
                    {
                        cells.Add(new Int3(x, y + tube, z));
                    }
                }
            }
        }
    }

    /// <summary>Rising along +X, each step solid down to the ground.</summary>
    private static void Stairs(HashSet<Int3> cells, int steps, int width)
    {
        (int x0, _) = Across(steps);
        (int z0, int z1) = Across(width);

        for (int step = 0; step < steps; step++)
        {
            for (int y = 0; y <= step; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    cells.Add(new Int3(x0 + step, y, z));
                }
            }
        }
    }

    /// <summary>A wall with a round-topped opening through it, a voxel of wall left at each side and over the top.</summary>
    private static void Arch(HashSet<Int3> cells, int width, int height, int thickness)
    {
        (int x0, int x1) = Across(width);
        (int z0, int z1) = Across(thickness);

        float middle = x0 + (width * 0.5f);
        float half = (width * 0.5f) - 1f;
        float spring = height - 1 - half;

        for (int x = x0; x <= x1; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float across = x + 0.5f - middle;
                float up = y + 0.5f;
                bool opening = MathF.Abs(across) < half
                    && (up < spring || Square(across) + Square(up - spring) < half * half);

                if (opening)
                {
                    continue;
                }

                for (int z = z0; z <= z1; z++)
                {
                    cells.Add(new Int3(x, y, z));
                }
            }
        }
    }

    /// <summary>Whether a cell has a side open to the outside — what a shell one voxel thick keeps.</summary>
    private static bool IsSurface(HashSet<Int3> cells, Int3 cell)
    {
        for (int f = 0; f < FaceInfo.Count; f++)
        {
            if (!cells.Contains(cell + FaceInfo.Offset((Face)f)))
            {
                return true;
            }
        }

        return false;
    }

    private static float Square(float value) => value * value;
}
