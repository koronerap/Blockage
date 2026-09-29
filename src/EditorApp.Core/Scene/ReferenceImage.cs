using System.Numerics;

namespace EditorApp.Core.Scene;

/// <summary>Which plane a reference image stands in, as the view that looks straight at it names it.</summary>
public enum ImagePlane
{
    /// <summary>Upright across X, seen from the front (Numpad 1).</summary>
    Front,

    /// <summary>Upright across Z, seen from the side (Numpad 3).</summary>
    Side,

    /// <summary>Lying flat, seen from the top (Numpad 7).</summary>
    Top,
}

/// <summary>
/// A picture to model over (Fullreleaseplan 7.3): a drawing, a photo, a floor plan, standing in the
/// level on one of the three planes — to build on it in perspective, or as the background of the view
/// that looks straight at it, Blender's reference and background images in one. Saved with the level
/// as the path to the picture, never exported.
/// </summary>
public sealed class ReferenceImage(string path)
{
    public const float MinWidth = 0.1f;
    public const float MaxWidth = 100_000f;

    /// <summary>The picture, a PNG, where it was when it was added.</summary>
    public string Path { get; set; } = path;

    public ImagePlane Plane { get; set; } = ImagePlane.Front;

    /// <summary>The middle of the picture in the world.</summary>
    public Vector3 Centre { get; set; }

    /// <summary>How wide the picture is in the world; its height follows its proportions.</summary>
    public float Width { get; set; } = 32f;

    /// <summary>0 invisible to 1 solid.</summary>
    public float Opacity { get; set; } = 0.5f;

    /// <summary>Only drawn when the view looks straight at its plane, as a background image is.</summary>
    public bool OnlyAligned { get; set; }

    /// <summary>Always behind the model, however it stands in the level; otherwise drawn where it stands.</summary>
    public bool Behind { get; set; } = true;

    public bool Visible { get; set; } = true;

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public ReferenceImage Copy() => new(Path)
    {
        Plane = Plane,
        Centre = Centre,
        Width = Width,
        Opacity = Opacity,
        OnlyAligned = OnlyAligned,
        Behind = Behind,
        Visible = Visible,
    };
}
