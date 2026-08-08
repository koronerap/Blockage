using EditorApp.Core.Commands;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Paint only ever recolors (EditorApp.md, "Paint"): it never creates a voxel and never removes
/// one, and it only touches faces that can actually be seen from outside.
///
/// It works a face at a time. An edge or corner voxel shows more than one side, and painting the
/// voxel would change all of them at once — so the target is the face under the cursor, and the
/// brush spreads across the surface that face belongs to rather than through the volume.
/// </summary>
public static class PaintOperations
{
    /// <summary>Solid, and with at least one face exposed.</summary>
    public static bool IsVisible(VoxelWorld world, Int3 cell)
    {
        if (!world.IsSolid(cell))
        {
            return false;
        }

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            if (!world.IsSolid(cell + FaceInfo.Offset((Face)f)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when this particular face is on the outside.</summary>
    public static bool IsFaceExposed(VoxelWorld world, Int3 cell, Face face) =>
        world.IsSolid(cell) && !world.IsSolid(cell + FaceInfo.Offset(face));

    /// <summary>
    /// Paints every exposed face pointing the same way as the one under the cursor, within a 3D
    /// euclidean radius of it. Radius 0 is exactly one face.
    ///
    /// Only faces sharing the cursor's direction are painted: a brush aimed at the top of a wall
    /// should not wrap around onto its sides just because they are within reach.
    /// </summary>
    public static int Brush(Int3 centre, Face face, float radius, byte paletteIndex, VoxelEditCommand command)
    {
        VoxelWorld world = command.Target;

        int extent = (int)MathF.Floor(MathF.Max(radius, 0f));
        float radiusSquared = radius * radius;

        int changed = 0;
        for (int dy = -extent; dy <= extent; dy++)
        {
            for (int dz = -extent; dz <= extent; dz++)
            {
                for (int dx = -extent; dx <= extent; dx++)
                {
                    // Euclidean, not a cube: a cube brush at radius 3 would reach 5.2 voxels into
                    // the corners and paint a shape the cursor never suggested.
                    if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                    {
                        continue;
                    }

                    var cell = centre + new Int3(dx, dy, dz);
                    if (IsFaceExposed(world, cell, face) && command.ApplyFace(cell, face, paletteIndex))
                    {
                        changed++;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Bucket fill: the connected run of exposed faces pointing the same way whose colour is within
    /// <paramref name="threshold"/> of the seed's. A threshold of 0 means an exact match.
    /// </summary>
    public static int Bucket(
        Int3 seed,
        Face face,
        byte paletteIndex,
        int threshold,
        VoxelEditCommand command,
        int limit = 2_000_000)
    {
        VoxelWorld world = command.Target;

        if (!IsFaceExposed(world, seed, face))
        {
            return 0;
        }

        Color32 target = world.Palette[world.GetFaceColor(seed, face)];

        int changed = 0;
        foreach (Int3 cell in Surface(world, seed, face, target, threshold, limit))
        {
            if (command.ApplyFace(cell, face, paletteIndex))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// The same connected surface as <see cref="Bucket"/>, but each face takes its colour from a
    /// tiled pattern projected onto the plane it lies in.
    /// </summary>
    public static int Pattern(
        Int3 seed,
        Face face,
        PatternSource pattern,
        int threshold,
        VoxelEditCommand command,
        int limit = 2_000_000)
    {
        VoxelWorld world = command.Target;

        if (!IsFaceExposed(world, seed, face))
        {
            return 0;
        }

        Color32 target = world.Palette[world.GetFaceColor(seed, face)];

        int changed = 0;
        foreach (Int3 cell in Surface(world, seed, face, target, threshold, limit))
        {
            if (command.ApplyFace(cell, face, pattern.Sample(world.Palette, cell, seed, face)))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Walks the connected patch of same-facing, similarly coloured, exposed faces around a seed.
    /// Four-way connected within the face's own plane, which is what keeps a fill on the surface
    /// it started on instead of turning a corner.
    /// </summary>
    private static List<Int3> Surface(
        VoxelWorld world,
        Int3 seed,
        Face face,
        Color32 target,
        int threshold,
        int limit)
    {
        int axis = FaceInfo.Axis(face);

        var found = new List<Int3>();
        var visited = new HashSet<Int3> { seed };
        var queue = new Queue<Int3>();
        queue.Enqueue(seed);

        while (queue.Count > 0 && found.Count < limit)
        {
            Int3 cell = queue.Dequeue();

            if (!IsFaceExposed(world, cell, face)
                || !IsWithinThreshold(world.Palette[world.GetFaceColor(cell, face)], target, threshold))
            {
                continue;
            }

            found.Add(cell);

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                var step = (Face)f;
                if (FaceInfo.Axis(step) == axis)
                {
                    continue;   // stay in the plane
                }

                Int3 neighbour = cell + FaceInfo.Offset(step);
                if (visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Paints a straight line of faces between two cells, brushing at every step of a 3D Bresenham
    /// walk. Applied once from the finished endpoints rather than as the cursor moves — a freehand
    /// path would otherwise leave half-drawn strokes everywhere the cursor happened to pass.
    /// </summary>
    public static int Line(Int3 from, Int3 to, Face face, float radius, byte paletteIndex, VoxelEditCommand command)
    {
        int changed = 0;
        foreach (Int3 cell in Walk(from, to))
        {
            changed += Brush(cell, face, radius, paletteIndex, command);
        }

        return changed;
    }

    /// <summary>Paints the twelve edges of the box spanned by two cells — a hollow frame.</summary>
    public static int BoxFrame(Int3 from, Int3 to, Face face, float radius, byte paletteIndex, VoxelEditCommand command)
    {
        VoxelBox box = VoxelBox.FromCorners(from, to);

        Span<Int3> corners =
        [
            new(box.Min.X, box.Min.Y, box.Min.Z), new(box.Max.X, box.Min.Y, box.Min.Z),
            new(box.Max.X, box.Min.Y, box.Max.Z), new(box.Min.X, box.Min.Y, box.Max.Z),
            new(box.Min.X, box.Max.Y, box.Min.Z), new(box.Max.X, box.Max.Y, box.Min.Z),
            new(box.Max.X, box.Max.Y, box.Max.Z), new(box.Min.X, box.Max.Y, box.Max.Z),
        ];

        int changed = 0;
        for (int i = 0; i < 4; i++)
        {
            changed += Line(corners[i], corners[(i + 1) & 3], face, radius, paletteIndex, command);
            changed += Line(corners[i + 4], corners[((i + 1) & 3) + 4], face, radius, paletteIndex, command);
            changed += Line(corners[i], corners[i + 4], face, radius, paletteIndex, command);
        }

        return changed;
    }

    /// <summary>3D Bresenham: every cell the straight line between two points passes through.</summary>
    public static IEnumerable<Int3> Walk(Int3 from, Int3 to)
    {
        int dx = Math.Abs(to.X - from.X);
        int dy = Math.Abs(to.Y - from.Y);
        int dz = Math.Abs(to.Z - from.Z);

        int sx = Math.Sign(to.X - from.X);
        int sy = Math.Sign(to.Y - from.Y);
        int sz = Math.Sign(to.Z - from.Z);

        Int3 current = from;
        yield return current;

        // Step along whichever axis is longest and carry the error on the other two.
        if (dx >= dy && dx >= dz)
        {
            int errorY = (2 * dy) - dx;
            int errorZ = (2 * dz) - dx;

            for (int i = 0; i < dx; i++)
            {
                if (errorY > 0) { current = current with { Y = current.Y + sy }; errorY -= 2 * dx; }
                if (errorZ > 0) { current = current with { Z = current.Z + sz }; errorZ -= 2 * dx; }

                errorY += 2 * dy;
                errorZ += 2 * dz;
                current = current with { X = current.X + sx };
                yield return current;
            }
        }
        else if (dy >= dz)
        {
            int errorX = (2 * dx) - dy;
            int errorZ = (2 * dz) - dy;

            for (int i = 0; i < dy; i++)
            {
                if (errorX > 0) { current = current with { X = current.X + sx }; errorX -= 2 * dy; }
                if (errorZ > 0) { current = current with { Z = current.Z + sz }; errorZ -= 2 * dy; }

                errorX += 2 * dx;
                errorZ += 2 * dz;
                current = current with { Y = current.Y + sy };
                yield return current;
            }
        }
        else
        {
            int errorX = (2 * dx) - dz;
            int errorY = (2 * dy) - dz;

            for (int i = 0; i < dz; i++)
            {
                if (errorX > 0) { current = current with { X = current.X + sx }; errorX -= 2 * dz; }
                if (errorY > 0) { current = current with { Y = current.Y + sy }; errorY -= 2 * dz; }

                errorX += 2 * dx;
                errorY += 2 * dy;
                current = current with { Z = current.Z + sz };
                yield return current;
            }
        }
    }

    /// <summary>Chebyshev distance in RGB — cheap, and predictable to reason about on a slider.</summary>
    private static bool IsWithinThreshold(Color32 candidate, Color32 target, int threshold) =>
        Math.Abs(candidate.R - target.R) <= threshold
        && Math.Abs(candidate.G - target.G) <= threshold
        && Math.Abs(candidate.B - target.B) <= threshold;

    /// <summary>The eyedropper: the colour of the face under the cursor, or null if there is none.</summary>
    public static byte? Sample(VoxelWorld world, Int3 cell, Face face)
    {
        byte index = world.GetFaceColor(cell, face);
        return index == Palette.EmptyIndex ? null : index;
    }
}
