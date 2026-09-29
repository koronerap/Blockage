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
    /// <summary>A chunk of a grid, by the grid's <see cref="VoxelWorld.Serial"/>: linked copies share one grid, and so its meshes.</summary>
    private readonly record struct ChunkKey(int MeshId, ChunkCoord Coord);

    /// <summary>The grids drawn this frame, by serial, for their chunks to be meshed from.</summary>
    private readonly Dictionary<int, VoxelWorld> _grids = [];

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
    private readonly ShaderProgram _imageShader;
    private readonly ShaderProgram _shadowShader;
    private readonly ShaderProgram _maskShader;
    private readonly ShaderProgram _outlineShader;

    private uint _maskFramebuffer;
    private uint _maskTexture;
    private uint _maskDepth;
    private uint _maskWidth;
    private uint _maskHeight;

    /// <summary>
    /// The objects outlined round what can be seen of them, and how: the active one, the rest of the
    /// selection, the one the outliner points at, the one the mouse is over. Empty for none.
    /// </summary>
    public IReadOnlyList<(int Id, OutlineKind Kind)> Outlines { get; set; } = [];

    /// <summary>How many pixels out from the edge the outline reaches.</summary>
    public int OutlineWidth { get; set; } = 2;

    /// <summary>How many texels across the sun's depth map is.</summary>
    private const int ShadowSize = 2048;

    private uint _shadowFramebuffer;
    private uint _shadowTexture;

    /// <summary>How much a corner enclosed by voxels darkens: 0 for none, 1 for the full shade.</summary>
    public float AmbientOcclusion { get; set; } = 1f;

    /// <summary>Whether the sun casts shadows, in Lit shading.</summary>
    public bool Shadows { get; set; } = true;

    /// <summary>The section box nothing outside of is drawn; null for none.</summary>
    public ClipBox? Clip { get; set; }

    /// <summary>
    /// Rendered shading's picture, drawn in the voxels' place; 0 draws the voxels. They still go into
    /// the depth buffer, unseen, so the overlays on top meet the model where they should.
    /// </summary>
    public uint RenderedImage { get; set; }

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
        // The desktop's voxel shader has the sun's shadows; a head without them leaves the define out.
        _voxelShader = new ShaderProgram(gl, Shaders.VoxelVertex, Shaders.VoxelFragment.Replace("#version 330 core", "#version 330 core\n#define BLOCKAGE_SHADOWS"));
        _shadowShader = new ShaderProgram(gl, Shaders.ShadowVertex, Shaders.ShadowFragment);
        _maskShader = new ShaderProgram(gl, Shaders.MaskVertex, Shaders.MaskFragment);
        _outlineShader = new ShaderProgram(gl, Shaders.BackgroundVertex, Shaders.OutlineFragment);
        _lineShader = new ShaderProgram(gl, Shaders.LineVertex, Shaders.LineFragment);
        _backgroundShader = new ShaderProgram(gl, Shaders.BackgroundVertex, Shaders.BackgroundFragment);
        _imageShader = new ShaderProgram(gl, Shaders.BackgroundVertex, Shaders.ImageFragment);
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
        // Each grid once, however many linked copies show it. A grid no object shows any more — an
        // object deleted, its modifiers changed, a copy made a single user — is let go of.
        _grids.Clear();
        foreach (VoxelObject o in scene.Objects)
        {
            VoxelWorld shown = o.Shown;
            if (!_grids.TryAdd(shown.Serial, shown))
            {
                continue;
            }

            foreach (ChunkCoord coord in shown.ConsumeDirtyChunks())
            {
                var key = new ChunkKey(shown.Serial, coord);
                if (_pending.Add(key))
                {
                    _pendingOrder.Enqueue(key);
                }
            }
        }

        DropUnusedMeshes();

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

            if (!_grids.TryGetValue(key.MeshId, out VoxelWorld? grid))
            {
                continue;   // nothing shows that grid any more
            }

            stepClock.Restart();
            EditMesher.BuildChunk(grid, key.Coord, _scratch);
            LastMeshMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            stepClock.Restart();
            ApplyChunkMesh(key);
            _meshRevision++;
            LastUploadMilliseconds += stepClock.Elapsed.TotalMilliseconds;

            LastRemeshedChunks++;
        }
    }

    /// <summary>Lets go of everything drawn for a grid nothing shows any more.</summary>
    private void DropUnusedMeshes()
    {
        List<int>? stale = null;
        foreach (int id in _buffers.Keys)
        {
            if (!_grids.ContainsKey(id))
            {
                (stale ??= []).Add(id);
            }
        }

        foreach (int id in stale ?? [])
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

    /// <summary>What each palette entry is made of, a texel each, for the voxel shader to look up.</summary>
    private uint _materialTexture;
    private Palette? _materialsOf;
    private int _materialsRevision = -1;

    /// <summary>Puts the palette's materials on the GPU, when they have changed since last time.</summary>
    private unsafe void UploadMaterials(Palette palette)
    {
        if (_materialTexture == 0)
        {
            _materialTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _materialTexture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        if (ReferenceEquals(palette, _materialsOf) && palette.MaterialRevision == _materialsRevision)
        {
            return;
        }

        _materialsOf = palette;
        _materialsRevision = palette.MaterialRevision;

        var texels = new byte[Palette.Size * 4];
        for (int i = 0; i < Palette.Size; i++)
        {
            VoxelMaterial m = palette.Material(i);
            texels[(i * 4) + 0] = (byte)MathF.Round(m.Emission * 255f);
            texels[(i * 4) + 1] = (byte)MathF.Round(m.Metallic * 255f);
            texels[(i * 4) + 2] = (byte)MathF.Round(m.Smoothness * 255f);
            texels[(i * 4) + 3] = (byte)MathF.Round(m.Opacity * 255f);
        }

        _gl.BindTexture(TextureTarget.Texture2D, _materialTexture);
        fixed (byte* data = texels)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, Palette.Size, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }


    private void ApplyChunkMesh(ChunkKey key)
    {
        if (!_buffers.TryGetValue(key.MeshId, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
        {
            chunks = [];
            _buffers.Add(key.MeshId, chunks);
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

        // The sun's shadow map first, drawn in a framebuffer of its own; the viewport is put back after.
        UploadLightsForShadow(scene);
        Matrix4x4? shadow = Shadows && Lighting.IsLit && _shadowLight >= 0 ? RenderShadowMap(scene) : null;
        if (shadow is not null)
        {
            _gl.Viewport((int)viewportPosition.X, glY, width, height);
        }

        _voxelShader.Use();
        UploadMaterials(scene.Palette);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _materialTexture);
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, _shadowTexture);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _voxelShader.SetInt("uMaterials", 1);
        _voxelShader.SetInt("uShadowMap", 2);
        _voxelShader.SetInt("uShadowLight", shadow is not null ? _shadowLight : -1);
        _voxelShader.SetMatrix4("uShadowMatrix", shadow ?? Matrix4x4.Identity);
        _voxelShader.SetFloat("uShadowBias", _shadowBias);
        _voxelShader.SetFloat("uOcclusion", Lighting.Mode == ShadingMode.Wireframe ? 0f : AmbientOcclusion);
        SetClip(_voxelShader);
        _voxelShader.SetVector3("uCameraPosition", camera.Position);
        _voxelShader.SetInt("uPass", 2);
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

        // Rendered shading: the voxels only into the depth buffer, and the picture where they would be.
        bool rendered = Lighting.Mode == ShadingMode.Rendered && RenderedImage != 0;
        if (rendered)
        {
            _voxelShader.SetInt("uPass", 2);
            _gl.ColorMask(false, false, false, false);
            DrawObjects(scene, frustum, count: true);
            _gl.ColorMask(true, true, true, true);
            DrawRenderedImage();
            _voxelShader.Use();
        }

        // Solid faces, then — when anything is see-through — the glass over them, blended and
        // writing no depth, so what is behind it still shows.
        _voxelShader.SetInt("uPass", blended ? 2 : 0);
        if (!rendered)
        {
            DrawObjects(scene, frustum, count: true);
        }

        if (!rendered && !blended && scene.Palette.AnyTransparent)
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.One);
            _gl.DepthMask(false);
            _voxelShader.SetInt("uPass", 1);
            DrawObjects(scene, frustum, count: false);
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        if (blended)
        {
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.DepthFunc(DepthFunction.Less);

        DrawOutlines(scene, viewProjection, frustum, (int)viewportPosition.X, glY, width, height);

        Reference.Draw(viewProjection);
        DrawOverlays(viewProjection);

        _gl.BindVertexArray(0);
    }

    /// <summary>The level's own lights and ambient floor. Whether they are used at all is the Lit switch.</summary>
    /// <summary>
    /// Blender's selection outline: what shows of each outlined object drawn into a mask, then its
    /// outside edge painted over the viewport in the object's outline colour.
    /// </summary>
    private unsafe void DrawOutlines(VoxelScene scene, Matrix4x4 viewProjection, Frustum frustum, int x, int y, uint width, uint height)
    {
        if (Outlines.Count == 0 || Lighting.Mode == ShadingMode.Wireframe)
        {
            return;
        }

        if (_maskFramebuffer == 0 || _maskWidth != width || _maskHeight != height)
        {
            if (_maskFramebuffer != 0)
            {
                _gl.DeleteFramebuffer(_maskFramebuffer);
                _gl.DeleteTexture(_maskTexture);
                _gl.DeleteRenderbuffer(_maskDepth);
            }

            _maskTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _maskTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, width, height, 0, PixelFormat.Red, PixelType.UnsignedByte, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.BindTexture(TextureTarget.Texture2D, 0);

            _maskDepth = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _maskDepth);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, width, height);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            _maskFramebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _maskFramebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _maskTexture, 0);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _maskDepth);
            _maskWidth = width;
            _maskHeight = height;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _maskFramebuffer);
        _gl.Viewport(0, 0, width, height);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(true);

        _maskShader.Use();
        _maskShader.SetMatrix4("uViewProjection", viewProjection);
        SetClip(_maskShader);

        // What stands in front of an outlined object hides its outline there — unless X-Ray sees through.
        if (XRay <= 0f)
        {
            _gl.ColorMask(false, false, false, false);
            foreach (VoxelObject o in scene.Objects)
            {
                if (o.Visible && _buffers.TryGetValue(o.Shown.Serial, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
                {
                    _maskShader.SetMatrix4("uModel", o.Transform.ToMatrix());
                    DrawObjectChunks(o, chunks, frustum, count: false);
                }
            }

            _gl.ColorMask(true, true, true, true);
        }

        _gl.DepthFunc(DepthFunction.Lequal);
        foreach ((int id, OutlineKind kind) in Outlines.OrderBy(outline => outline.Kind))
        {
            if (FindShown(scene, id) is not { } o || !_buffers.TryGetValue(o.Shown.Serial, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
            {
                continue;
            }

            _maskShader.SetFloat("uCode", (int)kind / 4f);
            _maskShader.SetMatrix4("uModel", o.Transform.ToMatrix());
            DrawObjectChunks(o, chunks, frustum, count: false);
        }

        _gl.DepthFunc(DepthFunction.Less);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(x, y, width, height);

        _outlineShader.Use();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _maskTexture);
        _outlineShader.SetInt("uMask", 0);
        _outlineShader.SetInt("uOutlineWidth", Math.Clamp(OutlineWidth, 1, 6));
        _outlineShader.SetVector4Array("uOutlineColour",
        [
            Vector4.Zero,
            EditorOverlays.ObjectHovered.ToVector4() with { W = 0.7f },
            EditorOverlays.Highlight.ToVector4(),
            EditorOverlays.ObjectSelected.ToVector4(),
            EditorOverlays.ObjectActive.ToVector4(),
        ]);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static VoxelObject? FindShown(VoxelScene scene, int id) =>
        scene.Find(id) is { Visible: true } o ? o : null;

    /// <summary>Which of the uploaded lights is the first sun, the one that casts shadows; -1 for none.</summary>
    private int _shadowLight = -1;

    /// <summary>How far a point is pushed off its face before its shadow is looked up, in world units.</summary>
    private float _shadowBias;

    /// <summary>What the shadow map was last drawn for, so a still scene is not drawn into it every frame.</summary>
    private int _shadowKey;
    private Matrix4x4? _shadowMatrix;

    /// <summary>Goes up with every chunk meshed again: a change the shadow map has to follow.</summary>
    private long _meshRevision;

    private void UploadLightsForShadow(VoxelScene scene)
    {
        _lights.Pack(scene.Lights);
        _shadowLight = -1;
        for (int i = 0; i < _lights.Count; i++)
        {
            if (_lights.Positions[i].W == 0f)
            {
                _shadowLight = i;
                break;
            }
        }
    }

    /// <summary>
    /// The depth of every shown object as the sun sees it, fitted round what there is to see, and the
    /// matrix that takes a point of the world into it; null when there is nothing to cast a shadow.
    /// </summary>
    private unsafe Matrix4x4? RenderShadowMap(VoxelScene scene)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (VoxelObject o in scene.Objects)
        {
            if (o.Visible && o.TryGetWorldBounds(out Vector3 low, out Vector3 high))
            {
                min = Vector3.Min(min, low);
                max = Vector3.Max(max, high);
            }
        }

        if (min.X > max.X)
        {
            return null;
        }

        if (_shadowFramebuffer == 0)
        {
            _shadowTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _shadowTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, ShadowSize, ShadowSize, 0, PixelFormat.DepthComponent, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);

            _shadowFramebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _shadowTexture, 0);
            _gl.DrawBuffer(DrawBufferMode.None);
            _gl.ReadBuffer(ReadBufferMode.None);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        // The sun looks along the way it shines, from far enough out to see all of it.
        Vector3 shining = Vector3.Normalize(-new Vector3(_lights.Positions[_shadowLight].X, _lights.Positions[_shadowLight].Y, _lights.Positions[_shadowLight].Z));

        // Drawn again only when something it shows could have changed: the sun, where things stand, their meshes.
        var key = new HashCode();
        key.Add(shining);
        key.Add(min);
        key.Add(max);
        key.Add(TotalVertices);
        key.Add(_meshRevision);
        key.Add(Clip);
        foreach (VoxelObject o in scene.Objects)
        {
            key.Add(o.Visible);
            key.Add(o.Transform);
        }

        int shadowKey = key.ToHashCode();
        if (shadowKey == _shadowKey && _shadowMatrix is { } kept)
        {
            return kept;
        }

        Vector3 centre = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 1f);
        Vector3 up = MathF.Abs(shining.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 view = Matrix4x4.CreateLookAt(centre - (shining * radius * 2f), centre, up);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, radius * 0.5f, radius * 3.5f);
        Matrix4x4 lightViewProjection = view * projection;

        // A texel's width, as far as a face is pushed off itself.
        _shadowBias = (radius * 2f / ShadowSize) * 1.5f;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFramebuffer);
        _gl.Viewport(0, 0, ShadowSize, ShadowSize);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);

        _shadowShader.Use();
        _shadowShader.SetMatrix4("uLightViewProjection", lightViewProjection);
        SetClip(_shadowShader);
        foreach (VoxelObject o in scene.Objects)
        {
            if (!o.Visible || !_buffers.TryGetValue(o.Shown.Serial, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
            {
                continue;
            }

            _shadowShader.SetMatrix4("uModel", o.Transform.ToMatrix());
            foreach (ChunkMeshBuffer buffer in chunks.Values)
            {
                if (!buffer.IsEmpty)
                {
                    buffer.Draw();
                }
            }
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _shadowKey = shadowKey;
        _shadowMatrix = lightViewProjection;
        return lightViewProjection;
    }

    private void SetClip(ShaderProgram shader)
    {
        shader.SetInt("uClip", Clip is null ? 0 : 1);
        shader.SetVector3("uClipMin", Clip?.Min ?? Vector3.Zero);
        shader.SetVector3("uClipMax", Clip?.Max ?? Vector3.Zero);
    }

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
    private void DrawRenderedImage()
    {
        _imageShader.Use();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, RenderedImage);
        _imageShader.SetInt("uImage", 0);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

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
            if (!o.Visible || !_buffers.TryGetValue(o.Shown.Serial, out Dictionary<ChunkCoord, ChunkMeshBuffer>? chunks))
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
        _imageShader.Dispose();
        _shadowShader.Dispose();
        _maskShader.Dispose();
        _outlineShader.Dispose();
        if (_maskFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_maskFramebuffer);
            _gl.DeleteTexture(_maskTexture);
            _gl.DeleteRenderbuffer(_maskDepth);
        }

        if (_shadowFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_shadowFramebuffer);
            _gl.DeleteTexture(_shadowTexture);
        }

        _lineShader.Dispose();
        if (_materialTexture != 0)
        {
            _gl.DeleteTexture(_materialTexture);
        }
    }
}
