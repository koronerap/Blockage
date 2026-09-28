using System.Numerics;

namespace EditorApp.Core.Scene;

/// <summary>
/// Rotations as three angles in degrees, for typing into a panel: X is the pitch, Y the yaw, Z the
/// roll, in the order <see cref="Quaternion.CreateFromYawPitchRoll"/> applies them — roll first, then
/// pitch, then yaw. A rotation is stored as a quaternion; these are only how it is shown and typed.
/// </summary>
public static class Rotations
{
    private const float Degrees = 180f / MathF.PI;

    public static Quaternion FromEulerDegrees(Vector3 degrees) => Quaternion.Normalize(
        Quaternion.CreateFromYawPitchRoll(degrees.Y / Degrees, degrees.X / Degrees, degrees.Z / Degrees));

    /// <summary>
    /// The angles that make a rotation. Pitch comes out between -90 and 90; straight up or down, where
    /// yaw and roll turn about the same axis, the whole turn is given to the yaw.
    /// </summary>
    public static Vector3 ToEulerDegrees(Quaternion rotation)
    {
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));

        // For roll, then pitch, then yaw, with row vectors: M32 = -sin(pitch), M31 and M33 carry the
        // yaw scaled by cos(pitch), M12 and M22 the roll.
        float pitch = MathF.Asin(Math.Clamp(-m.M32, -1f, 1f));

        float yaw;
        float roll;
        if (MathF.Abs(m.M32) < 0.99999f)
        {
            yaw = MathF.Atan2(m.M31, m.M33);
            roll = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            yaw = MathF.Atan2(-m.M13, m.M11);
            roll = 0f;
        }

        return new Vector3(Tidy(pitch * Degrees), Tidy(yaw * Degrees), Tidy(roll * Degrees));
    }

    /// <summary>Rounds away float dust, so an unturned object reads 0 rather than -1.2e-6, and -0 as 0.</summary>
    private static float Tidy(float degrees)
    {
        float rounded = MathF.Round(degrees, 4);
        return rounded == 0f ? 0f : rounded;
    }
}
