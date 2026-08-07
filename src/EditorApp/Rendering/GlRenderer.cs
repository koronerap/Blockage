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
    private readonly ShaderProgram _voxelShader;
    private readonly ShaderProgram _lineShader;

    public LineBatch Lines { get; }

    /// <summary>Chunks meshed in the most recent sync. Shown in the stats overlay.</summary>
    public int LastRemeshedChunks { get; private set; }

    public int VisibleChunks { get; private set; }

    public int DrawnTriangles { get; private set; }

    public int TotalVertices { get; private set; }

    public Color32 BackgroundColor { get; set; } = new(38, 42, 48);

    public GlRenderer(GL gl)
    {
        _gl = gl;
        _voxelShader = new ShaderProgram(gl, Shaders.VoxelVertex, Shaders.VoxelFragment);
        _lineShader = new ShaderProgram(gl, Shaders.LineVertex, Shaders.LineFragment);
        Lines = new LineBatch(gl);

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
        List<ChunkCoord> dirty = world.ConsumeDirtyChunks();
        LastRemeshedChunks = dirty.Count;

        foreach (ChunkCoord coord in dirty)
        {
            EditMesher.BuildChunk(world, coord, _scratch);

            if (_scratch.IsEmpty)
            {
                if (_buffers.Remove(coord, out ChunkMeshBuffer? removed))
                {
                    removed.Dispose();
                }

                continue;
            }

            if (!_buffers.TryGetValue(coord, out ChunkMeshBuffer? buffer))
            {
                buffer = new ChunkMeshBuffer(_gl);
                _buffers.Add(coord, buffer);
            }

            buffer.Upload(_scratch.Vertices, _scratch.Indices);
        }

        if (dirty.Count > 0)
        {
            TotalVertices = 0;
            foreach (ChunkMeshBuffer buffer in _buffers.Values)
            {
                TotalVertices += buffer.VertexCount;
            }
        }
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
        TotalVertices = 0;
    }

    public void Dispose()
    {
        ResetBuffers();
        Lines.Dispose();
        _voxelShader.Dispose();
        _lineShader.Dispose();
    }
}
