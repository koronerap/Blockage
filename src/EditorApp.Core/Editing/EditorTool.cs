namespace EditorApp.Core.Editing;

/// <summary>
/// The tool set is fixed at exactly four tools plus a camera-only mode (EditorApp.md, "Araçlar").
///
/// There is deliberately no Place and no Erase tool. Adding voxels <i>is</i> pulling Extrude
/// outward; removing them <i>is</i> pushing it in. A separate place/erase tool would let one job be
/// done through two different mental models and would make the extrude flow redundant.
/// </summary>
public enum EditorTool
{
    /// <summary>Moves and rotates a whole object. The default tool.</summary>
    Transform,

    /// <summary>Selects a surface and drags it along its normal: outward adds, inward deletes.</summary>
    Extrude,

    /// <summary>Recolors existing, externally visible voxels. Never creates or deletes.</summary>
    Paint,

    /// <summary>Splits the grid at an axis-aligned plane into two independent objects.</summary>
    LoopCut,

    /// <summary>Camera only — no editing at all.</summary>
    View,

    /// <summary>
    /// Picks what the other tools work on: a click selects, a drag draws a box. Last in the list so
    /// the numbers the others were saved under stay theirs; the toolbar shows it first.
    /// </summary>
    Select,
}

/// <summary>What a Transform drag of several things turns about, Blender's pivot point.</summary>
public enum TransformPivot
{
    /// <summary>The middle of everything selected.</summary>
    MedianPoint,

    /// <summary>The active one's centre.</summary>
    ActiveElement,

    /// <summary>Each turns about its own centre; a move still moves them all together.</summary>
    IndividualOrigins,
}

/// <summary>Transform's two sub-modes, toggled with F.</summary>
public enum TransformMode
{
    Move,
    Rotate,
}

/// <summary>Toggled with X. Affects Move only; the edge hinge is always local.</summary>
public enum TransformSpace
{
    Global,
    Local,
}

/// <summary>How Extrude picks its surface. Toggled with F.</summary>
public enum ExtrudeSelectionMode
{
    /// <summary>Mouse-down on a face and drag out a rectangle.</summary>
    Box,

    /// <summary>One click takes the whole connected, coplanar, externally visible patch.</summary>
    Face,
}

/// <summary>What a drag does to the current selection. Captured when the drag starts.</summary>
public enum SelectionOperation
{
    Replace,
    Add,
    Subtract,
}

/// <summary>Paint's sub-modes, cycled with X.</summary>
public enum PaintMode
{
    Brush,
    Bucket,
    Pattern,
}
