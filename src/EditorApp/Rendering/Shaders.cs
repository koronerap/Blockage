namespace EditorApp.Rendering;

/// <summary>
/// GLSL sources. One shader draws voxels, one draws overlay lines for the hovered face and the
/// ground grid.
///
/// The voxel shader has two shading modes. Unlit is EditorApp.md §5 unchanged — vertex colour times
/// a constant per-normal shade, no lighting math. Lit adds one fixed directional light so the way a
/// level catches light can be judged in the editor. Neither reaches the exported file, which stays
/// flat by design (§10.4).
/// </summary>
public static class Shaders
{
    public const string VoxelVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;
        layout(location = 2) in float aFace;

        uniform mat4 uViewProjection;

        // Each object carries its own place in the world; the voxel grid itself is always axis
        // aligned in its own space.
        uniform mat4 uModel;

        // 0 for a background object, 1 for the one being edited. Eased on the CPU so focus moves
        // as a fade rather than a jump.
        uniform float uFocus;

        // How much of the focus effect to apply at all. Neither end of uFocus is neutral - one
        // dims and the other lifts - so turning the highlight off needs its own way of saying so
        // rather than a value of uFocus that happens to mean nothing.
        uniform float uFocusStrength;

        // Per-face lookup tables, uploaded once from FaceInfo rather than written out again here,
        // so there is only one place the constants can be wrong.
        uniform vec3 uFaceNormal[6];
        uniform float uFaceShade[6];

        // 0 = lit, 1 = unlit.
        uniform int uUnlit;

        // Points towards the light, world space, unit length.
        uniform vec3 uLightDirection;
        uniform float uLightIntensity;
        uniform float uAmbient;

        out vec4 vColor;

        void main()
        {
            int face = int(aFace + 0.5);
            float shade;

            if (uUnlit != 0)
            {
                shade = uFaceShade[face];
            }
            else
            {
                // Rotated into the world, or turning an object would leave its shading behind.
                vec3 normal = normalize(mat3(uModel) * uFaceNormal[face]);

                // Computed per vertex, which costs nothing and loses nothing: a face normal is
                // constant across a quad, so interpolating this gives the same value everywhere.
                shade = uAmbient + uLightIntensity * max(dot(normal, uLightDirection), 0.0);
            }

            vec3 lit = aColor.rgb * shade;

            // Lift towards white rather than scaling: multiplying leaves an already-white model
            // exactly as it was, which is the one case that has to read as focused.
            vec3 highlighted = mix(lit * 0.82, mix(lit, vec3(1.0), 0.10), uFocus);
            lit = mix(lit, highlighted, uFocusStrength);

            vColor = vec4(clamp(lit, 0.0, 1.0), aColor.a);
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
    /// A full-screen triangle built from <c>gl_VertexID</c> alone — no vertex buffer, no attributes,
    /// just three vertices covering the viewport. Used for the background gradient.
    /// </summary>
    public const string BackgroundVertex = """
        #version 330 core

        out vec2 vUv;

        void main()
        {
            vec2 corner = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = corner;
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    public const string BackgroundFragment = """
        #version 330 core
        in vec2 vUv;

        uniform vec3 uTop;
        uniform vec3 uBottom;

        out vec4 fragColor;

        void main()
        {
            // A little lighter towards the top. Enough to give the scene a horizon without the
            // background becoming something the eye looks at.
            fragColor = vec4(mix(uBottom, uTop, vUv.y), 1.0);
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
