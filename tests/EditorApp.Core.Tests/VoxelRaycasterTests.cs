using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

public class VoxelRaycasterTests
{
    private static VoxelWorld WorldWith(params Int3[] voxels)
    {
        var world = new VoxelWorld();
        foreach (Int3 voxel in voxels)
        {
            world.SetVoxel(voxel, 1);
        }

        return world;
    }

    [Theory]
    // Ray travelling along +X hits the -X face; the placement cell is one step back.
    [InlineData(1, 0, 0, (int)Face.NegX)]
    [InlineData(-1, 0, 0, (int)Face.PosX)]
    [InlineData(0, 1, 0, (int)Face.NegY)]
    [InlineData(0, -1, 0, (int)Face.PosY)]
    [InlineData(0, 0, 1, (int)Face.NegZ)]
    [InlineData(0, 0, -1, (int)Face.PosZ)]
    public void AxisAlignedRayReportsTheEnteredFace(int dx, int dy, int dz, int expectedFace)
    {
        var target = new Int3(0, 0, 0);
        VoxelWorld world = WorldWith(target);

        var direction = new Vector3(dx, dy, dz);
        Vector3 origin = new Vector3(0.5f, 0.5f, 0.5f) - direction * 6f;
        var ray = new Ray(origin, direction);

        Assert.True(VoxelRaycaster.TryCast(world, ray, out RaycastHit hit));
        Assert.Equal(target, hit.Voxel);
        Assert.Equal((Face)expectedFace, hit.Face);
        Assert.Equal(new Int3(-dx, -dy, -dz), hit.Placement);
        Assert.Equal(5.5f, hit.Distance, 3);
    }

    [Fact]
    public void RayStopsAtTheFirstSolidVoxel()
    {
        VoxelWorld world = WorldWith(new Int3(3, 0, 0), new Int3(7, 0, 0));

        var ray = new Ray(new Vector3(-5f, 0.5f, 0.5f), Vector3.UnitX);

        Assert.True(VoxelRaycaster.TryCast(world, ray, out RaycastHit hit));
        Assert.Equal(new Int3(3, 0, 0), hit.Voxel);
    }

    [Fact]
    public void EmptyWorldIsAMiss()
    {
        var world = new VoxelWorld();
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);

        Assert.False(VoxelRaycaster.TryCast(world, ray, out _));
    }

    [Fact]
    public void MaxDistanceIsRespected()
    {
        VoxelWorld world = WorldWith(new Int3(40, 0, 0));
        var ray = new Ray(new Vector3(0.5f, 0.5f, 0.5f), Vector3.UnitX);

        Assert.False(VoxelRaycaster.TryCast(world, ray, out _, maxDistance: 10f));
        Assert.True(VoxelRaycaster.TryCast(world, ray, out _, maxDistance: 100f));
    }

    [Fact]
    public void DiagonalRayLandsOnTheVoxelItPassesThrough()
    {
        VoxelWorld world = WorldWith(new Int3(5, 5, 5));

        var ray = Ray.Normalized(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));

        Assert.True(VoxelRaycaster.TryCast(world, ray, out RaycastHit hit));
        Assert.Equal(new Int3(5, 5, 5), hit.Voxel);
    }

    [Fact]
    public void RayStartingInsideAVoxelHitsItImmediately()
    {
        VoxelWorld world = WorldWith(new Int3(2, 2, 2));

        var ray = new Ray(new Vector3(2.5f, 2.5f, 2.5f), Vector3.UnitX);

        Assert.True(VoxelRaycaster.TryCast(world, ray, out RaycastHit hit));
        Assert.Equal(new Int3(2, 2, 2), hit.Voxel);
        Assert.Equal(0f, hit.Distance);
        Assert.Equal(Face.NegX, hit.Face);
    }

    [Fact]
    public void GrazingRayAlongASurfaceDoesNotPickTheVoxelBelow()
    {
        // A ray travelling exactly along the top plane of a floor must not report a hit on the
        // floor voxels: it never enters them.
        var world = new VoxelWorld();
        for (int x = 0; x < 10; x++)
        {
            world.SetVoxel(x, 0, 0, 1);
        }

        var ray = new Ray(new Vector3(-1f, 1.0f, 0.5f), Vector3.UnitX);

        Assert.False(VoxelRaycaster.TryCast(world, ray, out _, maxDistance: 20f));
    }

    [Fact]
    public void RayIsCorrectInNegativeCoordinates()
    {
        VoxelWorld world = WorldWith(new Int3(-40, -3, -70));

        var ray = new Ray(new Vector3(-39.5f, 20f, -69.5f), -Vector3.UnitY);

        Assert.True(VoxelRaycaster.TryCast(world, ray, out RaycastHit hit));
        Assert.Equal(new Int3(-40, -3, -70), hit.Voxel);
        Assert.Equal(Face.PosY, hit.Face);
        Assert.Equal(new Int3(-40, -2, -70), hit.Placement);
    }

    [Fact]
    public void GroundPlaneFallbackGivesAPlacementCell()
    {
        var ray = Ray.Normalized(new Vector3(2.5f, 10f, 3.5f), new Vector3(0f, -1f, 0f));

        Assert.True(VoxelRaycaster.TryHitGroundPlane(ray, 0, out Int3 cell));
        Assert.Equal(new Int3(2, 0, 3), cell);
    }

    [Fact]
    public void GroundPlaneBehindTheCameraIsAMiss()
    {
        var ray = Ray.Normalized(new Vector3(0f, 10f, 0f), new Vector3(0f, 1f, 0f));
        Assert.False(VoxelRaycaster.TryHitGroundPlane(ray, 0, out _));
    }

    [Fact]
    public void HitDistanceGrowsMonotonicallyAlongTheRay()
    {
        var world = new VoxelWorld();
        world.SetVoxel(20, 0, 0, 1);

        var near = new Ray(new Vector3(0.5f, 0.5f, 0.5f), Vector3.UnitX);
        var far = new Ray(new Vector3(-10.5f, 0.5f, 0.5f), Vector3.UnitX);

        Assert.True(VoxelRaycaster.TryCast(world, near, out RaycastHit nearHit));
        Assert.True(VoxelRaycaster.TryCast(world, far, out RaycastHit farHit));
        Assert.True(farHit.Distance > nearHit.Distance);
        Assert.Equal(11f, farHit.Distance - nearHit.Distance, 3);
    }
}
