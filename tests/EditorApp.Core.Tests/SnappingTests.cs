using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The world side of snapping: boxes, their corners and edge middles, and standing on a surface.</summary>
public class SnappingTests
{
    private static VoxelWorld Cube(int side)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, 5);
                }
            }
        }

        return grid;
    }

    [Fact]
    public void ABoxHasEightCornersAndTwelveEdgeMiddlesInTheWorld()
    {
        var scene = new VoxelScene();
        VoxelObject cube = scene.Add(Cube(2), new ObjectTransform(new Vector3(10f, 0f, 0f), Quaternion.Identity, 0.5f));

        Vector3[] corners = Snapping.Corners(cube, cube.Transform);
        Vector3[] edges = Snapping.EdgeCentres(cube, cube.Transform);

        Assert.Equal(8, corners.Distinct().Count());
        Assert.Contains(new Vector3(10f, 0f, 0f), corners);
        Assert.Contains(new Vector3(11f, 1f, 1f), corners);
        Assert.Equal(12, edges.Distinct().Count());
        Assert.Contains(new Vector3(10.5f, 0f, 0f), edges);
        Assert.Equal(new Vector3(10.5f, 0.5f, 0.5f), Snapping.Centre(cube, cube.Transform));
    }

    /// <summary>Set down on a floor, a box stands on its bottom; against a wall facing +X, on its -X side.</summary>
    [Fact]
    public void TheContactPointIsTheSideThatFacesTheSurface()
    {
        var scene = new VoxelScene();
        VoxelObject cube = scene.Add(Cube(2), ObjectTransform.Identity);

        Assert.Equal(new Vector3(1f, 0f, 1f), Snapping.ContactPoint(cube, cube.Transform, Vector3.UnitY));
        Assert.Equal(new Vector3(0f, 1f, 1f), Snapping.ContactPoint(cube, cube.Transform, Vector3.UnitX));
        Assert.Equal(new Vector3(1f, 2f, 1f), Snapping.ContactPoint(cube, cube.Transform, -Vector3.UnitY));
    }

    [Fact]
    public void TheClosestBaseIsTheCornerNearestTheTarget()
    {
        var scene = new VoxelScene();
        VoxelObject cube = scene.Add(Cube(2), ObjectTransform.Identity);

        Vector3 based = Snapping.BasePoint(cube, cube.Transform, SnapBase.Closest, new Vector3(5f, 5f, -5f));

        Assert.Equal(new Vector3(2f, 2f, 0f), based);
        Assert.Equal(new Vector3(1f, 1f, 1f), Snapping.BasePoint(cube, cube.Transform, SnapBase.Center, Vector3.Zero));
        Assert.Equal(Vector3.Zero, Snapping.BasePoint(cube, cube.Transform, SnapBase.Origin, new Vector3(5f)));
    }

    [Fact]
    public void StandingOnAWallTurnsUpToFaceOutOfIt()
    {
        Quaternion standing = Snapping.Standing(Quaternion.Identity, Vector3.UnitX);

        Vector3 up = Vector3.Transform(Vector3.UnitY, standing);
        Assert.True(Vector3.Distance(up, Vector3.UnitX) < 1e-4f);
        Assert.Equal(Quaternion.Identity, Snapping.Standing(Quaternion.Identity, Vector3.UnitY));

        // Upside down, not left as it was.
        Vector3 under = Vector3.Transform(Vector3.UnitY, Snapping.Standing(Quaternion.Identity, -Vector3.UnitY));
        Assert.True(Vector3.Distance(under, -Vector3.UnitY) < 1e-4f);
    }

    [Fact]
    public void TargetsAreOnlyWhatMaySnapBeTo()
    {
        var scene = new VoxelScene();
        VoxelObject moved = scene.Add(Cube(1), ObjectTransform.Identity, "moved");
        VoxelObject other = scene.Add(Cube(1), ObjectTransform.At(new Vector3(5f, 0f, 0f)), "other");
        VoxelObject hidden = scene.Add(Cube(1), ObjectTransform.At(new Vector3(10f, 0f, 0f)), "hidden");
        VoxelObject locked = scene.Add(Cube(1), ObjectTransform.At(new Vector3(15f, 0f, 0f)), "locked");
        hidden.Visible = false;
        locked.Locked = true;

        var settings = new SnapSettings { Targets = SnapTarget.Corner };
        VoxelObject[] owners = [.. Snapping.TargetPoints(scene, settings, o => o == moved).Select(t => t.Owner).Distinct()];

        Assert.Equal([other], owners);

        settings.ExcludeLocked = false;
        Assert.Contains(locked, Snapping.TargetPoints(scene, settings, o => o == moved).Select(t => t.Owner));
    }

    /// <summary>A surface under the cursor is found behind the moved thing, never on it.</summary>
    [Fact]
    public void TheSurfaceIsFoundThroughTheMovedThing()
    {
        var scene = new VoxelScene();
        VoxelObject moved = scene.Add(Cube(2), ObjectTransform.At(new Vector3(0f, 5f, 0f)), "moved");
        VoxelObject floor = scene.Add(Cube(4), ObjectTransform.At(new Vector3(-1f, 0f, -1f)), "floor");
        var ray = new Ray(new Vector3(1f, 20f, 1f), -Vector3.UnitY);

        Assert.True(Snapping.TrySurface(scene, ray, new SnapSettings(), o => o == moved, out Vector3 point, out Vector3 normal, out VoxelObject? owner));

        Assert.Equal(floor, owner);
        Assert.Equal(4f, point.Y, 3);
        Assert.Equal(Vector3.UnitY, normal);
    }

    [Fact]
    public void SettingsSwitchTargetsOneAtATimeAndKeepRotationInRange()
    {
        var settings = new SnapSettings();
        Assert.False(settings.Enabled);
        Assert.Equal(SnapTarget.Increment, settings.Targets);

        settings.Set(SnapTarget.Surface, true);
        settings.Set(SnapTarget.Increment, false);
        Assert.Equal(SnapTarget.Surface, settings.Targets);

        settings.RotationIncrement = 0f;
        Assert.Equal(SnapSettings.MinRotationIncrement, settings.RotationIncrement);
    }
}
