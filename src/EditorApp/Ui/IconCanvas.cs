using System.Numerics;

namespace EditorApp.Ui;

/// <summary>
/// The handful of shapes every icon in the editor is built from.
///
/// It exists so <see cref="Icons"/> can describe a pictogram without knowing what will draw it. The
/// desktop draws through ImGui's draw list and the phone through an Android canvas; an icon that
/// meant one thing on one screen and something else on the other would be worse than no icon.
///
/// Coordinates are in pixels, already positioned. <c>width</c> is a multiplier on whatever stroke
/// weight the canvas was set up with, so an icon can say "lighter than the rest of me" without
/// knowing how heavy that is.
/// </summary>
public interface IIconCanvas
{
    void Line(Vector2 from, Vector2 to, float width = 1f);

    /// <summary>An open run of points. Curves are sampled into these rather than described.</summary>
    void Polyline(ReadOnlySpan<Vector2> points, float width = 1f);

    void Rect(Vector2 min, Vector2 max, float rounding = 0f, float width = 1f);

    void FilledRect(Vector2 min, Vector2 max, float rounding = 0f);

    void Circle(Vector2 centre, float radius, float width = 1f);

    void FilledCircle(Vector2 centre, float radius);

    void FilledTriangle(Vector2 a, Vector2 b, Vector2 c);
}
