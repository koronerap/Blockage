using System.Numerics;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Rendering;

/// <summary>
/// The renderer (Fullreleaseplan 5.1): path tracing through the voxel lattice, on the CPU, every
/// core at once. Soft light from the sky, shadows from the sun and the lamps, light bounced off
/// walls, glowing colours, metal and glass — the level as it would look lit, not as the viewport
/// shades it to work in.
///
/// Progressive: each call adds one more sample to every pixel, so a picture shows at once and
/// clears as it goes. Deterministic: every pixel's randomness comes from the seed, its place and the
/// sample's number, so the same scene and settings give the same image, whichever thread drew what.
/// </summary>
public sealed class PathTracer
{
    /// <summary>How far a ray starts off a surface, in world units, so it does not find the surface again.</summary>
    private const float Nudge = 1e-3f;

    private readonly RenderScene _scene;
    private readonly RenderCamera _camera;
    private readonly RenderSettings _settings;
    private readonly Vector4[] _sum;
    private readonly Vector3 _skyTop;
    private readonly Vector3 _skyHorizon;
    private readonly float _skyScale;

    public PathTracer(RenderScene scene, RenderCamera camera, RenderSettings settings)
    {
        _scene = scene;
        _camera = camera;
        _settings = settings.Clamped();
        Width = _settings.Width;
        Height = _settings.Height;
        _sum = new Vector4[Width * Height];

        _skyTop = Linear(_settings.SkyTop);
        _skyHorizon = Linear(_settings.SkyHorizon);

        // A sky as bright as the level's ambient floor lights a face as the viewport's ambient does.
        _skyScale = _settings.SkyStrength * MathF.Max(scene.Ambient, 0.05f) * 2.5f;
    }

    public int Width { get; }

    public int Height { get; }

    public int SamplesDone { get; private set; }

    public bool IsFinished => SamplesDone >= _settings.Samples;

