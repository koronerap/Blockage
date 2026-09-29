using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// Draws the scene: one buffer per chunk per object, dirty chunks remeshed on the CPU and
/// re-uploaded, plus a line pass for the grid, the selection and the gizmos.
///
/// Vertices stay in their object's local space and the object's transform arrives as a uniform, so
/// moving or rotating an object costs one matrix rather than a remesh.
/// </summary>
public sealed class GlRenderer : IDisposable
{
    private readonly record struct ChunkKey(int ObjectId, ChunkCoord Coord);

    private readonly GL _gl;
    private readonly Dictionary<int, Dictionary<ChunkCoord, ChunkMeshBuffer>> _buffers = new();
    private readonly MeshBuilder _scratch = new();

    // Chunks waiting to be remeshed. The set and the queue are kept in step so a chunk dirtied
    // repeatedly before it is reached is still only rebuilt once.
    private readonly HashSet<ChunkKey> _pending = new();
    private readonly Queue<ChunkKey> _pendingOrder = new();
    private readonly ShaderProgram _voxelShader;
    private readonly ShaderProgram _lineShader;
    private readonly ShaderProgram _backgroundShader;

    /// <summary>Core profile refuses to draw without a bound VAO, even for a vertex-less shader.</summary>
    private readonly uint _emptyVao;

    /// <summary>Depth-tested overlays: the grid, selections, the hovered face.</summary>
    public LineBatch Lines { get; }

    /// <summary>
    /// Overlays drawn with the depth test off, so they are never swallowed by the model. A move
    /// gizmo sits at the centre of its object's bounding box, which is usually inside solid voxels —
    /// depth tested, it would be invisible exactly when it is needed.
    /// </summary>
    public LineBatch GizmoLines { get; }

    /// <summary>The imported guide model, drawn between the voxels and the overlay lines.</summary>
    public ReferenceModelRenderer Reference { get; }

    /// <summary>Chunks meshed in the most recent sync. Shown in the stats overlay.</summary>
    public int LastRemeshedChunks { get; private set; }

    public int VisibleChunks { get; private set; }

    public int DrawnTriangles { get; private set; }

    public int TotalVertices { get; private set; }

    /// <summary>
    /// How long one frame may spend rebuilding chunk meshes. A large paste or a palette edit can
    /// dirty hundreds of chunks at once; without a cap the frame that follows would simply stop.
    /// At least one chunk is always rebuilt, so progress is guaranteed no matter how small this is.
    /// </summary>
    public double RemeshBudgetMilliseconds { get; set; } = 6.0;

    /// <summary>Chunks still waiting to be rebuilt.</summary>
    public int PendingChunks => _pendingOrder.Count;

    public double LastMeshMilliseconds { get; private set; }

    public double LastUploadMilliseconds { get; private set; }

    /// <summary>The gradient's lower colour, and what the framebuffer is cleared to.</summary>
    public Color32 BackgroundColor { get; set; } = new(38, 42, 48);

    /// <summary>The gradient's upper colour. Kept close to the lower one — this is a backdrop.</summary>
    public Color32 BackgroundTopColor { get; set; } = new(52, 56, 62);

    /// <summary>How the viewport shades faces. A way of looking at the level, not part of it.</summary>
    /// <summary>The Lit / Unlit switch. The lights themselves are the level's.</summary>
    public SceneLighting Lighting { get; } = new();

    private readonly LightUniforms _lights = new();

    /// <summary>Lights switched on beyond what the shader has room for, left out of the last frame.</summary>
    public int DroppedLights { get; private set; }

