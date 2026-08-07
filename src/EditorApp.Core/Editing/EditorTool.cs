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
}
