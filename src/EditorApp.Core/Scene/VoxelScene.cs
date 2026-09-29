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
    private readonly List<SceneCamera> _cameras = [];
    private readonly List<SceneCollection> _collections = [];
    private readonly HashSet<int> _selected = [];

    // One counter for objects and lights alike, so an id names one thing in the level and nothing else.
    private int _nextId = 1;

    public VoxelScene()
    {
        Palette = Palette.CreateDefault();
    }

    public Palette Palette { get; private set; }

    /// <summary>How the level is rendered: size, samples, sky, exposure. Saved with it.</summary>
    public Rendering.RenderSettings RenderSettings { get; set; } = new();

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

    /// <summary>The level's collections, in the order the outliner lists them, each after the one it is inside.</summary>
    public IReadOnlyList<SceneCollection> Collections => _collections;

    private int _activeCollectionId;

    /// <summary>The collection new objects and lights go into — the one last picked in the outliner; 0 for the top.</summary>
    public int ActiveCollectionId
    {
        get => FindCollection(_activeCollectionId) is null ? 0 : _activeCollectionId;
        set => _activeCollectionId = value;
    }

    public SceneCollection? FindCollection(int id) => id == 0 ? null : _collections.Find(c => c.Id == id);

    /// <summary>The collections directly inside <paramref name="parentId"/>, in list order; 0 for the top.</summary>
    public IEnumerable<SceneCollection> CollectionsIn(int parentId) => _collections.Where(c => c.ParentId == parentId);

    /// <summary>Whether <paramref name="id"/> is <paramref name="ancestor"/> or somewhere inside it.</summary>
    public bool IsInside(int id, int ancestor)
    {
        for (int hops = 0; id != 0 && hops <= _collections.Count; hops++)
        {
            if (id == ancestor)
            {
                return true;
            }

            id = FindCollection(id)?.ParentId ?? 0;
        }

        return false;
    }

    /// <summary>A new collection with a fresh id, inside <paramref name="parentId"/>, put in the level.</summary>
    public SceneCollection AddCollection(string name, int parentId)
    {
        var collection = new SceneCollection(_nextId++, name) { ParentId = FindCollection(parentId) is null ? 0 : parentId };
        _collections.Add(collection);
        RefreshCollections();
        return collection;
    }

    /// <summary>
    /// Puts a thing — an object or a light — in a collection; 0 takes it out to the top. A
    /// collection that is not there is none.
    /// </summary>
    public bool SetCollection(int thingId, int collectionId)
    {
        collectionId = FindCollection(collectionId) is null ? 0 : collectionId;
        switch (FindPlaceable(thingId))
        {
            case VoxelObject o when o.CollectionId != collectionId:
                o.CollectionId = collectionId;
                break;
            case SceneLight light when light.CollectionId != collectionId:
                light.CollectionId = collectionId;
                break;
            default:
                return false;
        }

        RefreshCollections();
        return true;
    }

    /// <summary>Moves a collection inside another, or to the top with 0. Refused where it would end up inside itself.</summary>
    public bool MoveCollection(int id, int parentId)
    {
        if (FindCollection(id) is not { } collection || (parentId != 0 && (FindCollection(parentId) is null || IsInside(parentId, id))))
        {
            return false;
        }

        collection.ParentId = parentId;
        RefreshCollections();
        return true;
    }

    /// <summary>
    /// Takes a collection out. What was in it — things and collections alike — goes up into the
    /// collection it was in, as deleting one does in Blender.
    /// </summary>
    public bool RemoveCollection(int id)
    {
        if (FindCollection(id) is not { } collection)
        {
            return false;
        }

        foreach (SceneCollection inner in _collections.Where(c => c.ParentId == id))
        {
            inner.ParentId = collection.ParentId;
        }

        foreach (VoxelObject o in _objects.Where(o => o.CollectionId == id))
        {
            o.CollectionId = collection.ParentId;
        }

        foreach (SceneLight light in _lights.Where(l => l.CollectionId == id))
        {
            light.CollectionId = collection.ParentId;
        }

        _collections.Remove(collection);
        RefreshCollections();
        return true;
    }

    /// <summary>The collections and who is in which, to be put back as they are with <see cref="RestoreCollections"/>.</summary>
    public CollectionLayout CaptureCollections()
    {
        var membership = new Dictionary<int, int>();
        foreach (VoxelObject o in _objects)
        {
            membership[o.Id] = o.CollectionId;
        }

        foreach (SceneLight light in _lights)
        {
            membership[light.Id] = light.CollectionId;
        }

        return new CollectionLayout([.. _collections.Select(c => c.State)], membership);
    }

    public void RestoreCollections(CollectionLayout layout)
    {
        _collections.Clear();
        foreach (CollectionState state in layout.Collections)
        {
            _collections.Add(state.Create());
            _nextId = Math.Max(_nextId, state.Id + 1);
        }

        foreach (VoxelObject o in _objects)
        {
            o.CollectionId = layout.Membership.TryGetValue(o.Id, out int collection) ? collection : o.CollectionId;
        }

        foreach (SceneLight light in _lights)
        {
            light.CollectionId = layout.Membership.TryGetValue(light.Id, out int collection) ? collection : light.CollectionId;
        }

        RefreshCollections();
    }

    /// <summary>
    /// Works out again what every collection's switches mean for what is in it: hidden, locked or
    /// kept out of exports by any collection above. Called after anything about collections changes.
    /// </summary>
    public void RefreshCollections()
    {
        var hidden = new Dictionary<int, (bool Hidden, bool Locked, bool Excluded)>();
        (bool Hidden, bool Locked, bool Excluded) Of(int id, int depth)
        {
            if (id == 0 || depth > _collections.Count || FindCollection(id) is not { } collection)
            {
                return (false, false, false);
            }

            if (hidden.TryGetValue(id, out var known))
            {
                return known;
            }

            var above = Of(collection.ParentId, depth + 1);
            var mine = (above.Hidden || !collection.Visible, above.Locked || collection.Locked, above.Excluded || !collection.Export);
            hidden[id] = mine;
            return mine;
        }

        foreach (VoxelObject o in _objects)
        {
            (bool isHidden, bool isLocked, bool isExcluded) = Of(o.CollectionId, 0);
            o.HiddenByCollection = isHidden;
            o.LockedByCollection = isLocked;
            o.ExcludedByCollection = isExcluded;
        }

        foreach (SceneLight light in _lights)
        {
            (bool isHidden, bool isLocked, _) = Of(light.CollectionId, 0);
            light.HiddenByCollection = isHidden;
            light.LockedByCollection = isLocked;
        }

        // What can no longer be picked is let go.
        _selected.RemoveWhere(id => !CanSelect(id));
    }

    /// <summary>The level's cameras, in the order the outliner lists them. Never exported.</summary>
    public IReadOnlyList<SceneCamera> Cameras => _cameras;

    /// <summary>The camera a render is seen from; 0 for none, when a render is seen from the view.</summary>
    public int ActiveCameraId { get; private set; }

    public SceneCamera? ActiveCamera => FindCamera(ActiveCameraId);

    public SceneCamera? FindCamera(int id) => id == 0 ? null : _cameras.Find(c => c.Id == id);

    /// <summary>An id for a new camera, from the counter objects and lights take theirs from.</summary>
    public int NewCameraId() => _nextId++;

    /// <summary>
    /// Replaces the cameras, and which is active — an active id that is not among them is none.
    /// Commands and loading set them this way, as a whole.
    /// </summary>
    public void SetCameras(IEnumerable<SceneCamera> cameras, int activeId)
    {
        List<SceneCamera> list = [.. cameras];
        _cameras.Clear();
        _cameras.AddRange(list);
        ActiveCameraId = _cameras.Any(c => c.Id == activeId) ? activeId : 0;
        foreach (SceneCamera camera in _cameras)
        {
            _nextId = Math.Max(_nextId, camera.Id + 1);
        }
    }

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

    // ---- Selection -----------------------------------------------------------------------------

    /// <summary>
    /// The objects and lights picked out for the tools to work on, by id. Not part of undo — picking
    /// things is not an edit — but saved with the level, and kept by the commands that take things out
    /// and put them back, so an undone delete comes back selected.
    /// </summary>
    public IReadOnlyCollection<int> SelectedIds => _selected;

    public int SelectedCount => _selected.Count;

    public bool IsSelected(int id) => _selected.Contains(id);

    /// <summary>The selected objects, in list order.</summary>
    public IEnumerable<VoxelObject> SelectedObjects => _objects.Where(o => _selected.Contains(o.Id));

    /// <summary>The selected lights, in list order.</summary>
    public IEnumerable<SceneLight> SelectedLights => _lights.Where(l => _selected.Contains(l.Id));

    /// <summary>
    /// Whether something may be selected: an object that is shown and not locked, or a light that is
    /// not locked. Hidden and locked things are out of the tools' reach, which is what hiding and
    /// locking are for.
    /// </summary>
    public bool CanSelect(int id) =>
        Find(id) is { Visible: true, Locked: false } || FindLight(id) is { Locked: false };

    /// <summary>Adds something to the selection. False when it cannot be selected or already is.</summary>
    public bool Select(int id) => CanSelect(id) && _selected.Add(id);

    public bool Deselect(int id) => _selected.Remove(id);

    public void DeselectAll() => _selected.Clear();

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

        var created = new VoxelObject(_nextId, grid, transform, name ?? $"Object {_nextId}")
        {
            CollectionId = ActiveCollectionId,
        };

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
    public SceneLight CreateLight(LightKind kind, string name) => new(_nextId++, kind, name) { CollectionId = ActiveCollectionId };

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
        (light.HiddenByCollection, light.LockedByCollection, _) = CollectionFlags(light.CollectionId);
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
        _selected.Remove(id);
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

        (item.HiddenByCollection, item.LockedByCollection, item.ExcludedByCollection) = CollectionFlags(item.CollectionId);
    }

    /// <summary>What the collections a thing is in say about it: hidden, locked, kept out of exports.</summary>
    private (bool Hidden, bool Locked, bool Excluded) CollectionFlags(int collectionId)
    {
        bool hidden = false, locked = false, excluded = false;
        for (int hops = 0; FindCollection(collectionId) is { } collection && hops <= _collections.Count; hops++)
        {
            hidden |= !collection.Visible;
            locked |= collection.Locked;
            excluded |= !collection.Export;
            collectionId = collection.ParentId;
        }

        return (hidden, locked, excluded);
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
            RenderSettings = RenderSettings,
            ActiveCameraId = ActiveCameraId,
            _activeCollectionId = _activeCollectionId,
        };

        copy._cameras.AddRange(_cameras);
        copy._collections.AddRange(_collections.Select(c => c.State.Create()));

        copy._selected.UnionWith(_selected);

        // Linked copies stay linked in the copy: each grid copied once, shared as before.
        var grids = new Dictionary<VoxelWorld, VoxelWorld>(ReferenceEqualityComparer.Instance);
        foreach (VoxelObject o in _objects)
        {
            if (!grids.TryGetValue(o.Grid, out VoxelWorld? grid))
            {
                grid = o.Grid.Copy();
                grid.ReplacePalette(copy.Palette);
                grids[o.Grid] = grid;
            }

            var copied = new VoxelObject(o.Id, grid, o.Transform, o.Name)
            {
                OwnVisible = o.OwnVisible,
                OwnLocked = o.OwnLocked,
                ParentId = o.ParentId,
                ParentOffset = o.ParentOffset,
                CollectionId = o.CollectionId,
                Marker = o.Marker,
                Properties = o.Properties,
            };

            if (o.Modifiers.Count > 0)
            {
                copied.SetModifiers(o.Modifiers);
            }

            copy._objects.Add(copied);
        }

        foreach (SceneLight light in _lights)
        {
            copy._lights.Add(light.Copy(light.Id));
        }

        copy.RefreshCollections();
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
        _selected.Remove(id);

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
            // A marker has no voxels to lose.
            if (o.IsEmpty && !o.IsMarker)
            {
                (empty ??= []).Add(o.Id);
            }
        }

        if (empty is null)
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
        _cameras.Clear();
        _collections.Clear();
        _activeCollectionId = 0;
        _selected.Clear();
        FocusId = 0;
        ActiveCameraId = 0;
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
    /// <param name="shown">
    /// Pick what objects show, their modifiers' copies too — for choosing an object. The tools that
    /// edit voxels pick the voxels themselves, which is all they can change.
    /// </param>
    public bool TryPick(
        Ray worldRay,
        out ScenePick pick,
        float maxDistance = VoxelRaycaster.DefaultMaxDistance,
        bool includeLocked = false,
        Func<VoxelObject, bool>? skip = null,
        bool shown = false,
        ClipBox? clip = null)
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
            // What a section box cuts away is looked straight through.
            ObjectTransform placed = o.Transform;
            Func<int, int, int, bool>? cut = clip is { } box ? (x, y, z) => !box.Contains(placed, x, y, z) : null;
            if (!VoxelRaycaster.TryCast(shown ? o.Shown : o.Grid, localRay, out RaycastHit hit, maxDistance / scale, cut))
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
