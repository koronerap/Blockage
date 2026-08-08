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

    public Color32 BackgroundColor { get; set; } = new(38, 42, 48);

    public GlRenderer(GL gl)
    {
        _gl = gl;
        _voxelShader = new ShaderProgram(gl, Shaders.VoxelVertex, Shaders.VoxelFragment);
        _lineShader = new ShaderProgram(gl, Shaders.LineVertex, Shaders.LineFragment);
        Lines = new LineBatch(gl);
        GizmoLines = new LineBatch(gl);
        Reference = new ReferenceModelRenderer(gl);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);
    }

    /// <summary>
    /// Remeshes and re-uploads dirty chunks. Chunks that became empty release their buffers.
    /// </summary>
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
            EditMesher.BuildChunk(owner.Grid, key.Coord, _scratch);
            LastMeshMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            stepClock.Restart();
            ApplyChunkMesh(key);
            LastUploadMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            LastRemeshedChunks++;
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

    public void Render(VoxelScene scene, FlyCamera camera, Vector2 viewportSize)
    {
        _gl.Viewport(0, 0, (uint)MathF.Max(viewportSize.X, 1f), (uint)MathF.Max(viewportSize.Y, 1f));

        Vector4 background = BackgroundColor.ToVector4();
        _gl.ClearColor(background.X, background.Y, background.Z, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Matrix4x4 viewProjection = camera.ViewProjection(viewportSize.X / MathF.Max(viewportSize.Y, 1f));
        Frustum frustum = Frustum.FromViewProjection(viewProjection);

        _voxelShader.Use();
        _voxelShader.SetMatrix4("uViewProjection", viewProjection);

        VisibleChunks = 0;
        DrawnTriangles = 0;

        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible || !_buffers.TryGetValue(o.Id, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
            {
                continue;
            }

            _voxelShader.SetMatrix4("uModel", o.Transform.ToMatrix());
            DrawObjectChunks(o, chunks, frustum);
        }

        Reference.Draw(viewProjection);
        DrawOverlays(viewProjection);

        _gl.BindVertexArray(0);
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
            _gl.Disable(EnableCap.DepthTest);
            GizmoLines.Draw();
            _gl.Enable(EnableCap.DepthTest);
        }

        _gl.Enable(EnableCap.CullFace);
    }

    private void DrawObjectChunks(
        VoxelObject o,
        Dictionary<ChunkCoord, ChunkMeshBuffer> chunks,
        Frustum frustum)
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
            VisibleChunks++;
            DrawnTriangles += buffer.IndexCount / 3;
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
        _voxelShader.Dispose();
        _lineShader.Dispose();
    }
}
