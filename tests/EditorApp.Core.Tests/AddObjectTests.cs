using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The shapes the Add menu makes: each stands on its origin, centred, the size it says.</summary>
public class ShapeTests
{
    public static TheoryData<ShapeKind> Kinds() => [.. Shapes.All];

    private static VoxelWorld Build(ShapeKind kind, params int[] values) =>
        Shapes.Build(values.Length == 0 ? Shapes.Defaults(kind) : new ShapeSettings(kind, values), Palette.WhiteIndex);

    private static (Int3 Min, Int3 Max) Bounds(VoxelWorld world)
    {
        Assert.True(world.TryGetBounds(out Int3 min, out Int3 max));
        return (min, max);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryShapeStandsOnItsOriginCentred(ShapeKind kind)
    {
        (Int3 min, Int3 max) = Bounds(Build(kind));

        Assert.Equal(0, min.Y);
        Assert.InRange(min.X + max.X, -1, 0);
        Assert.InRange(min.Z + max.Z, -1, 0);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryShapeIsListedWithANameAndDefaultsInRange(ShapeKind kind)
    {
        Assert.False(string.IsNullOrWhiteSpace(Shapes.NameOf(kind)));
        foreach (ShapeField field in Shapes.FieldsOf(kind))
        {
            Assert.InRange(field.Default, field.Min, field.Max);
        }
    }

    [Fact]
    public void BoxesAreTheSizeTheySay()
    {
        Assert.Equal(1, Build(ShapeKind.Voxel).SolidCount);
        Assert.Equal(2 * 3 * 5, Build(ShapeKind.Cube, 2, 3, 5).SolidCount);
        Assert.Equal(6 * 4 * 2, Build(ShapeKind.Plane, 6, 4, 2).SolidCount);
        Assert.Equal((new Int3(-3, 0, -2), new Int3(2, 1, 1)), Bounds(Build(ShapeKind.Plane, 6, 4, 2)));
        Assert.Equal((new Int3(-4, 0, 0), new Int3(3, 4, 0)), Bounds(Build(ShapeKind.Wall, 8, 5, 1)));
    }

    /// <summary>Hollow keeps the outside a voxel thick and nothing within.</summary>
    [Fact]
    public void AHollowShapeIsItsShell()
    {
        VoxelWorld solid = Shapes.Build(new ShapeSettings(ShapeKind.Cube, [4, 4, 4]), 5);
        VoxelWorld shell = Shapes.Build(new ShapeSettings(ShapeKind.Cube, [4, 4, 4], Hollow: true), 5);

        Assert.Equal(64, solid.SolidCount);
        Assert.Equal(64 - 8, shell.SolidCount);
        Assert.False(shell.IsSolid(new Int3(0, 1, 0)));

        VoxelWorld ball = Shapes.Build(new ShapeSettings(ShapeKind.Sphere, [5], Hollow: true), 5);
        Assert.False(ball.IsSolid(new Int3(0, 5, 0)));
        Assert.True(ball.SolidCount < Build(ShapeKind.Sphere, 5).SolidCount);
    }

    [Fact]
    public void HollowIsOnlyForShapesWithAnInside()
    {
        VoxelWorld stairs = Shapes.Build(new ShapeSettings(ShapeKind.Stairs, [4, 3], Hollow: true), 5);

        Assert.Equal(Build(ShapeKind.Stairs, 4, 3).SolidCount, stairs.SolidCount);
    }

    [Fact]
    public void ASphereIsRoundAndTheWidthOfTwoRadii()
    {
        VoxelWorld sphere = Build(ShapeKind.Sphere, 4);

        Assert.Equal((new Int3(-4, 0, -4), new Int3(3, 7, 3)), Bounds(sphere));

        // The same seen from every side: mirrored across each axis, nothing changes.
        for (int x = -4; x < 4; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                for (int z = -4; z < 4; z++)
                {
                    bool here = sphere.IsSolid(new Int3(x, y, z));
                    Assert.Equal(here, sphere.IsSolid(new Int3(-1 - x, y, z)));
                    Assert.Equal(here, sphere.IsSolid(new Int3(x, 7 - y, z)));
                    Assert.Equal(here, sphere.IsSolid(new Int3(z, y, x)));
                }
            }
        }

        Assert.False(sphere.IsSolid(new Int3(-4, 0, -4)));
        Assert.True(sphere.IsSolid(new Int3(0, 0, 0)));

        // Round up and down too, not only across: at the bottom the edge is empty, at the middle full.
        Assert.False(sphere.IsSolid(new Int3(3, 0, 0)));
        Assert.True(sphere.IsSolid(new Int3(3, 4, 0)));
    }

    [Fact]
    public void ACylinderIsTheSameAllTheWayUpAndAConeNarrows()
    {
        VoxelWorld cylinder = Build(ShapeKind.Cylinder, 3, 5);
        VoxelWorld cone = Build(ShapeKind.Cone, 4, 6);

        int[] cylinderLayers = [.. Enumerable.Range(0, 5).Select(y => Layer(cylinder, y, 3))];
        int[] coneLayers = [.. Enumerable.Range(0, 6).Select(y => Layer(cone, y, 4))];

        Assert.All(cylinderLayers, count => Assert.Equal(cylinderLayers[0], count));
        Assert.Equal(5, Bounds(cylinder).Max.Y + 1);
        for (int y = 1; y < coneLayers.Length; y++)
        {
            Assert.True(coneLayers[y] <= coneLayers[y - 1], $"layer {y} of the cone is wider than the one under it");
        }

        Assert.Equal(4, coneLayers[^1]);
    }

    private static int Layer(VoxelWorld world, int y, int extent)
    {
        int count = 0;
        for (int x = -extent; x < extent; x++)
        {
            for (int z = -extent; z < extent; z++)
            {
                count += world.IsSolid(new Int3(x, y, z)) ? 1 : 0;
            }
        }

        return count;
    }

    [Fact]
    public void APyramidStepsInAVoxelAtEveryLayer()
    {
        VoxelWorld pyramid = Build(ShapeKind.Pyramid, 7);

        Assert.Equal(49 + 25 + 9 + 1, pyramid.SolidCount);
        Assert.True(pyramid.IsSolid(new Int3(0, 3, 0)));
        Assert.False(pyramid.IsSolid(new Int3(1, 3, 0)));
    }

    [Fact]
    public void ATorusHasAHole()
    {
        VoxelWorld torus = Build(ShapeKind.Torus, 5, 2);

        Assert.False(torus.IsSolid(new Int3(0, 2, 0)));
        Assert.True(torus.IsSolid(new Int3(5, 2, 0)));
        Assert.Equal(3, Bounds(torus).Max.Y);
    }

    [Fact]
    public void StairsRiseAVoxelAStep()
    {
        VoxelWorld stairs = Build(ShapeKind.Stairs, 4, 3);

        Assert.Equal((1 + 2 + 3 + 4) * 3, stairs.SolidCount);
        Assert.True(stairs.IsSolid(new Int3(1, 3, 0)));
        Assert.False(stairs.IsSolid(new Int3(0, 3, 0)));
    }

    [Fact]
    public void AnArchHasAWayThroughUnderASolidTop()
    {
        VoxelWorld arch = Build(ShapeKind.Arch, 7, 7, 1);

        Assert.False(arch.IsSolid(new Int3(0, 0, 0)));
        Assert.False(arch.IsSolid(new Int3(0, 4, 0)));
        Assert.True(arch.IsSolid(new Int3(-3, 0, 0)));
        Assert.True(arch.IsSolid(new Int3(3, 0, 0)));
        Assert.True(arch.IsSolid(new Int3(0, 6, 0)));
    }

    [Fact]
    public void NumbersAreHeldInsideTheirFields()
    {
        var huge = new ShapeSettings(ShapeKind.Cube, [1000, -5, 3]);

        Assert.Equal(Shapes.MaxSide, huge[0]);
        Assert.Equal(1, huge[1]);
        Assert.Equal(Shapes.MaxSide * 3, Shapes.Build(huge, 5).SolidCount);
    }

    [Fact]
    public void ChangingOneNumberKeepsTheOthers()
    {
        ShapeSettings cube = new ShapeSettings(ShapeKind.Cube, [2, 3, 5], Hollow: true).With(1, 9);

        Assert.Equal([2, 9, 5], new[] { cube[0], cube[1], cube[2] });
        Assert.Equal(new ShapeSettings(ShapeKind.Cube, [2, 9, 5], Hollow: true), cube);
        Assert.NotEqual(new ShapeSettings(ShapeKind.Cube, [2, 9, 5]), cube);
    }
}

/// <summary>The props: made, standing, and in the level's own colours.</summary>
public class PropPresetTests
{
    public static TheoryData<PropKind> Kinds() => [.. PropPresets.All];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryPropStandsOnItsOriginInMoreThanOneColour(PropKind kind)
    {
        Palette palette = Palette.CreateDefault();
        VoxelWorld prop = PropPresets.Build(kind, palette);

        Assert.True(prop.TryGetBounds(out Int3 min, out Int3 max));
        Assert.Equal(0, min.Y);
        Assert.True(max.Y > 0);
        Assert.False(string.IsNullOrWhiteSpace(PropPresets.NameOf(kind)));

        var colours = new HashSet<byte>();
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    byte index = prop.GetVoxel(new Int3(x, y, z));
                    if (index != Palette.EmptyIndex)
                    {
                        colours.Add(index);
                        Assert.NotEqual(0, palette[index].A);
                    }
                }
            }
        }

        Assert.True(colours.Count >= 2, $"{kind} is in one colour");
    }

    [Fact]
    public void TheNearestColourIsTheColourWhenThePaletteHasIt()
    {
        Palette palette = Palette.CreateDefault();

        Assert.Equal(Palette.WhiteIndex, palette.Nearest(new Color32(255, 255, 255)));
        Assert.Equal(palette[40], palette[palette.Nearest(palette[40])]);

        // A green finds a green, not the grey of the same brightness.
        Color32 green = palette[palette.Nearest(new Color32(70, 140, 60))];
        Assert.True(green.G > green.R && green.G > green.B);
    }
}

