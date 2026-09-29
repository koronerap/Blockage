namespace EditorApp.Rendering;

/// <summary>How an object is outlined, weakest first: where two meet, the stronger is drawn.</summary>
public enum OutlineKind
{
    /// <summary>The mouse is over it: a click would select it.</summary>
    Hovered = 1,

    /// <summary>Its row in the outliner is under the mouse.</summary>
    Listed = 2,

    Selected = 3,

    /// <summary>The active one of the selection, what Properties shows.</summary>
    Active = 4,
}
