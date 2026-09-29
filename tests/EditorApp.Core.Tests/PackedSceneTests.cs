using System.Numerics;
using EditorApp.Core.Rendering;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>The scene laid out flat for the GPU engine: what the compute shader reads has to be where it looks.</summary>
public class PackedSceneTests
{
    private static float Float(uint[] words, int index) => BitConverter.UInt32BitsToSingle(words[index]);

    /// <summary>What the shader's voxelIn does: a chunk's voxels, four to a word.</summary>
    private static byte VoxelIn(PackedScene packed, int chunk, int x, int y, int z)
    {
        int linear = Chunk.LinearIndex(x & Chunk.SizeMask, y & Chunk.SizeMask, z & Chunk.SizeMask);
        return (byte)(packed.Voxels[(chunk * PackedScene.ChunkWords) + (linear >> 2)] >> ((linear & 3) * 8));
    }

    /// <summary>What the shader's chunkAt does: the chunk that holds a cell, or −1.</summary>
    private static int ChunkAt(PackedScene packed, int model, int x, int y, int z)
    {
        int b = model * PackedScene.ModelWords;
        int kx = (x >> Chunk.SizeShift) - (int)packed.Models[b + 16];
        int ky = (y >> Chunk.SizeShift) - (int)packed.Models[b + 17];
        int kz = (z >> Chunk.SizeShift) - (int)packed.Models[b + 18];
        int sx = (int)packed.Models[b + 20], sy = (int)packed.Models[b + 21], sz = (int)packed.Models[b + 22];
        if (kx < 0 || ky < 0 || kz < 0 || kx >= sx || ky >= sy || kz >= sz)
        {
            return -1;
        }

        return packed.ChunkTable[(int)packed.Models[b + 19] + (((kz * sy) + ky) * sx) + kx];
    }

    [Fact]
    public void EveryVoxelIsWhereTheShaderLooksForIt()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(0, 0, 0, 5);
        grid.SetVoxel(40, 3, -7, 9);
        grid.SetVoxel(-33, 70, 2, 200);
        scene.Add(grid, new ObjectTransform(new Vector3(1f, 2f, 3f), Quaternion.Identity, 0.5f), "Scattered");

        PackedScene packed = PackedScene.Pack(RenderScene.Capture(scene));

        Assert.Equal(1, packed.ModelCount);
        foreach ((int x, int y, int z, byte colour) in new[] { (0, 0, 0, (byte)5), (40, 3, -7, (byte)9), (-33, 70, 2, (byte)200) })
        {
            int chunk = ChunkAt(packed, 0, x, y, z);
            Assert.True(chunk >= 0);
            Assert.Equal(colour, VoxelIn(packed, chunk, x, y, z));
        }

        // An empty chunk inside the object's span is none; the neighbour of a voxel is empty.
        Assert.Equal(-1, ChunkAt(packed, 0, 40, 70, -7));
        Assert.Equal(0, VoxelIn(packed, ChunkAt(packed, 0, 0, 0, 0), 1, 0, 0));

        // Position and voxel size, as the shader turns a ray into the object's cells.
        Assert.Equal(1f, Float(packed.Models, 4));
        Assert.Equal(0.5f, Float(packed.Models, 7));
    }

    [Fact]
    public void PaintedFacesAreSortedForTheShadersSearch()
    {
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        grid.SetVoxel(2, 2, 2, 5);
        grid.SetVoxel(1, 2, 2, 5);
        grid.SetFaceColor(2, 2, 2, Face.PosY, 30);
        grid.SetFaceColor(1, 2, 2, Face.NegZ, 31);
        grid.SetFaceColor(2, 2, 2, Face.PosX, 32);
        scene.Add(grid, ObjectTransform.Identity, "Painted");

        PackedScene packed = PackedScene.Pack(RenderScene.Capture(scene));

        Assert.Equal(0u, packed.ChunkFaces[0]);
        Assert.Equal(3u, packed.ChunkFaces[1]);
        uint[] faces = packed.Faces[..3];
        Assert.Equal(faces.Order().ToArray(), faces);

        static uint Key(int x, int y, int z, Face face) => ((uint)Chunk.LinearIndex(x, y, z) << 3) | (uint)face;
        Assert.Contains((Key(2, 2, 2, Face.PosY) << 8) | 30u, faces);
        Assert.Contains((Key(1, 2, 2, Face.NegZ) << 8) | 31u, faces);
        Assert.Contains((Key(2, 2, 2, Face.PosX) << 8) | 32u, faces);
    }

    [Fact]
    public void AnEmptyLevelStillGivesEveryBufferAWord()
    {
        PackedScene packed = PackedScene.Pack(RenderScene.Capture(new VoxelScene()));

        Assert.Equal(0, packed.ModelCount);
        Assert.Equal(0, packed.LightCount);
        Assert.NotEmpty(packed.Models);
        Assert.NotEmpty(packed.ChunkTable);
        Assert.NotEmpty(packed.Voxels);
        Assert.NotEmpty(packed.Faces);
        Assert.NotEmpty(packed.Lights);
        Assert.Equal(Palette.Size * PackedScene.PaletteWords, packed.Palette.Length);
    }

    [Fact]
    public void LightsAndMaterialsAreLaidOutInFours()
    {
        var scene = new VoxelScene();
        scene.AddDefaultSun();
        scene.Palette.SetMaterial(7, new VoxelMaterial(0.25f, 0.5f, 0.75f, 0.5f));

        PackedScene packed = PackedScene.Pack(RenderScene.Capture(scene));

        Assert.Equal(1, packed.LightCount);
        Assert.Equal((float)LightKind.Directional, Float(packed.Lights, 3));
        Assert.Equal(0.25f, Float(packed.Palette, (7 * PackedScene.PaletteWords) + 4));
        Assert.Equal(0.5f, Float(packed.Palette, (7 * PackedScene.PaletteWords) + 7));
    }
}
