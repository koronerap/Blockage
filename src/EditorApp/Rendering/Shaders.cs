namespace EditorApp.Rendering;

/// <summary>
/// GLSL sources. One shader draws voxels (EditorApp.md §5: vertex color times a constant per-normal
/// shade, no lighting math), one draws overlay lines for the hovered face and the ground grid.
/// </summary>
public static class Shaders
{
    public const string VoxelVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;
        layout(location = 2) in float aShade;

        uniform mat4 uViewProjection;

        out vec4 vColor;

        void main()
        {
            vColor = vec4(aColor.rgb * aShade, aColor.a);
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    public const string VoxelFragment = """
        #version 330 core
        in vec4 vColor;
        out vec4 fragColor;

        void main()
        {
            fragColor = vColor;
        }
        """;

    /// <summary>
    /// Reference models are drawn translucent with a soft two-sided lambert term, so a guide reads
    /// as a shape without ever being mistaken for level geometry.
    /// </summary>
    public const string ReferenceVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;

        out vec3 vNormal;

        void main()
        {
            vNormal = mat3(uModel) * aNormal;
            gl_Position = uViewProjection * uModel * vec4(aPosition, 1.0);
        }
        """;

    public const string ReferenceFragment = """
        #version 330 core
        in vec3 vNormal;

        uniform vec4 uColor;

        out vec4 fragColor;

        void main()
        {
            vec3 normal = normalize(vNormal);
            float lambert = abs(dot(normal, normalize(vec3(0.4, 0.9, 0.3))));
            fragColor = vec4(uColor.rgb * (0.55 + 0.45 * lambert), uColor.a);
        }
        """;

    public const string LineVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;

        uniform mat4 uViewProjection;

        out vec4 vColor;

        void main()
        {
            vColor = aColor;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    public const string LineFragment = """
        #version 330 core
        in vec4 vColor;
        out vec4 fragColor;

        void main()
        {
            fragColor = vColor;
        }
        """;
}
