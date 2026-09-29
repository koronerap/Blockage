using System.Reflection;
using System.Text.RegularExpressions;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The shaders are only compiled by a GPU driver, which the tests do not have — so a local named with
/// one of GLSL's reserved words, which compiles as C# and fails at start-up, is caught here instead.
/// Both "half" and "packed" got as far as a start-up crash before this was written.
/// </summary>
public partial class ShaderSourceTests
{
    private static readonly HashSet<string> Reserved =
    [
        "common", "partition", "active", "asm", "class", "union", "enum", "typedef", "template", "this",
        "packed", "resource", "goto", "inline", "noinline", "public", "static", "extern", "external",
        "interface", "long", "short", "double", "half", "fixed", "unsigned", "superp", "input", "output",
        "hvec2", "hvec3", "hvec4", "fvec2", "fvec3", "fvec4", "sampler3DRect", "filter", "image1D",
        "image2D", "image3D", "imageCube", "sizeof", "cast", "namespace", "using", "row_major", "sample",
    ];

    [GeneratedRegex(@"\b(?:int|float|bool|vec[234]|ivec[234]|mat[234])\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Declaration();

    public static TheoryData<string> Sources()
    {
        var data = new TheoryData<string>();
        foreach (FieldInfo field in typeof(Shaders).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(string))
            {
                data.Add(field.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void NoVariableIsNamedWithAReservedWord(string shader)
    {
        string source = (string)typeof(Shaders).GetField(shader, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        foreach (Match match in Declaration().Matches(source))
        {
            Assert.DoesNotContain(match.Groups[1].Value, Reserved);
        }
    }
}
