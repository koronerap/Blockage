using System.Numerics;
using EditorApp.Core.Scene;

namespace EditorApp.Rendering;

/// <summary>
/// Lights as the voxel shader takes them: fixed-size arrays with one slot per light, and a count.
///
/// Every kind goes through the same few numbers so the shader has one formula, not three. A
/// directional light is a position at infinity — w = 0, and xyz the way towards it — that nothing
/// fades; a point light is a spot whose cone is wider than the whole sphere. Shared with the phone,
/// which fills it from its one viewing light.
/// </summary>
public sealed class LightUniforms
{
    /// <summary>Slots in the shader. The GLSL arrays are sized from this, so the two cannot drift.</summary>
    public const int MaxLights = 16;

    /// <summary>A cone test every direction passes: smoothstep(-2, -1, x) is 1 for any cosine.</summary>
    private static readonly Vector2 NoCone = new(-2f, -1f);

    public int Count { get; private set; }

    /// <summary>Lights left out because there were more switched on than the shader has slots for.</summary>
    public int Dropped { get; private set; }

    /// <summary>xyz: where a point or spot light is, or the way towards a directional one. w: 0 for directional.</summary>
    public Vector4[] Positions { get; } = new Vector4[MaxLights];

    /// <summary>Which way a spot shines. Unused by the others, but kept filled.</summary>
    public Vector3[] Directions { get; } = new Vector3[MaxLights];

    /// <summary>Colour times intensity.</summary>
    public Vector3[] Colours { get; } = new Vector3[MaxLights];

    /// <summary>x: range. y, z: the cosines where a spot's cone ends and where it reaches full strength.</summary>
    public Vector4[] Shapes { get; } = new Vector4[MaxLights];

    /// <summary>The lights that are on and give any light, in order, up to the shader's slots.</summary>
    public void Pack(IEnumerable<SceneLight> lights)
    {
        Count = 0;
        Dropped = 0;

        foreach (SceneLight light in lights)
        {
            if (!light.Visible || light.Intensity <= 0f || light.Colour == Vector3.Zero)
            {
                continue;
            }

            if (Count == MaxLights)
            {
                Dropped++;
                continue;
            }

            Put(light);
        }
    }

    /// <summary>One directional light from a direction towards it — the phone's viewing light.</summary>
    public void PackSingle(Vector3 towardsLight, float intensity)
    {
        Count = 1;
        Dropped = 0;
        Positions[0] = new Vector4(Vector3.Normalize(towardsLight), 0f);
        Directions[0] = -Vector3.Normalize(towardsLight);
        Colours[0] = new Vector3(intensity);
        Shapes[0] = new Vector4(0f, NoCone.X, NoCone.Y, 0f);
    }

    private void Put(SceneLight light)
    {
        int slot = Count++;
        Colours[slot] = light.Colour * light.Intensity;
        Directions[slot] = light.Direction;

        switch (light.Kind)
        {
            case LightKind.Directional:
                Positions[slot] = new Vector4(-light.Direction, 0f);
                Shapes[slot] = new Vector4(0f, NoCone.X, NoCone.Y, 0f);
                break;

            case LightKind.Point:
                Positions[slot] = new Vector4(light.Position, 1f);
                Shapes[slot] = new Vector4(light.Range, NoCone.X, NoCone.Y, 0f);
                break;

            default:
                float half = light.SpotAngle * 0.5f * (MathF.PI / 180f);
                float edge = MathF.Cos(half);

                // Blend is how much of the cone is spent fading: none gives a hard edge, all of it
                // fades from the centre out. The two cosines must differ, or smoothstep is undefined.
                float full = MathF.Max(MathF.Cos(half * (1f - light.SpotBlend)), edge + 1e-4f);

                Positions[slot] = new Vector4(light.Position, 1f);
                Shapes[slot] = new Vector4(light.Range, edge, full, 0f);
                break;
        }
    }
}
