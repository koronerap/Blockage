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
    public const float MinVoxelSize = ObjectTransform.MinVoxelSize;

    public const float MaxVoxelSize = ObjectTransform.MaxVoxelSize;

    /// <summary>The ambient floor a new level starts with: the viewport's before lights were in the level.</summary>
    public const float DefaultAmbient = 0.32f;

    private readonly List<VoxelObject> _objects = [];
    private readonly List<SceneLight> _lights = [];

    // One counter for objects and lights alike, so an id names one thing in the level and nothing else.
    private int _nextId = 1;

    public VoxelScene()
    {
        Palette = Palette.CreateDefault();
    }

    public Palette Palette { get; private set; }

    /// <summary>
    /// The voxel size every visible object shares, or null when they differ (or there are none). A
    /// single number is only worth showing when it describes the whole level.
    /// </summary>
    public float? SharedVoxelSize
    {
        get
        {
            float? shared = null;
            foreach (VoxelObject o in _objects)
            {
                if (!o.Visible || o.IsEmpty)
                {
                    continue;
                }

                if (shared is { } size && MathF.Abs(size - o.VoxelSize) > 1e-6f)
                {
                    return null;
                }

                shared = o.VoxelSize;
            }

            return shared;
        }
    }

    public IReadOnlyList<VoxelObject> Objects => _objects;

    /// <summary>The level's lights, in the order the outliner lists them. Never exported.</summary>
    public IReadOnlyList<SceneLight> Lights => _lights;

    /// <summary>
    /// How much light every face gets whichever way it points — without it, faces turned away from
    /// every light go black and their colour cannot be judged at all. Saved with the level.
    /// </summary>
    public float Ambient
    {
        get => _ambient;
        set => _ambient = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : _ambient;
    }

    private float _ambient = DefaultAmbient;

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
        Reattach(original.Id);

        if (FocusId == 0)
        {
            FocusId = original.Id;
        }
    }

    /// <summary>The object with this id, or null.</summary>
    public VoxelObject? Find(int id) => _objects.Find(o => o.Id == id);

    // ---- Lights --------------------------------------------------------------------------------

    /// <summary>A new light with a fresh id, not yet in the level — an add command puts it there.</summary>
    public SceneLight CreateLight(LightKind kind, string name) => new(_nextId++, kind, name);

    public SceneLight AddLight(LightKind kind, string name, int? insertAt = null)
    {
        SceneLight light = CreateLight(kind, name);
        RestoreLight(light, insertAt);
        return light;
    }

    /// <summary>Puts a light (back) into the level, keeping its identity. Adding one already there does nothing.</summary>
    public void RestoreLight(SceneLight light, int? insertAt = null)
    {
        if (_lights.Any(l => l.Id == light.Id))
        {
            return;
        }

        if (insertAt is { } at)
        {
            _lights.Insert(Math.Clamp(at, 0, _lights.Count), light);
        }
        else
        {
            _lights.Add(light);
        }

        light.Moved = OnMoved;
        HoldAtCurrentPlace(light);
        _nextId = Math.Max(_nextId, light.Id + 1);
    }

    // ---- Parents -------------------------------------------------------------------------------

    /// <summary>An object or a light by id, whichever it is.</summary>
    public IPlaceable? FindPlaceable(int id) => (IPlaceable?)Find(id) ?? FindLight(id);

    /// <summary>
    /// The parent something has now: an object still in the level. A parent that was deleted leaves
    /// its id behind on its children, so that undoing the delete puts them back under it; until then
    /// they are simply without one.
    /// </summary>
    public VoxelObject? ParentOf(IPlaceable child) => child.ParentId == 0 ? null : Find(child.ParentId);

    /// <summary>The objects and lights whose parent this is, in list order, objects first.</summary>
    public IEnumerable<IPlaceable> ChildrenOf(int id)
    {
        foreach (VoxelObject o in _objects)
        {
            if (o.ParentId == id && o.Id != id)
            {
                yield return o;
            }
        }

        foreach (SceneLight light in _lights)
        {
            if (light.ParentId == id)
            {
                yield return light;
            }
        }
    }

    /// <summary>Whether <paramref name="id"/> is below <paramref name="ancestorId"/> — its child, its child's child, and so on.</summary>
    public bool IsDescendantOf(int id, int ancestorId)
    {
        IPlaceable? step = FindPlaceable(id);

        // Bounded by the number of objects, so a cycle that got in some other way cannot hang this.
        for (int hops = 0; step is not null && hops <= _objects.Count; hops++)
        {
            if (ParentOf(step) is not { } parent)
            {
                return false;
            }

            if (parent.Id == ancestorId)
            {
                return true;
            }

            step = parent;
        }

        return false;
    }

    /// <summary>Why one thing cannot be made the child of another, in words for a tooltip; null when it can.</summary>
    public string? ParentProblem(int childId, int parentId)
    {
        if (FindPlaceable(childId) is not { } child)
        {
            return "There is nothing to parent.";
        }

        if (parentId == 0)
        {
            return null;
        }

        if (Find(parentId) is not { } parent)
        {
            return "Only an object can be a parent.";
        }

        if (parentId == childId)
        {
            return "Nothing can be its own parent.";
        }

        if (IsDescendantOf(parentId, childId))
        {
            return $"{parent.Name} is below {child.Name} already - it would be its own grandparent.";
        }

        return null;
    }

    /// <summary>
    /// Makes one thing the child of an object, or of nothing with 0. It stays exactly where it is —
    /// Blender's "Keep Transform" — and from now on moves with its parent. Refused as
    /// <see cref="ParentProblem"/> says.
    /// </summary>
    public bool SetParent(int childId, int parentId)
    {
        if (ParentProblem(childId, parentId) is not null || FindPlaceable(childId) is not { } child)
        {
            return false;
        }

        switch (child)
        {
            case VoxelObject o:
                o.ParentId = parentId;
                break;

            case SceneLight light:
                light.ParentId = parentId;
                break;
        }

        HoldAtCurrentPlace(child);
        return true;
    }

    /// <summary>Takes the offset a child is held at from where it and its parent are now.</summary>
    private void HoldAtCurrentPlace(IPlaceable child)
    {
        ObjectTransform offset = ParentOf(child) is { } parent
            ? Parenting.Relative(parent.Transform, child.Transform)
            : ObjectTransform.Identity;

        switch (child)
        {
            case VoxelObject o:
                o.ParentOffset = offset;
                break;

            case SceneLight light:
                light.ParentOffset = offset;
                break;
        }
    }

    /// <summary>
    /// Something moved. Moved by hand, it is held at its new place in its parent's frame; either way,
    /// its children go with it.
    /// </summary>
    private void OnMoved(IPlaceable moved, bool byParent)
    {
        if (!byParent)
        {
            HoldAtCurrentPlace(moved);
        }

        if (moved is not VoxelObject parent)
        {
            return;
        }

        foreach (IPlaceable child in ChildrenOf(parent.Id).ToArray())
        {
            ObjectTransform world = Parenting.Compose(parent.Transform, OffsetOf(child), child.Transform.VoxelSize);

            switch (child)
            {
                case VoxelObject o:
                    o.Follow(world);
                    break;

                case SceneLight light:
                    light.Follow(world);
                    break;
            }
        }
    }

    /// <summary>Where something is held in its parent's frame.</summary>
    public static ObjectTransform OffsetOf(IPlaceable thing) => thing switch
    {
        VoxelObject o => o.ParentOffset,
        SceneLight light => light.ParentOffset,
        _ => ObjectTransform.Identity,
    };

    /// <summary>
    /// Puts back a parent and offset exactly as they were, a deleted parent's id included — for undo,
    /// where nothing has moved since they were taken, so there is nothing to work out again.
    /// </summary>
    internal static void PlaceUnder(IPlaceable child, int parentId, ObjectTransform offset)
    {
        switch (child)
        {
            case VoxelObject o:
                o.ParentId = parentId;
                o.ParentOffset = offset;
                break;

            case SceneLight light:
                light.ParentId = parentId;
                light.ParentOffset = offset;
                break;
        }
    }

    /// <summary>
    /// A parent back in the level: its children, which kept its id, are held again at wherever they
    /// are now — they may have been moved while it was gone.
    /// </summary>
    private void Reattach(int parentId)
    {
        foreach (IPlaceable child in ChildrenOf(parentId).ToArray())
        {
            HoldAtCurrentPlace(child);
        }
    }

    public bool RemoveLight(int id)
    {
        int index = IndexOfLight(id);
        if (index < 0)
        {
            return false;
        }

        // Out of the level, moving it — as an undo holding it might — moves nothing that is in it.
        _lights[index].Moved = null;
        _lights.RemoveAt(index);
        return true;
    }

    public SceneLight? FindLight(int id) => _lights.Find(l => l.Id == id);

    public int IndexOfLight(int id) => _lights.FindIndex(l => l.Id == id);

    /// <summary>
    /// The sun a new level starts with, and a level from before lights were saved gets: the light
    /// the viewport always had, from the same bearing and height, so nothing looks different.
    /// </summary>
    public SceneLight AddDefaultSun()
    {
        SceneLight sun = AddLight(LightKind.Directional, "Sun");
        Vector3 direction = SceneLight.ShiningFrom(SceneLight.SunAzimuth, SceneLight.SunElevation);

        // Placed up the way the light comes from, where its icon is out of the way of the model.
        Vector3 above = TryGetWorldBounds(out Vector3 min, out Vector3 max) ? (min + max) * 0.5f : Vector3.Zero;
        float lift = MathF.Max((max - min).Length(), 8f);

        sun.Transform = new ObjectTransform(above - (direction * lift), SceneLight.Aiming(direction));
        sun.Intensity = SceneLight.SunIntensity;
        return sun;
    }

    /// <summary>Where the object sits in the list, or -1.</summary>
    public int IndexOf(int id) => _objects.FindIndex(o => o.Id == id);

    private void Insert(VoxelObject item, int? index)
    {
        item.Moved = OnMoved;

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
            _nextId = _nextId,
            _ambient = _ambient,
            FocusId = FocusId,
            Palette = Palette.Clone(),
        };

        foreach (VoxelObject o in _objects)
        {
            VoxelWorld grid = o.Grid.Copy();
            grid.ReplacePalette(copy.Palette);
            copy._objects.Add(new VoxelObject(o.Id, grid, o.Transform, o.Name)
            {
                Visible = o.Visible,
                Locked = o.Locked,
                ParentId = o.ParentId,
                ParentOffset = o.ParentOffset,
            });
        }

        foreach (SceneLight light in _lights)
        {
            copy._lights.Add(light.Copy(light.Id));
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

        // Its children keep its id, and so find it again if it is put back; until then they are free.
        _objects[index].Moved = null;
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
        foreach (VoxelObject o in _objects)
        {
            o.Moved = null;
        }

        foreach (SceneLight light in _lights)
        {
            light.Moved = null;
        }

        _objects.Clear();
        _lights.Clear();
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
    ///
    /// Locked objects are passed through, so what stands in front of or on top of them can be
    /// reached. Something that only needs a point in the world — where to aim a light — asks for them
    /// with <paramref name="includeLocked"/>, since a floor is exactly what a light is aimed at.
    /// </summary>
    /// <param name="skip">Objects to see straight through — the one being moved, when snapping it to what is behind.</param>
    public bool TryPick(
        Ray worldRay,
        out ScenePick pick,
        float maxDistance = VoxelRaycaster.DefaultMaxDistance,
        bool includeLocked = false,
        Func<VoxelObject, bool>? skip = null)
    {
        pick = default;
        bool found = false;
        float nearest = float.MaxValue;

        foreach (VoxelObject o in _objects)
        {
            if (!o.Visible || o.IsEmpty || (o.Locked && !includeLocked) || (skip?.Invoke(o) ?? false))
            {
                continue;
            }

            // The local ray walks voxels, so its distances are in this object's voxels. Objects of
            // different sizes are compared in the world, or a small-voxel object behind a large one
            // would win just for having more cells in the way.
            float scale = o.VoxelSize;
            Ray localRay = o.Transform.InverseTransformRay(worldRay);
            if (!VoxelRaycaster.TryCast(o.Grid, localRay, out RaycastHit hit, maxDistance / scale))
            {
                continue;
            }

            float distance = hit.Distance * scale;
            if (distance >= nearest)
            {
                continue;
            }

            nearest = distance;
            pick = new ScenePick(o, hit, distance);
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
            hash = Mix(hash, Quantise(o.Transform.VoxelSize));

            total += hash;
        }

        return total;
    }

    /// <summary>Rounds to about a thousandth of a voxel, well below anything that can be seen.</summary>
    private static long Quantise(float value) => (long)MathF.Round(value * 1024f);

    private static ulong Mix(ulong hash, long value) =>
        (hash ^ (ulong)value) * 0x9E3779B97F4A7C15UL;

    /// <summary>A scene holding one 8³ white cube and the sun — what New starts from.</summary>
    public static VoxelScene CreateStarter(byte paletteIndex = Palette.WhiteIndex)
    {
        var scene = new VoxelScene();
        scene.Add(Editing.EditorSession.CreateStarterWorld(paletteIndex), ObjectTransform.Identity, "Object 1");
        scene.AddDefaultSun();
        return scene;
    }
}
