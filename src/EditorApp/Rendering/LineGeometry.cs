using System.Numerics;
using System.Runtime.InteropServices;
using EditorApp.Core.Voxels;

namespace EditorApp.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LineVertex(Vector3 position, uint rgba)
{
    public Vector3 Position = position;
    public uint Rgba = rgba;

    public const int SizeInBytes = 16;
}

/// <summary>
/// Builds the overlay geometry: the ground grid, selection outlines and the gizmos.
///
/// Nothing here touches a graphics API — it turns lines, boxes and cones into two vertex lists, and
/// leaves uploading them to whoever inherits. That split is what lets the Android head draw the same
/// grid and the same gizmos through OpenGL ES without a second copy of the geometry.
///
/// Anything that needs to be thicker than a hairline is built as camera-facing quads rather than
/// asking for a wide <c>GL_LINE</c>. A core-profile driver is only required to support a line width
/// of 1, so <c>glLineWidth</c> is silently ignored on many of them — the one thing a gizmo cannot
/// afford. Width is scaled by distance so it stays constant on screen.
/// </summary>
public abstract class LineGeometry
{
    private readonly List<LineVertex> _lines = [];
    private readonly List<LineVertex> _quads = [];
    private readonly List<LineVertex> _fills = [];

    /// <summary>The GL_LINES half — hairlines, where a single pixel is all the grid needs.</summary>
    protected ReadOnlySpan<LineVertex> LineVertices => CollectionsMarshal.AsSpan(_lines);

    /// <summary>The triangle half — everything built with width: thick strokes and cone heads.</summary>
    protected ReadOnlySpan<LineVertex> QuadVertices => CollectionsMarshal.AsSpan(_quads);

    /// <summary>
    /// Translucent surfaces — the tint over a selection, the face a cut will open. Drawn first, blended
    /// and without writing depth, so a stroke drawn after is never hidden behind a tint in front of it.
    /// </summary>
    protected ReadOnlySpan<LineVertex> FillVertices => CollectionsMarshal.AsSpan(_fills);

    public bool IsEmpty => _lines.Count == 0 && _quads.Count == 0 && _fills.Count == 0;

    /// <summary>
    /// Applied to every point added from now on. Selections and hover highlights are expressed in
    /// the focused object's own space, so this carries them into the world without a second shader
    /// or a second draw pass.
    /// </summary>
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    /// <summary>Where the camera is, so thick lines can be turned to face it and sized on screen.</summary>
    public Vector3 CameraPosition { get; set; }

    /// <summary>
    /// Half-width of a thick line as a fraction of its distance from the camera. Roughly a
    /// constant number of pixels at the default field of view.
    /// </summary>
    public float ThicknessScale { get; set; } = 0.0035f;

    public void Clear()
    {
        _lines.Clear();
        _quads.Clear();
        _fills.Clear();
        Transform = Matrix4x4.Identity;
    }

    public void AddLine(Vector3 from, Vector3 to, Color32 color)
    {
        uint rgba = color.Rgba;
        _lines.Add(new LineVertex(Vector3.Transform(from, Transform), rgba));
        _lines.Add(new LineVertex(Vector3.Transform(to, Transform), rgba));
    }

    /// <summary>
    /// A line with body. <paramref name="width"/> is a multiplier on <see cref="ThicknessScale"/>,
    /// so 1 is a normal overlay stroke and 3 is a gizmo handle you can actually grab.
    /// </summary>
    public void AddThickLine(Vector3 from, Vector3 to, Color32 color, float width = 1f)
    {
        Vector3 start = Vector3.Transform(from, Transform);
        Vector3 end = Vector3.Transform(to, Transform);

        Vector3 along = end - start;
        if (along.LengthSquared() < 1e-10f)
        {
            return;
        }

        Vector3 midpoint = (start + end) * 0.5f;
        Vector3 toCamera = CameraPosition - midpoint;
        if (toCamera.LengthSquared() < 1e-10f)
        {
            toCamera = Vector3.UnitY;
        }

        // Perpendicular to both the line and the view, which is what makes the quad read as a
        // round stroke from any angle.
        Vector3 side = Vector3.Cross(Vector3.Normalize(along), Vector3.Normalize(toCamera));
        if (side.LengthSquared() < 1e-8f)
        {
            // Looking straight down the line: any perpendicular will do.
            side = Vector3.Cross(Vector3.Normalize(along), Vector3.UnitY);
            if (side.LengthSquared() < 1e-8f)
            {
                side = Vector3.Cross(Vector3.Normalize(along), Vector3.UnitX);
            }
        }

        float halfWidth = MathF.Max(toCamera.Length() * ThicknessScale * width, 1e-4f);
        side = Vector3.Normalize(side) * halfWidth;

        uint rgba = color.Rgba;
        var a = new LineVertex(start - side, rgba);
        var b = new LineVertex(start + side, rgba);
        var c = new LineVertex(end + side, rgba);
        var d = new LineVertex(end - side, rgba);

        // Two triangles; the overlay pass draws with culling off, so winding does not matter.
        _quads.Add(a);
        _quads.Add(b);
        _quads.Add(c);
        _quads.Add(a);
        _quads.Add(c);
        _quads.Add(d);
    }

