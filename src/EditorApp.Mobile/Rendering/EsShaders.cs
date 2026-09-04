using EditorApp.Rendering;

namespace EditorApp.Mobile.Rendering;

/// <summary>
/// The desktop shaders, retargeted to OpenGL ES 3.0.
///
/// The sources are shared, not copied — <see cref="Shaders"/> is compiled into this project too, so
/// the shading arithmetic that was measured to the byte on the desktop is the same arithmetic on a
/// phone. GLSL ES differs from desktop GLSL in exactly two ways that matter to these shaders: the
/// version directive, and the fact that a fragment shader has no default float precision. Both are
/// handled here, so the shader bodies never have to be written twice.
/// </summary>
public static class EsShaders
{
    private const string DesktopDirective = "#version 330 core";

    /// <summary>
    /// highp rather than mediump. The desktop shading was verified against exact byte values and
    /// mediump is free to round differently; ES 3.0 guarantees highp in fragment shaders, so
    /// asking for it costs nothing on any device that can run this at all.
    /// </summary>
    private const string EsDirective = "#version 300 es\nprecision highp float;\nprecision highp int;";

    public static string VoxelVertex { get; } = Translate(Shaders.VoxelVertex);

    public static string VoxelFragment { get; } = Translate(Shaders.VoxelFragment);

    public static string BackgroundVertex { get; } = Translate(Shaders.BackgroundVertex);

    public static string BackgroundFragment { get; } = Translate(Shaders.BackgroundFragment);

    public static string ReferenceVertex { get; } = Translate(Shaders.ReferenceVertex);

    public static string ReferenceFragment { get; } = Translate(Shaders.ReferenceFragment);

    public static string LineVertex { get; } = Translate(Shaders.LineVertex);

    public static string LineFragment { get; } = Translate(Shaders.LineFragment);

    /// <summary>
    /// Swaps the version directive for the ES one. The directive has to be the first thing in a
    /// shader, so this insists on finding it at the very start rather than anywhere in the text —
    /// a shader that grew a leading comment would otherwise be translated into something that
    /// cannot compile, and the failure would surface on a device instead of here.
    /// </summary>
    public static string Translate(string desktopSource)
    {
        ArgumentNullException.ThrowIfNull(desktopSource);

        if (!desktopSource.StartsWith(DesktopDirective, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A shader must begin with '{DesktopDirective}' to be translated.",
                nameof(desktopSource));
        }

        return EsDirective + desktopSource[DesktopDirective.Length..];
    }
}
