namespace EditorApp.Rendering;

/// <summary>
/// GLSL sources. One shader draws voxels, one draws overlay lines for the hovered face and the
/// ground grid.
///
/// The voxel shader has two shading modes. Unlit is EditorApp.md §5 unchanged — vertex colour times
/// a constant per-normal shade, no lighting math. Lit uses the level's own lights, so the way a
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

        // Each object carries its own place in the world and its own voxel size; the voxel grid
        // itself is always axis aligned in its own space.
        uniform mat4 uModel;

        // Per-face lookup tables, uploaded once from FaceInfo rather than written out again here,
        // so there is only one place the constants can be wrong.
        uniform vec3 uFaceNormal[6];
        uniform float uFaceShade[6];

        out vec4 vColor;
        out vec3 vNormal;
        out vec3 vWorldPosition;

        // The unlit mode's flat shade for this face, carried along so the fragment stage can choose.
        out float vFaceShade;

        // Where on the object's own lattice this point is, and which way its face points there: the
        // wireframe is drawn from these, a line wherever a coordinate across the face is whole.
        out vec3 vLocal;
        out vec3 vLocalNormal;

        void main()
        {
            int face = int(aFace + 0.5);
            vec4 world = uModel * vec4(aPosition, 1.0);

            // Rotated into the world, or turning an object would leave its shading behind. The voxel
            // size is a uniform scale, which normalising takes straight back out.
            vNormal = normalize(mat3(uModel) * uFaceNormal[face]);
            vWorldPosition = world.xyz;
            vFaceShade = uFaceShade[face];
            vColor = aColor;
            vLocal = aPosition;
            vLocalNormal = uFaceNormal[face];

            gl_Position = uViewProjection * world;
        }
        """;

    /// <summary>
    /// The level's lights, worked out per pixel: a point or spot light's reach and cone can end
    /// halfway across a face, which a per-corner value would smear into a gradient that is not there.
    /// Every kind is the same formula — see <see cref="LightUniforms"/>.
    /// </summary>
    public static readonly string VoxelFragment = $$"""
        #version 330 core
        in vec4 vColor;
        in vec3 vNormal;
        in vec3 vWorldPosition;
        in float vFaceShade;
        in vec3 vLocal;
        in vec3 vLocalNormal;

        // 0 = lit, 1 = unlit (the per-face shade), 2 = flat (no shade at all).
        uniform int uUnlit;

        // Where a face's colour comes from: 0 its palette colour, 1 one colour for everything,
        // 2 a colour for the object. Every one of these is left at 0 by a head that never sets it.
        uniform int uColorMode;
        uniform vec3 uSingleColor;
        uniform vec3 uObjectColor;

        // The voxel lattice over the faces: how strongly (0, not at all), and whether the lines
        // are all that is drawn - the Wireframe shading.
        uniform float uWire;
        uniform int uWireOnly;

        // X-Ray: the alpha every face is drawn with. 0 is off, solid as ever.
        uniform float uXRay;
        uniform float uAmbient;

        uniform int uLightCount;
        uniform vec4 uLightPosition[{{LightUniforms.MaxLights}}];
        uniform vec3 uLightDirection[{{LightUniforms.MaxLights}}];
        uniform vec3 uLightColor[{{LightUniforms.MaxLights}}];
        uniform vec4 uLightShape[{{LightUniforms.MaxLights}}];

        // 0 for a background object, 1 for the one being edited. Eased on the CPU so focus moves
        // as a fade rather than a jump.
        uniform float uFocus;

        // How much of the focus effect to apply at all. Neither end of uFocus is neutral - one
        // dims and the other lifts - so turning the highlight off needs its own way of saying so
        // rather than a value of uFocus that happens to mean nothing.
        uniform float uFocusStrength;

        out vec4 fragColor;

        vec3 lightArriving(vec3 normal)
        {
            vec3 total = vec3(uAmbient);

            for (int i = 0; i < uLightCount; i++)
            {
                vec3 towards;
                float strength = 1.0;

                if (uLightPosition[i].w == 0.0)
                {
                    towards = uLightPosition[i].xyz;
                }
                else
                {
                    vec3 offset = uLightPosition[i].xyz - vWorldPosition;
                    float dist = length(offset);
                    towards = offset / max(dist, 0.0001);

                    // Full strength at the light, nothing at its range, and no hard ring between.
                    float range = uLightShape[i].x;
                    float reach = clamp(1.0 - (dist * dist) / (range * range), 0.0, 1.0);
                    strength = reach * reach;

                    strength *= smoothstep(uLightShape[i].y, uLightShape[i].z, dot(-towards, uLightDirection[i]));
                }

                total += uLightColor[i] * (max(dot(normal, towards), 0.0) * strength);
            }

            return total;
        }

        /// How much of a line of the lattice this pixel is: 1 on one, 0 a pixel and a half away.
        float latticeLine()
        {
            // Distance to the nearest whole coordinate, in pixels, along each axis across the face.
            vec3 away = abs(fract(vLocal + 0.5) - 0.5) / max(fwidth(vLocal), vec3(0.0001));

            // The axis the face points along runs through it, not across it: it makes no line.
            away += abs(vLocalNormal) * 1.0e6;
            return 1.0 - smoothstep(0.6, 1.4, min(away.x, min(away.y, away.z)));
        }

        void main()
        {
            vec3 albedo = uColorMode == 1 ? uSingleColor : uColorMode == 2 ? uObjectColor : vColor.rgb;
            vec3 light = uUnlit == 2 ? vec3(1.0) : uUnlit != 0 ? vec3(vFaceShade) : lightArriving(normalize(vNormal));
            vec3 lit = albedo * light;

            // Lift towards white rather than scaling: multiplying leaves an already-white model
            // exactly as it was, which is the one case that has to read as focused.
            vec3 highlighted = mix(lit * 0.82, mix(lit, vec3(1.0), 0.10), uFocus);
            lit = mix(lit, highlighted, uFocusStrength);

            float alpha = uXRay > 0.0 ? uXRay : vColor.a;

            if (uWireOnly != 0)
            {
                // Lines only, in the face's own colour lifted a little, so a model reads in its
                // colours even as a wireframe.
                float line = latticeLine();
                if (line < 0.02)
                {
                    discard;
                }

                fragColor = vec4(clamp(mix(lit, vec3(1.0), 0.2), 0.0, 1.0), line * max(alpha, 0.6));
                return;
            }

            if (uWire > 0.0)
            {
                lit = mix(lit, lit * 0.22, latticeLine() * uWire);
            }

            fragColor = vec4(clamp(lit, 0.0, 1.0), alpha);
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
