namespace EditorApp.Rendering;

/// <summary>
/// The path tracer as a compute shader (Fullreleaseplan 5.1, the GPU engine): the CPU's
/// <see cref="EditorApp.Core.Rendering.PathTracer"/> line for line — the same sky, lights, glass,
/// metal and roulette, the same hash and xorshift for its randomness — over the scene
/// <see cref="EditorApp.Core.Rendering.PackedScene"/> lays out. Each dispatch adds one sample to a
/// band of rows of the sum; the picture is developed from the sum on the CPU, as the CPU's is.
/// </summary>
public static class PathTraceShader
{
    public const string Compute = """
        #version 430
        layout(local_size_x = 8, local_size_y = 8) in;

        layout(rgba32f, binding = 0) uniform image2D uSum;

        struct Model
        {
            vec4 rotation;
            vec4 positionSize;
            vec4 worldMin;
            vec4 worldMax;
            ivec4 chunkMin;
            ivec4 chunkSpan;
        };

        struct Light
        {
            vec4 positionKind;
            vec4 directionRange;
            vec4 colourOuter;
            vec4 innerRest;
        };

        layout(std430, binding = 1) readonly buffer ModelBuffer { Model models[]; };
        layout(std430, binding = 2) readonly buffer TableBuffer { int chunkTable[]; };
        layout(std430, binding = 3) readonly buffer VoxelBuffer { uint voxels[]; };
        layout(std430, binding = 4) readonly buffer ChunkFaceBuffer { uint chunkFaces[]; };
        layout(std430, binding = 5) readonly buffer FaceBuffer { uint faces[]; };
        layout(std430, binding = 6) readonly buffer LightBuffer { Light lights[]; };
        layout(std430, binding = 7) readonly buffer PaletteBuffer { vec4 palette[]; };

        uniform int uModelCount;
        uniform int uLightCount;
        uniform ivec2 uSize;
        uniform int uRowStart;
        uniform int uRowEnd;
        uniform int uSampleIndex;
        uniform int uSeed;
        uniform int uBounces;

        uniform vec3 uCameraPosition;
        uniform vec3 uCameraForward;
        uniform vec3 uCameraRight;
        uniform vec3 uCameraUp;
        uniform float uTanHalfFov;
        uniform int uOrthographic;
        uniform float uOrthographicHalf;
        uniform float uAperture;
        uniform float uFocus;

        uniform vec3 uSkyTop;
        uniform vec3 uSkyHorizon;
        uniform float uSkyScale;
        uniform float uFog;
        uniform float uEmissionStrength;
        uniform int uTransparentBackground;
        uniform int uColourBackground;
        uniform vec3 uBackdrop;
        uniform float uSunSpread;

        const float Nudge = 1e-3;
        const float Tau = 6.28318530718;
        const float Far = 3.0e38;
        const int ChunkWords = 8192;

        uint state;

        uint mixBits(uint h)
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }

        float rnd()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return float(state >> 8) * (1.0 / 16777216.0);
        }

        vec3 rotateBy(vec4 q, vec3 v)
        {
            vec3 t = 2.0 * cross(q.xyz, v);
            return v + (q.w * t) + cross(q.xyz, t);
        }

        vec3 faceNormal(int face)
        {
            if (face == 0) return vec3(1.0, 0.0, 0.0);
            if (face == 1) return vec3(-1.0, 0.0, 0.0);
            if (face == 2) return vec3(0.0, 1.0, 0.0);
            if (face == 3) return vec3(0.0, -1.0, 0.0);
            if (face == 4) return vec3(0.0, 0.0, 1.0);
            return vec3(0.0, 0.0, -1.0);
        }

        void basis(vec3 n, out vec3 tangent, out vec3 bitangent)
        {
            vec3 helper = abs(n.x) > 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
            tangent = normalize(cross(helper, n));
            bitangent = cross(n, tangent);
        }

        vec2 disc()
        {
            float radius = sqrt(rnd());
            float angle = Tau * rnd();
            return vec2(radius * cos(angle), radius * sin(angle));
        }

        vec3 cosineHemisphere(vec3 n)
        {
            float u = rnd();
            float v = rnd();
            float radius = sqrt(u);
            float angle = Tau * v;
            vec3 tangent;
            vec3 bitangent;
            basis(n, tangent, bitangent);
            return normalize((tangent * (radius * cos(angle))) + (bitangent * (radius * sin(angle))) + (n * sqrt(max(0.0, 1.0 - u))));
        }

        vec3 glossy(vec3 axis, float roughness)
        {
            if (roughness <= 0.001)
            {
                return axis;
            }

            float exponent = 2.0 / max(roughness * roughness * roughness * roughness, 1e-4);
            float cosine = pow(rnd(), 1.0 / (exponent + 1.0));
            float sine = sqrt(max(0.0, 1.0 - (cosine * cosine)));
            float angle = Tau * rnd();
            vec3 tangent;
            vec3 bitangent;
            basis(axis, tangent, bitangent);
            return normalize((tangent * (sine * cos(angle))) + (bitangent * (sine * sin(angle))) + (axis * cosine));
        }

        float schlick(float f0, float cosine)
        {
            return f0 + ((1.0 - f0) * pow(1.0 - clamp(cosine, 0.0, 1.0), 5.0));
        }

        vec3 sky(vec3 direction)
        {
            vec3 colour = direction.y >= 0.0
                ? mix(uSkyHorizon, uSkyTop, sqrt(direction.y))
                : uSkyHorizon * (0.55 + (0.45 * (1.0 + direction.y)));
            return colour * uSkyScale;
        }

        // ---- The scene ----

        int chunkAt(int m, ivec3 cell)
        {
            ivec3 k = (cell >> 5) - models[m].chunkMin.xyz;
            ivec3 span = models[m].chunkSpan.xyz;
            if (any(lessThan(k, ivec3(0))) || any(greaterThanEqual(k, span)))
            {
                return -1;
            }

            return chunkTable[models[m].chunkMin.w + (((k.z * span.y) + k.y) * span.x) + k.x];
        }

        int linearOf(ivec3 cell)
        {
            ivec3 l = cell & 31;
            return (l.y << 10) | (l.z << 5) | l.x;
        }

        uint voxelIn(int chunk, int linear)
        {
            return (voxels[(chunk * ChunkWords) + (linear >> 2)] >> uint((linear & 3) * 8)) & 0xFFu;
        }

        uint faceColour(int chunk, int linear, int face, uint voxel)
        {
            int lo = int(chunkFaces[chunk * 2]);
            int hi = lo + int(chunkFaces[(chunk * 2) + 1]) - 1;
            uint key = (uint(linear) << 3) | uint(face);
            while (lo <= hi)
            {
                int middle = (lo + hi) >> 1;
                uint found = faces[middle] >> 8;
                if (found == key)
                {
                    return faces[middle] & 0xFFu;
                }

                if (found < key)
                {
                    lo = middle + 1;
                }
                else
                {
                    hi = middle - 1;
                }
            }

            return voxel;
        }

        bool slab(vec3 origin, vec3 direction, vec3 boxMin, vec3 boxMax, out float tNear, out float tFar)
        {
            tNear = 0.0;
            tFar = Far;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = origin[axis];
                float d = direction[axis];
                if (abs(d) < 1e-9)
                {
                    if (o < boxMin[axis] || o > boxMax[axis])
                    {
                        return false;
                    }

                    continue;
                }

                float t1 = (boxMin[axis] - o) / d;
                float t2 = (boxMax[axis] - o) / d;
                tNear = max(tNear, min(t1, t2));
                tFar = min(tFar, max(t1, t2));
                if (tNear > tFar)
                {
                    return false;
                }
            }

            return true;
        }

        // Amanatides-Woo through one object's lattice: the first solid cell, and the face it was entered by.
        bool walk(int m, vec3 origin, vec3 direction, float start, float end, uint skip, out ivec3 cell, out int face, out float hitT, out int chunkHit)
        {
            cell = ivec3(0);
            face = 0;
            hitT = 0.0;
            chunkHit = -1;

            float t = max(start - 1e-3, 0.0);
            vec3 p = origin + (direction * t);
            ivec3 c = ivec3(floor(p));
            ivec3 stepDir = ivec3(sign(direction));
            vec3 nextT = vec3(Far);
            vec3 delta = vec3(Far);
            for (int axis = 0; axis < 3; axis++)
            {
                if (stepDir[axis] != 0)
                {
                    float boundary = float(stepDir[axis] > 0 ? c[axis] + 1 : c[axis]);
                    nextT[axis] = ((boundary - p[axis]) / direction[axis]) + t;
                    delta[axis] = abs(1.0 / direction[axis]);
                }
            }

            for (int guard = 0; guard < 100000; guard++)
            {
                if (nextT.x <= nextT.y && nextT.x <= nextT.z)
                {
                    c.x += stepDir.x;
                    t = nextT.x;
                    nextT.x += delta.x;
                    face = stepDir.x > 0 ? 1 : 0;
                }
                else if (nextT.y <= nextT.z)
                {
                    c.y += stepDir.y;
                    t = nextT.y;
                    nextT.y += delta.y;
                    face = stepDir.y > 0 ? 3 : 2;
                }
                else
                {
                    c.z += stepDir.z;
                    t = nextT.z;
                    nextT.z += delta.z;
                    face = stepDir.z > 0 ? 5 : 4;
                }

                if (t > end || t >= Far)
                {
                    return false;
                }

                int chunk = chunkAt(m, c);
                if (chunk < 0)
                {
                    continue;
                }

                uint value = voxelIn(chunk, linearOf(c));
                if (value != 0u && value != skip)
                {
                    cell = c;
                    hitT = t;
                    chunkHit = chunk;
                    return true;
                }
            }

            return false;
        }

        struct Hit
        {
            int object;
            float travelled;
            vec3 point;
            vec3 normal;
            uint index;
            uint voxel;
        };

        bool intersect(vec3 origin, vec3 direction, float maxDistance, int glassObject, uint glassIndex, out Hit hit)
        {
            hit = Hit(-1, 0.0, vec3(0.0), vec3(0.0), 0u, 0u);
            float nearest = maxDistance;
            bool found = false;

            for (int i = 0; i < uModelCount; i++)
            {
                float tNear;
                float tFar;
                if (!slab(origin, direction, models[i].worldMin.xyz - vec3(Nudge), models[i].worldMax.xyz + vec3(Nudge), tNear, tFar) || tNear >= nearest)
                {
                    continue;
                }

                vec4 unturn = vec4(-models[i].rotation.xyz, models[i].rotation.w);
                float size = models[i].positionSize.w;
                vec3 localOrigin = rotateBy(unturn, origin - models[i].positionSize.xyz) / size;
                vec3 localDirection = rotateBy(unturn, direction);
                uint skip = i == glassObject ? glassIndex : 0u;

                ivec3 cell;
                int face;
                float local;
                int chunk;
                if (walk(i, localOrigin, localDirection, max(tNear, 0.0) / size, min(tFar, nearest) / size, skip, cell, face, local, chunk))
                {
                    float along = local * size;
                    if (along < nearest)
                    {
                        nearest = along;
                        int linear = linearOf(cell);
                        uint voxel = voxelIn(chunk, linear);
                        hit.object = i;
                        hit.travelled = along;
                        hit.point = origin + (direction * along);
                        hit.normal = normalize(rotateBy(models[i].rotation, faceNormal(face)));
                        hit.voxel = voxel;
                        hit.index = faceColour(chunk, linear, face, voxel);
                        found = true;
                    }
                }
            }

            return found;
        }

        // ---- Light ----

        vec3 transmission(vec3 point, vec3 towards, float remaining)
        {
            vec3 passed = vec3(1.0);
            vec3 origin = point;
            int glassObject = -1;
            uint glassIndex = 0u;

            for (int crossings = 0; crossings < 8; crossings++)
            {
                Hit hit;
                if (!intersect(origin, towards, remaining, glassObject, glassIndex, hit))
                {
                    return passed;
                }

                vec4 material = palette[(hit.index * 2u) + 1u];
                if (material.w <= 0.001)
                {
                    return vec3(0.0);
                }

                passed *= palette[hit.index * 2u].rgb * material.w;
                glassObject = hit.object;
                glassIndex = hit.voxel;
                remaining -= hit.travelled;
                origin = hit.point + (towards * Nudge);
            }

            return vec3(0.0);
        }

        vec3 directLight(vec3 point, vec3 normal, vec3 incoming, vec3 albedo, vec4 material)
        {
            vec3 total = vec3(0.0);
            vec3 reflected = reflect(incoming, normal);
            vec3 highlightColour = mix(vec3(0.04), albedo, material.y);
            float power = 4.0 + (252.0 * material.z * material.z);

            for (int i = 0; i < uLightCount; i++)
            {
                int kind = int(lights[i].positionKind.w + 0.5);
                vec3 towards;
                float strength = 1.0;
                float lightDistance = Far;

                if (kind == 0)
                {
                    towards = -lights[i].directionRange.xyz;
                    if (uSunSpread > 0.0)
                    {
                        vec2 onDisc = disc() * uSunSpread;
                        vec3 tangent;
                        vec3 bitangent;
                        basis(towards, tangent, bitangent);
                        towards = normalize(towards + (tangent * onDisc.x) + (bitangent * onDisc.y));
                    }
                }
                else
                {
                    vec3 offset = lights[i].positionKind.xyz - point;
                    lightDistance = length(offset);
                    towards = offset / max(lightDistance, 1e-4);

                    float range = lights[i].directionRange.w;
                    float reach = clamp(1.0 - ((lightDistance * lightDistance) / (range * range)), 0.0, 1.0);
                    strength = reach * reach;

                    if (kind == 2)
                    {
                        strength *= smoothstep(lights[i].colourOuter.w, lights[i].innerRest.x, dot(-towards, lights[i].directionRange.xyz));
                    }
                }

                float cosine = dot(normal, towards);
                if (cosine <= 0.0 || strength <= 0.0)
                {
                    continue;
                }

                vec3 passed = transmission(point, towards, lightDistance);
                if (passed.x <= 0.0 && passed.y <= 0.0 && passed.z <= 0.0)
                {
                    continue;
                }

                vec3 diffuse = albedo * ((1.0 - material.y) * cosine);
                vec3 shine = highlightColour * (pow(max(dot(reflected, towards), 0.0), power) * (0.25 + material.z));
                total += lights[i].colourOuter.rgb * strength * passed * (diffuse + shine);
            }

            return total;
        }

        vec4 trace(vec3 origin, vec3 direction)
        {
            vec3 radiance = vec3(0.0);
            vec3 throughput = vec3(1.0);
            float alpha = 1.0;
            int glassObject = -1;
            uint glassIndex = 0u;
            bool direct = true;

            for (int bounce = 0; bounce <= uBounces; bounce++)
            {
                Hit hit;
                if (!intersect(origin, direction, Far, glassObject, glassIndex, hit))
                {
                    if (bounce == 0 && uTransparentBackground != 0)
                    {
                        alpha = 0.0;
                        break;
                    }

                    radiance += throughput * (direct && uColourBackground != 0 ? uBackdrop : sky(direction));
                    break;
                }

                if (uFog > 0.0)
                {
                    float kept = exp(-hit.travelled * uFog * 0.05);
                    radiance += throughput * (1.0 - kept) * uSkyHorizon * uSkyScale;
                    throughput *= kept;
                }

                vec3 albedo = palette[hit.index * 2u].rgb;
                vec4 material = palette[(hit.index * 2u) + 1u];
                radiance += throughput * albedo * (material.x * uEmissionStrength);

                float facing = max(-dot(direction, hit.normal), 0.0);

                if (material.w > 0.001)
                {
                    if (rnd() < schlick(0.04, facing))
                    {
                        origin = hit.point + (hit.normal * Nudge);
                        direction = reflect(direction, hit.normal);
                        direct = false;
                        continue;
                    }

                    if (rnd() < material.w)
                    {
                        throughput *= albedo;
                        glassObject = hit.object;
                        glassIndex = hit.voxel;
                        origin = hit.point + (direction * Nudge);
                        continue;
                    }
                }

                glassObject = -1;
                glassIndex = 0u;
                direct = false;

                vec3 surface = hit.point + (hit.normal * Nudge);
                radiance += throughput * directLight(surface, hit.normal, direction, albedo, material);

                float specular = material.y + ((1.0 - material.y) * material.z * schlick(0.04, facing));
                if (rnd() < specular)
                {
                    vec3 mirrored = reflect(direction, hit.normal);
                    vec3 spread = glossy(mirrored, 1.0 - material.z);
                    if (dot(spread, hit.normal) <= 0.0)
                    {
                        spread = mirrored;
                    }

                    throughput *= mix(vec3(1.0), albedo, material.y);
                    origin = surface;
                    direction = spread;
                }
                else
                {
                    throughput *= albedo;
                    origin = surface;
                    direction = cosineHemisphere(hit.normal);
                }

                if (bounce >= 3)
                {
                    float keep = clamp(max(throughput.x, max(throughput.y, throughput.z)), 0.05, 0.95);
                    if (rnd() > keep)
                    {
                        break;
                    }

                    throughput /= keep;
                }
            }

            return vec4(radiance, alpha);
        }

        void main()
        {
            ivec2 pixel = ivec2(gl_GlobalInvocationID.xy) + ivec2(0, uRowStart);
            if (pixel.x >= uSize.x || pixel.y >= uRowEnd)
            {
                return;
            }

            uint h = uint(uSeed) * 0x9E3779B9u;
            h = mixBits(h ^ uint(pixel.x));
            h = mixBits(h ^ (uint(pixel.y) * 0x85EBCA6Bu));
            h = mixBits(h ^ (uint(uSampleIndex) * 0xC2B2AE35u));
            state = h == 0u ? 1u : h;

            float aspect = float(uSize.x) / float(uSize.y);
            float u = (((float(pixel.x) + rnd()) / float(uSize.x)) * 2.0) - 1.0;
            float v = 1.0 - (((float(pixel.y) + rnd()) / float(uSize.y)) * 2.0);

            vec3 origin;
            vec3 direction;
            if (uOrthographic != 0)
            {
                origin = uCameraPosition + (uCameraRight * (u * uOrthographicHalf * aspect)) + (uCameraUp * (v * uOrthographicHalf));
                direction = uCameraForward;
            }
            else
            {
                origin = uCameraPosition;
                direction = normalize(uCameraForward + (uCameraRight * (u * uTanHalfFov * aspect)) + (uCameraUp * (v * uTanHalfFov)));
                if (uAperture > 0.0)
                {
                    vec2 lens = disc();
                    float along = uFocus / max(dot(direction, uCameraForward), 1e-3);
                    vec3 focal = origin + (direction * along);
                    origin = uCameraPosition + (uCameraRight * (lens.x * uAperture * 0.5)) + (uCameraUp * (lens.y * uAperture * 0.5));
                    direction = normalize(focal - origin);
                }
            }

            vec4 result = trace(origin, direction);
            if (any(isnan(result)) || any(isinf(result)))
            {
                result = vec4(0.0, 0.0, 0.0, result.w == result.w ? result.w : 1.0);
            }

            vec4 sum = uSampleIndex == 0 ? vec4(0.0) : imageLoad(uSum, pixel);
            imageStore(uSum, pixel, sum + result);
        }
        """;
}
