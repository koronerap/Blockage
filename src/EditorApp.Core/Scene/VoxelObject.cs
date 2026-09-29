using System.Numerics;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Scene;

/// <summary>
/// One independently placeable piece of the level: a voxel grid plus where it sits in the world.
/// Loop Cut and Extrude's Create sub-mode both produce these (EditorApp.md, "Ortak davranışlar").
///
/// The grid is always axis-aligned in its own space; only the transform knows about rotation. That
/// is what keeps meshing, picking and greedy export working on a plain integer lattice however the
/// object is turned.
/// </summary>
public sealed class VoxelObject(int id, VoxelWorld grid, ObjectTransform transform, string name) : IPlaceable
{
    public int Id { get; } = id;

    public string Name { get; set; } = name;

    public VoxelWorld Grid { get; } = grid;

    private ObjectTransform _transform = transform;

    /// <summary>
    /// Where it is in the world. Set by anything that moves it; a parent's move sets it through
    /// <see cref="Follow"/> instead, which is how children go with their parent without every place
    /// that moves an object having to know about children.
    /// </summary>
    public ObjectTransform Transform
    {
        get => _transform;
        set
        {
            _transform = value;
            Moved?.Invoke(this, false);
        }
    }

    /// <summary>The object this is a child of; 0 for none. An id with no object behind it — a deleted parent — is no parent.</summary>
    public int ParentId { get; internal set; }

    /// <summary>Where this sits in its parent's frame, kept while it has one.</summary>
    public ObjectTransform ParentOffset { get; internal set; } = ObjectTransform.Identity;

    /// <summary>Told when this moves, so its children follow and its own offset stays true. Set by the scene.</summary>
    internal Action<IPlaceable, bool>? Moved { get; set; }

    /// <summary>Moved by its parent: the world placement changes, the offset it is held at does not.</summary>
    internal void Follow(ObjectTransform world)
    {
        _transform = world;
        Moved?.Invoke(this, true);
    }

    public bool Visible { get; set; } = true;

    /// <summary>
    /// Locked objects are drawn and exported like any other, but the viewport passes over them: they
    /// cannot be picked, take focus or be edited — a floor that stays put while what stands on it is
    /// built. Saved with the level; outside undo, like hiding.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>World units one of this object's voxels measures — its transform's scale.</summary>
    public float VoxelSize => Transform.VoxelSize;

    public bool IsEmpty => Grid.SolidCount == 0;

    // ---- Modifiers -----------------------------------------------------------------------------

    private IReadOnlyList<VoxelModifier> _modifiers = [];
    private ModifierEvaluator? _evaluator;

    /// <summary>The modifiers drawn over the voxels, in the order they apply.</summary>
    public IReadOnlyList<VoxelModifier> Modifiers => _modifiers;

    /// <summary>Goes up whenever the modifiers change, so what draws the object knows to start its drawing over.</summary>
    public int ModifierGeneration { get; private set; }

    public bool HasModifiers => _modifiers.Any(m => m.Enabled);

    public void SetModifiers(IEnumerable<VoxelModifier> modifiers)
    {
        _modifiers = [.. modifiers.Select(m => m.Clamped())];
        _evaluator = null;
        ModifierGeneration++;

        // Whichever grid is shown now is drawn afresh, all of it.
        Grid.MarkAllDirty();
    }

    /// <summary>
    /// What the object shows, and what is exported: its voxels, with whatever its modifiers make of
    /// them — the voxels themselves when it has none. Brought up to date with every change to the
    /// voxels as it is asked for.
    /// </summary>
    public VoxelWorld Shown
    {
        get
        {
            if (!HasModifiers)
            {
                return Grid;
            }

            if (_evaluator is null)
            {
                Grid.ConsumeDirtyChunks();
                _evaluator = new ModifierEvaluator(Grid, _modifiers);
            }
            else if (Grid.DirtyChunks.Count > 0)
            {
                _evaluator.Update(Grid, Grid.ConsumeDirtyChunks());
            }

            return _evaluator.Shown;
        }
    }

    /// <summary>Local-space bounds of what it shows, in voxels (a cell spans one unit of its own space).</summary>
    public bool TryGetLocalBounds(out Vector3 min, out Vector3 max)
    {
        if (!Shown.TryGetBounds(out Int3 minCell, out Int3 maxCell))
        {
            min = max = Vector3.Zero;
            return false;
        }

        min = minCell.ToVector3();
        max = maxCell.ToVector3() + Vector3.One;
        return true;
    }

    /// <summary>
    /// The centre of the object's own bounding box, in world space — the pivot the rotate rings
    /// turn around, and where the move gizmo sits.
    /// </summary>
    public Vector3 WorldCentre() =>
        TryGetLocalBounds(out Vector3 min, out Vector3 max)
            ? Transform.TransformPoint((min + max) * 0.5f)
            : Transform.Position;

    /// <summary>
    /// World-space axis-aligned bounds. A rotated object needs all eight corners tested, since its
    /// own box is no longer axis aligned once it is turned.
    /// </summary>
    public bool TryGetWorldBounds(out Vector3 min, out Vector3 max)
    {
        min = max = Vector3.Zero;
        if (!TryGetLocalBounds(out Vector3 localMin, out Vector3 localMax))
        {
            return false;
        }

        bool first = true;
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? localMin.X : localMax.X,
                (corner & 2) == 0 ? localMin.Y : localMax.Y,
                (corner & 4) == 0 ? localMin.Z : localMax.Z);

            Vector3 world = Transform.TransformPoint(local);
            min = first ? world : Vector3.Min(min, world);
            max = first ? world : Vector3.Max(max, world);
            first = false;
        }

        return true;
    }
}