    /// <summary>
    /// A solid cone, for arrow heads. Four crossed barbs read as a bundle of sticks from most
    /// angles; a cone reads as a direction from all of them.
    ///
    /// The shader has no lighting, so each side facet is shaded by how much it faces the camera —
    /// enough to keep the cone from flattening into a coloured triangle.
    /// </summary>
    public void AddCone(Vector3 tip, Vector3 baseCentre, float radius, Color32 color, int segments = 12)
    {
        Vector3 worldTip = Vector3.Transform(tip, Transform);
        Vector3 worldBase = Vector3.Transform(baseCentre, Transform);

        Vector3 axis = worldTip - worldBase;
        if (axis.LengthSquared() < 1e-10f)
        {
            return;
        }

        axis = Vector3.Normalize(axis);

        Vector3 reference = MathF.Abs(axis.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, reference)) * radius;
        Vector3 v = Vector3.Normalize(Vector3.Cross(axis, u)) * radius;

        Vector3 toCamera = Vector3.Normalize(CameraPosition - worldBase);

        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;

            Vector3 p0 = worldBase + u * MathF.Cos(a0) + v * MathF.Sin(a0);
            Vector3 p1 = worldBase + u * MathF.Cos(a1) + v * MathF.Sin(a1);

            Vector3 outward = Vector3.Normalize((p0 + p1) * 0.5f - worldBase);
            uint facet = Shade(color, 0.72f + 0.28f * MathF.Max(Vector3.Dot(outward, toCamera), 0f));

            AddTriangle(worldTip, p0, p1, facet);

            // The base cap, a touch darker, so the cone still has an edge seen from behind.
            AddTriangle(worldBase, p1, p0, Shade(color, 0.55f));
        }
    }

    /// <summary>A flat four-cornered fill, blended by its colour's alpha. Corners go round the edge.</summary>
    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
    {
        uint rgba = color.Rgba;
        var va = new LineVertex(Vector3.Transform(a, Transform), rgba);
        var vb = new LineVertex(Vector3.Transform(b, Transform), rgba);
        var vc = new LineVertex(Vector3.Transform(c, Transform), rgba);
        var vd = new LineVertex(Vector3.Transform(d, Transform), rgba);

        _fills.Add(va);
        _fills.Add(vb);
        _fills.Add(vc);
        _fills.Add(va);
        _fills.Add(vc);
        _fills.Add(vd);
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, uint rgba)
    {
        _quads.Add(new LineVertex(a, rgba));
        _quads.Add(new LineVertex(b, rgba));
        _quads.Add(new LineVertex(c, rgba));
    }

    private static uint Shade(Color32 color, float amount)
    {
        static byte Scale(byte channel, float amount) =>
            (byte)Math.Clamp(channel * amount, 0f, 255f);

        return new Color32(Scale(color.R, amount), Scale(color.G, amount), Scale(color.B, amount), color.A).Rgba;
    }

    /// <summary>Outlines one face of a voxel, pushed slightly outward so it does not z-fight.</summary>
    public void AddVoxelFace(Int3 voxel, Face face, Color32 color, float offset = 0.004f, float width = 0f)
    {
        Vector3 push = FaceInfo.Normal(face) * offset;
        Vector3 origin = voxel.ToVector3();

        Vector3 Corner(int index) => origin + FaceInfo.Corner(face, index).ToVector3() + push;

        for (int i = 0; i < 4; i++)
        {
            AddEdge(Corner(i), Corner((i + 1) & 3), color, width);
        }
    }

    public void AddBox(Vector3 min, Vector3 max, Color32 color, float width = 0f)
    {
        Span<Vector3> corners =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ];

        for (int i = 0; i < 4; i++)
        {
            AddEdge(corners[i], corners[(i + 1) & 3], color, width);
            AddEdge(corners[i + 4], corners[((i + 1) & 3) + 4], color, width);
            AddEdge(corners[i], corners[i + 4], color, width);
        }
    }

    /// <summary>Thin when no width is asked for, so the grid stays cheap.</summary>
    private void AddEdge(Vector3 from, Vector3 to, Color32 color, float width)
    {
        if (width > 0f)
        {
            AddThickLine(from, to, color, width);
        }
        else
        {
            AddLine(from, to, color);
        }
    }

    /// <summary>A ground grid on the Y = 0 plane, with every tenth line emphasised.</summary>
    /// <param name="halfCells">How far the grid reaches from the origin, counted in cells.</param>
    /// <param name="spacing">Width of one cell, in voxels. See <see cref="GroundGrid"/>.</param>
    public void AddGroundGrid(int halfCells, float spacing, Color32 minor, Color32 major)
    {
        if (spacing <= 0f || !float.IsFinite(spacing))
        {
            return;
        }

        halfCells = Math.Min(halfCells, GroundGrid.MaxLinesPerAxis);
        float extent = halfCells * spacing;

        for (int i = -halfCells; i <= halfCells; i++)
        {
            float offset = i * spacing;
            Color32 color = i % 10 == 0 ? major : minor;

            AddLine(new Vector3(offset, 0, -extent), new Vector3(offset, 0, extent), color);
            AddLine(new Vector3(-extent, 0, offset), new Vector3(extent, 0, offset), color);
        }
    }
}
