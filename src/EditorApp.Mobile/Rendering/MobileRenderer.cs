using System.Diagnostics;
using System.Numerics;
using Android.Opengl;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// Draws the scene on ES: one buffer per chunk per object, dirty chunks remeshed on the CPU and
/// re-uploaded, plus a line pass for the grid and the overlays.
///
/// The same shape as the desktop's GlRenderer, and for the same reasons — vertices stay in their
/// object's local space with the transform arriving as a uniform, so moving an object costs a matrix
/// rather than a remesh. What it does not carry across is the reference-model pass and the stats
/// counters; neither has anywhere to go on a phone yet.
/// </summary>
public sealed class MobileRenderer : IDisposable
{
    private readonly record struct ChunkKey(int ObjectId, ChunkCoord Coord);

    private readonly Dictionary<int, Dictionary<ChunkCoord, EsChunkMeshBuffer>> _buffers = new();
    private readonly MeshBuilder _scratch = new();

    // Chunks waiting to be remeshed. The set and the queue are kept in step so a chunk dirtied
    // repeatedly before it is reached is still only rebuilt once.
    private readonly HashSet<ChunkKey> _pending = new();
    private readonly Queue<ChunkKey> _pendingOrder = new();

    private readonly GlProgram _voxelShader;
    private readonly GlProgram _lineShader;
    private readonly GlProgram _backgroundShader;

    /// <summary>The gradient's lower colour, and what the framebuffer is cleared to.</summary>
    public Color32 BackgroundColor { get; set; } = new(38, 42, 48);

    /// <summary>The gradient's upper colour. Kept close to the lower one — this is a backdrop.</summary>
    public Color32 BackgroundTopColor { get; set; } = new(52, 56, 62);

    /// <summary>Depth-tested overlays: the grid, selections, the hovered face.</summary>
    public EsLineBatch Lines { get; }

    /// <summary>Overlays drawn on a cleared depth buffer, so a gizmo is never swallowed by a model.</summary>
    public EsLineBatch GizmoLines { get; }

    /// <summary>
    /// How the viewport shades faces. A way of looking at the level, not part of it.
    ///
    /// Settable so the surface can own it: the renderer is built and destroyed with the GL context,
    /// and a light angle the user chose must not go with it.
    /// </summary>
    public SceneLighting Lighting { get; set; } = new();

    private readonly LightUniforms _lights = new();

    public int VisibleChunks { get; private set; }

    public int DrawnTriangles { get; private set; }

    /// <summary>
    /// How long one frame may spend rebuilding chunk meshes. A phone has less to spare than a
    /// desktop, but the floor matters more than the ceiling: at least one chunk is always rebuilt,
    /// so a big edit still finishes, just spread over more frames.
    /// </summary>
    public double RemeshBudgetMilliseconds { get; set; } = 4.0;

    public MobileRenderer()
    {
        _voxelShader = new GlProgram(EsShaders.VoxelVertex, EsShaders.VoxelFragment);
        _lineShader = new GlProgram(EsShaders.LineVertex, EsShaders.LineFragment);
        _backgroundShader = new GlProgram(EsShaders.BackgroundVertex, EsShaders.BackgroundFragment);

        Lines = new EsLineBatch();
        GizmoLines = new EsLineBatch();

        GLES30.GlEnable(GLES30.GlDepthTest);
        GLES30.GlEnable(GLES30.GlCullFaceConst);
        GLES30.GlCullFace(GLES30.GlBack);
        GLES30.GlFrontFace(GLES30.GlCcw);

        // The per-face tables never change, and a uniform keeps its value for the life of the
        // program, so they are uploaded once here instead of every frame.
        _voxelShader.Use();
        _voxelShader.SetVector3Array("uFaceNormal", Flatten(FaceInfo.AllNormals));
        _voxelShader.SetFloatArray("uFaceShade", FaceInfo.AllShades.ToArray());
    }

    private static float[] Flatten(ReadOnlySpan<Vector3> vectors)
    {
        float[] flat = new float[vectors.Length * 3];
        for (int i = 0; i < vectors.Length; i++)
        {
            flat[i * 3] = vectors[i].X;
            flat[(i * 3) + 1] = vectors[i].Y;
            flat[(i * 3) + 2] = vectors[i].Z;
        }

        return flat;
    }

    private static float[] Flatten(ReadOnlySpan<Vector4> vectors)
    {
        float[] flat = new float[vectors.Length * 4];
        for (int i = 0; i < vectors.Length; i++)
        {
            flat[i * 4] = vectors[i].X;
            flat[(i * 4) + 1] = vectors[i].Y;
            flat[(i * 4) + 2] = vectors[i].Z;
            flat[(i * 4) + 3] = vectors[i].W;
        }

        return flat;
    }

    /// <summary>Chunks still waiting to be rebuilt.</summary>
    public int PendingChunks => _pendingOrder.Count;

    /// <summary>Remeshes and re-uploads dirty chunks. Chunks that became empty release their buffers.</summary>
    public void SyncDirtyChunks(VoxelScene scene)
    {
        DropDeletedObjects(scene);

        foreach (VoxelObject o in scene.Objects)
        {
            foreach (ChunkCoord coord in o.Grid.ConsumeDirtyChunks())
            {
                var key = new ChunkKey(o.Id, coord);
                if (_pending.Add(key))
                {
                    _pendingOrder.Enqueue(key);
                }
            }
        }

        if (_pendingOrder.Count == 0)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        int rebuilt = 0;

        while (_pendingOrder.Count > 0)
        {
            // Spend the budget, but never skip a frame's worth of work entirely.
            if (rebuilt > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= RemeshBudgetMilliseconds)
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

            // A quad a face, as ever: merged faces need their corners grown in the shader to meet
            // without gaps (Shaders.GrownCorner on the desktop), and this head's shaders do not.
            EditMesher.BuildChunk(owner.Grid, key.Coord, _scratch, merge: false);
            ApplyChunkMesh(key);
            rebuilt++;
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
            if (!_buffers.Remove(id, out Dictionary<ChunkCoord, EsChunkMeshBuffer>? chunks))
            {
                continue;
            }

            foreach (EsChunkMeshBuffer buffer in chunks.Values)
            {
                buffer.Dispose();
            }
        }
    }

