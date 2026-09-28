using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Rendering;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// What the shader is handed for each kind of light. The shader itself cannot run here, so the
/// numbers it reads are what get pinned down: every kind through one formula only works if each kind
/// fills the slots the way that formula expects.
/// </summary>
public class LightUniformTests
{
    [Fact]
    public void ADirectionalLightIsAPositionAtInfinityTowardsTheLight()
    {
        var sun = new SceneLight(1, LightKind.Directional, "sun")
        {
            Transform = new ObjectTransform(new Vector3(40f, 40f, 40f), SceneLight.Aiming(new Vector3(0f, -1f, 0f))),
            Intensity = 0.5f,
        };

        var uniforms = new LightUniforms();
        uniforms.Pack([sun]);

        Assert.Equal(1, uniforms.Count);
        Assert.Equal(0f, uniforms.Positions[0].W);
        Assert.True(Vector3.Distance(Vector3.UnitY, new Vector3(uniforms.Positions[0].X, uniforms.Positions[0].Y, uniforms.Positions[0].Z)) < 1e-4f);
        Assert.Equal(new Vector3(0.5f), uniforms.Colours[0]);
    }

    /// <summary>A point light is a spot whose cone every direction passes.</summary>
    [Fact]
    public void APointLightHasARangeAndNoCone()
    {
        var bulb = new SceneLight(1, LightKind.Point, "bulb")
        {
            Transform = ObjectTransform.At(new Vector3(1f, 2f, 3f)),
            Range = 7f,
        };

        var uniforms = new LightUniforms();
        uniforms.Pack([bulb]);

        Assert.Equal(new Vector4(1f, 2f, 3f, 1f), uniforms.Positions[0]);
        Assert.Equal(7f, uniforms.Shapes[0].X);

        // smoothstep(y, z, cosine) is 1 for every cosine from -1 up only if z is at most -1.
        Assert.True(uniforms.Shapes[0].Z <= -1f);
    }

    [Fact]
    public void ASpotsConeIsTheCosinesOfItsEdgeAndItsFullStrength()
    {
        var spot = new SceneLight(1, LightKind.Spot, "spot") { SpotAngle = 60f, SpotBlend = 0.5f };

        var uniforms = new LightUniforms();
        uniforms.Pack([spot]);

        Assert.Equal(MathF.Cos(30f * MathF.PI / 180f), uniforms.Shapes[0].Y, 5);
        Assert.Equal(MathF.Cos(15f * MathF.PI / 180f), uniforms.Shapes[0].Z, 5);
    }

    /// <summary>A hard edge would make both cosines equal, where smoothstep is undefined.</summary>
    [Fact]
    public void AHardEdgedSpotStillHasTwoDifferentCosines()
    {
        var spot = new SceneLight(1, LightKind.Spot, "spot") { SpotBlend = 0f };

        var uniforms = new LightUniforms();
        uniforms.Pack([spot]);

        Assert.True(uniforms.Shapes[0].Z > uniforms.Shapes[0].Y);
    }

    [Fact]
    public void LightsThatGiveNoLightAreLeftOut()
    {
        var off = new SceneLight(1, LightKind.Point, "off") { Visible = false };
        var dark = new SceneLight(2, LightKind.Point, "dark") { Intensity = 0f };
        var black = new SceneLight(3, LightKind.Point, "black") { Colour = Vector3.Zero };
        var on = new SceneLight(4, LightKind.Point, "on") { Transform = ObjectTransform.At(Vector3.One) };

        var uniforms = new LightUniforms();
        uniforms.Pack([off, dark, black, on]);

        Assert.Equal(1, uniforms.Count);
        Assert.Equal(1f, uniforms.Positions[0].X);
    }

    [Fact]
    public void BeyondTheShadersSlotsLightsAreCountedAsDropped()
    {
        SceneLight[] many = [.. Enumerable.Range(1, LightUniforms.MaxLights + 3).Select(i => new SceneLight(i, LightKind.Point, $"l{i}"))];

        var uniforms = new LightUniforms();
        uniforms.Pack(many);

        Assert.Equal(LightUniforms.MaxLights, uniforms.Count);
        Assert.Equal(3, uniforms.Dropped);
    }

    /// <summary>The GLSL arrays are sized from the same constant, so the two cannot drift apart.</summary>
    [Fact]
    public void TheShadersArraysHaveAsManySlotsAsThePacker() =>
        Assert.Contains($"uLightPosition[{LightUniforms.MaxLights}]", Shaders.VoxelFragment, StringComparison.Ordinal);
}

