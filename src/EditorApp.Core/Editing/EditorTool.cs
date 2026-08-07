namespace EditorApp.Core.Editing;

/// <summary>The modelling tools of v1 (EditorApp.md §1).</summary>
public enum EditorTool
{
    /// <summary>Adds a voxel in the empty cell in front of the picked face.</summary>
    Place,

    /// <summary>Removes the picked voxel.</summary>
    Erase,

    /// <summary>Recolors the picked voxel without changing the shape.</summary>
    Paint,

    /// <summary>Recolors the connected run of same-colored voxels.</summary>
    Fill,

    /// <summary>Eyedropper: adopts the picked voxel's color as the active one.</summary>
    Pick,

    /// <summary>Drags out an axis-aligned box selection to operate on as a whole.</summary>
    BoxSelect,

    /// <summary>Pulls the connected coplanar surface under the cursor out by one layer.</summary>
    Extrude,
}
