using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Scene;

/// <summary>What a picking ray hit, and which object it belongs to.</summary>
/// <param name="Object">The object that was hit.</param>
/// <param name="Hit">The hit in that object's local voxel space.</param>
/// <param name="Distance">Distance along the world ray. Rigid transforms preserve it, so local and world agree.</param>
public readonly record struct ScenePick(VoxelObject Object, RaycastHit Hit, float Distance);

/// <summary>
/// The level as a set of independently placed objects (EditorApp.md, "Ortak davranışlar"). One
/// palette is shared by all of them: colour is a property of the level, not of a piece of it, and
/// the export has to end up as one material either way.
/// </summary>
public sealed class VoxelScene
{
    private readonly List<VoxelObject> _objects = [];
    private int _nextId = 1;

    public VoxelScene()
    {
        Palette = Palette.CreateDefault();
    }

    public Palette Palette { get; private set; }

    public IReadOnlyList<VoxelObject> Objects => _objects;

    /// <summary>Id of the object the tools act on. 0 when the scene is empty.</summary>
    public int FocusId { get; private set; }

    public VoxelObject? Focus => _objects.Find(o => o.Id == FocusId);

    public int SolidCount
    {
        get
        {
            int total = 0;
            foreach (VoxelObject o in _objects)
            {
                total += o.Grid.SolidCount;
            }

            return total;
        }
    }

    public VoxelObject Add(VoxelWorld grid, ObjectTransform transform, string? name = null)
    {
        // Every grid points at the scene's palette, so recoloring an index repaints the whole level.
        grid.ReplacePalette(Palette);

        var created = new VoxelObject(_nextId, grid, transform, name ?? $"Object {_nextId}");
        _nextId++;

        _objects.Add(created);
        if (FocusId == 0)
        {
            FocusId = created.Id;
        }

        return created;
    }

    public bool Remove(int id)
    {
        int index = _objects.FindIndex(o => o.Id == id);
        if (index < 0)
        {
            return false;
        }

        _objects.RemoveAt(index);

        if (FocusId == id)
        {
            FocusId = _objects.Count > 0 ? _objects[Math.Min(index, _objects.Count - 1)].Id : 0;
        }

        return true;
    }

    /// <summary>Drops objects that no longer hold any voxels — what a full intrude leaves behind.</summary>
    public int RemoveEmptyObjects()
    {
        List<int>? empty = null;
        foreach (VoxelObject o in _objects)
        {
            if (o.IsEmpty)
            {
                (empty ??= []).Add(o.Id);
            }
        }

        if (empty is null)
        {
            return 0;
        }

        // Never leave the scene with nothing at all: with no Place tool there would be no way back.
        if (empty.Count == _objects.Count)
        {
            return 0;
        }

        foreach (int id in empty)
        {
            Remove(id);
        }

        return empty.Count;
    }

    public bool SetFocus(int id)
    {
        if (_objects.All(o => o.Id != id))
        {
            return false;
        }

        FocusId = id;
        return true;
    }

    public void Clear()
    {
        _objects.Clear();
        FocusId = 0;
        _nextId = 1;
    }

    public void ReplacePalette(Palette palette)
    {
        Palette = palette;
        foreach (VoxelObject o in _objects)
        {
            o.Grid.ReplacePalette(palette);
        }
    }

    public void MarkAllDirty()
    {
        foreach (VoxelObject o in _objects)
        {
            o.Grid.MarkAllDirty();
        }
    }

    /// <summary>
    /// Picks across every visible object by moving the ray into each one's own space. The nearest
    /// hit wins, which is what makes focus follow whatever the cursor is actually over.
    /// </summary>
    public bool TryPick(Ray worldRay, out ScenePick pick, float maxDistance = VoxelRaycaster.DefaultMaxDistance)
    {
        pick = default;
        bool found = false;
        float nearest = float.MaxValue;

        foreach (VoxelObject o in _objects)
        {
            if (!o.Visible || o.IsEmpty)
            {
                continue;
            }

            Ray localRay = o.Transform.InverseTransformRay(worldRay);
            if (!VoxelRaycaster.TryCast(o.Grid, localRay, out RaycastHit hit, maxDistance))
            {
                continue;
            }

            if (hit.Distance >= nearest)
            {
                continue;
            }

            nearest = hit.Distance;
            pick = new ScenePick(o, hit, hit.Distance);
            found = true;
        }

        return found;
    }

    /// <summary>World-space bounds of everything in the scene.</summary>
    public bool TryGetWorldBounds(out Vector3 min, out Vector3 max)
    {
        min = max = Vector3.Zero;
        bool any = false;

        foreach (VoxelObject o in _objects)
        {
            if (!o.TryGetWorldBounds(out Vector3 objectMin, out Vector3 objectMax))
            {
                continue;
            }

            min = any ? Vector3.Min(min, objectMin) : objectMin;
            max = any ? Vector3.Max(max, objectMax) : objectMax;
            any = true;
        }

        return any;
    }

    /// <summary>
    /// Order-independent hash of the whole scene. Used by tests to compare states.
    ///
    /// Transform components are quantised rather than hashed bit for bit: a transform that has been
    /// through a file and back is the same placement even if the last bit of a float moved, and a
    /// hash that disagreed about that would be useless for exactly the comparison it exists for.
    /// </summary>
    public ulong ContentHash()
    {
        ulong total = 0;
        foreach (VoxelObject o in _objects)
        {
            ulong hash = o.Grid.ContentHash();

            hash = Mix(hash, Quantise(o.Transform.Position.X));
            hash = Mix(hash, Quantise(o.Transform.Position.Y));
            hash = Mix(hash, Quantise(o.Transform.Position.Z));
            hash = Mix(hash, Quantise(o.Transform.Rotation.X));
            hash = Mix(hash, Quantise(o.Transform.Rotation.Y));
            hash = Mix(hash, Quantise(o.Transform.Rotation.Z));
            hash = Mix(hash, Quantise(o.Transform.Rotation.W));

            total += hash;
        }

        return total;
    }

    /// <summary>Rounds to about a thousandth of a voxel, well below anything that can be seen.</summary>
    private static long Quantise(float value) => (long)MathF.Round(value * 1024f);

    private static ulong Mix(ulong hash, long value) =>
        (hash ^ (ulong)value) * 0x9E3779B97F4A7C15UL;

    /// <summary>A scene holding one 8³ white cube — what New starts from.</summary>
    public static VoxelScene CreateStarter(byte paletteIndex = Palette.WhiteIndex)
    {
        var scene = new VoxelScene();
        scene.Add(Editing.EditorSession.CreateStarterWorld(paletteIndex), ObjectTransform.Identity, "Object 1");
        return scene;
    }
}
