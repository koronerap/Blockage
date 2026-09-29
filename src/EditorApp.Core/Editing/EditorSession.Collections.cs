using EditorApp.Core.Commands;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Editing;

/// <summary>A change to the level's collections — made, taken out, moved or filled — as their layout before and after.</summary>
public sealed class CollectionCommand(VoxelScene scene, string name, CollectionLayout before, CollectionLayout after) : ICommand
{
    public string Name { get; } = name;

    public int RetainedCells => 0;

    public void Redo() => scene.RestoreCollections(after);

    public void Undo() => scene.RestoreCollections(before);
}

/// <summary>
/// Collections (Fullreleaseplan 6.1): making, nesting and deleting them and moving things into them
/// are undo steps; their switches — shown, locked, exported — are ways of looking, outside undo, as an
/// object's own eye and lock are.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// A new collection inside <paramref name="parentId"/> (0 for the top), holding
    /// <paramref name="moving"/> if given, and made the one new things go into. One undo step.
    /// </summary>
    public SceneCollection NewCollection(int parentId = 0, IEnumerable<int>? moving = null)
    {
        CollectionLayout before = Scene.CaptureCollections();
        string name = Scene.Collections.Any(c => c.Name == "Collection")
            ? DuplicateName("Collection", Scene.Collections.Select(c => c.Name))
            : "Collection";

        SceneCollection created = Scene.AddCollection(name, parentId);
        foreach (int id in moving ?? [])
        {
            Scene.SetCollection(id, created.Id);
        }

        PushLayout(before, $"New {name}");
        Scene.ActiveCollectionId = created.Id;
        KeepFocusOnShown();
        return created;
    }

    /// <summary>Takes a collection out; what was in it goes up into the collection it was in. One undo step.</summary>
    public bool DeleteCollection(int id)
    {
        if (Scene.FindCollection(id) is not { } collection)
        {
            return false;
        }

        CollectionLayout before = Scene.CaptureCollections();
        Scene.RemoveCollection(id);
        PushLayout(before, $"Delete {collection.Name}");
        KeepFocusOnShown();
        return true;
    }

    /// <summary>Blender's M: things put in a collection, or taken out to the top with 0. One undo step.</summary>
    public bool MoveToCollection(IEnumerable<int> thingIds, int collectionId)
    {
        CollectionLayout before = Scene.CaptureCollections();
        bool moved = false;
        foreach (int id in thingIds.ToList())
        {
            moved |= Scene.SetCollection(id, collectionId);
        }

        if (!moved)
        {
            return false;
        }

        PushLayout(before, collectionId == 0 ? "Move Out of Collections" : $"Move to {Scene.FindCollection(collectionId)?.Name}");
        KeepFocusOnShown();
        return true;
    }

    /// <summary>What is selected, moved into a collection. One undo step.</summary>
    public bool MoveSelectedToCollection(int collectionId) =>
        MoveToCollection([.. Scene.SelectedObjects.Select(o => o.Id), .. Scene.SelectedLights.Select(l => l.Id)], collectionId);

    /// <summary>A collection put inside another, or at the top with 0. Refused where it would be inside itself. One undo step.</summary>
    public bool MoveCollection(int id, int parentId)
    {
        if (Scene.FindCollection(id) is not { } collection || collection.ParentId == parentId)
        {
            return false;
        }

        CollectionLayout before = Scene.CaptureCollections();
        if (!Scene.MoveCollection(id, parentId))
        {
            return false;
        }

        PushLayout(before, $"Move {collection.Name}");
        KeepFocusOnShown();
        return true;
    }

    public bool RenameCollection(int id, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || Scene.FindCollection(id) is not { } collection)
        {
            return false;
        }

        if (collection.Name != trimmed)
        {
            collection.Name = trimmed;
            HasUnsavedChanges = true;
        }

        return true;
    }

    /// <summary>Shows or hides everything in a collection. Saved, outside undo, like an object's eye.</summary>
    public bool SetCollectionVisible(int id, bool visible) => SetSwitch(id, c => c.Visible, (c, on) => c.Visible = on, visible);

    /// <summary>Locks or unlocks everything in a collection. Saved, outside undo, like an object's lock.</summary>
    public bool SetCollectionLocked(int id, bool locked) => SetSwitch(id, c => c.Locked, (c, on) => c.Locked = on, locked);

    /// <summary>Keeps what is in a collection out of exports, or lets it back in. Saved, outside undo.</summary>
    public bool SetCollectionExported(int id, bool export) => SetSwitch(id, c => c.Export, (c, on) => c.Export = on, export);

    /// <summary>Selects everything that can be selected in a collection and the collections inside it.</summary>
    public void SelectCollection(int id)
    {
        if (Scene.FindCollection(id) is null)
        {
            return;
        }

        List<int> inside =
        [
            .. Scene.Objects.Where(o => Scene.IsInside(o.CollectionId, id)).Select(o => o.Id),
            .. Scene.Lights.Where(l => Scene.IsInside(l.CollectionId, id)).Select(l => l.Id),
        ];

        SelectMany(inside, SelectionOperation.Replace);
        Scene.ActiveCollectionId = id;
    }

    private bool SetSwitch(int id, Func<SceneCollection, bool> get, Action<SceneCollection, bool> set, bool on)
    {
        if (Scene.FindCollection(id) is not { } collection || get(collection) == on)
        {
            return false;
        }

        set(collection, on);
        Scene.RefreshCollections();
        KeepFocusOnShown();
        HasUnsavedChanges = true;
        return true;
    }

    private void PushLayout(CollectionLayout before, string name)
    {
        History.Push(new CollectionCommand(Scene, name, before, Scene.CaptureCollections()));
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// After a collection hid or locked what was being worked on: the tools let go of it, and focus
    /// moves to the nearest object that can hold it, as hiding the object itself does.
    /// </summary>
    private void KeepFocusOnShown()
    {
        KeepEditModeHonest();
        if (Scene.Focus is { } focus && (!focus.Visible || focus.Locked))
        {
            EndStroke();
            CancelExtrude();
            Selection = null;
            if (NearestVisible(Scene.IndexOf(focus.Id)) is { } next)
            {
                Scene.SetFocus(next.Id);
            }
        }

        if (SelectedLightId != 0 && !Scene.IsSelected(SelectedLightId))
        {
            SelectedLightId = 0;
        }
    }
}
