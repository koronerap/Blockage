using System.Numerics;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export.Mimicraft;

/// <summary>What a level is being written as.</summary>
public enum MimicraftTarget
{
    /// <summary>One piece per object, each object's name naming the rig slot it fills.</summary>
    Character,

    /// <summary>Every object merged into one grid under a single weapon id.</summary>
    Weapon,
}

/// <summary>Something that would make Mimicraft refuse the file, named so it can be fixed.</summary>
public readonly record struct MimicraftProblem(string Subject, string Message);

/// <summary>
/// Whether a level can be written at all, decided before anything is.
///
/// Mimicraft's decoders reject rather than repair — a file that breaks one rule is not partly loaded,
/// it is refused, and the only sign is a line in the Unity console. So the answer belongs here, in
/// front of the person who can still move a voxel, and it has to be the same answer the reader would
/// give. Everything below is a rule taken from the reader, not a house style.
/// </summary>
public static class MimicraftValidation
{
    public static IReadOnlyList<MimicraftProblem> Check(VoxelScene scene, MimicraftTarget target)
    {
        var problems = new List<MimicraftProblem>();
        IReadOnlyList<VoxelObject> objects = [.. scene.Objects.Where(o => o.Visible && !o.IsEmpty)];

        if (objects.Count == 0)
        {
            problems.Add(new MimicraftProblem("Level", "Nothing to write: every object is empty or hidden."));
            return problems;
        }

        foreach (VoxelObject o in objects)
        {
            // A rotation cannot come along. The formats carry no placement — a part hangs off a bone
            // and a weapon off its prefab — so a translated object writes exactly the same bytes as
            // an untranslated one, and refusing it would be refusing nothing. A rotated one is
            // different in kind: the shape itself is turned, and a voxel grid cannot hold that
            // without resampling it into a different model.
            if (!IsIdentity(o.Transform.Rotation))
            {
                problems.Add(new MimicraftProblem(
                    o.Name,
                    "Rotated. These formats carry no rotation - set it back to zero, or rebuild the "
                    + "object in the orientation you want it exported in."));
            }
        }

        if (target == MimicraftTarget.Character)
        {
            CheckCharacter(objects, problems);
        }
        else
        {
            CheckWeapon(objects, problems);
        }

        int voxels = objects.Sum(o => o.Grid.SolidCount);
        if (voxels > MimicraftBody.MaxTotalVoxels)
        {
            problems.Add(new MimicraftProblem(
                "Level",
                $"{voxels:N0} voxels, and the reader stops at {MimicraftBody.MaxTotalVoxels:N0}."));
        }

        return problems;
    }

    private static void CheckCharacter(IReadOnlyList<VoxelObject> objects, List<MimicraftProblem> problems)
    {
        if (objects.Count > MimicraftFiles.MaxParts)
        {
            problems.Add(new MimicraftProblem(
                "Level",
                $"{objects.Count} objects, and a character may claim at most {MimicraftFiles.MaxParts} slots."));
        }

        foreach (VoxelObject o in objects)
        {
            CheckBox(o.Name, o.Grid, problems);
            CheckId(o.Name, problems);
        }

        // Two objects with one name would fill the same slot twice; the reader keeps the last and
        // the other is quietly gone, which is worse than being told.
        foreach (IGrouping<string, VoxelObject> group in objects.GroupBy(o => o.Name, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                problems.Add(new MimicraftProblem(
                    group.Key,
                    $"{group.Count()} objects share this name. Each one names a rig slot, so only the "
                    + "last would survive."));
            }
        }
    }

    private static void CheckWeapon(IReadOnlyList<VoxelObject> objects, List<MimicraftProblem> problems)
    {
        // Merged into one grid, so it is the merged extent that has to fit, not each piece's.
        if (!TryMergedBounds(objects, out Int3 min, out Int3 max))
        {
            return;
        }

        Int3 size = max - min + Int3.One;
        CheckExtent("Weapon", size, problems);
    }

    private static void CheckBox(string name, VoxelWorld grid, List<MimicraftProblem> problems)
    {
        if (grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            CheckExtent(name, max - min + Int3.One, problems);
        }
    }

    private static void CheckExtent(string subject, Int3 size, List<MimicraftProblem> problems)
    {
        if (size.X <= MimicraftBody.MaxBoxExtent
            && size.Y <= MimicraftBody.MaxBoxExtent
            && size.Z <= MimicraftBody.MaxBoxExtent)
        {
            return;
        }

        // Named per axis, because "too big" leaves the modeller measuring it themselves.
        var over = new List<string>();
        if (size.X > MimicraftBody.MaxBoxExtent)
        {
            over.Add($"X is {size.X}");
        }

        if (size.Y > MimicraftBody.MaxBoxExtent)
        {
            over.Add($"Y is {size.Y}");
        }

        if (size.Z > MimicraftBody.MaxBoxExtent)
        {
            over.Add($"Z is {size.Z}");
        }

        problems.Add(new MimicraftProblem(
            subject,
            $"{string.Join(", ", over)} - no axis may exceed {MimicraftBody.MaxBoxExtent} voxels."));
    }

    private static void CheckId(string name, List<MimicraftProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add(new MimicraftProblem("Object", "Unnamed. The name is the rig slot it fills."));
            return;
        }

        int bytes = System.Text.Encoding.UTF8.GetByteCount(name);
        if (bytes > MimicraftFiles.MaxIdBytes)
        {
            problems.Add(new MimicraftProblem(
                name,
                $"The name is {bytes} bytes and the limit is {MimicraftFiles.MaxIdBytes}."));
        }
    }

    /// <summary>Bounds of every object together, with each one's translation applied.</summary>
    public static bool TryMergedBounds(IReadOnlyList<VoxelObject> objects, out Int3 min, out Int3 max)
    {
        min = default;
        max = default;
        bool any = false;

        foreach (VoxelObject o in objects)
        {
            if (!o.Grid.TryGetBounds(out Int3 objectMin, out Int3 objectMax))
            {
                continue;
            }

            Int3 offset = Offset(o.Transform.Position);
            objectMin += offset;
            objectMax += offset;

            min = any ? Int3.Min(min, objectMin) : objectMin;
            max = any ? Int3.Max(max, objectMax) : objectMax;
            any = true;
        }

        return any;
    }

    /// <summary>
    /// An object's translation as whole voxels. Only ever used for the weapon path, where several
    /// objects become one grid and their positions relative to each other are the model.
    /// </summary>
    public static Int3 Offset(Vector3 position) => new(
        (int)MathF.Round(position.X),
        (int)MathF.Round(position.Y),
        (int)MathF.Round(position.Z));

    private static bool IsIdentity(Quaternion rotation) =>
        MathF.Abs(MathF.Abs(rotation.W) - 1f) < 1e-4f;
}