/// <summary>A picked light takes the Transform tool's gizmo, and the outliner is how it is picked.</summary>
[Collection(nameof(ImGuiCollection))]
public sealed class LightEditingTests : IDisposable
{
    private static readonly Vector2 Viewport = new(1280f, 720f);

    private readonly ImGuiHarness _ui = new();

    public void Dispose() => _ui.Dispose();

    private static EditorSession Session()
    {
        var session = new EditorSession();
        session.ReplaceWorld(EditorSession.CreateStarterWorld(), projectPath: null);
        session.ActiveTool = EditorTool.Transform;
        session.TransformMode = TransformMode.Move;
        return session;
    }

    private static Vector2 ScreenOf(FlyCamera camera, Vector3 world)
    {
        Assert.True(camera.TryProjectToScreen(world, Viewport, out Vector2 screen));
        return screen;
    }

    [Fact]
    public void TheMoveArrowsMoveThePickedLightNotTheObject()
    {
        EditorSession session = Session();
        SceneLight point = session.AddLight(LightKind.Point, new Vector3(4f, 12f, 4f), -Vector3.UnitY);
        ObjectTransform objectBefore = session.Scene.Objects[0].Transform;

        var camera = new FlyCamera { Position = new Vector3(20f, 22f, 26f) };
        camera.LookAt(point.Position);
        var transform = new TransformInteraction(session);

        GizmoHandle arrow = transform.Handles(camera).First(h => h.Kind == GizmoKind.MoveAxis && h.Axis == 0);
        Assert.Equal(point.Position, arrow.Origin);

        (Vector3 start, Vector3 end) = transform.Segment(arrow, camera);
        Vector3 grip = (start + end) * 0.5f;

        Assert.True(transform.OnPress(ScreenOf(camera, grip), Viewport, camera));
        transform.OnDrag(ScreenOf(camera, grip + (arrow.Direction * 3f)), Viewport, camera, freeform: false);
        transform.OnRelease();

        Assert.Equal(7f, point.Position.X, 3);
        Assert.Equal(objectBefore, session.Scene.Objects[0].Transform);
        Assert.Equal("Move light", session.History.NextUndoName);
    }

    /// <summary>A light has no box, so no edge hinges are offered on it — only arrows or rings.</summary>
    [Fact]
    public void ALightHasNoHinges()
    {
        EditorSession session = Session();
        session.SelectLight(session.Scene.Lights[0].Id);

        var camera = new FlyCamera();
        var transform = new TransformInteraction(session);

        Assert.DoesNotContain(transform.Handles(camera), h => h.Kind == GizmoKind.EdgeHinge);
    }

    /// <summary>
    /// Every kind shows a different set of controls. Drawn untouched for a few frames, none of them
    /// may change the light or leave anything in history.
    /// </summary>
    [Theory]
    [InlineData(LightKind.Directional)]
    [InlineData(LightKind.Point)]
    [InlineData(LightKind.Spot)]
    public void ThePanelDrawsEveryKindWithoutChangingIt(LightKind kind)
    {
        EditorSession session = Session();
        SceneLight light = session.AddLight(kind, new Vector3(1f, 6f, 2f), Vector3.Normalize(new Vector3(0.3f, -1f, 0.2f)));
        LightState before = light.State;
        int history = session.History.UndoCount;

        for (int i = 0; i < 4; i++)
        {
            _ui.Frame(() => LightPropertiesPanel.DrawContent(session, light));
        }

        Assert.Equal(before, light.State);
        Assert.Equal(history, session.History.UndoCount);
    }

    [Fact]
    public void ClickingALightsRowPicksItAndAnObjectsRowLetsGo()
    {
        EditorSession session = Session();
        SceneLight sun = session.Scene.Lights[0];
        var camera = new FlyCamera();

        void Draw() => ObjectListPanel.Draw(session, camera);

        _ui.Frame(Draw);
        _ui.Frame(Draw);

        Vector2 RowOf(int id)
        {
            for (float y = 20f; y < 220f; y += 2f)
            {
                _ui.Frame(Draw, new Vector2(300f, y));
                _ui.Frame(Draw, new Vector2(300f, y));
                if (ObjectListPanel.HoveredId == id)
                {
                    return new Vector2(300f, y + 4f);
                }
            }

            throw new InvalidOperationException("row not found");
        }

        _ui.Click(RowOf(sun.Id), Draw);
        Assert.Equal(sun.Id, session.SelectedLightId);

        _ui.Click(RowOf(session.Scene.Objects[0].Id), Draw);
        Assert.Equal(0, session.SelectedLightId);
    }
}
