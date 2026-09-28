using System.Numerics;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Tests;

/// <summary>Rotations as three typed angles: what the Object tab shows has to be what it sets.</summary>
public class RotationsTests
{
    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(30f, 0f, 0f)]
    [InlineData(0f, 90f, 0f)]
    [InlineData(0f, 0f, -45f)]
    [InlineData(20f, 135f, -60f)]
    [InlineData(-75f, -170f, 110f)]
    public void AnglesComeBackAsTyped(float x, float y, float z)
    {
        var typed = new Vector3(x, y, z);

        Vector3 shown = Rotations.ToEulerDegrees(Rotations.FromEulerDegrees(typed));

        Assert.Equal(x, shown.X, 2);
        Assert.Equal(y, shown.Y, 2);
        Assert.Equal(z, shown.Z, 2);
    }

    /// <summary>Whatever angles come out, they make the same rotation back.</summary>
    [Fact]
    public void AnyRotationSurvivesTheTripThroughAngles()
    {
        var random = new Random(1234);

        for (int i = 0; i < 200; i++)
        {
            Quaternion rotation = Quaternion.Normalize(new Quaternion(
                (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f));

            Quaternion back = Rotations.FromEulerDegrees(Rotations.ToEulerDegrees(rotation));

            // q and -q are the same rotation.
            Assert.True(MathF.Abs(Quaternion.Dot(rotation, back)) > 0.9999f, $"{rotation} came back as {back}.");
        }
    }

    [Fact]
    public void AQuarterTurnAboutYReadsAsNinetyDegreesOfY()
    {
        Vector3 shown = Rotations.ToEulerDegrees(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));

        Assert.Equal(new Vector3(0f, 90f, 0f), shown);
    }

    /// <summary>Straight up or down, yaw and roll turn about the same axis; the turn still survives.</summary>
    [Fact]
    public void LookingStraightUpIsStillTheSameRotation()
    {
        Quaternion rotation = Rotations.FromEulerDegrees(new Vector3(90f, 30f, 20f));

        Quaternion back = Rotations.FromEulerDegrees(Rotations.ToEulerDegrees(rotation));

        Assert.True(MathF.Abs(Quaternion.Dot(rotation, back)) > 0.9999f);
    }
}