    public GlRenderer(GL gl)
    {
        _gl = gl;
        _voxelShader = new ShaderProgram(gl, Shaders.VoxelVertex, Shaders.VoxelFragment);
        _lineShader = new ShaderProgram(gl, Shaders.LineVertex, Shaders.LineFragment);
        _backgroundShader = new ShaderProgram(gl, Shaders.BackgroundVertex, Shaders.BackgroundFragment);
        _emptyVao = gl.GenVertexArray();
        Lines = new LineBatch(gl);
        GizmoLines = new LineBatch(gl);
        Reference = new ReferenceModelRenderer(gl);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        // The per-face tables never change, and a uniform keeps its value for the life of the
        // program, so they are uploaded once here instead of every frame.
        _voxelShader.Use();
        _voxelShader.SetVector3Array("uFaceNormal", FaceInfo.AllNormals);
        _voxelShader.SetFloatArray("uFaceShade", FaceInfo.AllShades);
    }

    /// <summary>
    /// Remeshes and re-uploads dirty chunks. Chunks that became empty release their buffers.
    /// </summary>
    public void SyncDirtyChunks(VoxelScene scene)
    {
        DropDeletedObjects(scene);

        foreach (VoxelObject o in scene.Objects)
        {
            // Its modifiers changed: whatever was drawn for it may be of chunks it no longer has.
            if (_modifierGenerations.GetValueOrDefault(o.Id) != o.ModifierGeneration)
            {
                _modifierGenerations[o.Id] = o.ModifierGeneration;
                DropBuffers(o.Id);
            }

            foreach (ChunkCoord coord in o.Shown.ConsumeDirtyChunks())
            {
                var key = new ChunkKey(o.Id, coord);
                if (_pending.Add(key))
                {
                    _pendingOrder.Enqueue(key);
                }
            }
        }

        LastRemeshedChunks = 0;
        LastMeshMilliseconds = 0;
        LastUploadMilliseconds = 0;

        if (_pendingOrder.Count == 0)
        {
            return;
        }

        var frameClock = Stopwatch.StartNew();
        var stepClock = new Stopwatch();

        while (_pendingOrder.Count > 0)
        {
            // Spend the budget, but never skip a frame's worth of work entirely.
            if (LastRemeshedChunks > 0 && frameClock.Elapsed.TotalMilliseconds >= RemeshBudgetMilliseconds)
            {
                break;
            }

            ChunkKey key = _pendingOrder.Dequeue();
            _pending.Remove(key);

            VoxelObject? owner = FindObject(scene, key.ObjectId);
            if (owner is null)
            {
                continue;   // the object went away before its chunk came up
            }

            stepClock.Restart();
            EditMesher.BuildChunk(owner.Shown, key.Coord, _scratch);
            LastMeshMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            stepClock.Restart();
            ApplyChunkMesh(key);
            LastUploadMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            LastRemeshedChunks++;
        }
    }

    /// <summary>The modifier generation each object was last drawn at.</summary>
    private readonly Dictionary<int, int> _modifierGenerations = [];