/// <summary>Where a new object is set down: against the surface it was added at, on the lattice.</summary>
public class PlacementTests
{
    private static readonly VoxelWorld Cube = Shapes.Build(new ShapeSettings(ShapeKind.Cube, [4, 2, 4]), 5);

    private static (Vector3 Min, Vector3 Max) WorldBox(ObjectTransform at)
    {
        Assert.True(Cube.TryGetBounds(out Int3 min, out Int3 max));
        return (at.Position + (min.ToVector3() * at.VoxelSize), at.Position + ((max + Int3.One).ToVector3() * at.VoxelSize));
    }

    [Fact]
    public void OnATopItStandsCentredOnThePoint()
    {
        ObjectTransform at = Placement.Against(Cube, new Vector3(3f, 5f, -2f), Vector3.UnitY, 1f);
        (Vector3 min, Vector3 max) = WorldBox(at);

        Assert.Equal(5f, min.Y);
        Assert.Equal(3f, (min.X + max.X) * 0.5f);
        Assert.Equal(-2f, (min.Z + max.Z) * 0.5f);
    }

    [Fact]
    public void AgainstASideItTouchesIt()
    {
        (Vector3 min, _) = WorldBox(Placement.Against(Cube, new Vector3(6f, 3f, 0f), Vector3.UnitX, 1f));
        (_, Vector3 max) = WorldBox(Placement.Against(Cube, new Vector3(0f, 3f, -6f), -Vector3.UnitZ, 1f));

        Assert.Equal(6f, min.X);
        Assert.Equal(-6f, max.Z);
    }

