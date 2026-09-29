using System.Runtime.InteropServices;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using VoxelPalette = EditorApp.Core.Voxels.Palette;

namespace EditorApp.Core.Rendering;

/// <summary>
/// A <see cref="RenderScene"/> laid out flat for the path tracer on the GPU: every array one of
/// 32-bit words in std430's layout, ready to become a shader storage buffer as it is.
///
/// The voxels go chunk by chunk, four to a word, only the chunks that hold any; each object has a
/// table of its chunks, −1 where one is empty. Painted faces are few — only an exposed face can be
/// painted — so they go in a list of their own, sorted, for the shader to search.
/// </summary>
public sealed class PackedScene
{
    /// <summary>Words one object takes: its rotation, position and voxel size, world box, and where its chunks are.</summary>
    public const int ModelWords = 24;

    /// <summary>Words one light takes: position and kind, direction and range, colour and cone.</summary>
    public const int LightWords = 16;

    /// <summary>Words one chunk's voxels take, four voxels to a word.</summary>
    public const int ChunkWords = Chunk.VoxelCount / 4;

    /// <summary>Words a palette entry takes: its colour, linear, and its material.</summary>
    public const int PaletteWords = 8;

    private PackedScene(uint[] models, int[] chunkTable, uint[] voxels, uint[] chunkFaces, uint[] faces, uint[] lights, uint[] palette, int modelCount, int lightCount)
    {
        Models = models;
        ChunkTable = chunkTable;
        Voxels = voxels;
        ChunkFaces = chunkFaces;
        Faces = faces;
        Lights = lights;
        Palette = palette;
        ModelCount = modelCount;
        LightCount = lightCount;
    }

    /// <summary>
    /// Per object: rotation (x, y, z, w); position and voxel size; world box min; world box max;
    /// its smallest chunk coordinate and the first of its entries in <see cref="ChunkTable"/>; how
    /// many chunks it spans along each axis.
    /// </summary>
    public uint[] Models { get; }

    /// <summary>Per object, x fastest, then y, then z: the chunk in <see cref="Voxels"/> that is there, −1 for none.</summary>
    public int[] ChunkTable { get; }

    /// <summary>The chunks' voxels, <see cref="ChunkWords"/> words each, in <see cref="Chunk.LinearIndex"/> order.</summary>
    public uint[] Voxels { get; }

    /// <summary>Per chunk, two words: its first painted face in <see cref="Faces"/> and how many it has.</summary>
    public uint[] ChunkFaces { get; }

    /// <summary>Painted faces, sorted within each chunk: <c>((linear &lt;&lt; 3 | face) &lt;&lt; 8) | colour</c>.</summary>
    public uint[] Faces { get; }

    public uint[] Lights { get; }

    /// <summary>Per palette entry: colour (r, g, b, unused), then material (emission, metallic, smoothness, transparency).</summary>
    public uint[] Palette { get; }

    public int ModelCount { get; }

    public int LightCount { get; }

    /// <summary>How big the largest of the arrays is, in bytes: what a single storage buffer has to hold.</summary>
    public long LargestBuffer => 4L * new[] { Models.Length, ChunkTable.Length, Voxels.Length, ChunkFaces.Length, Faces.Length, Lights.Length, Palette.Length }.Max();

    public static PackedScene Pack(RenderScene scene)
    {
        var models = new List<uint>();
        var table = new List<int>();
        var voxels = new List<uint>();
        var chunkFaces = new List<uint>();
        var faces = new List<uint>();
        int chunkCount = 0;

        foreach (RenderScene.Model model in scene.Objects)
        {
            var solid = model.Grid.Chunks.Where(pair => !pair.Value.IsEmpty).ToList();
            if (solid.Count == 0)
            {
                continue;
            }

            int minX = solid.Min(pair => pair.Key.X), minY = solid.Min(pair => pair.Key.Y), minZ = solid.Min(pair => pair.Key.Z);
            int spanX = solid.Max(pair => pair.Key.X) - minX + 1;
            int spanY = solid.Max(pair => pair.Key.Y) - minY + 1;
            int spanZ = solid.Max(pair => pair.Key.Z) - minZ + 1;
            int tableStart = table.Count;
            table.AddRange(Enumerable.Repeat(-1, spanX * spanY * spanZ));

            foreach ((ChunkCoord coord, Chunk chunk) in solid)
            {
                int slot = ((coord.Z - minZ) * spanY + (coord.Y - minY)) * spanX + (coord.X - minX);
                table[tableStart + slot] = chunkCount++;
                voxels.AddRange(MemoryMarshal.Cast<byte, uint>(chunk.Indices).ToArray());

                var painted = chunk.FaceOverrides()
                    .Select(face => ((uint)((face.Linear << 3) | (int)face.Face) << 8) | face.PaletteIndex)
                    .Order()
                    .ToList();
                chunkFaces.Add((uint)faces.Count);
                chunkFaces.Add((uint)painted.Count);
                faces.AddRange(painted);
            }

            ObjectTransform transform = model.Transform;
            models.AddRange(Floats(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W));
            models.AddRange(Floats(transform.Position.X, transform.Position.Y, transform.Position.Z, transform.VoxelSize));
            models.AddRange(Floats(model.WorldMin.X, model.WorldMin.Y, model.WorldMin.Z, 0f));
            models.AddRange(Floats(model.WorldMax.X, model.WorldMax.Y, model.WorldMax.Z, 0f));
            models.AddRange(Ints(minX, minY, minZ, tableStart));
            models.AddRange(Ints(spanX, spanY, spanZ, 0));
        }

        var lights = new List<uint>();
        foreach (RenderScene.Light light in scene.Lights)
        {
            lights.AddRange(Floats(light.Position.X, light.Position.Y, light.Position.Z, (float)light.Kind));
            lights.AddRange(Floats(light.Direction.X, light.Direction.Y, light.Direction.Z, light.Range));
            lights.AddRange(Floats(light.Colour.X, light.Colour.Y, light.Colour.Z, light.ConeOuter));
            lights.AddRange(Floats(light.ConeInner, 0f, 0f, 0f));
        }

        var palette = new uint[VoxelPalette.Size * PaletteWords];
        for (int i = 0; i < VoxelPalette.Size; i++)
        {
            VoxelMaterial material = scene.Materials[i];
            Floats(scene.Colours[i].X, scene.Colours[i].Y, scene.Colours[i].Z, 0f).CopyTo(palette, i * PaletteWords);
            Floats(material.Emission, material.Metallic, material.Smoothness, material.Transparency).CopyTo(palette, (i * PaletteWords) + 4);
        }

        int modelCount = models.Count / ModelWords;
        int lightCount = lights.Count / LightWords;

        // A storage buffer may not be empty: an array with nothing in it still gets one word.
        return new PackedScene(
            AtLeastOne(models),
            table.Count > 0 ? [.. table] : [-1],
            AtLeastOne(voxels),
            AtLeastOne(chunkFaces),
            AtLeastOne(faces),
            AtLeastOne(lights),
            palette,
            modelCount,
            lightCount);
    }

    private static uint[] AtLeastOne(List<uint> words) => words.Count > 0 ? [.. words] : [0u];

    private static uint[] Floats(float a, float b, float c, float d) =>
        [BitConverter.SingleToUInt32Bits(a), BitConverter.SingleToUInt32Bits(b), BitConverter.SingleToUInt32Bits(c), BitConverter.SingleToUInt32Bits(d)];

    private static uint[] Ints(int a, int b, int c, int d) => [(uint)a, (uint)b, (uint)c, (uint)d];
}
