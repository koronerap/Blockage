using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// How one object's cells land on another's lattice — when they do. Two objects share a lattice when
/// their voxels are the same size and one is placed on the other's grid: moved by whole voxels and
/// turned, if at all, by quarter turns. Then every cell of one is exactly a cell of the other, and
/// joining them moves voxels without resampling anything.
/// </summary>
public readonly record struct LatticeMap(Int3 Origin, Int3 X, Int3 Y, Int3 Z)
{
    /// <summary>How far from a whole number still counts as one — well below anything a snap leaves.</summary>
    private const float Tolerance = 0.01f;

    public Int3 Cell(Int3 cell) => Origin + (X * cell.X) + (Y * cell.Y) + (Z * cell.Z);

    public Face Face(Face face)
    {
        Int3 offset = FaceInfo.Offset(face);
        Int3 turned = (X * offset.X) + (Y * offset.Y) + (Z * offset.Z);

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            if (FaceInfo.Offset((Face)f) == turned)
            {
                return (Face)f;
            }
        }

        return face;
    }

    /// <summary>
    /// The map from <paramref name="from"/>'s cells to <paramref name="to"/>'s, or null with the
    /// reason the two do not share a lattice.
    /// </summary>
    public static LatticeMap? Between(ObjectTransform from, ObjectTransform to, out string reason)
    {
        float size = MathF.Max(from.VoxelSize, to.VoxelSize);
        if (MathF.Abs(from.VoxelSize - to.VoxelSize) > size * 1e-4f)
        {
            reason = "Their voxels are different sizes.";
            return null;
        }

        // A cell's centre carried from one object's space into the other's: whole numbers plus a
        // half, if the two share a lattice.
        Vector3 Map(Vector3 cell) => to.InverseTransformPoint(from.TransformPoint(cell + new Vector3(0.5f))) - new Vector3(0.5f);

        // The turn first: a turn off the quarters also throws the origin off the grid, and the turn
        // is the reason worth giving.
        Vector3 origin = Map(Vector3.Zero);
        if (!TryAxis(Map(Vector3.UnitX) - origin, out Int3 x)
            || !TryAxis(Map(Vector3.UnitY) - origin, out Int3 y)
            || !TryAxis(Map(Vector3.UnitZ) - origin, out Int3 z))
        {
            reason = "One is turned by something other than quarter turns.";
            return null;
        }

        if (!TryWhole(origin, out Int3 o))
        {
            reason = "One is off the other's grid by part of a voxel.";
            return null;
        }

        reason = string.Empty;
        return new LatticeMap(o, x, y, z);
    }

    private static bool TryWhole(Vector3 value, out Int3 whole)
    {
        whole = new Int3((int)MathF.Round(value.X), (int)MathF.Round(value.Y), (int)MathF.Round(value.Z));
        return Vector3.Distance(value, whole.ToVector3()) < Tolerance;
    }

    private static bool TryAxis(Vector3 value, out Int3 axis) =>
        TryWhole(value, out axis) && Math.Abs(axis.X) + Math.Abs(axis.Y) + Math.Abs(axis.Z) == 1;
}

/// <summary>Voxels copied or cut, with where they came from, waiting to be pasted.</summary>
/// <param name="Grid">Only the copied cells, at the coordinates they had in their object.</param>
/// <param name="Transform">The placement of the object they came from, so a paste lands on its lattice.</param>
public sealed record VoxelClipboard(VoxelWorld Grid, ObjectTransform Transform, string Name);

/// <summary>Copying, pasting and joining, as pure operations on grids.</summary>
public static class ClipboardOperations
{
    /// <summary>
    /// The voxels behind a selection: from each selected face straight into the object, for as long
    /// as the voxels are solid. A wall's thickness, then — not everything the line would pass through
    /// beyond it, which for a window in the front of a house would take a piece of the back wall too.
    /// </summary>
    public static IEnumerable<Int3> RegionBehind(FaceSelection selection, VoxelWorld grid)
    {
        Int3 inward = FaceInfo.Offset(FaceInfo.Opposite(selection.Direction));
        var seen = new HashSet<Int3>();

        foreach (Int3 start in selection.Voxels)
        {
            for (Int3 cell = start; grid.IsSolid(cell); cell += inward)
            {
                if (seen.Add(cell))
                {
                    yield return cell;
                }
            }
        }
    }

    /// <summary>Every solid cell of a grid.</summary>
    public static IEnumerable<Int3> Everything(VoxelWorld grid)
    {
        if (!grid.TryGetBounds(out Int3 min, out Int3 max))
        {
            yield break;
        }

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var cell = new Int3(x, y, z);
                    if (grid.IsSolid(cell))
                    {
                        yield return cell;
                    }
                }
            }
        }
    }

    /// <summary>The given cells of a grid, with their painted faces, as a grid of their own.</summary>
    public static VoxelWorld Extract(VoxelWorld source, IEnumerable<Int3> cells)
    {
        var copy = new VoxelWorld();
        copy.ReplacePalette(source.Palette);

        foreach (Int3 cell in cells)
        {
            CopyCell(source, cell, copy, cell, face => face);
        }

        return copy;
    }

    /// <summary>
    /// Writes one grid into another through a lattice map, recording every write in the command so
    /// it undoes as one step. What arrives overwrites what was there, face colours and all.
    /// </summary>
    public static int WriteInto(VoxelWorld source, LatticeMap map, VoxelEditCommand command)
    {
        int written = 0;

        foreach (Int3 cell in Everything(source))
        {
            Int3 target = map.Cell(cell);
            byte value = source.GetVoxel(cell);

            command.Apply(target, value);
            written++;

            for (int f = 0; f < FaceInfo.Count; f++)
            {
                byte painted = source.GetFaceColor(cell, (Face)f);
                if (painted != value)
                {
                    command.ApplyFace(target, map.Face((Face)f), painted);
                }
            }
        }

        return written;
    }

    private static void CopyCell(VoxelWorld from, Int3 cell, VoxelWorld to, Int3 target, Func<Face, Face> face)
    {
        byte value = from.GetVoxel(cell);
        if (value == Palette.EmptyIndex)
        {
            return;
        }

        to.SetVoxel(target, value);

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            byte painted = from.GetFaceColor(cell, (Face)f);
            if (painted != value)
            {
                to.SetFaceColor(target, face((Face)f), painted);
            }
        }
    }
}

/// <summary>
/// One object's voxels moved into another's and the first taken out of the level, as one step. Undo
/// takes the voxels back out of the target and puts the source back where it was in the list.
/// </summary>
public sealed class JoinCommand(VoxelScene scene, VoxelObject source, VoxelObject target, LatticeMap map) : ICommand
{
    private VoxelEditCommand? _writes;
    private int _index = -1;
    private int _previousFocus;

    public string Name => $"Join {source.Name} into {target.Name}";

    public int RetainedCells => _writes?.RetainedCells ?? source.Grid.SolidCount;

    public void Redo()
    {
        _previousFocus = scene.FocusId;

        if (_writes is null)
        {
            _writes = new VoxelEditCommand(Name, target.Grid);
            ClipboardOperations.WriteInto(source.Grid, map, _writes);
        }
        else
        {
            _writes.Redo();
        }

        _index = scene.IndexOf(source.Id);
        scene.Remove(source.Id);
        scene.SetFocus(target.Id);
    }

    public void Undo()
    {
        _writes?.Undo();
        scene.Restore(source, _index >= 0 ? _index : null);
        scene.SetFocus(_previousFocus);
    }
}
