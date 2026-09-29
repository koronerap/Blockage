using EditorApp.Core.Commands;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>What a Sculpt dab does to the voxels under the brush.</summary>
public enum SculptMode
{
    /// <summary>Fills the brush, set against the surface: a blob built up where it is dabbed.</summary>
    Add,

    /// <summary>Empties the brush, sunk into the surface: carving.</summary>
    Remove,

    /// <summary>The surface under the brush pulled out one voxel along its face: terrain raised a layer at a time.</summary>
    Raise,

    /// <summary>Raise the other way: the surface under the brush pushed in one voxel.</summary>
    Lower,

    /// <summary>What stands above the surface's level cut away and what is below it filled: a plane.</summary>
    Flatten,

    /// <summary>Each voxel of the brush becomes what most of its neighbours are: bumps worn down, pits filled.</summary>
    Smooth,
}

/// <summary>The brush's shape.</summary>
public enum SculptShape
{
    Sphere,
    Cube,
}

/// <summary>
/// The Sculpt tool (Fullreleaseplan 3.7): brushes that shape volume along a surface — a deliberate
/// exception to "volume comes from Extrude", for terrain and anything organic, where pulling faces
/// one rectangle at a time is far too slow.
/// </summary>
public static class SculptOperations
{
    public const float MinRadius = 0.5f;

    /// <summary>What a mode does turned round, as Ctrl turns a brush in Blender: Add and Remove, Raise and Lower.</summary>
    public static SculptMode Inverse(SculptMode mode) => mode switch
    {
        SculptMode.Add => SculptMode.Remove,
        SculptMode.Remove => SculptMode.Add,
        SculptMode.Raise => SculptMode.Lower,
        SculptMode.Lower => SculptMode.Raise,
        _ => mode,
    };

    public const float MaxRadius = 24f;

    /// <summary>The cells the brush covers about <paramref name="centre"/>.</summary>
    public static IEnumerable<Int3> Brush(Int3 centre, float radius, SculptShape shape)
    {
        int extent = (int)MathF.Floor(radius);
        float squared = radius * radius;

        for (int dx = -extent; dx <= extent; dx++)
        {
            for (int dy = -extent; dy <= extent; dy++)
            {
                for (int dz = -extent; dz <= extent; dz++)
                {
                    if (shape == SculptShape.Sphere && (dx * dx) + (dy * dy) + (dz * dz) > squared)
                    {
                        continue;
                    }

                    yield return centre + new Int3(dx, dy, dz);
                }
            }
        }
    }

    /// <summary>
    /// One dab of the brush at the surface point <paramref name="hit"/>, written through
    /// <paramref name="command"/> — which repeats it across the mirror planes. Returns how many voxels
    /// changed on the side the cursor is on.
    /// </summary>
    public static int Dab(SculptMode mode, SculptShape shape, float radius, RaycastHit hit, byte colour, VoxelEditCommand command)
    {
        VoxelWorld grid = command.Target;
        Int3 normal = FaceInfo.Offset(hit.Face);
        int changed = 0;

        switch (mode)
        {
            case SculptMode.Add:
                // Centred just outside the surface, so a small brush builds on it rather than into it.
                foreach (Int3 cell in Brush(hit.Voxel + normal, radius, shape))
                {
                    if (!grid.IsSolid(cell) && command.Apply(cell, colour))
                    {
                        changed++;
                    }
                }

                break;

            case SculptMode.Remove:
                foreach (Int3 cell in Brush(hit.Voxel, radius, shape))
                {
                    if (grid.IsSolid(cell) && command.Apply(cell, Palette.EmptyIndex))
                    {
                        changed++;
                    }
                }

                break;

            case SculptMode.Raise:
                // Every face of the surface under the brush that points the way the dabbed one does
                // gets a voxel in front of it: one layer, whatever shape the ground has.
                List<Int3> raised = [];
                foreach (Int3 cell in Brush(hit.Voxel, radius, shape))
                {
                    if (grid.IsSolid(cell) && !grid.IsSolid(cell + normal))
                    {
                        raised.Add(cell + normal);
                    }
                }

                foreach (Int3 cell in raised)
                {
                    if (command.Apply(cell, grid.GetVoxel(cell - normal) is var under && under != Palette.EmptyIndex ? under : colour))
                    {
                        changed++;
                    }
                }

                break;

            case SculptMode.Lower:
                // The voxels of the surface under the brush whose face points the dabbed way.
                List<Int3> lowered = [];
                foreach (Int3 cell in Brush(hit.Voxel, radius, shape))
                {
                    if (grid.IsSolid(cell) && !grid.IsSolid(cell + normal))
                    {
                        lowered.Add(cell);
                    }
                }

                foreach (Int3 cell in lowered)
                {
                    if (command.Apply(cell, Palette.EmptyIndex))
                    {
                        changed++;
                    }
                }

                break;

            case SculptMode.Flatten:
                // The level of the dabbed face, along its normal: above it goes, at and below it fills.
                int axis = FaceInfo.Axis(hit.Face);
                int level = Component(hit.Voxel, axis);
                int sign = Component(normal, axis);

                foreach (Int3 cell in Brush(hit.Voxel, radius, shape))
                {
                    int height = (Component(cell, axis) - level) * sign;
                    byte wanted = height > 0 ? Palette.EmptyIndex : colour;
                    bool solid = grid.IsSolid(cell);

                    if (height > 0 ? solid : !solid)
                    {
                        if (command.Apply(cell, wanted))
                        {
                            changed++;
                        }
                    }
                }

                break;

            case SculptMode.Smooth:
                changed += Smooth(grid, Brush(hit.Voxel, radius, shape), colour, command);
                break;
        }

        return changed;
    }

    /// <summary>
    /// Spikes worn off and holes filled: a voxel touching at most one other face to face goes, and
    /// an empty cell touching four or more fills, in the colour round it. Read from the grid as it
    /// was before any of the brush was written, so the order the cells are visited in cannot change
    /// the result. A thin wall or plate, four neighbours round each voxel, is left alone.
    /// </summary>
    private static int Smooth(VoxelWorld grid, IEnumerable<Int3> cells, byte colour, VoxelEditCommand command)
    {
        var decisions = new List<(Int3 Cell, byte Value)>();

        foreach (Int3 cell in cells)
        {
            int touching = 0;
            byte neighbourColour = Palette.EmptyIndex;

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                byte value = grid.GetVoxel(cell + FaceInfo.Offset((Face)f));
                if (value != Palette.EmptyIndex)
                {
                    touching++;
                    neighbourColour = value;
                }
            }

            bool isSolid = grid.IsSolid(cell);
            if (isSolid && touching <= 1)
            {
                decisions.Add((cell, Palette.EmptyIndex));
            }
            else if (!isSolid && touching >= 4)
            {
                // A filled hole takes the colour round it, not the brush's.
                decisions.Add((cell, neighbourColour != Palette.EmptyIndex ? neighbourColour : colour));
            }
        }

        int changed = 0;
        foreach ((Int3 cell, byte value) in decisions)
        {
            if (command.Apply(cell, value))
            {
                changed++;
            }
        }

        return changed;
    }

    private static int Component(Int3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
