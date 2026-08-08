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

        // Each object carries its own place in the world; the voxel grid itself is always axis
        // aligned in its own space.
        uniform mat4 uModel;

        // 0 for a background object, 1 for the one being edited. Eased on the CPU so focus moves
        // as a fade rather than a jump.
        uniform float uFocus;

        out vec4 vColor;

        void main()
        {
            vec3 lit = aColor.rgb * aShade;

            // Lift towards white rather than scaling: multiplying leaves an already-white model
            // exactly as it was, which is the one case that has to read as focused.
            lit = mix(lit * 0.82, mix(lit, vec3(1.0), 0.10), uFocus);

            vColor = vec4(lit, aColor.a);
            gl_Position = uViewProjection * uModel * vec4(aPosition, 1.0);
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
