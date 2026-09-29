using System.Numerics;
using EditorApp.Core.Import;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using Silk.NET.OpenGL;

namespace EditorApp.Rendering;

/// <summary>
/// Draws the level's reference images (Fullreleaseplan 7.3): each a picture on its plane, see-through
/// as far as its opacity says — behind the model whatever stands in front, or where it stands among
/// the rest; and, for one kept to its own view, only while the view looks straight at it.
/// </summary>
public sealed class ReferenceImageRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly Dictionary<string, (uint Texture, int Width, int Height, DateTime Written)> _pictures = [];
    private readonly HashSet<string> _unreadable = [];

    public unsafe ReferenceImageRenderer(GL gl)
    {
        _gl = gl;
        _shader = new ShaderProgram(gl, Shaders.ImagePlaneVertex, Shaders.ImagePlaneFragment);

        // A unit quad about the origin, with the picture's corners on it: position, then where in it.
        float[] quad =
        [
            -0.5f, -0.5f, 0f, 0f, 1f,
            0.5f, -0.5f, 0f, 1f, 1f,
            0.5f, 0.5f, 0f, 1f, 0f,
            -0.5f, -0.5f, 0f, 0f, 1f,
            0.5f, 0.5f, 0f, 1f, 0f,
            -0.5f, 0.5f, 0f, 0f, 0f,
        ];

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* data = quad)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    /// <summary>The pictures that could not be read, by path, for the panel to say so.</summary>
    public bool CannotRead(string path) => _unreadable.Contains(path);

    /// <summary>How tall a picture is for its width: 1 until it has been read.</summary>
    public float Aspect(string path) => _pictures.TryGetValue(path, out var known) && known.Width > 0 ? known.Height / (float)known.Width : 1f;

    /// <summary>
    /// Draws the pictures of one kind: those kept behind the model, before it is drawn, or the rest,
    /// among it once it has been.
    /// </summary>
    public void Draw(VoxelScene scene, FlyCamera camera, Matrix4x4 viewProjection, bool behind)
    {
        if (scene.ReferenceImages.Count == 0)
        {
            return;
        }

        AlignedView? aligned = camera.CurrentAlignedView();
        _shader.Use();
        _shader.SetMatrix4("uViewProjection", viewProjection);
        _shader.SetInt("uImage", 0);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(false);
        if (behind)
        {
            _gl.Disable(EnableCap.DepthTest);
        }

        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        foreach (ReferenceImage image in scene.ReferenceImages)
        {
            if (!image.Visible || image.Behind != behind || image.Opacity <= 0f
                || (image.OnlyAligned && !Faces(image.Plane, aligned))
                || Picture(image.Path) is not { } picture)
            {
                continue;
            }

            float height = image.Width * picture.Height / MathF.Max(picture.Width, 1);
            Matrix4x4 turn = image.Plane switch
            {
                ImagePlane.Side => Matrix4x4.CreateRotationY(MathF.PI / 2f),
                ImagePlane.Top => Matrix4x4.CreateRotationX(-MathF.PI / 2f),
                _ => Matrix4x4.Identity,
            };

            _shader.SetMatrix4("uModel", Matrix4x4.CreateScale(image.Width, height, 1f) * turn * Matrix4x4.CreateTranslation(image.Centre));
            _shader.SetFloat("uOpacity", image.Opacity);
            _gl.BindTexture(TextureTarget.Texture2D, picture.Texture);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.BindVertexArray(0);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
    }

    /// <summary>Whether the view looks straight at a plane, from either side.</summary>
    private static bool Faces(ImagePlane plane, AlignedView? view) => (plane, view) switch
    {
        (ImagePlane.Front, AlignedView.Front or AlignedView.Back) => true,
        (ImagePlane.Side, AlignedView.Right or AlignedView.Left) => true,
        (ImagePlane.Top, AlignedView.Top or AlignedView.Bottom) => true,
        _ => false,
    };

    /// <summary>A picture on the GPU, read again when the file changes; null when it cannot be read.</summary>
    private unsafe (uint Texture, int Width, int Height)? Picture(string path)
    {
        if (!File.Exists(path))
        {
            _unreadable.Add(path);
            return null;
        }

        DateTime written = File.GetLastWriteTimeUtc(path);
        if (_pictures.TryGetValue(path, out var known) && known.Written == written)
        {
            return (known.Texture, known.Width, known.Height);
        }

        if (_pictures.Remove(path, out var stale))
        {
            _gl.DeleteTexture(stale.Texture);
        }

        DecodedImage image;
        try
        {
            image = PngReader.Decode(path);
            _unreadable.Remove(path);
        }
        catch (ImageDecodeException)
        {
            _unreadable.Add(path);
            return null;
        }

        uint texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        fixed (Color32* pixels = image.Pixels)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _pictures[path] = (texture, image.Width, image.Height, written);
        return (texture, image.Width, image.Height);
    }

    public void Dispose()
    {
        foreach (var picture in _pictures.Values)
        {
            _gl.DeleteTexture(picture.Texture);
        }

        _pictures.Clear();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
