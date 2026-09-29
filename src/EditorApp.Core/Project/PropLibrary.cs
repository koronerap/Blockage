using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Project;

/// <summary>A prop in the library: a small level of its own, and the picture of it the library shows.</summary>
public sealed record PropEntry(string Name, string LevelPath, string ThumbnailPath);

/// <summary>
/// The prop library (Fullreleaseplan 6.3): your own props — a barrel, a lamp post, a house front —
/// kept in a folder as small levels, each with a rendered picture beside it, to be dropped into any
/// level. A prop sits with the middle of its underside at its origin, so it stands where it is put.
/// </summary>
public static class PropLibrary
{
    /// <summary>How many pixels across a prop's picture is.</summary>
    public const int ThumbnailSize = 96;

    /// <summary>Beside the palette library, in the user's own settings.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blockage", "Props");

    /// <summary>The props in a folder, by name; none for a folder that is not there.</summary>
    public static IReadOnlyList<PropEntry> List(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(directory, "*" + VxLevelFile.Extension)
            .Select(path => new PropEntry(Path.GetFileNameWithoutExtension(path), path, Path.ChangeExtension(path, ".png")))
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Keeps <paramref name="objects"/> as a prop called <paramref name="name"/>: copies of them in a
    /// level of their own, moved together so the middle of their underside is at the origin, with
    /// the colours and materials they are painted in, and a picture. A prop of that name is replaced.
    /// </summary>
    public static PropEntry Save(string directory, string name, VoxelScene scene, IReadOnlyList<VoxelObject> objects)
    {
        if (objects.Count == 0)
        {
            throw new ArgumentException("A prop needs at least one object.", nameof(objects));
        }

        Directory.CreateDirectory(directory);
        VoxelScene prop = Build(scene, objects);

        string safe = FileNames.Safe(name).Trim();
        if (safe.Length == 0)
        {
            safe = "Prop";
        }

        string levelPath = Path.Combine(directory, safe + VxLevelFile.Extension);
        string thumbnailPath = Path.ChangeExtension(levelPath, ".png");
        VxLevelFile.Save(prop, levelPath, name);
        File.WriteAllBytes(thumbnailPath, PngWriter.EncodeRgba(Thumbnail(prop), ThumbnailSize, ThumbnailSize));
        return new PropEntry(safe, levelPath, thumbnailPath);
    }

    public static void Delete(PropEntry entry)
    {
        File.Delete(entry.LevelPath);
        if (File.Exists(entry.ThumbnailPath))
        {
            File.Delete(entry.ThumbnailPath);
        }
    }

    /// <summary>The prop as a level of its own: copies of the objects around the origin, linked copies still linked, with a sun to be seen by.</summary>
    public static VoxelScene Build(VoxelScene scene, IReadOnlyList<VoxelObject> objects)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (VoxelObject o in objects)
        {
            if (o.TryGetWorldBounds(out Vector3 objectMin, out Vector3 objectMax))
            {
                min = Vector3.Min(min, objectMin);
                max = Vector3.Max(max, objectMax);
            }
        }

        Vector3 origin = min.X <= max.X ? new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f) : Vector3.Zero;
        float size = objects[0].VoxelSize;
        origin = new Vector3(MathF.Round(origin.X / size), MathF.Round(origin.Y / size), MathF.Round(origin.Z / size)) * size;

        var prop = new VoxelScene();
        prop.ReplacePalette(scene.Palette.Clone());
        var grids = new Dictionary<Voxels.VoxelWorld, Voxels.VoxelWorld>(ReferenceEqualityComparer.Instance);
        var copyOf = new Dictionary<int, int>();
        foreach (VoxelObject o in objects)
        {
            if (!grids.TryGetValue(o.Grid, out Voxels.VoxelWorld? grid))
            {
                grid = o.Grid.Copy();
                grids[o.Grid] = grid;
            }

            VoxelObject copy = prop.Add(grid, o.Transform with { Position = o.Transform.Position - origin }, o.Name);
            copy.CopyDataFrom(o);

            copyOf[o.Id] = copy.Id;
        }

        // Parents among them stay their parents.
        foreach (VoxelObject o in objects)
        {
            if (scene.ParentOf(o) is { } parent && copyOf.TryGetValue(parent.Id, out int parentCopy))
            {
                prop.SetParent(copyOf[o.Id], parentCopy);
            }
        }

        prop.AddDefaultSun();
        return prop;
    }

    /// <summary>A picture of a prop, rendered from the isometric angle against nothing, RGBA rows from the top.</summary>
    public static byte[] Thumbnail(VoxelScene prop)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (VoxelObject o in prop.Objects)
        {
            if (o.TryGetWorldBounds(out Vector3 objectMin, out Vector3 objectMax))
            {
                min = Vector3.Min(min, objectMin);
                max = Vector3.Max(max, objectMax);
            }
        }

        if (min.X > max.X)
        {
            return new byte[ThumbnailSize * ThumbnailSize * 4];
        }

        Vector3 centre = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 0.5f);
        var view = new SceneCamera(0, "Thumbnail", Vector3.Zero, MathF.PI * 1.25f, -30f * (MathF.PI / 180f), 40f, CameraKind.Isometric, radius * 2.1f, radius * 4f);
        view = view with { Position = centre - (view.Forward * radius * 4f) };

        var settings = new RenderSettings
        {
            Width = ThumbnailSize,
            Height = ThumbnailSize,
            Samples = 24,
            Bounces = 2,
            TransparentBackground = true,
        };

        var tracer = new PathTracer(RenderScene.Capture(prop), view.ToRenderCamera(), settings);
        while (!tracer.IsFinished)
        {
            tracer.AddSample();
        }

        return tracer.ToRgba();
    }
}