    private void ApplyChunkMesh(ChunkKey key)
    {
        if (!_buffers.TryGetValue(key.ObjectId, out Dictionary<ChunkCoord, EsChunkMeshBuffer>? chunks))
        {
            chunks = [];
            _buffers.Add(key.ObjectId, chunks);
        }

        if (_scratch.IsEmpty)
        {
            if (chunks.Remove(key.Coord, out EsChunkMeshBuffer? removed))
            {
                removed.Dispose();
            }

            return;
        }

        if (!chunks.TryGetValue(key.Coord, out EsChunkMeshBuffer? buffer))
        {
            buffer = new EsChunkMeshBuffer();
            chunks.Add(key.Coord, buffer);
        }

        buffer.Upload(_scratch.Vertices, _scratch.Indices);
    }

    public void Render(VoxelScene scene, FlyCamera camera, Vector2 viewportSize)
    {
        GLES30.GlViewport(0, 0, (int)MathF.Max(viewportSize.X, 1f), (int)MathF.Max(viewportSize.Y, 1f));

        Vector4 background = BackgroundColor.ToVector4();
        GLES30.GlClearColor(background.X, background.Y, background.Z, 1f);
        GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit);

        DrawBackgroundGradient();

        Matrix4x4 viewProjection = camera.ViewProjection(viewportSize.X / MathF.Max(viewportSize.Y, 1f));
        Frustum frustum = Frustum.FromViewProjection(viewProjection);

        _voxelShader.Use();
        _voxelShader.SetMatrix4("uViewProjection", viewProjection);
        _voxelShader.SetInt("uUnlit", Lighting.IsLit ? 0 : 1);

        // The phone still lights with its one viewing light rather than the level's own: the shader
        // is shared, so that light goes in as the only entry of the same arrays.
        _lights.PackSingle(Lighting.Direction, Lighting.Intensity);
        _voxelShader.SetFloat("uAmbient", Lighting.Ambient);
        _voxelShader.SetInt("uLightCount", _lights.Count);
        _voxelShader.SetVector4Array("uLightPosition", Flatten(_lights.Positions));
        _voxelShader.SetVector3Array("uLightDirection", Flatten(_lights.Directions));
        _voxelShader.SetVector3Array("uLightColor", Flatten(_lights.Colours));
        _voxelShader.SetVector4Array("uLightShape", Flatten(_lights.Shapes));

        // No focus highlight yet. With one object on screen and no object list to switch between,
        // dimming everything else has nothing to say.
        _voxelShader.SetFloat("uFocusStrength", 0f);
        _voxelShader.SetFloat("uFocus", 0f);

        VisibleChunks = 0;
        DrawnTriangles = 0;

        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible
                || !_buffers.TryGetValue(o.Id, out Dictionary<ChunkCoord, EsChunkMeshBuffer>? chunks))
            {
                continue;
            }

            _voxelShader.SetMatrix4("uModel", o.Transform.ToMatrix());
            DrawObjectChunks(o, chunks, frustum);
        }

        DrawOverlays(viewProjection);
        GLES30.GlBindVertexArray(0);
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

        GLES30.GlDisable(GLES30.GlDepthTest);
        GLES30.GlDepthMask(false);
        GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
        GLES30.GlDepthMask(true);
        GLES30.GlEnable(GLES30.GlDepthTest);
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
        GLES30.GlDisable(GLES30.GlCullFaceConst);

        Lines.Draw();

        if (!GizmoLines.IsEmpty)
        {
            // Clear depth rather than switching the test off: that would put the gizmo in front of
            // the model but also stop it occluding itself, showing a cone's own back faces through
            // its front.
            GLES30.GlClear(GLES30.GlDepthBufferBit);
            GizmoLines.Draw();
        }

        GLES30.GlEnable(GLES30.GlCullFaceConst);
    }

    private void DrawObjectChunks(
        VoxelObject o,
        Dictionary<ChunkCoord, EsChunkMeshBuffer> chunks,
        Frustum frustum)
    {
        foreach ((ChunkCoord coord, EsChunkMeshBuffer buffer) in chunks)
        {
            if (buffer.IndexCount == 0)
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
            VisibleChunks++;
            DrawnTriangles += buffer.IndexCount / 3;
        }
    }

    private static (Vector3 Min, Vector3 Max) TransformBounds(
        ObjectTransform transform,
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
        foreach (Dictionary<ChunkCoord, EsChunkMeshBuffer> chunks in _buffers.Values)
        {
            foreach (EsChunkMeshBuffer buffer in chunks.Values)
            {
                buffer.Dispose();
            }
        }

        _buffers.Clear();
        _pending.Clear();
        _pendingOrder.Clear();
    }

    public void Dispose()
    {
        ResetBuffers();
        Lines.Dispose();
        GizmoLines.Dispose();
        _voxelShader.Dispose();
        _lineShader.Dispose();
        _backgroundShader.Dispose();
    }
}
