using System.Numerics;
using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests.Mimicraft;

/// <summary>
/// What the export screen refuses. Mimicraft's decoders reject rather than repair, so the answer has
/// to be given here, in front of the person who can still move a voxel.
/// </summary>
public class MimicraftValidationTests
{
    private static VoxelWorld Box(int sizeX, int sizeY, int sizeZ)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        return grid;
    }

    private static VoxelScene SceneOf(params (string Name, VoxelWorld Grid)[] objects)
    {
        var scene = new VoxelScene();
        foreach ((string name, VoxelWorld grid) in objects)
        {
            scene.Add(grid, ObjectTransform.Identity, name);
        }

        return scene;
    }

    [Fact]
    public void AnOrdinaryCharacterPasses()
    {
        VoxelScene scene = SceneOf(("head", Box(6, 6, 6)), ("torso", Box(8, 10, 4)));

        Assert.Empty(MimicraftValidation.Check(scene, MimicraftTarget.Character));
    }

    [Fact]
    public void AnObjectOverSixtyFourOnAnyAxisIsRefused()
    {
        VoxelScene scene = SceneOf(("barrel", Box(4, 4, 72)));

        MimicraftProblem problem = Assert.Single(MimicraftValidation.Check(scene, MimicraftTarget.Character));

        Assert.Equal("barrel", problem.Subject);
        Assert.Contains("Z is 72", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOversizedAxisIsNamed()
    {
        // "Too big" leaves the modeller measuring it themselves.
        VoxelScene scene = SceneOf(("slab", Box(70, 4, 80)));

        MimicraftProblem problem = Assert.Single(MimicraftValidation.Check(scene, MimicraftTarget.Character));

        Assert.Contains("X is 70", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Z is 80", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Y is", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARotatedObjectIsRefused()
    {
        var scene = new VoxelScene();
        scene.Add(
            Box(4, 4, 4),
            new ObjectTransform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f)),
            "door");

        MimicraftProblem problem = Assert.Single(MimicraftValidation.Check(scene, MimicraftTarget.Character));

        Assert.Equal("door", problem.Subject);
        Assert.Contains("Rotated", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMovedObjectIsNotRefused()
    {
        // Neither format carries placement - a part hangs off a bone - so a translated object writes
        // the same bytes as an untranslated one. Refusing it would be refusing nothing, and it would
        // stop a perfectly good model that happens to have two doors side by side.
        var scene = new VoxelScene();
        scene.Add(Box(4, 4, 4), ObjectTransform.Identity, "door_left");
        scene.Add(Box(4, 4, 4), new ObjectTransform(new Vector3(20f, 0f, 0f), Quaternion.Identity), "door_right");

        Assert.Empty(MimicraftValidation.Check(scene, MimicraftTarget.Character));
    }

    [Fact]
    public void TwoObjectsWithOneNameAreRefused()
    {
        // Each name is a rig slot. Two of them and only the last would arrive.
        VoxelScene scene = SceneOf(("arm", Box(2, 2, 2)), ("arm", Box(3, 3, 3)));

        MimicraftProblem problem = Assert.Single(MimicraftValidation.Check(scene, MimicraftTarget.Character));

        Assert.Equal("arm", problem.Subject);
        Assert.Contains("share this name", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyLevelIsRefused()
    {
        Assert.Single(MimicraftValidation.Check(new VoxelScene(), MimicraftTarget.Character));
    }

    [Fact]
    public void AWeaponIsMeasuredAcrossEverythingTogether()
    {
        // The pieces merge into one grid, so it is the whole thing that has to fit - two halves that
        // each pass on their own can still be too long once they are side by side.
        var scene = new VoxelScene();
        scene.Add(Box(40, 4, 4), ObjectTransform.Identity, "front");
        scene.Add(Box(40, 4, 4), new ObjectTransform(new Vector3(40f, 0f, 0f), Quaternion.Identity), "back");

        Assert.Empty(MimicraftValidation.Check(scene, MimicraftTarget.Character));

        MimicraftProblem problem = Assert.Single(MimicraftValidation.Check(scene, MimicraftTarget.Weapon));
        Assert.Contains("X is 80", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MergingAWeaponKeepsThePiecesWhereTheyWerePlaced()
    {
        var scene = new VoxelScene();
        scene.Add(Box(2, 1, 1), ObjectTransform.Identity, "a");
        scene.Add(Box(2, 1, 1), new ObjectTransform(new Vector3(5f, 0f, 0f), Quaternion.Identity), "b");

        MimicraftPiece piece = MimicraftScene.BuildWeaponPiece(scene, "gun")!;
        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([piece], scene.Palette));

        // Two voxels, a gap of three, two more: seven cells across, two runs.
        Assert.Equal(new Int3(7, 1, 1), body.Pieces[0].BoxSize);
        Assert.Equal(4, body.Pieces[0].Voxels.Count);
        Assert.Equal(2, body.Pieces[0].RunCount);
    }

    [Fact]
    public void MergingCarriesPaintedFacesAcross()
    {
        var scene = new VoxelScene();
        var grid = Box(1, 1, 1);
        grid.SetFaceColor(new Int3(0, 0, 0), Face.PosY, 90);

        scene.Add(Box(1, 1, 1), ObjectTransform.Identity, "a");
        scene.Add(grid, new ObjectTransform(new Vector3(3f, 0f, 0f), Quaternion.Identity), "b");

        MimicraftPiece piece = MimicraftScene.BuildWeaponPiece(scene, "gun")!;
        DecodedBody body = MimicraftReader.ReadBody(MimicraftBody.Encode([piece], scene.Palette));

        Assert.Equal(scene.Palette[90], body.Pieces[0].Faces[(3, 2)]);
    }

    [Fact]
    public void TooManyPartsAreRefused()
    {
        var scene = new VoxelScene();
        for (int i = 0; i < 65; i++)
        {
            scene.Add(Box(1, 1, 1), ObjectTransform.Identity, $"part{i}");
        }

        Assert.Contains(
            MimicraftValidation.Check(scene, MimicraftTarget.Character),
            p => p.Message.Contains("at most 64", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOverlongNameIsRefused()
    {
        VoxelScene scene = SceneOf((new string('a', 65), Box(2, 2, 2)));

        Assert.Contains(
            MimicraftValidation.Check(scene, MimicraftTarget.Character),
            p => p.Message.Contains("bytes and the limit", StringComparison.Ordinal));
    }
}
