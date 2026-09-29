using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// Objects brought in from another level (Fullreleaseplan 6.3): a prop from the library, set down
/// on a surface, or objects appended from a .vxlevel where they stood there. Their colours are
/// found in this level's palette, or given free slots of their own, or the nearest there is.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>A prop from the library, standing on the surface at <paramref name="point"/>, selected. One undo step.</summary>
    public IReadOnlyList<VoxelObject> PlaceProp(VoxelScene prop, string name, Vector3 point)
    {
        float size = prop.Objects.FirstOrDefault()?.VoxelSize ?? 1f;
        Vector3 snapped = new Vector3(MathF.Round(point.X / size), MathF.Round(point.Y / size), MathF.Round(point.Z / size)) * size;
        return Insert(prop, prop.Objects, offset: snapped, $"Add {name}");
    }

    /// <summary>Objects from another level where they stood in it, selected. One undo step.</summary>
    public IReadOnlyList<VoxelObject> AppendObjects(VoxelScene source, IEnumerable<int> ids, string from)
    {
        HashSet<int> wanted = [.. ids];
        return Insert(source, [.. source.Objects.Where(o => wanted.Contains(o.Id))], Vector3.Zero, $"Append from {from}");
    }

    private IReadOnlyList<VoxelObject> Insert(VoxelScene source, IReadOnlyList<VoxelObject> objects, Vector3 offset, string name)
    {
        if (objects.Count == 0)
        {
            return [];
        }

        ExitEditMode();
        EndStroke();
        CancelExtrude();
        Selection = null;

        var steps = new List<ICommand>();
        byte[] map = ColoursFrom(source.Palette, objects, steps);

        // Parents before children, so each child's parent is in already; linked copies stay linked.
        var grids = new Dictionary<VoxelWorld, VoxelWorld>(ReferenceEqualityComparer.Instance);
        var copyOf = new Dictionary<int, int>();
        var made = new List<VoxelObject>();
        foreach (VoxelObject o in objects.OrderBy(o => DepthIn(source, o)))
        {
            if (!grids.TryGetValue(o.Grid, out VoxelWorld? grid))
            {
                grid = Recoloured(o.Grid, map);
                grids[o.Grid] = grid;
            }

            int parent = source.ParentOf(o) is { } p && copyOf.TryGetValue(p.Id, out int parentCopy) ? parentCopy : 0;
            var command = new CreateObjectCommand(
                Scene,
                grid,
                o.Transform with { Position = o.Transform.Position + offset },
                Scene.Objects.Any(existing => existing.Name == o.Name) ? DuplicateName(o.Name, Scene.Objects.Select(existing => existing.Name)) : o.Name,
                name,
                parentId: parent);

            command.Redo();
            if (o.Modifiers.Count > 0)
            {
                command.Created!.SetModifiers(o.Modifiers);
            }

            steps.Add(command);
            made.Add(command.Created!);
            copyOf[o.Id] = command.Created!.Id;
        }

        SelectOnly([.. made.Select(o => o.Id)]);
        History.Push(CompositeCommand.Of(name, steps));
        HasUnsavedChanges = true;
        return made;
    }

    private static int DepthIn(VoxelScene scene, VoxelObject o)
    {
        int depth = 0;
        for (VoxelObject? parent = scene.ParentOf(o); parent is not null && depth <= scene.Objects.Count; parent = scene.ParentOf(parent))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Where each colour of <paramref name="palette"/> the objects use goes in this level's palette:
    /// the same colour if it is there, else a free custom slot given it (and its material), else the
    /// nearest. The slots claimed are commands in <paramref name="steps"/>, to undo with the rest.
    /// </summary>
    private byte[] ColoursFrom(Palette palette, IReadOnlyList<VoxelObject> objects, List<ICommand> steps)
    {
        var map = new byte[Palette.Size];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = (byte)i;
        }

        if (ReferenceEquals(palette, Scene.Palette))
        {
            return map;
        }

        var used = new bool[Palette.Size];
        foreach (VoxelObject o in objects)
        {
            foreach (Chunk chunk in o.Grid.Chunks.Values)
            {
                foreach (byte index in chunk.Indices)
                {
                    used[index] = true;
                }

                foreach ((_, _, byte index) in chunk.FaceOverrides())
                {
                    used[index] = true;
                }
            }
        }

        for (int index = 1; index < Palette.Size; index++)
        {
            if (!used[index])
            {
                continue;
            }

            Color32 colour = palette[index];
            if (Scene.Palette.FindExact(colour) is { } same)
            {
                map[index] = same;
                continue;
            }

            int free = Enumerable.Range(Palette.CustomStart, Palette.CustomCount).FirstOrDefault(Scene.Palette.IsCustomSlotFree, -1);
            if (free >= 0)
            {
                var claim = new PaletteEditCommand(Scene, free, Scene.Palette[free], colour with { A = 255 });
                claim.Redo();
                Scene.Palette.SetCustomSaved(free, true);
                steps.Add(claim);

                if (!palette.Material(index).IsPlain)
                {
                    var material = new MaterialEditCommand(Scene.Palette, free, Scene.Palette.Material(free), palette.Material(index));
                    material.Redo();
                    steps.Add(material);
                }

                map[index] = (byte)free;
                continue;
            }

            map[index] = Scene.Palette.Nearest(colour);
        }

        return map;
    }

    /// <summary>A copy of a grid with every voxel and painted face's colour put through <paramref name="map"/>.</summary>
    private static VoxelWorld Recoloured(VoxelWorld grid, byte[] map)
    {
        var copy = new VoxelWorld();
        var indices = new byte[Chunk.VoxelCount];
        foreach ((ChunkCoord coord, Chunk chunk) in grid.Chunks)
        {
            if (chunk.IsEmpty)
            {
                continue;
            }

            chunk.Indices.CopyTo(indices);
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = map[indices[i]];
            }

            Chunk target = copy.GetOrCreateChunk(coord);
            target.LoadIndices(indices);
            foreach ((int linear, Face face, byte index) in chunk.FaceOverrides())
            {
                target.LoadFaceOverride(linear, face, map[index]);
            }
        }

        return copy;
    }
}
