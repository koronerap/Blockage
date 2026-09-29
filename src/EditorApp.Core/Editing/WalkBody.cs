using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// How the walker is built and moves (Fullreleaseplan 7.5), in metres — and how many voxels make a
/// metre, which is the game's to say, not the editor's: the same level can be a doll's house or a
/// city. A voxel of size 1 is one world unit.
/// </summary>
public sealed record WalkSettings
{
    public float VoxelsPerMetre { get; init; } = 10f;

    /// <summary>Head to toe.</summary>
    public float Height { get; init; } = 1.8f;

    /// <summary>Half how wide the walker is.</summary>
    public float Radius { get; init; } = 0.3f;

    /// <summary>The highest step it climbs without jumping.</summary>
    public float Step { get; init; } = 0.45f;

    /// <summary>Walking, in metres a second; running is <see cref="Run"/> times it.</summary>
    public float Speed { get; init; } = 4.5f;

    public float Run { get; init; } = 2f;

    /// <summary>How high a jump lifts the feet.</summary>
    public float Jump { get; init; } = 1.1f;

    /// <summary>Metres a second, each second.</summary>
    public float Gravity { get; init; } = 9.81f;

    public WalkSettings Clamped() => this with
    {
        VoxelsPerMetre = Math.Clamp(VoxelsPerMetre, 0.01f, 1000f),
        Height = Math.Clamp(Height, 0.1f, 100f),
        Radius = Math.Clamp(Radius, 0.05f, 50f),
        Step = Math.Clamp(Step, 0f, 50f),
        Speed = Math.Clamp(Speed, 0.1f, 500f),
        Run = Math.Clamp(Run, 1f, 10f),
        Jump = Math.Clamp(Jump, 0f, 100f),
        Gravity = Math.Clamp(Gravity, 0f, 1000f),
    };

    /// <summary>World units in a metre.</summary>
    public float Unit => VoxelsPerMetre;

    /// <summary>Where the eyes are above the feet, in world units.</summary>
    public float EyeHeight => Height * 0.93f * Unit;
}

/// <summary>
/// The walker (Fullreleaseplan 7.5): a box the size of a person, standing on the level's voxels,
/// falling when there is nothing under it, climbing what is no higher than a step and stopped by
/// what is — to try the level at the size it is played at.
/// </summary>
public sealed class WalkBody(VoxelScene scene, WalkSettings settings)
{
    private readonly WalkSettings _settings = settings.Clamped();
    private float _vertical;

    public Vector3 Feet { get; set; }

    public bool OnGround { get; private set; }

    /// <summary>Flying: no gravity, up and down by hand, through nothing still.</summary>
    public bool Flying { get; set; }

    public WalkSettings Settings => _settings;

    /// <summary>Where the eyes are.</summary>
    public Vector3 Eye => Feet + new Vector3(0f, _settings.EyeHeight, 0f);

    /// <summary>The lowest anything can fall to: the bottom of the level, or the ground plane under it.</summary>
    private float Floor
    {
        get
        {
            float lowest = 0f;
            foreach (VoxelObject o in scene.Objects)
            {
                if (o.Visible && o.TryGetWorldBounds(out Vector3 min, out _))
                {
                    lowest = MathF.Min(lowest, min.Y);
                }
            }

            return lowest;
        }
    }

    /// <summary>Stands the walker on whatever is under <paramref name="start"/> — the level, or the ground plane.</summary>
    public void PlaceBelow(Vector3 start)
    {
        var down = new Ray(start, -Vector3.UnitY);
        float floor = Floor;
        Feet = scene.TryPick(down, out ScenePick pick) && pick.Distance < start.Y - floor + 1f
            ? start - new Vector3(0f, pick.Distance, 0f)
            : start with { Y = floor };

        // Up out of anything it would be standing in.
        for (int lift = 0; lift < 64 && Blocked(Feet); lift++)
        {
            Feet += new Vector3(0f, 0.5f, 0f);
        }

        _vertical = 0f;
        OnGround = true;
    }