    /// <summary>Lets go of everything drawn for one object.</summary>
    private void DropBuffers(int id)
    {
        if (!_buffers.Remove(id, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
        {
            return;
        }

        foreach (ChunkMeshBuffer buffer in chunks.Values)
        {
            TotalVertices -= buffer.VertexCount;
            buffer.Dispose();
        }
    }

    private static VoxelObject? FindObject(VoxelScene scene, int id)
    {
        foreach (VoxelObject o in scene.Objects)
        {
            if (o.Id == id)
            {
                return o;
            }
        }

        return null;
    }

    private void DropDeletedObjects(VoxelScene scene)
    {
        List<int>? stale = null;
        foreach (int id in _buffers.Keys)
        {
            if (FindObject(scene, id) is null)
            {
                (stale ??= []).Add(id);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (int id in stale)
        {
            if (!_buffers.Remove(id, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
            {
                continue;
            }

            foreach (ChunkMeshBuffer buffer in chunks.Values)
            {
                TotalVertices -= buffer.VertexCount;
                buffer.Dispose();
            }
        }
    }

    private void ApplyChunkMesh(ChunkKey key)
    {
        if (!_buffers.TryGetValue(key.ObjectId, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
        {
            chunks = [];
            _buffers.Add(key.ObjectId, chunks);
        }

        if (_scratch.IsEmpty)
        {
            if (chunks.Remove(key.Coord, out ChunkMeshBuffer? removed))
            {
                TotalVertices -= removed.VertexCount;
                removed.Dispose();
            }

            return;
        }

        if (!chunks.TryGetValue(key.Coord, out ChunkMeshBuffer? buffer))
        {
            buffer = new ChunkMeshBuffer(_gl);
            chunks.Add(key.Coord, buffer);
        }

        // Tracked incrementally: recounting every buffer each frame is wasted work once a level
        // reaches thousands of chunks.
        TotalVertices -= buffer.VertexCount;
        buffer.Upload(_scratch.Vertices, _scratch.Indices);
        TotalVertices += buffer.VertexCount;
    }

    /// <summary>
    /// Draws the scene into a sub-rectangle of the framebuffer, so the 3D view is centred on the
    /// space the shell actually leaves it rather than on the whole window. Coordinates are in
    /// framebuffer pixels with the origin at the top left, as the UI sees them.
    /// </summary>
    /// <summary>
    /// How strongly each object reads as focused, eased towards its target every frame so focus
    /// moving between objects is a fade rather than a jump.
    /// </summary>
    private readonly Dictionary<int, float> _focusAmount = new();

    /// <summary>Seconds for the focus fade to substantially complete.</summary>
    public float FocusFadeSeconds { get; set; } = 0.12f;

    /// <summary>
    /// Whether the focused object is lifted and the rest held back.
    ///
    /// Turned off where the colours themselves are the thing being judged. The highlight is a lie
    /// about brightness — a useful one when the question is which object you are editing, and a
    /// misleading one when it is what colour that face actually is.
    /// </summary>
    public bool FocusHighlight { get; set; } = true;

    /// <summary>How Solid shading lights a face, where one colour comes from, and the lattice lines — the header's Shading and Overlays.</summary>
    public SolidLighting SolidLighting { get; set; } = SolidLighting.Studio;

    public ColourMode Colour { get; set; } = ColourMode.Palette;

    public Vector3 SingleColour { get; set; } = new(0.78f);

    /// <summary>The voxel lattice over the faces, 0 for none.</summary>
    public float WireOverlay { get; set; }

    /// <summary>X-Ray: how solid a face stays, 0 for off.</summary>
    public float XRay { get; set; }

    public void AdvanceFocusFade(VoxelScene scene, float deltaSeconds)
    {
        // Frame-rate independent easing: the same fade whether the editor runs at 60 or 300 fps.
        float blend = 1f - MathF.Exp(-deltaSeconds / MathF.Max(FocusFadeSeconds, 1e-4f));

        foreach (VoxelObject o in scene.Objects)
        {
            float target = scene.IsSelected(o.Id) ? 1f : 0f;
            float current = _focusAmount.GetValueOrDefault(o.Id, target);
            _focusAmount[o.Id] = current + (target - current) * blend;
        }
    }

    public void Render(VoxelScene scene, FlyCamera camera, Vector2 framebufferSize, Vector2 viewportPosition, Vector2 viewportSize)
    {
        uint width = (uint)MathF.Max(viewportSize.X, 1f);
        uint height = (uint)MathF.Max(viewportSize.Y, 1f);

        // Clear the whole framebuffer first: the chrome paints over the rest, but anything left
        // undefined outside the viewport would flicker with whatever was in the buffer before.
        _gl.Viewport(0, 0, (uint)MathF.Max(framebufferSize.X, 1f), (uint)MathF.Max(framebufferSize.Y, 1f));

        Vector4 background = BackgroundColor.ToVector4();
        _gl.ClearColor(background.X, background.Y, background.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // GL counts rows from the bottom; the UI counts from the top.
        int glY = (int)(framebufferSize.Y - (viewportPosition.Y + viewportSize.Y));
        _gl.Viewport((int)viewportPosition.X, glY, width, height);

        DrawBackgroundGradient();

        Matrix4x4 viewProjection = camera.ViewProjection(viewportSize.X / MathF.Max(viewportSize.Y, 1f));
        Frustum frustum = Frustum.FromViewProjection(viewProjection);

        bool wireframe = Lighting.Mode == ShadingMode.Wireframe;

        _voxelShader.Use();
        _voxelShader.SetMatrix4("uViewProjection", viewProjection);
        _voxelShader.SetInt("uUnlit", Lighting.IsLit ? 0 : SolidLighting == SolidLighting.Flat ? 2 : 1);
        UploadLights(scene);
        _voxelShader.SetFloat("uFocusStrength", FocusHighlight ? 1f : 0f);
        _voxelShader.SetInt("uColorMode", (int)Colour);
        _voxelShader.SetVector3("uSingleColor", SingleColour);
        _voxelShader.SetFloat("uWire", wireframe ? 0f : WireOverlay);
        _voxelShader.SetInt("uWireOnly", wireframe ? 1 : 0);
        _voxelShader.SetFloat("uXRay", XRay);

        VisibleChunks = 0;
        DrawnTriangles = 0;

        // A wireframe with nothing seen through it still hides what is behind: the faces go into the
        // depth buffer first, unseen, and only the lines on the nearest of them pass.
        if (wireframe && XRay <= 0f)
        {
            _gl.ColorMask(false, false, false, false);
            DrawObjects(scene, frustum, count: false);
            _gl.ColorMask(true, true, true, true);
            _gl.DepthFunc(DepthFunction.Lequal);
        }

        // See-through: blended, writing no depth so what is behind still draws. Only the faces that
        // face the camera, as ever — the backs of the far faces are the model's inside, and drawn
        // through the front they read as a second, inside-out model. A see-through wireframe is the
        // exception: there every line is wanted, the far ones included.
        bool blended = wireframe || XRay > 0f;
        if (blended)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.One);
            _gl.DepthMask(false);
        }

        if (wireframe && XRay > 0f)
        {
            _gl.Disable(EnableCap.CullFace);
        }

        DrawObjects(scene, frustum, count: true);

        if (blended)
        {
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.DepthFunc(DepthFunction.Less);

        Reference.Draw(viewProjection);
        DrawOverlays(viewProjection);

        _gl.BindVertexArray(0);
    }

    /// <summary>The level's own lights and ambient floor. Whether they are used at all is the Lit switch.</summary>
    private void UploadLights(VoxelScene scene)
    {
        _lights.Pack(scene.Lights);
        DroppedLights = _lights.Dropped;

        _voxelShader.SetFloat("uAmbient", scene.Ambient);
        _voxelShader.SetInt("uLightCount", _lights.Count);
        _voxelShader.SetVector4Array("uLightPosition", _lights.Positions);
        _voxelShader.SetVector3Array("uLightDirection", _lights.Directions);
        _voxelShader.SetVector3Array("uLightColor", _lights.Colours);
        _voxelShader.SetVector4Array("uLightShape", _lights.Shapes);
    }

    /// <summary>
    /// Fills the viewport with a vertical gradient before anything else. Drawn rather than cleared
    /// because a clear can only be one flat colour, and it writes no depth, so the scene lands on
    /// top of it normally.
    /// </summary>
    private void DrawBackgroundGradient()
    {
        _backgroundShader.Use();
        _backgroundShader.SetVector3("uTop", ToRgb(BackgroundTopColor));
        _backgroundShader.SetVector3("uBottom", ToRgb(BackgroundColor));

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);

        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
    }

    private static Vector3 ToRgb(Color32 color)
    {
        Vector4 value = color.ToVector4();
        return new Vector3(value.X, value.Y, value.Z);
    }

    private void DrawOverlays(Matrix4x4 viewProjection)
    {
        if (Lines.IsEmpty && GizmoLines.IsEmpty)
        {
            return;
        }

        _lineShader.Use();
        _lineShader.SetMatrix4("uViewProjection", viewProjection);

        // Thick overlays are quads, and a quad seen from behind is still the stroke the user asked
        // for, so culling comes off for the whole overlay pass.
        _gl.Disable(EnableCap.CullFace);

        Lines.Draw();

        if (!GizmoLines.IsEmpty)
        {
            // Clear depth rather than switching the test off. Turning it off puts the gizmo in
            // front of the model, but it also stops the gizmo occluding *itself* — a cone drawn
            // that way shows its own back faces and inner cap through the front. Clearing gives
            // the pass a fresh depth range: still in front of everything, still solid in itself.
            _gl.Clear(ClearBufferMask.DepthBufferBit);
            GizmoLines.Draw();
        }

        _gl.Enable(EnableCap.CullFace);
    }

    private void DrawObjects(VoxelScene scene, Frustum frustum, bool count)
    {
        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible || !_buffers.TryGetValue(o.Id, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
            {
                continue;
            }

            _voxelShader.SetMatrix4("uModel", o.Transform.ToMatrix());
            _voxelShader.SetFloat("uFocus", _focusAmount.GetValueOrDefault(o.Id, scene.IsSelected(o.Id) ? 1f : 0f));
            _voxelShader.SetVector3("uObjectColor", ViewportSettings.ObjectColour(o.Id));
            DrawObjectChunks(o, chunks, frustum, count);
        }
    }

    private void DrawObjectChunks(
        VoxelObject o,
        Dictionary<ChunkCoord, ChunkMeshBuffer> chunks,
        Frustum frustum,
        bool count)
    {
        foreach ((ChunkCoord coord, ChunkMeshBuffer buffer) in chunks)
        {
            if (buffer.IsEmpty)
            {
                continue;
            }

            // The chunk box is axis aligned in the object's space, so once the object is turned its
            // world bounds have to come from all eight corners.
            Vector3 localMin = coord.Origin.ToVector3();
            Vector3 localMax = localMin + new Vector3(Chunk.Size);
            (Vector3 min, Vector3 max) = TransformBounds(o.Transform, localMin, localMax);

            if (!frustum.Intersects(min, max))
            {
                continue;
            }

            buffer.Draw();

            if (count)
            {
                VisibleChunks++;
                DrawnTriangles += buffer.IndexCount / 3;
            }
        }
    }

    private static (Vector3 Min, Vector3 Max) TransformBounds(
        Core.Scene.ObjectTransform transform,
        Vector3 localMin,
        Vector3 localMax)
    {
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;

        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? localMin.X : localMax.X,
                (corner & 2) == 0 ? localMin.Y : localMax.Y,
                (corner & 4) == 0 ? localMin.Z : localMax.Z);

            Vector3 world = transform.TransformPoint(local);
            min = corner == 0 ? world : Vector3.Min(min, world);
            max = corner == 0 ? world : Vector3.Max(max, world);
        }

        return (min, max);
    }

    /// <summary>Drops every GPU buffer. Used when the level is replaced by New or Open.</summary>
    public void ResetBuffers()
    {
        foreach (Dictionary<ChunkCoord, ChunkMeshBuffer> chunks in _buffers.Values)
        {
            foreach (ChunkMeshBuffer buffer in chunks.Values)
            {
                buffer.Dispose();
            }
        }

        _buffers.Clear();
        _pending.Clear();
        _pendingOrder.Clear();
        TotalVertices = 0;
    }

    public void Dispose()
    {
        ResetBuffers();
        Reference.Dispose();
        Lines.Dispose();
        GizmoLines.Dispose();
        _backgroundShader.Dispose();
        _gl.DeleteVertexArray(_emptyVao);
        _voxelShader.Dispose();
        _lineShader.Dispose();
    }
}