    [Fact]
    public void UnderACeilingItHangs()
    {
        (_, Vector3 max) = WorldBox(Placement.Against(Cube, new Vector3(0f, 10f, 0f), -Vector3.UnitY, 1f));

        Assert.Equal(10f, max.Y);
    }

    [Fact]
    public void ItLandsOnItsOwnLattice()
    {
        ObjectTransform at = Placement.Against(Cube, new Vector3(0.9f, 2.2f, 1.26f), Vector3.UnitY, 0.5f);

        Assert.Equal(0.5f, at.VoxelSize);
        Assert.Equal(at.Position / 0.5f, new Vector3(MathF.Round(at.Position.X / 0.5f), MathF.Round(at.Position.Y / 0.5f), MathF.Round(at.Position.Z / 0.5f)));
        Assert.Equal(Quaternion.Identity, at.Rotation);
    }
}

/// <summary>Adding from the Add menu, and adjusting what was added.</summary>
public class AddObjectTests
{
    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceScene(VoxelScene.CreateStarter(), projectPath: null);
        return session;
    }

    [Fact]
    public void AShapeIsAddedOnTheSurfaceFocusedAndOneStep()
    {
        EditorSession session = Session();
        int steps = session.History.UndoCount;

        VoxelObject sphere = session.AddShape(Shapes.Defaults(ShapeKind.Sphere), new Vector3(0f, 8f, 0f), Vector3.UnitY, 1f);

        Assert.Equal("Sphere", sphere.Name);
        Assert.Equal(sphere.Id, session.Scene.FocusId);
        Assert.Equal(steps + 1, session.History.UndoCount);
        Assert.True(sphere.TryGetWorldBounds(out Vector3 min, out _));
        Assert.Equal(8f, min.Y, 3);
        Assert.True(session.HasUnsavedChanges);

        session.Undo();
        Assert.Null(session.Scene.Find(sphere.Id));
    }

    [Fact]
    public void ASecondOfTheSameNameIsNumbered()
    {
        EditorSession session = Session();

        session.AddShape(Shapes.Defaults(ShapeKind.Cube), Vector3.Zero, Vector3.UnitY, 1f);
        VoxelObject second = session.AddShape(Shapes.Defaults(ShapeKind.Cube), Vector3.Zero, Vector3.UnitY, 1f);

        Assert.Equal("Cube.001", second.Name);
    }

    [Fact]
    public void AShapeIsInTheColourInHandAndTheSizeOfWhatIsWorkedOn()
    {
        EditorSession session = Session();
        session.ActiveColorIndex = 40;

        VoxelObject added = session.AddShape(Shapes.Defaults(ShapeKind.Voxel), Vector3.Zero, Vector3.UnitY, 0.25f);

        Assert.Equal(40, added.Grid.GetVoxel(Int3.Zero));
        Assert.Equal(0.25f, added.VoxelSize);
    }

    /// <summary>Adjusting remakes the same object in the same step: one undo takes all of it away.</summary>
    [Fact]
    public void AdjustingRemakesItInPlace()
    {
        EditorSession session = Session();
        VoxelObject cube = session.AddShape(Shapes.Defaults(ShapeKind.Cube), new Vector3(0f, 8f, 0f), Vector3.UnitY, 1f);
        int steps = session.History.UndoCount;

        Assert.True(session.ReshapeLast(new ShapeSettings(ShapeKind.Cube, [6, 3, 2])));

        Assert.Same(cube, session.Scene.Find(cube.Id));
        Assert.Equal(36, cube.Grid.SolidCount);
        Assert.Equal(steps, session.History.UndoCount);
        Assert.True(cube.TryGetWorldBounds(out Vector3 min, out _));
        Assert.Equal(8f, min.Y, 3);
        Assert.Equal(6, session.LastShape!.Settings[0]);

        session.Undo();
        Assert.Null(session.Scene.Find(cube.Id));
        session.Redo();
        Assert.Equal(36, session.Scene.Find(cube.Id)!.Grid.SolidCount);
    }

    /// <summary>Against a wall, a shape made wider stays against the wall rather than growing into it.</summary>
    [Fact]
    public void AdjustedAgainstASideItStaysAgainstIt()
    {
        EditorSession session = Session();
        VoxelObject cube = session.AddShape(Shapes.Defaults(ShapeKind.Cube), new Vector3(10f, 2f, 0f), Vector3.UnitX, 1f);

        session.ReshapeLast(new ShapeSettings(ShapeKind.Cube, [8, 4, 4]));

        Assert.True(cube.TryGetWorldBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(10f, min.X, 3);
        Assert.Equal(18f, max.X, 3);
    }

    [Fact]
    public void OnceSomethingElseIsDoneThereIsNothingToAdjust()
    {
        EditorSession session = Session();
        VoxelObject cube = session.AddShape(Shapes.Defaults(ShapeKind.Cube), Vector3.Zero, Vector3.UnitY, 1f);
        Assert.NotNull(session.LastShape);

        ObjectTransform before = cube.Transform;
        session.ApplyTransform(cube, cube.Transform.Translated(Vector3.UnitX));
        session.PushTransformEdit(cube, before, "Move Cube");

        Assert.Null(session.LastShape);
        Assert.False(session.ReshapeLast(new ShapeSettings(ShapeKind.Cube, [2, 2, 2])));
        Assert.Equal(64, cube.Grid.SolidCount);
    }

    /// <summary>A locked object is not changed, by the Adjust panel any more than by a tool.</summary>
    [Fact]
    public void ALockedShapeIsNotAdjusted()
    {
        EditorSession session = Session();
        VoxelObject cube = session.AddShape(Shapes.Defaults(ShapeKind.Cube), Vector3.Zero, Vector3.UnitY, 1f);

        session.SetObjectLocked(cube.Id, true);

        Assert.Null(session.LastShape);
        Assert.False(session.ReshapeLast(new ShapeSettings(ShapeKind.Cube, [2, 2, 2])));
        Assert.Equal(64, cube.Grid.SolidCount);
    }

    [Fact]
    public void UndoneThereIsNothingToAdjust()
    {
        EditorSession session = Session();
        session.AddShape(Shapes.Defaults(ShapeKind.Cube), Vector3.Zero, Vector3.UnitY, 1f);

        session.Undo();

        Assert.Null(session.LastShape);
    }

    [Fact]
    public void AnotherKindIsNotAnAdjustment()
    {
        EditorSession session = Session();
        session.AddShape(Shapes.Defaults(ShapeKind.Cube), Vector3.Zero, Vector3.UnitY, 1f);

        Assert.False(session.ReshapeLast(Shapes.Defaults(ShapeKind.Sphere)));
    }

    [Fact]
    public void APropIsAddedLikeAShapeButNotForAdjusting()
    {
        EditorSession session = Session();

        VoxelObject crate = session.AddObject(PropPresets.Build(PropKind.Crate, session.Scene.Palette), "Crate", new Vector3(10f, 0f, 0f), Vector3.UnitY, 1f);

        Assert.Equal(crate.Id, session.Scene.FocusId);
        Assert.Equal(216, crate.Grid.SolidCount);
        Assert.Null(session.LastShape);
    }
}
