using EditorApp.Mobile.Rendering;
using EditorApp.Rendering;

namespace EditorApp.Tests.Mobile;

/// <summary>
/// The desktop shaders are shared with the Android head and retargeted on the way. These tests are
/// the only place that retargeting can be checked without a phone in hand.
/// </summary>
public class EsShaderTests
{
    public static TheoryData<string, string> AllShaders() => new()
    {
        { nameof(Shaders.VoxelVertex), Shaders.VoxelVertex },
        { nameof(Shaders.VoxelFragment), Shaders.VoxelFragment },
        { nameof(Shaders.BackgroundVertex), Shaders.BackgroundVertex },
        { nameof(Shaders.BackgroundFragment), Shaders.BackgroundFragment },
        { nameof(Shaders.ReferenceVertex), Shaders.ReferenceVertex },
        { nameof(Shaders.ReferenceFragment), Shaders.ReferenceFragment },
        { nameof(Shaders.LineVertex), Shaders.LineVertex },
        { nameof(Shaders.LineFragment), Shaders.LineFragment },
    };

    /// <summary>
    /// The directive has to be the very first thing in the file — a driver rejects a shader whose
    /// #version is preceded by anything but comments and whitespace.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllShaders))]
    public void TranslatedShaderStartsWithTheEsDirective(string name, string source)
    {
        string translated = EsShaders.Translate(source);

        Assert.True(
            translated.StartsWith("#version 300 es\n", StringComparison.Ordinal),
            $"{name} does not open with the ES directive. It opens with: {First(translated)}");
        Assert.True(
            !translated.Contains("#version 330", StringComparison.Ordinal),
            $"{name} still carries a desktop version directive.");
    }

    /// <summary>
    /// GLSL ES gives a fragment shader no default float precision, so one has to be declared or the
    /// shader will not compile. Vertex shaders default to highp and would be fine either way; both
    /// get the same prologue so there is only one thing to be right about.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllShaders))]
    public void TranslatedShaderDeclaresFloatPrecision(string name, string source)
    {
        Assert.True(
            EsShaders.Translate(source).Contains("precision highp float;", StringComparison.Ordinal),
            $"{name} declares no float precision, which a fragment shader cannot compile without.");
    }

    /// <summary>
    /// Everything after the directive is the shading itself, and it was measured against exact byte
    /// values on the desktop. Translation must not touch a character of it.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllShaders))]
    public void TranslationLeavesTheShaderBodyUntouched(string name, string source)
    {
        string desktopBody = source["#version 330 core".Length..];
        string translated = EsShaders.Translate(source);

        Assert.True(
            translated.EndsWith(desktopBody, StringComparison.Ordinal),
            $"{name} was altered below the version directive.");
    }

    private static string First(string source) => source[..Math.Min(40, source.Length)];

    [Fact]
    public void ShaderWithoutTheExpectedDirectiveIsRejected()
    {
        Assert.Throws<ArgumentException>(() => EsShaders.Translate("#version 450 core\nvoid main() {}"));
    }

    /// <summary>
    /// A leading comment would push the directive off the front, and a translation that searched for
    /// it anywhere would happily produce a shader with the ES prologue in the middle of the file.
    /// </summary>
    [Fact]
    public void ShaderWithTheDirectiveSomewhereOtherThanTheStartIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => EsShaders.Translate("// a note\n#version 330 core\nvoid main() {}"));
    }
}
