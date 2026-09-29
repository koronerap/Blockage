using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using EditorApp.Core.Commands;
using EditorApp.Core.Editing;
using EditorApp.Core.Project;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// A child is held at a rigid offset in its parent's frame: it stays where it is when parented, moves
/// and turns with its parent after, and keeps a place it is moved to by hand.
/// </summary>
public class ParentingTests
{
    private static readonly Quaternion QuarterTurnY = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

    private static VoxelWorld Block(int side = 2, byte index = 5)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, index);
                }
            }
        }

        return grid;
    }

    /// <summary>A table at the origin and a lamp on it, not yet parented.</summary>
    private static (EditorSession Session, VoxelObject Table, VoxelObject Lamp) TableAndLamp()
    {
        var scene = new VoxelScene();
        VoxelObject table = scene.Add(Block(4), ObjectTransform.Identity, "Table");
        VoxelObject lamp = scene.Add(Block(), ObjectTransform.At(new Vector3(1f, 4f, 1f)), "Lamp");

        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        return (session, table, lamp);
    }

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(MathF.Abs(Quaternion.Dot(expected, actual)) > 0.9999f, $"expected {expected}, got {actual}");

    [Fact]
    public void AnOffsetTakenAndPutBackIsWhereItWas()
    {
        var parent = new ObjectTransform(new Vector3(3f, 1f, -2f), Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, -0.3f), 0.5f);
        var child = new ObjectTransform(new Vector3(-1f, 6f, 4f), Quaternion.CreateFromYawPitchRoll(-1.1f, 0.3f, 0.7f), 0.25f);

        ObjectTransform back = Parenting.Compose(parent, Parenting.Relative(parent, child), child.VoxelSize);

        Near(child.Position, back.Position);
        Near(child.Rotation, back.Rotation);
        Assert.Equal(0.25f, back.VoxelSize);
    }

    [Fact]
    public void AChildStaysWhereItIsWhenParented()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.ApplyTransform(table, new ObjectTransform(new Vector3(2f, 0f, 0f), QuarterTurnY));
        ObjectTransform before = lamp.Transform;

        Assert.True(session.SetParent(lamp.Id, table.Id));

        Assert.Equal(before, lamp.Transform);
        Assert.Same(table, session.Scene.ParentOf(lamp));
    }

    [Fact]
    public void MovingTheParentCarriesTheChild()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);

        session.ApplyTransform(table, table.Transform.Translated(new Vector3(5f, 0f, -3f)));

        Near(new Vector3(6f, 4f, -2f), lamp.Transform.Position);
        Near(Quaternion.Identity, lamp.Transform.Rotation);
    }

    /// <summary>Turning the parent swings the child round the parent's origin and turns it the same way.</summary>
    [Fact]
    public void TurningTheParentSwingsTheChildRoundIt()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);

        session.ApplyTransform(table, table.Transform with { Rotation = QuarterTurnY });

        // A quarter turn about +Y takes +X to -Z.
        Near(new Vector3(1f, 4f, -1f), lamp.Transform.Position);
        Near(QuarterTurnY, lamp.Transform.Rotation);
    }

    [Fact]
    public void AChildMovedByHandKeepsItsNewPlaceOnTheParent()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);

        session.ApplyTransform(lamp, ObjectTransform.At(new Vector3(3f, 4f, 3f)));
        session.ApplyTransform(table, ObjectTransform.At(new Vector3(0f, 10f, 0f)));

        Near(new Vector3(3f, 14f, 3f), lamp.Transform.Position);
    }

    [Fact]
    public void GrandchildrenFollowToo()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        VoxelObject shade = session.Scene.Add(Block(1), ObjectTransform.At(new Vector3(1f, 6f, 1f)), "Shade");
        SceneLight bulb = session.AddLight(LightKind.Point, new Vector3(1.5f, 5.5f, 1.5f), -Vector3.UnitY);

        session.SetParent(lamp.Id, table.Id);
        session.SetParent(shade.Id, lamp.Id);
        session.SetParent(bulb.Id, shade.Id);

        session.ApplyTransform(table, table.Transform.Translated(new Vector3(0f, 0f, 7f)));

        Near(new Vector3(1f, 6f, 8f), shade.Transform.Position);
        Near(new Vector3(1.5f, 5.5f, 8.5f), bulb.Position);
        Assert.True(session.Scene.IsDescendantOf(bulb.Id, table.Id));
        Assert.False(session.Scene.IsDescendantOf(table.Id, bulb.Id));
    }

    /// <summary>A child's voxel size is what it is made of, not a scale: a parent's never changes it, or moves it.</summary>
    [Fact]
    public void AParentsVoxelSizeLeavesItsChildAlone()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);

        session.ApplyTransform(table, table.Transform with { VoxelSize = 0.25f });

        Assert.Equal(1f, lamp.VoxelSize);
        Near(new Vector3(1f, 4f, 1f), lamp.Transform.Position);
    }

    [Fact]
    public void LoopsAreRefusedWithAReason()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        VoxelObject shade = session.Scene.Add(Block(1), ObjectTransform.At(new Vector3(1f, 6f, 1f)), "Shade");
        session.SetParent(lamp.Id, table.Id);
        session.SetParent(shade.Id, lamp.Id);

        Assert.Contains("grandparent", session.ParentProblem(table.Id, shade.Id), StringComparison.Ordinal);
        Assert.False(session.SetParent(table.Id, shade.Id));
        Assert.False(session.SetParent(table.Id, lamp.Id));
        Assert.NotNull(session.ParentProblem(table.Id, table.Id));
        Assert.False(session.SetParent(table.Id, table.Id));
        Assert.Null(session.Scene.ParentOf(table));
    }

    [Fact]
    public void OnlyAnObjectCanBeAParent()
    {
        (EditorSession session, VoxelObject table, _) = TableAndLamp();
        SceneLight bulb = session.AddLight(LightKind.Point, new Vector3(0f, 8f, 0f), -Vector3.UnitY);

        Assert.NotNull(session.ParentProblem(table.Id, bulb.Id));
        Assert.False(session.SetParent(table.Id, bulb.Id));
        Assert.True(session.SetParent(bulb.Id, table.Id));
    }

    [Fact]
    public void ParentingIsOneUndoStep()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();

        session.SetParent(lamp.Id, table.Id);
        session.Undo();

        Assert.Null(session.Scene.ParentOf(lamp));
        session.ApplyTransform(table, table.Transform.Translated(Vector3.UnitX));
        Near(new Vector3(1f, 4f, 1f), lamp.Transform.Position);

        session.Redo();
        Assert.Same(table, session.Scene.ParentOf(lamp));
    }

    [Fact]
    public void ClearingKeepsTheChildWhereItIs()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);
        session.ApplyTransform(table, new ObjectTransform(new Vector3(4f, 0f, 0f), QuarterTurnY));
        ObjectTransform carried = lamp.Transform;

        Assert.True(session.ClearParent(lamp.Id));
        Assert.False(session.ClearParent(lamp.Id));

        Assert.Equal(carried, lamp.Transform);
        session.ApplyTransform(table, ObjectTransform.Identity);
        Assert.Equal(carried, lamp.Transform);
    }

    [Fact]
    public void NothingChangingIsNoStep()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);
        int steps = session.History.UndoCount;

        Assert.False(session.SetParent(lamp.Id, table.Id));
        Assert.Equal(steps, session.History.UndoCount);
    }

    /// <summary>Undoing a move of the parent takes the child back with it.</summary>
    [Fact]
    public void UndoingAParentsMoveTakesTheChildBack()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);

        ObjectTransform before = table.Transform;
        session.ApplyTransform(table, table.Transform.Translated(new Vector3(0f, 0f, 9f)));
        session.PushTransformEdit(table, before, "Move Table");
        Near(new Vector3(1f, 4f, 10f), lamp.Transform.Position);

        session.Undo();
        Near(new Vector3(1f, 4f, 1f), lamp.Transform.Position);

        session.Redo();
        Near(new Vector3(1f, 4f, 10f), lamp.Transform.Position);
    }

    /// <summary>
    /// A deleted parent's children stay where they are, free. Undoing the delete puts them back under
    /// it, held wherever they are then.
    /// </summary>
    [Fact]
    public void DeletingAParentFreesItsChildrenAndUndoTakesThemBack()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);

        Assert.True(session.DeleteObject(table.Id));
        Assert.Null(session.Scene.ParentOf(lamp));
        Near(new Vector3(1f, 4f, 1f), lamp.Transform.Position);

        // Out of the level, moving it moves nothing that is in it.
        table.Transform = table.Transform.Translated(Vector3.UnitY);
        Near(new Vector3(1f, 4f, 1f), lamp.Transform.Position);
        table.Transform = table.Transform.Translated(-Vector3.UnitY);

        session.Undo();
        Assert.Same(table, session.Scene.ParentOf(lamp));

        session.ApplyTransform(table, table.Transform.Translated(Vector3.UnitX));
        Near(new Vector3(2f, 4f, 1f), lamp.Transform.Position);
    }

    /// <summary>
    /// A child moved while its parent was gone is held by nothing; once that move and the delete are
    /// undone, it is held again where it is — not at the nothing it was held at meanwhile.
    /// </summary>
    [Fact]
    public void AChildMovedWhileItsParentWasGoneIsHeldRightWhenItComesBack()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.ApplyTransform(table, table.Transform.Translated(new Vector3(2f, 0f, 0f)));
        session.SetParent(lamp.Id, table.Id);
        session.DeleteObject(table.Id);

        ObjectTransform before = lamp.Transform;
        session.ApplyTransform(lamp, lamp.Transform.Translated(Vector3.UnitZ));
        session.PushTransformEdit(lamp, before, "Move Lamp");
        session.Undo();   // the lamp back
        session.Undo();   // the table back

        session.ApplyTransform(table, table.Transform.Translated(Vector3.UnitY));
        Near(new Vector3(1f, 5f, 1f), lamp.Transform.Position);
    }

    /// <summary>A parent undone away and back finds its children, as a deleted one does.</summary>
    [Fact]
    public void UndoingParentingPutsBackADeletedParent()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        VoxelObject shelf = session.Scene.Add(Block(), ObjectTransform.At(new Vector3(8f, 0f, 0f)), "Shelf");
        session.SetParent(lamp.Id, table.Id);
        session.DeleteObject(table.Id);

        session.SetParent(lamp.Id, shelf.Id);
        session.Undo();   // back to the deleted table's id
        session.Undo();   // the table back

        Assert.Same(table, session.Scene.ParentOf(lamp));
    }

    [Fact]
    public void ADuplicateHasTheSameParent()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        SceneLight bulb = session.AddLight(LightKind.Point, new Vector3(1f, 7f, 1f), -Vector3.UnitY);
        session.SetParent(lamp.Id, table.Id);
        session.SetParent(bulb.Id, table.Id);

        session.ChooseObject(lamp.Id);
        VoxelObject copy = session.DuplicateFocus(Vector3.UnitX)!;
        SceneLight lightCopy = session.DuplicateLight(bulb.Id, Vector3.UnitZ)!;

        Assert.Same(table, session.Scene.ParentOf(copy));
        Assert.Same(table, session.Scene.ParentOf(lightCopy));

        Vector3 copyAt = copy.Transform.Position;
        Vector3 lightAt = lightCopy.Position;
        session.ApplyTransform(table, table.Transform.Translated(Vector3.UnitY));
        Near(copyAt + Vector3.UnitY, copy.Transform.Position);
        Near(lightAt + Vector3.UnitY, lightCopy.Position);
    }

    /// <summary>A piece extruded out as an object of its own stays in the family it came from.</summary>
    [Fact]
    public void AnExtrudedObjectHasItsSourcesParent()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);
        session.ChooseObject(lamp.Id);
        session.ActiveTool = EditorTool.Extrude;
        session.ExtrudeCreatesObject = true;
        session.SetSelection(FaceSelection.Box(lamp.Grid, Face.PosY, 1, Int3.Zero, new Int3(1, 1, 1)));

        session.PreviewExtrude(1);
        Assert.True(session.ConfirmExtrude());

        VoxelObject piece = session.Scene.Objects[^1];
        Assert.NotSame(lamp, piece);
        Assert.Same(table, session.Scene.ParentOf(piece));
    }

    /// <summary>
    /// Both halves of a cut stay where the original was in the family; what was under it goes under the
    /// half that keeps its name. Undo puts every link back as it was.
    /// </summary>
    [Fact]
    public void ACutKeepsTheFamilyTogether()
    {
        var scene = new VoxelScene();
        VoxelObject table = scene.Add(Block(4), ObjectTransform.Identity, "Table");
        var bar = new VoxelWorld();
        for (int x = 0; x < 6; x++)
        {
            bar.SetVoxel(x, 0, 0, 5);
        }

        VoxelObject shelf = scene.Add(bar, ObjectTransform.At(new Vector3(0f, 4f, 0f)), "Shelf");
        VoxelObject book = scene.Add(Block(1), ObjectTransform.At(new Vector3(1f, 5f, 0f)), "Book");
        var session = new EditorSession { ActiveTool = EditorTool.LoopCut };
        session.ReplaceScene(scene, projectPath: null);
        session.SetParent(shelf.Id, table.Id);
        session.SetParent(book.Id, shelf.Id);
        session.ChooseObject(shelf.Id);

        Assert.True(session.ApplyLoopCut(new CutPlane(Axis.X, 3)));

        VoxelObject low = scene.Objects.Single(o => o.Name == "Shelf");
        VoxelObject high = scene.Objects.Single(o => o.Name == "Shelf (cut)");
        Assert.Same(table, scene.ParentOf(low));
        Assert.Same(table, scene.ParentOf(high));
        Assert.Same(low, scene.ParentOf(book));

        session.ApplyTransform(low, low.Transform.Translated(Vector3.UnitZ));
        Near(new Vector3(1f, 5f, 1f), book.Transform.Position);
        session.ApplyTransform(low, low.Transform.Translated(-Vector3.UnitZ));

        session.Undo();

        Assert.Same(shelf, scene.ParentOf(book));
        Assert.Same(table, scene.ParentOf(shelf));
        session.ApplyTransform(shelf, shelf.Transform.Translated(Vector3.UnitY));
        Near(new Vector3(1f, 6f, 0f), book.Transform.Position);
    }

    /// <summary>
    /// What was under a joined object goes under the one it joined into — and the one it joined into,
    /// if it was one of those, takes its place in the family instead of being its own parent.
    /// </summary>
    [Fact]
    public void AJoinHandsTheChildrenOn()
    {
        var scene = new VoxelScene();
        VoxelObject room = scene.Add(Block(1), ObjectTransform.At(new Vector3(0f, 0f, 0f)), "Room");
        VoxelObject table = scene.Add(Block(2), ObjectTransform.At(new Vector3(4f, 0f, 0f)), "Table");
        VoxelObject top = scene.Add(Block(2), ObjectTransform.At(new Vector3(4f, 2f, 0f)), "Top");
        VoxelObject cup = scene.Add(Block(1), ObjectTransform.At(new Vector3(4f, 4f, 0f)), "Cup");
        var session = new EditorSession();
        session.ReplaceScene(scene, projectPath: null);
        session.SetParent(table.Id, room.Id);
        session.SetParent(top.Id, table.Id);
        session.SetParent(cup.Id, table.Id);

        Assert.True(session.JoinInto(table.Id, top.Id));

        Assert.Same(room, scene.ParentOf(top));
        Assert.Same(top, scene.ParentOf(cup));

        session.Undo();

        Assert.Same(table, scene.ParentOf(top));
        Assert.Same(table, scene.ParentOf(cup));
        Assert.Same(room, scene.ParentOf(table));
    }

    [Fact]
    public void ParentsAreSavedEvenWhenAChildIsListedFirst()
    {
        var scene = new VoxelScene();
        VoxelObject lamp = scene.Add(Block(), ObjectTransform.At(new Vector3(1f, 4f, 1f)), "Lamp");
        VoxelObject table = scene.Add(Block(4), new ObjectTransform(new Vector3(2f, 0f, 0f), QuarterTurnY), "Table");
        VoxelObject loose = scene.Add(Block(1), ObjectTransform.At(new Vector3(9f, 0f, 0f)), "Loose");
        SceneLight bulb = scene.AddLight(LightKind.Point, "Bulb");
        bulb.Transform = ObjectTransform.At(new Vector3(1f, 7f, 1f));
        Assert.True(scene.SetParent(lamp.Id, table.Id));
        Assert.True(scene.SetParent(bulb.Id, lamp.Id));

        string path = Path.Combine(Path.GetTempPath(), $"parents-{Guid.NewGuid():N}{VxLevelFile.Extension}");
        try
        {
            VxLevelFile.Save(scene, path);
            VoxelScene loaded = VxLevelFile.LoadScene(path);

            VoxelObject loadedLamp = loaded.Objects.Single(o => o.Name == "Lamp");
            VoxelObject loadedTable = loaded.Objects.Single(o => o.Name == "Table");
            SceneLight loadedBulb = loaded.Lights.Single(l => l.Name == "Bulb");
            Assert.Same(loadedTable, loaded.ParentOf(loadedLamp));
            Assert.Same(loadedLamp, loaded.ParentOf(loadedBulb));
            Assert.Null(loaded.ParentOf(loaded.Objects.Single(o => o.Name == "Loose")));
            Near(new Vector3(1f, 4f, 1f), loadedLamp.Transform.Position);

            loadedTable.Transform = loadedTable.Transform.Translated(Vector3.UnitY);
            Near(new Vector3(1f, 8f, 1f), loadedBulb.Position);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A parent that is not in the file, or a loop someone wrote by hand, is no parent — not a refusal to open.</summary>
    [Fact]
    public void ABrokenParentInAFileIsNoParent()
    {
        var scene = new VoxelScene();
        scene.Add(Block(), ObjectTransform.Identity, "A");
        scene.Add(Block(), ObjectTransform.At(new Vector3(4f, 0f, 0f)), "B");
        scene.AddLight(LightKind.Point, "Bulb");

        string path = Path.Combine(Path.GetTempPath(), $"loop-{Guid.NewGuid():N}{VxLevelFile.Extension}");
        try
        {
            VxLevelFile.Save(scene, path);
            RewriteManifest(path, node =>
            {
                JsonArray objects = node["objects"]!.AsArray();
                objects[0]!["parent"] = objects[1]!["id"]!.GetValue<int>();
                objects[1]!["parent"] = objects[0]!["id"]!.GetValue<int>();
                node["lights"]![0]!["parent"] = 999;
            });

            VoxelScene loaded = VxLevelFile.LoadScene(path);

            VoxelObject a = loaded.Objects.Single(o => o.Name == "A");
            VoxelObject b = loaded.Objects.Single(o => o.Name == "B");
            Assert.Same(b, loaded.ParentOf(a));
            Assert.Null(loaded.ParentOf(b));
            Assert.Null(loaded.ParentOf(loaded.Lights[0]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A deleted parent's id is not written: a file only names what is in it.</summary>
    [Fact]
    public void ADeletedParentIsNotSaved()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        session.SetParent(lamp.Id, table.Id);
        session.DeleteObject(table.Id);

        string path = Path.Combine(Path.GetTempPath(), $"orphan-{Guid.NewGuid():N}{VxLevelFile.Extension}");
        try
        {
            VxLevelFile.Save(session.Scene, path);
            LevelManifest manifest = VxLevelFile.ReadManifest(path);
            Assert.All(manifest.Objects!, entry => Assert.Null(entry.Parent));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A save is written from a snapshot, so a snapshot that lost a parent would save without it.</summary>
    [Fact]
    public void ASnapshotKeepsTheFamily()
    {
        (EditorSession session, VoxelObject table, VoxelObject lamp) = TableAndLamp();
        SceneLight bulb = session.AddLight(LightKind.Point, new Vector3(1f, 7f, 1f), -Vector3.UnitY);
        session.SetParent(lamp.Id, table.Id);
        session.SetParent(bulb.Id, lamp.Id);

        VoxelScene copy = session.Scene.Snapshot();

        Assert.Equal(table.Id, copy.ParentOf(copy.Find(lamp.Id)!)?.Id);
        Assert.Equal(lamp.Id, copy.ParentOf(copy.FindLight(bulb.Id)!)?.Id);
    }

    private static void RewriteManifest(string path, Action<JsonNode> change)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        ZipArchiveEntry entry = archive.GetEntry("manifest.json")!;

        JsonNode node;
        using (var reader = new StreamReader(entry.Open()))
        {
            node = JsonNode.Parse(reader.ReadToEnd())!;
        }

        change(node);

        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
        writer.Write(node.ToJsonString());
    }
}
