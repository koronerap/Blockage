using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export.Mimicraft;

/// <summary>
/// Turning a level into the pieces a Mimicraft file is made of. The two targets want opposite
/// things from the same scene, which is why this is not one function.
///
/// A <b>character</b> is a set of independent parts: each object stays its own piece, named by the
/// rig slot it fills, and each is written as though its own lowest voxel were the origin. Where the
/// objects sat relative to each other was never part of the file — the bones decide that.
///
/// A <b>weapon</b> is one model that happens to have been built in pieces: every object is merged
/// into a single grid under a single id, and there the objects' positions relative to each other
/// <i>are</i> the model, so the translations are folded in.
/// </summary>
public static class MimicraftScene
{
    public static IReadOnlyList<MimicraftPiece> BuildCharacterParts(VoxelScene scene) =>
        [.. scene.Objects
            .Where(o => o.IsExported && !o.IsEmpty)
            .Select(o => new MimicraftPiece(o.Name, o.Shown))];

    /// <summary>
    /// Every visible object flattened into one grid, each one's translation applied.
    ///
    /// Later objects win where they overlap, which is the same thing the viewport shows: whatever
    /// was drawn on top. Face colours travel with their voxels — the whole point of the per-face
    /// section is that it survives the trip.
    /// </summary>
    public static MimicraftPiece? BuildWeaponPiece(VoxelScene scene, string weaponId)
    {
        IReadOnlyList<VoxelObject> objects = [.. scene.Objects.Where(o => o.IsExported && !o.IsEmpty)];
        if (objects.Count == 0)
        {
            return null;
        }

        // One object, sitting where it was built: nothing to merge, and copying it would only be a
        // chance to get the copy wrong.
        if (objects.Count == 1 && MimicraftValidation.Offset(objects[0].Transform) == Int3.Zero)
        {
            return new MimicraftPiece(weaponId, objects[0].Shown);
        }

        var merged = new VoxelWorld();
        merged.ReplacePalette(scene.Palette);

        foreach (VoxelObject o in objects)
        {
            if (!o.Shown.TryGetBounds(out Int3 min, out Int3 max))
            {
                continue;
            }

            Int3 offset = MimicraftValidation.Offset(o.Transform);

            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    for (int x = min.X; x <= max.X; x++)
                    {
                        if (!o.Shown.IsSolid(x, y, z))
                        {
                            continue;
                        }

                        Int3 to = new Int3(x, y, z) + offset;
                        merged.SetVoxel(to, o.Shown.GetVoxel(x, y, z));

                        for (int f = 0; f < FaceInfo.Count; f++)
                        {
                            byte painted = o.Shown.GetFaceColor(x, y, z, (Face)f);
                            if (painted != o.Shown.GetVoxel(x, y, z))
                            {
                                merged.SetFaceColor(to, (Face)f, painted);
                            }
                        }
                    }
                }
            }
        }

        return new MimicraftPiece(weaponId, merged);
    }
}
