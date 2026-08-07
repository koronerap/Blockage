using EditorApp.Core.Commands;
using EditorApp.Core.Raycast;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// What the editor is currently doing: the world, the undo history, the active tool and color.
/// Deliberately free of any windowing or GL dependency so tool behaviour can be tested directly.
/// </summary>
public sealed class EditorSession
{
    private VoxelEditCommand? _stroke;

    public VoxelWorld World { get; private set; } = new();

    public UndoStack History { get; } = new();

    public EditorTool ActiveTool { get; set; } = EditorTool.Place;

    /// <summary>Palette index the tools write. Never 0 — that is the empty marker.</summary>
    public byte ActiveColorIndex { get; set; } = 1;

    /// <summary>0 paints a single voxel, n paints a (2n+1)³ cube.</summary>
    public int BrushRadius { get; set; }

    /// <summary>True when the world has changed since the last save.</summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>Path of the open project, or null for an unsaved one.</summary>
    public string? ProjectPath { get; set; }

    public string ProjectName => ProjectPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>Number of voxels changed by the stroke in progress. Drives the status line.</summary>
    public int StrokeCellCount => _stroke?.RetainedCells ?? 0;

    public bool IsStrokeActive => _stroke is not null;

    /// <summary>
    /// Starts a gesture. Everything applied until <see cref="EndStroke"/> becomes one undo step, so
    /// a whole click-and-drag reverses as a single action.
    /// </summary>
    public void BeginStroke()
    {
        _stroke ??= new VoxelEditCommand(ActiveTool.ToString());
    }

    public void EndStroke()
    {
        if (_stroke is null)
        {
            return;
        }

        if (!_stroke.IsEmpty)
        {
            History.Push(_stroke);
            HasUnsavedChanges = true;
        }

        _stroke = null;
    }

    /// <summary>Applies the active tool to a picking result. Returns true when something changed.</summary>
    public bool ApplyTool(RaycastHit hit)
    {
        switch (ActiveTool)
        {
            case EditorTool.Pick:
                byte picked = World.GetVoxel(hit.Voxel);
                if (picked == Palette.EmptyIndex)
                {
                    return false;
                }

                ActiveColorIndex = picked;
                return true;

            case EditorTool.Place:
                return WriteBrush(hit.Placement, ActiveColorIndex);

            case EditorTool.Erase:
                return WriteBrush(hit.Voxel, Palette.EmptyIndex);

            case EditorTool.Paint:
                return WriteBrush(hit.Voxel, ActiveColorIndex);

            case EditorTool.Fill:
                BeginStroke();
                return FloodFill.Fill(World, hit.Voxel, ActiveColorIndex, _stroke!) > 0;

            default:
                return false;
        }
    }

    /// <summary>
    /// Places on empty space — used when the ray misses everything and falls back to the ground
    /// plane, so the first voxel of a new level has somewhere to land.
    /// </summary>
    public bool PlaceAt(Int3 cell) => WriteBrush(cell, ActiveColorIndex);

    private bool WriteBrush(Int3 center, byte paletteIndex)
    {
        BeginStroke();

        bool changed = false;
        int radius = Math.Max(BrushRadius, 0);

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    changed |= _stroke!.Apply(World, center + new Int3(dx, dy, dz), paletteIndex);
                }
            }
        }

        return changed;
    }

    public bool Undo()
    {
        EndStroke();
        if (!History.Undo(World))
        {
            return false;
        }

        HasUnsavedChanges = true;
        return true;
    }

    public bool Redo()
    {
        EndStroke();
        if (!History.Redo(World))
        {
            return false;
        }

        HasUnsavedChanges = true;
        return true;
    }

    /// <summary>Applies a palette color live, without recording history (used while dragging a picker).</summary>
    public void ApplyPaletteColor(int index, Color32 color)
    {
        World.Palette[index] = color;
        World.MarkAllDirty();
        HasUnsavedChanges = true;
    }

    /// <summary>Records a finished palette edit as one undo step.</summary>
    public void PushPaletteEdit(int index, Color32 before, Color32 after)
    {
        if (before == after)
        {
            return;
        }

        History.Push(new PaletteEditCommand(index, before, after));
        HasUnsavedChanges = true;
    }

    /// <summary>Replaces the whole world — New, or opening a project.</summary>
    public void ReplaceWorld(VoxelWorld world, string? projectPath)
    {
        _stroke = null;
        World = world;
        World.MarkAllDirty();
        History.Clear();
        ProjectPath = projectPath;
        HasUnsavedChanges = false;
    }
}
