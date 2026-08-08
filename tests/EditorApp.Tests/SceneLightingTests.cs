using System.Numerics;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// Where the viewport's one directional light points. Verified against the rendered image once by
/// sampling pixels; this keeps the arithmetic honest without a GL context.
/// </summary>
public class SceneLightingTests
{
    [Fact]
    public void StraightOverheadPointsUp()
    {
        var lighting = new SceneLighting { Elevation = 90f };

        Assert.Equal(0f, lighting.Direction.X, 4);
        Assert.Equal(1f, lighting.Direction.Y, 4);
        Assert.Equal(0f, lighting.Direction.Z, 4);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1f)]      // north: +Z
    [InlineData(90f, 1f, 0f, 0f)]     // east:  +X
    [InlineData(180f, 0f, 0f, -1f)]
    [InlineData(270f, -1f, 0f, 0f)]
    public void OnTheHorizonAzimuthIsACompassBearing(float azimuth, float x, float y, float z)
    {
        var lighting = new SceneLighting { Azimuth = azimuth, Elevation = 0f };

        Assert.Equal(x, lighting.Direction.X, 4);
        Assert.Equal(y, lighting.Direction.Y, 4);
        Assert.Equal(z, lighting.Direction.Z, 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(37f)]
    [InlineData(-20f)]
    [InlineData(90f)]
    public void TheDirectionIsAlwaysUnitLength(float elevation)
    {
        // It goes straight into a dot product as the L of a lambert term; a direction that was not
        // unit length would quietly scale every face's brightness.
        var lighting = new SceneLighting { Azimuth = 200f, Elevation = elevation };

        Assert.Equal(1f, lighting.Direction.Length(), 4);
    }

    [Fact]
    public void RaisingTheLightShortensItsHorizontalPart()
    {
        // What the compass in the panel draws with, so it has to shrink towards the middle.
        var low = new SceneLighting { Azimuth = 45f, Elevation = 10f };
        var high = new SceneLighting { Azimuth = 45f, Elevation = 80f };

        float Horizontal(SceneLighting l) => new Vector2(l.Direction.X, l.Direction.Z).Length();

        Assert.True(Horizontal(high) < Horizontal(low));
    }

    [Fact]
    public void TheDefaultLightsAllThreeFacesFacingTheOpeningCamera()
    {
        // The reason the default angle is what it is: a face left on pure ambient reads as a hole.
        var lighting = new SceneLighting();

        Vector3[] visible = [new(0f, 1f, 0f), new(-1f, 0f, 0f), new(0f, 0f, -1f)];

        foreach (Vector3 normal in visible)
        {
            Assert.True(
                Vector3.Dot(normal, lighting.Direction) > 0.1f,
                $"Face {normal} gets no light from the default angle.");
        }
    }

    [Fact]
    public void ResettingRestoresEveryAngle()
    {
        var lighting = new SceneLighting { Azimuth = 12f, Elevation = 3f, Intensity = 0.1f, Ambient = 0.9f };

        lighting.ResetAngles();

        Assert.Equal(SceneLighting.DefaultAzimuth, lighting.Azimuth);
        Assert.Equal(SceneLighting.DefaultElevation, lighting.Elevation);
        Assert.Equal(SceneLighting.DefaultIntensity, lighting.Intensity);
        Assert.Equal(SceneLighting.DefaultAmbient, lighting.Ambient);
    }
}