    /// <summary>One more sample for every pixel, the rows shared among the cores.</summary>
    public void AddSample(CancellationToken cancel = default)
    {
        int sample = SamplesDone;
        float aspect = Width / (float)Height;
        var options = new ParallelOptions { CancellationToken = cancel };

        Parallel.For(0, Height, options, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                var random = new Random32(Hash(_settings.Seed, x, y, sample));
                float u = (((x + random.Next()) / Width) * 2f) - 1f;
                float v = 1f - (((y + random.Next()) / Height) * 2f);
                (Vector3 colour, float alpha) = Trace(_camera.Through(u, v, aspect), ref random);
                _sum[(y * Width) + x] += new Vector4(colour, alpha);
            }
        });

        SamplesDone++;
    }

    /// <summary>The picture so far: tone-mapped, sRGB, row by row from the top, with alpha.</summary>
    public byte[] ToRgba()
    {
        var pixels = new byte[Width * Height * 4];
        float exposure = MathF.Pow(2f, _settings.Exposure);
        float samples = MathF.Max(SamplesDone, 1);

        for (int i = 0; i < _sum.Length; i++)
        {
            Vector4 mean = _sum[i] / samples;
            float alpha = Math.Clamp(mean.W, 0f, 1f);

            // What was drawn over a see-through background was drawn against nothing: straight alpha.
            Vector3 colour = alpha > 0f ? new Vector3(mean.X, mean.Y, mean.Z) / alpha : Vector3.Zero;
            colour = Aces(colour * exposure);

            pixels[(i * 4) + 0] = Srgb(colour.X);
            pixels[(i * 4) + 1] = Srgb(colour.Y);
            pixels[(i * 4) + 2] = Srgb(colour.Z);
            pixels[(i * 4) + 3] = (byte)MathF.Round(alpha * 255f);
        }

        return pixels;
    }

    // ---- One path ------------------------------------------------------------------------------

    private readonly record struct Hit(int Object, Int3 Cell, Face Face, float Distance, Vector3 Point, Vector3 Normal, byte Index, byte Voxel);

    private (Vector3 Radiance, float Alpha) Trace(Ray ray, ref Random32 random)
    {
        Vector3 radiance = Vector3.Zero;
        Vector3 throughput = Vector3.One;
        float alpha = 1f;

        // A glass volume being passed through: its own cells are seen through until the ray is out.
        int glassObject = -1;
        byte glassIndex = 0;

        for (int bounce = 0; bounce <= _settings.Bounces; bounce++)
        {
            if (!Intersect(ray, float.MaxValue, glassObject, glassIndex, out Hit hit))
            {
                if (bounce == 0 && _settings.TransparentBackground)
                {
                    alpha = 0f;
                    break;
                }

                radiance += throughput * Sky(ray.Direction);
                break;
            }

            if (_settings.Fog > 0f)
            {
                float kept = MathF.Exp(-hit.Distance * _settings.Fog * 0.05f);
                radiance += throughput * (1f - kept) * _skyHorizon * _skyScale;
                throughput *= kept;
            }

            Vector3 albedo = _scene.Colours[hit.Index];
            VoxelMaterial material = _scene.Materials[hit.Index];
            radiance += throughput * albedo * (material.Emission * _settings.EmissionStrength);

            float facing = MathF.Max(-Vector3.Dot(ray.Direction, hit.Normal), 0f);

            // Glass: a little reflected, the rest let through tinted by its colour, and the volume it
            // is part of seen through until the ray is out of it again.
            if (material.IsTransparent)
            {
                if (random.Next() < Schlick(0.04f, facing))
                {
                    ray = new Ray(hit.Point + (hit.Normal * Nudge), Vector3.Reflect(ray.Direction, hit.Normal));
                    continue;
                }

                if (random.Next() < material.Transparency)
                {
                    throughput *= albedo;
                    glassObject = hit.Object;
                    glassIndex = hit.Voxel;
                    ray = new Ray(hit.Point + (ray.Direction * Nudge), ray.Direction);
                    continue;
                }
            }

            glassObject = -1;
            glassIndex = 0;

            Vector3 origin = hit.Point + (hit.Normal * Nudge);
            radiance += throughput * DirectLight(origin, hit.Normal, ray.Direction, albedo, material, ref random);

            // Where the light came from: a reflection, blurred by roughness, or anywhere over the face.
            float specular = material.Metallic + ((1f - material.Metallic) * material.Smoothness * Schlick(0.04f, facing));
            if (random.Next() < specular)
            {
                Vector3 reflected = Vector3.Reflect(ray.Direction, hit.Normal);
                Vector3 direction = Glossy(reflected, 1f - material.Smoothness, ref random);
                if (Vector3.Dot(direction, hit.Normal) <= 0f)
                {
                    direction = reflected;
                }

                throughput *= Vector3.Lerp(Vector3.One, albedo, material.Metallic);
                ray = new Ray(origin, direction);
            }
            else
            {
                throughput *= albedo;
                ray = new Ray(origin, CosineHemisphere(hit.Normal, ref random));
            }

            // Russian roulette: a dim path is ended at random, the survivors made brighter to match.
            if (bounce >= 3)
            {
                float keep = Math.Clamp(MathF.Max(throughput.X, MathF.Max(throughput.Y, throughput.Z)), 0.05f, 0.95f);
                if (random.Next() > keep)
                {
                    break;
                }

                throughput /= keep;
            }
        }

        return (radiance, alpha);
    }

    /// <summary>
    /// Light arriving straight from the sun and the lamps, shadows and all: a diffuse share and a
    /// highlight, as the viewport's own lighting weighs them — colour times intensity is what a lamp
    /// puts on a face turned to it.
    /// </summary>
    private Vector3 DirectLight(Vector3 point, Vector3 normal, Vector3 incoming, Vector3 albedo, VoxelMaterial material, ref Random32 random)
    {
        Vector3 total = Vector3.Zero;
        Vector3 reflected = Vector3.Reflect(incoming, normal);
        Vector3 highlightColour = Vector3.Lerp(new Vector3(0.04f), albedo, material.Metallic);
        float power = 4f + (252f * material.Smoothness * material.Smoothness);

        foreach (RenderScene.Light light in _scene.Lights)
        {
            Vector3 towards;
            float strength = 1f;
            float distance = float.MaxValue;

            if (light.Kind == LightKind.Directional)
            {
                towards = -light.Direction;
            }
            else
            {
                Vector3 offset = light.Position - point;
                distance = offset.Length();
                towards = offset / MathF.Max(distance, 1e-4f);

                float reach = Math.Clamp(1f - ((distance * distance) / (light.Range * light.Range)), 0f, 1f);
                strength = reach * reach;

                if (light.Kind == LightKind.Spot)
                {
                    strength *= SmoothStep(light.ConeOuter, light.ConeInner, Vector3.Dot(-towards, light.Direction));
                }
            }

            float cosine = Vector3.Dot(normal, towards);
            if (cosine <= 0f || strength <= 0f)
            {
                continue;
            }

            Vector3 passed = Transmission(point, towards, distance, ref random);
            if (passed == Vector3.Zero)
            {
                continue;
            }

            Vector3 diffuse = albedo * ((1f - material.Metallic) * cosine);
            Vector3 shine = highlightColour * (MathF.Pow(MathF.Max(Vector3.Dot(reflected, towards), 0f), power) * (0.25f + material.Smoothness));
            total += light.Colour * strength * passed * (diffuse + shine);
        }

        return total;
    }

    /// <summary>How much light gets from a point to a light: none past anything solid, tinted through glass.</summary>
    private Vector3 Transmission(Vector3 point, Vector3 towards, float distance, ref Random32 random)
    {
        Vector3 passed = Vector3.One;
        var ray = new Ray(point, towards);
        int glassObject = -1;
        byte glassIndex = 0;

        for (int crossings = 0; crossings < 8; crossings++)
        {
            if (!Intersect(ray, distance, glassObject, glassIndex, out Hit hit))
            {
                return passed;
            }

            VoxelMaterial material = _scene.Materials[hit.Index];
            if (!material.IsTransparent)
            {
                return Vector3.Zero;
            }

            passed *= _scene.Colours[hit.Index] * material.Transparency;
            glassObject = hit.Object;
            glassIndex = hit.Voxel;
            distance -= hit.Distance;
            ray = new Ray(hit.Point + (towards * Nudge), towards);
        }

        return Vector3.Zero;
    }

    // ---- Finding what a ray hits ---------------------------------------------------------------

    private bool Intersect(Ray ray, float maxDistance, int glassObject, byte glassIndex, out Hit hit)
    {
        hit = default;
        float nearest = maxDistance;
        bool found = false;

        for (int i = 0; i < _scene.Objects.Length; i++)
        {
            RenderScene.Model o = _scene.Objects[i];
            if (!SlabHit(ray, o.WorldMin - new Vector3(Nudge), o.WorldMax + new Vector3(Nudge), out float near, out float far) || near >= nearest)
            {
                continue;
            }

            float size = o.Transform.VoxelSize;
            Vector3 origin = o.Transform.InverseTransformPoint(ray.Origin);
            Vector3 direction = o.Transform.InverseTransformDirection(ray.Direction);
            byte skip = i == glassObject ? glassIndex : Palette.EmptyIndex;

            if (Walk(o.Grid, origin, direction, MathF.Max(near, 0f) / size, MathF.Min(far, nearest) / size, skip, out Int3 cell, out Face face, out float local))
            {
                float distance = local * size;
                if (distance < nearest)
                {
                    nearest = distance;
                    Vector3 point = ray.Origin + (ray.Direction * distance);
                    Vector3 normal = Vector3.Normalize(o.Transform.TransformDirection(FaceInfo.Normal(face)));
                    hit = new Hit(i, cell, face, distance, point, normal, o.Grid.GetFaceColor(cell, face), o.Grid.GetVoxel(cell));
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Amanatides–Woo through one object's lattice, from <paramref name="start"/> to
    /// <paramref name="end"/> along the ray (in its cells): the first solid cell, and the face it was
    /// entered by. Cells of <paramref name="skip"/> are seen through — the glass being crossed.
    /// </summary>
    private static bool Walk(VoxelWorld grid, Vector3 origin, Vector3 direction, float start, float end, byte skip, out Int3 cell, out Face face, out float distance)
    {
        cell = default;
        face = default;
        distance = 0f;

        // Start just before the box, so the first step enters the first cell through the right face.
        float t = MathF.Max(start - 1e-3f, 0f);
        Vector3 p = origin + (direction * t);
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);

        int stepX = Math.Sign(direction.X), stepY = Math.Sign(direction.Y), stepZ = Math.Sign(direction.Z);
        float nextX = Boundary(p.X, direction.X, x, stepX) + t;
        float nextY = Boundary(p.Y, direction.Y, y, stepY) + t;
        float nextZ = Boundary(p.Z, direction.Z, z, stepZ) + t;
        float deltaX = stepX != 0 ? MathF.Abs(1f / direction.X) : float.PositiveInfinity;
        float deltaY = stepY != 0 ? MathF.Abs(1f / direction.Y) : float.PositiveInfinity;
        float deltaZ = stepZ != 0 ? MathF.Abs(1f / direction.Z) : float.PositiveInfinity;

        for (int guard = 0; guard < 100_000; guard++)
        {
            if (nextX <= nextY && nextX <= nextZ)
            {
                x += stepX;
                t = nextX;
                nextX += deltaX;
                face = stepX > 0 ? Face.NegX : Face.PosX;
            }
            else if (nextY <= nextZ)
            {
                y += stepY;
                t = nextY;
                nextY += deltaY;
                face = stepY > 0 ? Face.NegY : Face.PosY;
            }
            else
            {
                z += stepZ;
                t = nextZ;
                nextZ += deltaZ;
                face = stepZ > 0 ? Face.NegZ : Face.PosZ;
            }

            if (t > end || float.IsInfinity(t))
            {
                return false;
            }

            byte value = grid.GetVoxel(x, y, z);
            if (value != Palette.EmptyIndex && value != skip)
            {
                cell = new Int3(x, y, z);
                distance = t;
                return true;
            }
        }

        return false;
    }

    private static float Boundary(float origin, float direction, int cell, int step) =>
        step == 0 ? float.PositiveInfinity : ((step > 0 ? cell + 1 : cell) - origin) / direction;

    private static bool SlabHit(Ray ray, Vector3 min, Vector3 max, out float near, out float far)
    {
        near = 0f;
        far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = ray.Origin[axis];
            float d = ray.Direction[axis];
            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < min[axis] || o > max[axis])
                {
                    return false;
                }

                continue;
            }

            float t1 = (min[axis] - o) / d;
            float t2 = (max[axis] - o) / d;
            near = MathF.Max(near, MathF.Min(t1, t2));
            far = MathF.Min(far, MathF.Max(t1, t2));
            if (near > far)
            {
                return false;
            }
        }

        return true;
    }

    // ---- Light and colour ----------------------------------------------------------------------

    /// <summary>The sky a ray leaving the level sees: the horizon's colour up to the top's, and the ground below.</summary>
    private Vector3 Sky(Vector3 direction)
    {
        float up = direction.Y;
        Vector3 colour = up >= 0f
            ? Vector3.Lerp(_skyHorizon, _skyTop, MathF.Sqrt(up))
            : _skyHorizon * (0.55f + (0.45f * (1f + up)));
        return colour * _skyScale;
    }

    private static float Schlick(float f0, float cosine) => f0 + ((1f - f0) * MathF.Pow(1f - Math.Clamp(cosine, 0f, 1f), 5f));

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static Vector3 CosineHemisphere(Vector3 normal, ref Random32 random)
    {
        float u = random.Next();
        float v = random.Next();
        float radius = MathF.Sqrt(u);
        float angle = MathF.Tau * v;
        (Vector3 tangent, Vector3 bitangent) = Basis(normal);
        return Vector3.Normalize((tangent * (radius * MathF.Cos(angle))) + (bitangent * (radius * MathF.Sin(angle))) + (normal * MathF.Sqrt(MathF.Max(0f, 1f - u))));
    }

    /// <summary>A direction round <paramref name="axis"/>, spread wider the rougher: a blurred reflection.</summary>
    private static Vector3 Glossy(Vector3 axis, float roughness, ref Random32 random)
    {
        if (roughness <= 0.001f)
        {
            return axis;
        }

        float exponent = 2f / MathF.Max(roughness * roughness * roughness * roughness, 1e-4f);
        float cosine = MathF.Pow(random.Next(), 1f / (exponent + 1f));
        float sine = MathF.Sqrt(MathF.Max(0f, 1f - (cosine * cosine)));
        float angle = MathF.Tau * random.Next();
        (Vector3 tangent, Vector3 bitangent) = Basis(axis);
        return Vector3.Normalize((tangent * (sine * MathF.Cos(angle))) + (bitangent * (sine * MathF.Sin(angle))) + (axis * cosine));
    }

    private static (Vector3 Tangent, Vector3 Bitangent) Basis(Vector3 normal)
    {
        Vector3 helper = MathF.Abs(normal.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(helper, normal));
        return (tangent, Vector3.Cross(normal, tangent));
    }

    /// <summary>The ACES filmic curve: bright light rolls off instead of clipping flat.</summary>
    private static Vector3 Aces(Vector3 x)
    {
        Vector3 numerator = x * ((x * 2.51f) + new Vector3(0.03f));
        Vector3 denominator = (x * ((x * 2.43f) + new Vector3(0.59f))) + new Vector3(0.14f);
        return Vector3.Clamp(numerator / denominator, Vector3.Zero, Vector3.One);
    }

    private static byte Srgb(float linear)
    {
        float c = linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)MathF.Round(Math.Clamp(c, 0f, 1f) * 255f);
    }

    private static Vector3 Linear(Vector3 srgb) => new(
        RenderScene.Linear((byte)MathF.Round(Math.Clamp(srgb.X, 0f, 1f) * 255f)),
        RenderScene.Linear((byte)MathF.Round(Math.Clamp(srgb.Y, 0f, 1f) * 255f)),
        RenderScene.Linear((byte)MathF.Round(Math.Clamp(srgb.Z, 0f, 1f) * 255f)));

    private static uint Hash(int seed, int x, int y, int sample)
    {
        uint h = unchecked((uint)seed * 0x9E3779B9u);
        h = Mix(h ^ (uint)x);
        h = Mix(h ^ (uint)y * 0x85EBCA6Bu);
        h = Mix(h ^ (uint)sample * 0xC2B2AE35u);
        return h == 0 ? 1u : h;
    }

    private static uint Mix(uint h)
    {
        h ^= h >> 16;
        h = unchecked(h * 0x7FEB352Du);
        h ^= h >> 15;
        h = unchecked(h * 0x846CA68Bu);
        h ^= h >> 16;
        return h;
    }

    /// <summary>A small, fast random source for one pixel's sample: xorshift, seeded from its hash.</summary>
    private struct Random32(uint state)
    {
        private uint _state = state;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state >> 8) * (1f / 16777216f);
        }
    }
}
