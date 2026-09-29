using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Non-destructive modifiers (Fullreleaseplan 3.10): a mirror and a row of copies shown and exported
/// over an object's own voxels, kept up to date as those change, saved, applied and undone.
/// </summary>
public class ModifierTests
{
    private static (EditorSession Session, VoxelObject Piece) Piece()
    {
        var grid = new VoxelWorld();
        grid.SetVoxel(2, 0, 0, Palette.WhiteIndex);
        grid.SetVoxel(3, 0, 0, 30);

        var scene = new VoxelScene();
        VoxelObject piece = scene.Add(grid, ObjectTransform.Identity, "Piece");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return (session, piece);
    }

    private static Int3 C(int x, int y = 0, int z = 0) => new(x, y, z);

    [Fact]
    public void AMirrorShowsTheVoxelsAndTheirImageAcrossTheOrigin()
    {
        (_, VoxelObject piece) = Piece();

        piece.SetModifiers([new VoxelModifier(ModifierKind.Mirror, Axis.X, Plane: 0)]);

        Assert.Equal(4, piece.Shown.SolidCount);
        Assert.Equal(Palette.WhiteIndex, piece.Shown.GetVoxel(C(-3)));
        Assert.Equal(30, piece.Shown.GetVoxel(C(-4)));
        Assert.Equal(2, piece.Grid.SolidCount);

        // What it shows is what it is as big as.
        Assert.True(piece.TryGetLocalBounds(out Vector3 min, out Vector3 max));
        Assert.Equal(-4f, min.X);
        Assert.Equal(4f, max.X);
    }

    [Fact]
    public void AnArrayShowsARowOfCopiesAStepApart()
    {
        (_, VoxelObject piece) = Piece();

        piece.SetModifiers([new VoxelModifier(ModifierKind.Array, Axis.Z, Count: 3, Step: 5)]);

        Assert.Equal(6, piece.Shown.SolidCount);
        Assert.True(piece.Shown.IsSolid(C(2, 0, 10)));
        Assert.True(piece.Shown.IsSolid(C(3, 0, 5)));
    }

    [Fact]
    public void ModifiersStackInOrder()
    {
        (_, VoxelObject piece) = Piece();

        piece.SetModifiers([
            new VoxelModifier(ModifierKind.Mirror, Axis.X),
            new VoxelModifier(ModifierKind.Array, Axis.Y, Count: 3, Step: 2),
        ]);

        Assert.Equal(2 * 2 * 3, piece.Shown.SolidCount);
        Assert.True(piece.Shown.IsSolid(C(-4, 4)));
    }

    [Fact]
    public void ASwitchedOffModifierShowsTheVoxelsAsTheyAre()
    {
        (_, VoxelObject piece) = Piece();

        piece.SetModifiers([new VoxelModifier(ModifierKind.Mirror, Axis.X, Enabled: false)]);

        Assert.False(piece.HasModifiers);
        Assert.Same(piece.Grid, piece.Shown);
    }

    [Fact]
    public void EditsToTheVoxelsReachTheImagesPaintedFacesTurnedWithThem()
    {
        (_, VoxelObject piece) = Piece();
        piece.SetModifiers([new VoxelModifier(ModifierKind.Mirror, Axis.X)]);
        _ = piece.Shown;

        piece.Grid.SetVoxel(C(3), Palette.EmptyIndex);
        piece.Grid.SetFaceColor(C(2), Face.PosX, 50);

        Assert.False(piece.Shown.IsSolid(C(-4)));
        Assert.Equal(50, piece.Shown.GetFaceColor(C(2), Face.PosX));

        // The mirror image's face across the mirror is the painted one.
        Assert.Equal(50, piece.Shown.GetFaceColor(C(-3), Face.NegX));
        Assert.Equal(Palette.WhiteIndex, piece.Shown.GetFaceColor(C(-3), Face.PosX));
    }