    /// <summary>
    /// One step of <paramref name="seconds"/>: <paramref name="wish"/> is where it is steered, across
    /// the ground, 1 for walking pace (more when running); <paramref name="rise"/> is up and down
    /// when flying, 1 or −1.
    /// </summary>
    public void Advance(Vector2 wish, bool jump, float rise, float seconds)
    {
        seconds = Math.Clamp(seconds, 0f, 0.1f);
        float unit = _settings.Unit;
        Vector2 move = wish * (_settings.Speed * unit * seconds);

        // Small enough steps that a fast walker does not pass through a thin wall.
        int slices = Math.Max(1, (int)MathF.Ceiling(move.Length() / (0.25f * MathF.Max(unit * _settings.Radius, 0.5f))));
        for (int i = 0; i < slices; i++)
        {
            Slide(new Vector3(move.X / slices, 0f, 0f));
            Slide(new Vector3(0f, 0f, move.Y / slices));
        }

        if (Flying)
        {
            Vertical(rise * _settings.Speed * unit * seconds);
            _vertical = 0f;
            return;
        }

        if (jump && OnGround)
        {
            _vertical = MathF.Sqrt(2f * _settings.Gravity * _settings.Jump) * unit;
        }

        _vertical -= _settings.Gravity * unit * seconds;
        Vertical(_vertical * seconds);

        if (Feet.Y < Floor - (100f * unit))
        {
            Feet = Feet with { Y = Floor };
            _vertical = 0f;
        }
    }

    /// <summary>Across the ground: stopped by a wall, up and over anything no higher than a step.</summary>
    private void Slide(Vector3 by)
    {
        if (by == Vector3.Zero)
        {
            return;
        }

        Vector3 next = Feet + by;
        if (!Blocked(next))
        {
            Feet = next;
            return;
        }

        if (OnGround || Flying)
        {
            float step = _settings.Step * _settings.Unit;
            Vector3 raised = next + new Vector3(0f, step, 0f);
            if (!Blocked(Feet + new Vector3(0f, step, 0f)) && !Blocked(raised))
            {
                // Up onto it, then down to stand on it.
                Feet = raised;
                Settle(step);
            }
        }
    }

    /// <summary>Up or down: landing on what is under it, stopped by what is over it.</summary>
    private void Vertical(float by)
    {
        if (by == 0f)
        {
            return;
        }

        Vector3 next = Feet + new Vector3(0f, by, 0f);
        float floor = Floor;
        if (next.Y < floor)
        {
            Feet = Feet with { Y = floor };
            OnGround = true;
            _vertical = 0f;
            return;
        }

        if (!Blocked(next))
        {
            Feet = next;
            OnGround = by >= 0f ? false : Blocked(Feet - new Vector3(0f, 0.05f, 0f));
            return;
        }

        if (by < 0f)
        {
            // As far down as it goes: the top of what it lands on.
            Settle(-by);
            OnGround = true;
        }

        _vertical = 0f;
    }

    /// <summary>Down by as much as <paramref name="most"/>, to rest on what is under it.</summary>
    private void Settle(float most)
    {
        float low = 0f;
        float high = most;
        for (int i = 0; i < 12; i++)
        {
            float middle = (low + high) * 0.5f;
            if (Blocked(Feet - new Vector3(0f, middle, 0f)))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        Feet -= new Vector3(0f, low, 0f);
    }

    /// <summary>Whether the walker standing with its feet at <paramref name="feet"/> would be inside a voxel.</summary>
    public bool Blocked(Vector3 feet)
    {
        float radius = _settings.Radius * _settings.Unit;
        float height = _settings.Height * _settings.Unit;
        const float Skin = 1e-3f;
        Vector3 min = feet - new Vector3(radius - Skin, -Skin, radius - Skin);
        Vector3 max = feet + new Vector3(radius - Skin, height - Skin, radius - Skin);
        return Overlaps(scene, min, max);
    }

    /// <summary>Whether any shown voxel of the level is in a box of the world.</summary>
    public static bool Overlaps(VoxelScene scene, Vector3 min, Vector3 max)
    {
        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible || o.IsEmpty || !o.TryGetWorldBounds(out Vector3 low, out Vector3 high)
                || high.X <= min.X || low.X >= max.X || high.Y <= min.Y || low.Y >= max.Y || high.Z <= min.Z || low.Z >= max.Z)
            {
                continue;
            }

            // The box in the object's own cells: for a turned object, the cells round its turned corners.
            Vector3 localMin = new(float.MaxValue);
            Vector3 localMax = new(float.MinValue);
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z);
                Vector3 local = o.Transform.InverseTransformPoint(point);
                localMin = Vector3.Min(localMin, local);
                localMax = Vector3.Max(localMax, local);
            }

            int x0 = (int)MathF.Floor(localMin.X), x1 = (int)MathF.Ceiling(localMax.X) - 1;
            int y0 = (int)MathF.Floor(localMin.Y), y1 = (int)MathF.Ceiling(localMax.Y) - 1;
            int z0 = (int)MathF.Floor(localMin.Z), z1 = (int)MathF.Ceiling(localMax.Z) - 1;
            var reader = new VoxelReader(o.Shown);
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (reader.IsSolid(x, y, z))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
