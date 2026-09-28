using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// Copy, cut and paste of voxels, and joining one object into another — which is what makes a
/// pasted piece part of the model again rather than one more object beside it.
/// </summary>
public class ClipboardTests
{
    private const byte Wall = 20;
    private const byte Paint = 90;

    /// <summary>
    /// A hollow box seen from the front: a front wall two thick at z 0-1, a gap, a back wall at z 4.
    /// Ten wide, four tall.
    /// </summary>
    private static EditorSession House()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 10; x++)
        for (int y = 0; y < 4; y++)
        {
            grid.SetVoxel(x, y, 0, Wall);
            grid.SetVoxel(x, y, 1, Wall);
            grid.SetVoxel(x, y, 4, Wall);
        }

        grid.SetFaceColor(new Int3(2, 1, 0), Face.NegZ, Paint);

        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "House");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return session;
    }

    private static VoxelObject House(EditorSession session) => session.Scene.Objects[0];

    /// <summary>A 2 x 2 patch of the front face, as Extrude would have it selected.</summary>
    private static void SelectWindow(EditorSession session)
    {
        session.ActiveTool = EditorTool.Extrude;
        session.SetSelection(FaceSelection.Box(House(session).Grid, Face.NegZ, 0, new Int3(2, 1, 0), new Int3(3, 2, 0)));
    }

    // ---- Copy ---------------------------------------------------------------------------------------

    /// <summary>A window's worth of the front wall, both layers of it, and none of the back wall.</summary>
    [Fact]
    public void ACopyTakesTheWallsThicknessBehindTheSelectionAndStopsAtTheGap()
    {
        EditorSession session = House();
        SelectWindow(session);

        Assert.Equal(8, session.Copy());

        VoxelWorld copied = session.Clipboard!.Grid;
        Assert.True(copied.IsSolid(new Int3(2, 1, 0)));
        Assert.True(copied.IsSolid(new Int3(3, 2, 1)));
        Assert.False(copied.IsSolid(new Int3(2, 1, 4)));
        Assert.Equal(Paint, copied.GetFaceColor(new Int3(2, 1, 0), Face.NegZ));
    }

    [Fact]
    public void WithNothingSelectedTheWholeObjectIsCopied()
    {
        EditorSession session = House();

        Assert.Equal(House(session).Grid.SolidCount, session.Copy());
    }

    /// <summary>A selection outlives Extrude; copying from another tool is copying the object.</summary>
    [Fact]
    public void ASelectionLeftBehindByExtrudeDoesNotNarrowACopyFromAnotherTool()
    {
        EditorSession session = House();
        SelectWindow(session);
        session.ActiveTool = EditorTool.Transform;

        Assert.Equal(House(session).Grid.SolidCount, session.Copy());
    }

    // ---- Paste --------------------------------------------------------------------------------------

    [Fact]
    public void APasteIsANewObjectBesideWhereItWasCopiedFrom()
    {
        EditorSession session = House();
        SelectWindow(session);
        session.Copy();

        VoxelObject pasted = session.Paste(Vector3.UnitX)!;

        Assert.Equal(pasted.Id, session.Scene.FocusId);
        Assert.Equal("House.001", pasted.Name);
        Assert.Equal(8, pasted.Grid.SolidCount);
        Assert.Equal(Paint, pasted.Grid.GetFaceColor(new Int3(2, 1, 0), Face.NegZ));

        // Two voxels wide, then one clear: three along.
        Assert.Equal(new Vector3(3f, 0f, 0f), pasted.Transform.Position);
    }

    [Fact]
    public void EachPasteLandsAStepFurtherAlong()
    {
        EditorSession session = House();
        SelectWindow(session);
        session.Copy();

        session.Paste(Vector3.UnitX);
        VoxelObject second = session.Paste(Vector3.UnitX)!;

        Assert.Equal(new Vector3(6f, 0f, 0f), second.Transform.Position);
    }

    /// <summary>A paste is its own copy: pasting twice gives two objects that do not share voxels.</summary>
    [Fact]
    public void PastesDoNotShareVoxels()
    {
        EditorSession session = House();
        session.Copy();

        VoxelObject first = session.Paste(Vector3.UnitX)!;
        VoxelObject second = session.Paste(Vector3.UnitX)!;
        first.Grid.SetVoxel(new Int3(0, 0, 0), Palette.EmptyIndex);

        Assert.True(second.Grid.IsSolid(new Int3(0, 0, 0)));
        Assert.True(session.Clipboard!.Grid.IsSolid(new Int3(0, 0, 0)));
    }

    [Fact]
    public void APasteKeepsTheVoxelSizeAndTurnOfWhereItCameFrom()
    {
        EditorSession session = House();
        House(session).Transform = new ObjectTransform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f), 0.5f);
        session.Copy();

        VoxelObject pasted = session.Paste(Vector3.UnitX)!;

        Assert.Equal(0.5f, pasted.VoxelSize);
        Assert.Equal(House(session).Transform.Rotation, pasted.Transform.Rotation);
    }

    // ---- Cut ----------------------------------------------------------------------------------------

    [Fact]
    public void ACutTakesTheVoxelsOutAndOneUndoPutsThemBack()
    {
        EditorSession session = House();
        ulong before = House(session).Grid.ContentHash();
        SelectWindow(session);

        Assert.Equal(8, session.Cut());

        Assert.False(House(session).Grid.IsSolid(new Int3(2, 1, 0)));
        Assert.True(House(session).Grid.IsSolid(new Int3(2, 1, 4)));
        Assert.Null(session.Selection);

        session.Undo();

        Assert.Equal(before, House(session).Grid.ContentHash());
        Assert.Equal(Paint, House(session).Grid.GetFaceColor(new Int3(2, 1, 0), Face.NegZ));
    }

    /// <summary>With nothing selected a cut takes the object — but never the last one, which is only copied.</summary>
    [Fact]
    public void CuttingTheLastObjectOnlyCopiesIt()
    {
        EditorSession session = House();

        Assert.True(session.Cut() > 0);

        Assert.Single(session.Scene.Objects);
        Assert.NotNull(session.Clipboard);
    }

    // ---- Join ---------------------------------------------------------------------------------------

    [Fact]
    public void AJoinedPieceLandsWhereItStoodAndItsObjectGoes()
    {
        EditorSession session = House();
        SelectWindow(session);
        session.Cut();
        VoxelObject pasted = session.Paste(Vector3.UnitX)!;

        // Put back exactly where it was cut from, then joined.
        pasted.Transform = House(session).Transform;
        Assert.True(session.JoinInto(pasted.Id, House(session).Id));

        Assert.Single(session.Scene.Objects);
        Assert.True(House(session).Grid.IsSolid(new Int3(2, 1, 0)));
        Assert.Equal(Paint, House(session).Grid.GetFaceColor(new Int3(2, 1, 0), Face.NegZ));
        Assert.Equal(House(session).Id, session.Scene.FocusId);
    }

    [Fact]
    public void AJoinUndoesAsOneStep()
    {
        EditorSession session = House();
        session.Copy();
        VoxelObject pasted = session.Paste(Vector3.UnitX)!;
        ulong house = House(session).Grid.ContentHash();
        ulong piece = pasted.Grid.ContentHash();

        session.JoinInto(pasted.Id, House(session).Id);
        session.Undo();

        Assert.Equal(2, session.Scene.Objects.Count);
        Assert.Equal(house, House(session).Grid.ContentHash());
        Assert.Equal(piece, session.Scene.Objects[1].Grid.ContentHash());
        Assert.Equal(pasted.Id, session.Scene.FocusId);
    }

    /// <summary>
    /// A quarter turn is still the same lattice: every cell lands on a cell, and a painted face turns
    /// with its voxel.
    /// </summary>
    [Fact]
    public void AQuarterTurnedPieceJoinsTurned()
    {
        var target = new VoxelWorld();
        target.SetVoxel(0, 0, 0, Wall);

        var piece = new VoxelWorld();
        piece.SetVoxel(new Int3(1, 0, 0), Wall);
        piece.SetFaceColor(new Int3(1, 0, 0), Face.PosX, Paint);

        var scene = new VoxelScene();
        scene.Add(target, ObjectTransform.Identity, "target");

        // Turned a quarter about Y, which carries +X round to -Z, and moved two voxels along X.
        scene.Add(piece, new ObjectTransform(new Vector3(2f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f)), "piece");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);

        Assert.True(session.JoinInto(scene.Objects[1].Id, scene.Objects[0].Id));

        // The piece's cell 1 along its own X is one along the world's -Z from its origin at x = 2.
        VoxelWorld joined = session.Scene.Objects[0].Grid;
        Assert.True(joined.IsSolid(new Int3(2, 0, -2)));
        Assert.Equal(Paint, joined.GetFaceColor(new Int3(2, 0, -2), Face.NegZ));
    }

    [Theory]
    [InlineData(0.5f, 0f, 0f, "different sizes")]
    [InlineData(1f, 0.5f, 0f, "part of a voxel")]
    [InlineData(1f, 0f, 30f, "quarter turns")]
    public void ObjectsOffEachOthersLatticeAreRefusedWithTheReason(float size, float shift, float degrees, string reason)
    {
        EditorSession session = House();
        session.Copy();
        VoxelObject pasted = session.Paste(Vector3.UnitX)!;
        pasted.Transform = new ObjectTransform(
            new Vector3(shift, 0f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180f),
            size);

        string? problem = session.JoinProblem(pasted.Id, House(session).Id);

        Assert.NotNull(problem);
        Assert.Contains(reason, problem, StringComparison.Ordinal);
        Assert.False(session.JoinInto(pasted.Id, House(session).Id));
        Assert.Equal(2, session.Scene.Objects.Count);
    }

    [Fact]
    public void AnObjectCannotBeJoinedIntoItself()
    {
        EditorSession session = House();

        Assert.NotNull(session.JoinProblem(House(session).Id, House(session).Id));
        Assert.False(session.JoinInto(House(session).Id, House(session).Id));
    }

    /// <summary>Where the two overlap, the piece being joined in is what is left.</summary>
    [Fact]
    public void WhereTheyOverlapThePieceJoinedInWins()
    {
        EditorSession session = House();
        session.Copy();
        VoxelObject pasted = session.Paste(Vector3.UnitX)!;
        pasted.Transform = House(session).Transform;
        pasted.Grid.SetVoxel(new Int3(5, 2, 0), Paint);

        session.JoinInto(pasted.Id, House(session).Id);

        Assert.Equal(Paint, House(session).Grid.GetVoxel(new Int3(5, 2, 0)));
    }
}
