using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// Draws the world: one buffer per chunk, dirty chunks remeshed on the CPU and re-uploaded, plus a
/// line pass for the grid and the hovered face.
/// </summary>
public sealed class GlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<ChunkCoord, ChunkMeshBuffer> _buffers = new();
    private readonly MeshBuilder _scratch = new();

    // Chunks waiting to be remeshed. The set and the queue are kept in step so a chunk dirtied
    // repeatedly before it is reached is still only rebuilt once.
    private readonly HashSet<ChunkCoord> _pending = new();
    private readonly Queue<ChunkCoord> _pendingOrder = new();
    private readonly ShaderProgram _voxelShader;
    private readonly ShaderProgram _lineShader;

    public LineBatch Lines { get; }

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
    public void SyncDirtyChunks(VoxelWorld world)
    {
        foreach (ChunkCoord coord in world.ConsumeDirtyChunks())
        {
            if (_pending.Add(coord))
            {
                _pendingOrder.Enqueue(coord);
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

            ChunkCoord coord = _pendingOrder.Dequeue();
            _pending.Remove(coord);

            stepClock.Restart();
            EditMesher.BuildChunk(world, coord, _scratch);
            LastMeshMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            stepClock.Restart();
            ApplyChunkMesh(coord);
            LastUploadMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            LastRemeshedChunks++;
        }
    }

    private void ApplyChunkMesh(ChunkCoord coord)
    {
        if (_scratch.IsEmpty)
        {
            if (_buffers.Remove(coord, out ChunkMeshBuffer? removed))
            {
                TotalVertices -= removed.VertexCount;
                removed.Dispose();
            }

            return;
        }

        if (!_buffers.TryGetValue(coord, out ChunkMeshBuffer? buffer))
        {
            buffer = new ChunkMeshBuffer(_gl);
            _buffers.Add(coord, buffer);
        }

        // Tracked incrementally: recounting every buffer each frame is wasted work once a level
        // reaches thousands of chunks.
        TotalVertices -= buffer.VertexCount;
        buffer.Upload(_scratch.Vertices, _scratch.Indices);
        TotalVertices += buffer.VertexCount;
    }

    public void Render(FlyCamera camera, Vector2 viewportSize)
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

        foreach ((ChunkCoord coord, ChunkMeshBuffer buffer) in _buffers)
        {
            if (buffer.IsEmpty)
            {
                continue;
            }

            Vector3 min = coord.Origin.ToVector3();
            Vector3 max = min + new Vector3(Chunk.Size);
            if (!frustum.Intersects(min, max))
            {
                continue;
            }

            buffer.Draw();
            VisibleChunks++;
            DrawnTriangles += buffer.IndexCount / 3;
        }

        Reference.Draw(viewProjection);

        if (!Lines.IsEmpty)
        {
            _lineShader.Use();
            _lineShader.SetMatrix4("uViewProjection", viewProjection);
            Lines.Draw();
        }

        _gl.BindVertexArray(0);
    }

    /// <summary>Drops every GPU buffer. Used when the world is replaced by New or Open.</summary>
    public void ResetBuffers()
    {
        foreach (ChunkMeshBuffer buffer in _buffers.Values)
        {
            buffer.Dispose();
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
        _voxelShader.Dispose();
        _lineShader.Dispose();
    }
}
