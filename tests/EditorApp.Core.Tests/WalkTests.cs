using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The walker (Fullreleaseplan 7.5): stands on the voxels, climbs steps, is stopped by walls, falls and jumps.</summary>
public class WalkTests
{
    /// <summary>A floor, a wall across it at x = 20, and a step up at x = 10 to z = 5.</summary>
    private static VoxelScene Yard()
    {
        var grid = new VoxelWorld();
        for (int x = -20; x < 40; x++)
        for (int z = -20; z < 20; z++)
        {
            grid.SetVoxel(x, 0, z, 1);
        }

        for (int y = 1; y < 30; y++)
        for (int z = -20; z < 20; z++)
        {
            grid.SetVoxel(20, y, z, 2);
        }

        for (int x = 10; x < 20; x++)
        for (int z = -20; z < 20; z++)
        {
            grid.SetVoxel(x, 1, z, 3);
            grid.SetVoxel(x, 2, z, 3);
        }

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "Yard");
        return scene;
    }

    private static WalkBody Walker(VoxelScene scene) => new(scene, new WalkSettings { VoxelsPerMetre = 10f });

    private static void Walk(WalkBody body, Vector2 wish, float seconds, bool jump = false)
    {
        for (float t = 0f; t < seconds; t += 1f / 60f)
        {
            body.Advance(wish, jump, 0f, 1f / 60f);
            jump = false;
        }
    }

    [Fact]
    public void ItStandsOnTheGroundUnderWhereItStarts()
    {
        WalkBody body = Walker(Yard());

        body.PlaceBelow(new Vector3(0f, 40f, 0f));

        Assert.Equal(1f, body.Feet.Y, 2);
        Assert.True(body.OnGround);
        Assert.Equal(1f + (1.8f * 0.93f * 10f), body.Eye.Y, 2);
    }

    [Fact]
    public void AStepIsClimbedAndAWallIsNot()
    {
        WalkBody body = Walker(Yard());
        body.PlaceBelow(new Vector3(0f, 10f, 0f));

        Walk(body, Vector2.UnitX, 3f);

        // Up the step of two voxels — lower than the walker's 4.5 — and stopped by the wall at 20.
        Assert.Equal(3f, body.Feet.Y, 1);
        Assert.InRange(body.Feet.X, 16.5f, 17.1f);
    }

    [Fact]
    public void AJumpGoesUpAndComesDown()
    {
        WalkBody body = Walker(Yard());
        body.PlaceBelow(new Vector3(0f, 10f, 0f));

        body.Advance(Vector2.Zero, jump: true, 0f, 1f / 60f);
        float highest = body.Feet.Y;
        for (int i = 0; i < 60; i++)
        {
            body.Advance(Vector2.Zero, jump: false, 0f, 1f / 60f);
            highest = MathF.Max(highest, body.Feet.Y);
        }

        // 1.1 m at ten voxels a metre, give or take the frames it is sampled at.
        Assert.InRange(highest - 1f, 9.5f, 11.5f);
        Walk(body, Vector2.Zero, 1f);
        Assert.Equal(1f, body.Feet.Y, 2);
        Assert.True(body.OnGround);
    }

    [Fact]
    public void WithNothingUnderItItFallsToTheGroundPlane()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 1);
        scene.Add(grid, ObjectTransform.At(new Vector3(100f, 0f, 100f)), "Far");
        WalkBody body = Walker(scene);

        body.Feet = new Vector3(0f, 30f, 0f);
        Walk(body, Vector2.Zero, 4f);

        Assert.Equal(0f, body.Feet.Y, 2);
    }
}
