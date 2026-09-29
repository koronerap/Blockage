namespace EditorApp.Core.Scene;

/// <summary>
/// A collection (Fullreleaseplan 6.1), as Blender has them: a named group of objects and lights —
/// and of other collections, nested as deep as wanted — with a switch each for showing, locking and
/// exporting what is in it. A switch turned off holds for everything below, whatever that thing's
/// own switch says. Saved with the level; never exported as anything itself.
/// </summary>
public sealed class SceneCollection(int id, string name)
{
    public int Id { get; } = id;

    public string Name { get; set; } = name;

    /// <summary>The collection this one is inside; 0 for the level's top.</summary>
    public int ParentId { get; internal set; }

    /// <summary>What is in it is drawn — as far as its own switches and the collections above say.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>What is in it cannot be picked, moved or edited.</summary>
    public bool Locked { get; set; }

    /// <summary>What is in it goes into exports; off, it stays in the level but out of the game.</summary>
    public bool Export { get; set; } = true;

    public CollectionState State => new(Id, Name, ParentId, Visible, Locked, Export);
}

/// <summary>Everything about a collection at one moment, for undo and for copies.</summary>
public readonly record struct CollectionState(int Id, string Name, int ParentId, bool Visible, bool Locked, bool Export)
{
    public SceneCollection Create() => new(Id, Name)
    {
        ParentId = ParentId,
        Visible = Visible,
        Locked = Locked,
        Export = Export,
    };
}

/// <summary>
/// The level's collections and which one each object and light is in, as a whole: what a change to
/// them is undone to. Small — names, switches and ids — so undo keeps it whole.
/// </summary>
public sealed record CollectionLayout(IReadOnlyList<CollectionState> Collections, IReadOnlyDictionary<int, int> Membership);