    [Fact]
    public void ChoosingAnObjectPicksWhatItShowsTheToolsPickItsVoxels()
    {
        (EditorSession session, VoxelObject piece) = Piece();
        piece.SetModifiers([new VoxelModifier(ModifierKind.Mirror, Axis.X)]);

        // A ray down onto the mirror image only.
        var ray = new Ray(new Vector3(-2.5f, 10f, 0.5f), -Vector3.UnitY);

        Assert.True(session.Scene.TryPick(ray, out ScenePick shown, shown: true));
        Assert.Same(piece, shown.Object);
        Assert.False(session.Scene.TryPick(ray, out _));
    }

    [Fact]
    public void AddingAndRemovingAModifierAreUndoSteps()
    {
        (EditorSession session, VoxelObject piece) = Piece();

        Assert.True(session.AddModifier(piece.Id, ModifierKind.Array));
        Assert.Single(piece.Modifiers);
        Assert.Equal(3, piece.Modifiers[0].Count);

        Assert.True(session.RemoveModifier(piece.Id, 0));
        Assert.Empty(piece.Modifiers);

        session.Undo();
        Assert.Single(piece.Modifiers);
        session.Undo();
        Assert.Empty(piece.Modifiers);
    }

    [Fact]
    public void ALiveChangeIsOneStepOnceLetGo()
    {
        (EditorSession session, VoxelObject piece) = Piece();
        session.AddModifier(piece.Id, ModifierKind.Array);
        IReadOnlyList<VoxelModifier> before = piece.Modifiers;

        session.PreviewModifier(piece.Id, 0, piece.Modifiers[0] with { Count = 4 });
        session.PreviewModifier(piece.Id, 0, piece.Modifiers[0] with { Count = 5 });
        Assert.True(session.PushModifierEdit(piece.Id, before, "Change"));

        Assert.Equal(5, piece.Modifiers[0].Count);
        Assert.Equal(2, session.History.UndoCount);

        session.Undo();
        Assert.Equal(3, piece.Modifiers[0].Count);
    }

    [Fact]
    public void ApplyingMakesTheModifierVoxelsForGood()
    {
        (EditorSession session, VoxelObject piece) = Piece();
        session.AddModifier(piece.Id, ModifierKind.Mirror);

        Assert.True(session.ApplyModifier(piece.Id, 0));

        Assert.Empty(piece.Modifiers);
        Assert.Equal(4, piece.Grid.SolidCount);
        Assert.True(piece.Grid.IsSolid(C(-3)));

        session.Undo();
        Assert.Single(piece.Modifiers);
        Assert.Equal(2, piece.Grid.SolidCount);
    }

    [Fact]
    public void ModifiersAreSavedWithTheLevel()
    {
        (EditorSession session, VoxelObject piece) = Piece();
        piece.SetModifiers([
            new VoxelModifier(ModifierKind.Mirror, Axis.Z, Plane: 2),
            new VoxelModifier(ModifierKind.Array, Axis.Y, Count: 4, Step: -3, Enabled: false),
        ]);

        using var stream = new MemoryStream();
        VxLevelFile.Save(session.Scene, stream, "modified");
        stream.Position = 0;
        VoxelObject loaded = VxLevelFile.LoadScene(stream).Objects[0];

        Assert.Equal(piece.Modifiers, loaded.Modifiers);
        Assert.Equal(piece.Shown.SolidCount, loaded.Shown.SolidCount);
    }

    [Fact]
    public void ADuplicateKeepsItsModifiers()
    {
        (EditorSession session, VoxelObject piece) = Piece();
        session.AddModifier(piece.Id, ModifierKind.Mirror);
        session.ClickSelect(piece.Id);

        IReadOnlyList<IPlaceable> copies = session.DuplicateSelected(Vector3.UnitZ);

        VoxelObject copy = Assert.IsType<VoxelObject>(Assert.Single(copies));
        Assert.Equal(piece.Modifiers, copy.Modifiers);
    }

    [Fact]
    public void ATooLongRowIsHeldToItsLimit()
    {
        var modifier = new VoxelModifier(ModifierKind.Array, Count: 10_000, Step: 1_000_000).Clamped();

        Assert.Equal(VoxelModifier.MaxCount, modifier.Count);
        Assert.Equal(VoxelModifier.MaxStep, modifier.Step);
    }
}
