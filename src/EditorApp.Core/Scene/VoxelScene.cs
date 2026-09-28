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
    /// <summary>Smallest and largest world size a single voxel may be given.</summary>
    public const float MinVoxelSize = 0.001f;

    public const float MaxVoxelSize = 1000f;

    private readonly List<VoxelObject> _objects = [];
    private int _nextId = 1;
    private float _voxelSize = 1f;

    public VoxelScene()
    {
        Palette = Palette.CreateDefault();
    }

    public Palette Palette { get; private set; }

    /// <summary>
    /// How much world space one voxel occupies in the exported mesh. Fixed for the whole level: it
    /// is the unit the level is drawn in, and a level whose unit changed partway through would not
    /// mean anything.
    ///
    /// Nothing inside the editor is measured in these units. Editing, picking, transforms and the
    /// grid all stay at one unit per voxel, because that is the space voxels are actually indexed
    /// in and scaling it would put a conversion between every click and the cell it lands on for no
    /// gain. The size is applied once, to the finished export, and shown wherever a real-world
    /// dimension is worth reading.
    /// </summary>
    public float VoxelSize
    {
        get => _voxelSize;
        set => _voxelSize = float.IsFinite(value) ? Math.Clamp(value, MinVoxelSize, MaxVoxelSize) : _voxelSize;
    }

    /// <summary>True when one voxel is not one world unit, so scaled figures are worth showing.</summary>
    public bool HasCustomVoxelSize => MathF.Abs(_voxelSize - 1f) > 1e-6f;

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

    /// <param name="insertAt">Where in the list it goes; the end when null.</param>
    public VoxelObject Add(VoxelWorld grid, ObjectTransform transform, string? name = null, int? insertAt = null)
    {
        // Every grid points at the scene's palette, so recoloring an index repaints the whole level.
        grid.ReplacePalette(Palette);

        var created = new VoxelObject(_nextId, grid, transform, name ?? $"Object {_nextId}");
        _nextId++;

        Insert(created, insertAt);
        if (FocusId == 0)
        {
            FocusId = created.Id;
        }

        return created;
    }

    /// <summary>
    /// Puts a previously removed object back, keeping its identity. Undo needs the original object
    /// itself, not a copy of it, so anything still holding a reference stays correct — and it goes
    /// back where it was in the list, so undoing a delete does not reshuffle the outliner.
    /// </summary>
    public void Restore(VoxelObject original, int? insertAt = null)
    {
        if (_objects.Any(o => o.Id == original.Id))
        {
            return;
        }

        original.Grid.ReplacePalette(Palette);
        Insert(original, insertAt);

        if (FocusId == 0)
        {
            FocusId = original.Id;
        }
    }

    /// <summary>The object with this id, or null.</summary>
    public VoxelObject? Find(int id) => _objects.Find(o => o.Id == id);

    /// <summary>Where the object sits in the list, or -1.</summary>
    public int IndexOf(int id) => _objects.FindIndex(o => o.Id == id);

    private void Insert(VoxelObject item, int? index)
    {
        if (index is { } at)
        {
            _objects.Insert(Math.Clamp(at, 0, _objects.Count), item);
        }
        else
        {
            _objects.Add(item);
        }
    }

    /// <summary>
    /// A deep copy of the whole level — objects, voxels, painted faces, palette — sharing nothing
    /// with this one. For writing it out away from the editing thread: once taken, nothing the editor
    /// does can reach into it halfway through being saved.
    /// </summary>
    public VoxelScene Snapshot()
    {
        var copy = new VoxelScene
        {
            _voxelSize = _voxelSize,
            _nextId = _nextId,
            FocusId = FocusId,
            Palette = Palette.Clone(),
        };

        foreach (VoxelObject o in _objects)
        {
            VoxelWorld grid = o.Grid.Copy();
            grid.ReplacePalette(copy.Palette);
            copy._objects.Add(new VoxelObject(o.Id, grid, o.Transform, o.Name) { Visible = o.Visible });
        }

        return copy;
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
