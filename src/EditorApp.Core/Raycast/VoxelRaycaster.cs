using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Raycast;

/// <summary>What a picking ray hit.</summary>
/// <param name="Voxel">The solid voxel that was hit — the target for delete, paint and eyedropper.</param>
/// <param name="Face">The face it entered through.</param>
/// <param name="Distance">Distance along the ray, in voxels.</param>
public readonly record struct RaycastHit(Int3 Voxel, Face Face, float Distance)
{
    /// <summary>The empty cell in front of the hit face — where a new voxel goes.</summary>
    public Int3 Placement => Voxel + FaceInfo.Offset(Face);
}

/// <summary>
/// Amanatides-Woo grid traversal (EditorApp.md §5). Walking the ray through the voxel grid on the
/// CPU beats casting against the mesh: it is independent of how the world happens to be meshed, it
/// answers exactly the question the tools ask — which voxel, which face — and it removes any need
/// for a triangle-to-voxel side table.
/// </summary>
public static class VoxelRaycaster
{
    public const float DefaultMaxDistance = 512f;

    public static bool TryCast(VoxelWorld world, Ray ray, out RaycastHit hit, float maxDistance = DefaultMaxDistance)
    {
        hit = default;

        Vector3 direction = ray.Direction;
        if (direction.LengthSquared() < 1e-12f)
        {
            return false;
        }

        int x = (int)MathF.Floor(ray.Origin.X);
        int y = (int)MathF.Floor(ray.Origin.Y);
        int z = (int)MathF.Floor(ray.Origin.Z);

        // A camera sitting inside geometry still needs an answer; report the face most opposed to
        // the view direction so the highlight lands somewhere sensible.
        if (world.IsSolid(x, y, z))
        {
            hit = new RaycastHit(new Int3(x, y, z), MostOpposedFace(direction), 0f);
            return true;
        }

        int stepX = Math.Sign(direction.X);
        int stepY = Math.Sign(direction.Y);
        int stepZ = Math.Sign(direction.Z);

        float tMaxX = FirstBoundary(ray.Origin.X, direction.X, x, stepX);
        float tMaxY = FirstBoundary(ray.Origin.Y, direction.Y, y, stepY);
        float tMaxZ = FirstBoundary(ray.Origin.Z, direction.Z, z, stepZ);

        float tDeltaX = stepX != 0 ? MathF.Abs(1f / direction.X) : float.PositiveInfinity;
        float tDeltaY = stepY != 0 ? MathF.Abs(1f / direction.Y) : float.PositiveInfinity;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(1f / direction.Z) : float.PositiveInfinity;

        while (true)
        {
            float distance;
            Face enteredFace;

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                x += stepX;
                distance = tMaxX;
                tMaxX += tDeltaX;
                enteredFace = stepX > 0 ? Face.NegX : Face.PosX;
            }
            else if (tMaxY <= tMaxZ)
            {
                y += stepY;
                distance = tMaxY;
                tMaxY += tDeltaY;
                enteredFace = stepY > 0 ? Face.NegY : Face.PosY;
            }
            else
            {
                z += stepZ;
                distance = tMaxZ;
                tMaxZ += tDeltaZ;
                enteredFace = stepZ > 0 ? Face.NegZ : Face.PosZ;
            }

            if (distance > maxDistance || float.IsInfinity(distance))
            {
                return false;
            }

            if (world.IsSolid(x, y, z))
            {
                hit = new RaycastHit(new Int3(x, y, z), enteredFace, distance);
                return true;
            }
        }
    }

    /// <summary>
    /// Where the ray meets the Y = <paramref name="planeY"/> plane, as the cell just above it.
    /// The fallback target when nothing is hit, so the first voxel of an empty level has somewhere
    /// to go.
    /// </summary>
    public static bool TryHitGroundPlane(Ray ray, int planeY, out Int3 cell, float maxDistance = DefaultMaxDistance)
    {
        cell = default;

        if (MathF.Abs(ray.Direction.Y) < 1e-6f)
        {
            return false;
        }

        float distance = (planeY - ray.Origin.Y) / ray.Direction.Y;
        if (distance < 0f || distance > maxDistance)
        {
            return false;
        }

        Vector3 point = ray.PointAt(distance);
        cell = new Int3((int)MathF.Floor(point.X), planeY, (int)MathF.Floor(point.Z));
        return true;
    }

    private static float FirstBoundary(float origin, float direction, int voxel, int step)
    {
        if (step == 0)
        {
            return float.PositiveInfinity;
        }

        float boundary = step > 0 ? voxel + 1 : voxel;
        return (boundary - origin) / direction;
    }

    private static Face MostOpposedFace(Vector3 direction)
    {
        float ax = MathF.Abs(direction.X);
        float ay = MathF.Abs(direction.Y);
        float az = MathF.Abs(direction.Z);

        if (ax >= ay && ax >= az)
        {
            return direction.X > 0 ? Face.NegX : Face.PosX;
        }

        return ay >= az
            ? direction.Y > 0 ? Face.NegY : Face.PosY
            : direction.Z > 0 ? Face.NegZ : Face.PosZ;
    }
}
