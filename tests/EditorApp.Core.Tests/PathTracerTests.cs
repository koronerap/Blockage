using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Raycast;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The path tracer (Fullreleaseplan 5.1), on small images: the same seed the same picture, and light where it should be.</summary>
public class PathTracerTests
{
    private static VoxelWorld Box(int sx, int sy, int sz, byte colour)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < sx; x++)
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        {
            grid.SetVoxel(x, y, z, colour);
        }

        return grid;
    }

    /// <summary>A white 8³ cube on a grey floor, lit by the default sun, seen from the front and above.</summary>
    private static (VoxelScene Scene, RenderCamera Camera) CubeOnFloor()
    {
        var scene = new VoxelScene();
        scene.Add(Box(24, 1, 24, 8), new ObjectTransform(new Vector3(-12f, -1f, -12f), Quaternion.Identity), "Floor");
        scene.Add(Box(8, 8, 8, Palette.WhiteIndex), new ObjectTransform(new Vector3(-4f, 0f, -4f), Quaternion.Identity), "Cube");
        scene.AddDefaultSun();

        var camera = new RenderCamera(new Vector3(14f, 12f, 18f), Vector3.Normalize(new Vector3(-14f, -8f, -18f)), Vector3.UnitY, 45f, false, 0f);
        return (scene, camera);
    }

    private static byte[] Render(VoxelScene scene, RenderCamera camera, RenderSettings settings)
    {
        var tracer = new PathTracer(RenderScene.Capture(scene), camera, settings);
        while (!tracer.IsFinished)
        {
            tracer.AddSample();
        }

        return tracer.ToRgba();
    }

    private static float Brightness(byte[] rgba, int width, int x, int y)
    {
        int i = ((y * width) + x) * 4;
        return (rgba[i] + rgba[i + 1] + rgba[i + 2]) / 3f;
    }

    [Fact]
    public void TheSameSeedGivesTheSamePicture()
    {
        (VoxelScene scene, RenderCamera camera) = CubeOnFloor();
        var settings = new RenderSettings { Width = 48, Height = 32, Samples = 4, Seed = 7 };

        Assert.Equal(Render(scene, camera, settings), Render(scene, camera, settings));
        Assert.NotEqual(Render(scene, camera, settings), Render(scene, camera, settings with { Seed = 8 }));
    }

    [Fact]
    public void AnEmissiveVoxelGlowsInTheDarkAndTheBackgroundCanBeSeeThrough()
    {
        var scene = new VoxelScene { Ambient = 0f };
        scene.Palette.SetMaterial(60, VoxelMaterial.Of(1f, 0f, 1f, 1f));
        scene.Add(Box(2, 2, 2, 60), new ObjectTransform(new Vector3(-1f, -1f, -1f), Quaternion.Identity), "Lamp");
        var camera = new RenderCamera(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, Vector3.UnitY, 30f, false, 0f);

        byte[] image = Render(scene, camera, new RenderSettings { Width = 32, Height = 32, Samples = 2, SkyStrength = 0f, TransparentBackground = true });

        Assert.True(Brightness(image, 32, 16, 16) > 60f);
        Assert.Equal(255, image[(((16 * 32) + 16) * 4) + 3]);
        Assert.Equal(0, image[3]);
    }

    [Fact]
    public void ASunlitFaceIsBrighterThanOneInShadow()
    {
        (VoxelScene scene, RenderCamera camera) = CubeOnFloor();
        var settings = new RenderSettings { Width = 64, Height = 48, Samples = 16, Bounces = 2 };
        var tracer = new PathTracer(RenderScene.Capture(scene), camera, settings);
        while (!tracer.IsFinished)
        {
            tracer.AddSample();
        }

        byte[] image = tracer.ToRgba();

        // The cube's top faces the sun.
        Vector3 top = new(0f, 8f, 0f);
        Assert.True(Brightness(image, 64, Pixel(camera, top).X, Pixel(camera, top).Y) > 20f);
    }

    private static (int X, int Y) Pixel(RenderCamera camera, Vector3 world)
    {
        Vector3 forward = Vector3.Normalize(camera.Forward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        Vector3 up = Vector3.Cross(right, forward);
        Vector3 d = world - camera.Position;
        float z = Vector3.Dot(d, forward);
        float tan = MathF.Tan(camera.VerticalFov * 0.5f * (MathF.PI / 180f));
        float u = Vector3.Dot(d, right) / (z * tan * (64f / 48f));
        float v = Vector3.Dot(d, up) / (z * tan);
        return ((int)((u + 1f) * 0.5f * 64f), (int)((1f - v) * 0.5f * 48f));
    }

    [Fact]
    public void GlassLetsWhatIsBehindItShowThrough()
    {
        var scene = new VoxelScene { Ambient = 0f };
        scene.Palette.SetMaterial(60, VoxelMaterial.Of(1f, 0f, 1f, 1f));
        scene.Palette.SetMaterial(Palette.WhiteIndex, VoxelMaterial.Of(0f, 0f, 1f, 1f - VoxelMaterial.MaxTransparency));
        scene.Add(Box(4, 4, 1, 60), new ObjectTransform(new Vector3(-2f, -2f, -4f), Quaternion.Identity), "Lamp");
        scene.Add(Box(4, 4, 1, Palette.WhiteIndex), new ObjectTransform(new Vector3(-2f, -2f, 0f), Quaternion.Identity), "Glass");
        var camera = new RenderCamera(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, Vector3.UnitY, 20f, false, 0f);

        byte[] image = Render(scene, camera, new RenderSettings { Width = 16, Height = 16, Samples = 8, SkyStrength = 0f });

        // Through the glass, the lamp glows.
        Assert.True(Brightness(image, 16, 8, 8) > 40f);
    }

    [Fact]
    public void BloomSpillsLightAroundABrightSpot()
    {
        var scene = new VoxelScene { Ambient = 0f };
        scene.Palette.SetMaterial(60, VoxelMaterial.Of(1f, 0f, 1f, 1f));
        scene.Add(Box(1, 1, 1, 60), new ObjectTransform(new Vector3(-0.5f, -0.5f, -0.5f), Quaternion.Identity), "Lamp");
        var camera = new RenderCamera(new Vector3(0f, 0f, 20f), -Vector3.UnitZ, Vector3.UnitY, 20f, false, 0f);
        var settings = new RenderSettings { Width = 64, Height = 64, Samples = 2, SkyStrength = 0f, EmissionStrength = 40f };

        byte[] plain = Render(scene, camera, settings);
        byte[] bloomed = Render(scene, camera, settings with { Bloom = 2f });

        // Beside the lamp, where no ray meets it, only the bloom puts light.
        Assert.Equal(0f, Brightness(plain, 64, 32, 40));
        Assert.True(Brightness(bloomed, 64, 32, 40) > 0f);
    }

    [Fact]
    public void ALensKeepsTheFocusSharpAndMovesOnlyWhereTheRayStarts()
    {
        var camera = new RenderCamera(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 40f, false, 0f);
        Ray pinhole = camera.Through(0.3f, -0.2f, 1.5f);

        Assert.Equal(pinhole, camera.Through(0.3f, -0.2f, 1.5f, 0f, 10f, new Vector2(1f, 0f)));

        // Wherever on the lens it leaves from, the ray meets the pinhole's at the focus distance.
        Ray lens = camera.Through(0.3f, -0.2f, 1.5f, 2f, 10f, new Vector2(0.7f, -0.5f));
        Vector3 focal = pinhole.Origin + (pinhole.Direction * (10f / Vector3.Dot(pinhole.Direction, -Vector3.UnitZ)));
        float along = Vector3.Dot(focal - lens.Origin, lens.Direction);
        Assert.True(Vector3.Distance(lens.Origin + (lens.Direction * along), focal) < 1e-3f);
        Assert.NotEqual(pinhole.Origin, lens.Origin);
    }

    [Fact]
    public void DevelopingTheSumGivesTheTracersOwnPicture()
    {
        (VoxelScene scene, RenderCamera camera) = CubeOnFloor();
        var settings = new RenderSettings { Width = 24, Height = 16, Samples = 3, Bloom = 0.5f };

        byte[] picture = Render(scene, camera, settings);

        Assert.Equal(picture, Render(scene, camera, settings));
        Assert.Equal(24 * 16 * 4, picture.Length);
    }

    [Fact]
    public void RenderSettingsAreSavedWithTheLevel()
    {
        var scene = VoxelScene.CreateStarter();
        scene.RenderSettings = new RenderSettings
        {
            Engine = RenderEngine.Gpu,
            Width = 320,
            Samples = 12,
            Aperture = 0.4f,
            FocusDistance = 33f,
            Bloom = 0.75f,
            SunSize = 3f,
            Fog = 0.2f,
        };

        using var stream = new MemoryStream();
        Project.VxLevelFile.Save(scene, stream, "render");
        stream.Position = 0;

        Assert.Equal(scene.RenderSettings, Project.VxLevelFile.LoadScene(stream).RenderSettings);
    }

    /// <summary>Writes a render to look at when BLOCKAGE_RENDER_OUT names a file; nothing otherwise.</summary>
    [Fact]
    public void RenderToLookAt()
    {
        string? path = Environment.GetEnvironmentVariable("BLOCKAGE_RENDER_OUT");
        if (path is null)
        {
            return;
        }

        (VoxelScene scene, RenderCamera camera) = CubeOnFloor();
        scene.Palette.SetMaterial(Palette.WhiteIndex, VoxelMaterial.Of(0f, 0f, 1f, 1f));
        var settings = new RenderSettings { Width = 480, Height = 320, Samples = 32 };
        File.WriteAllBytes(path, PngWriter.EncodeRgba(Render(scene, camera, settings), 480, 320));
    }
}
