using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// The unwrap has to give every face its own area of the sheet, with nothing overlapping and nothing
/// off the edge — that is the whole difference from the palette layout, where all four corners of a
/// quad share one texel.
/// </summary>
public class UvUnwrapTests
{
    private static VoxelWorld Cube(int side, byte index = Palette.WhiteIndex)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, index);
                }
            }
        }

        return grid;
    }

    private static VoxelScene SceneOf(VoxelWorld grid, float voxelSize = 1f)
    {
        var scene = new VoxelScene { VoxelSize = voxelSize };
        scene.Add(grid, ObjectTransform.Identity, "cube");
        return scene;
    }

    /// <summary>A blob with several colours, so quads come in a range of sizes.</summary>
    private static VoxelWorld MixedWorld()
    {
        var grid = new VoxelWorld();
        var random = new Random(20260816);

        for (int x = 0; x < 14; x++)
        {
            for (int y = 0; y < 9; y++)
            {
                for (int z = 0; z < 11; z++)
                {
                    if (random.NextDouble() < 0.72)
                    {
                        grid.SetVoxel(x, y, z, (byte)(20 + random.Next(4)));
                    }
                }
            }
        }

        return grid;
    }

    [Fact]
    public void EveryQuadGetsAreaInsteadOfASinglePoint()
    {
        VoxelScene scene = SceneOf(Cube(4));
        ExportMesh mesh = GreedyMesher.BuildScene(scene);

        // The palette layout it replaces: four identical UVs per quad.
        Assert.All(
            Enumerable.Range(0, mesh.QuadCount),
            q => Assert.Equal(mesh.Uvs[q * 4], mesh.Uvs[(q * 4) + 2]));

        UvUnwrap.Apply(mesh);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            Vector2 a = mesh.Uvs[quad * 4];
            Vector2 c = mesh.Uvs[(quad * 4) + 2];

            Assert.True(MathF.Abs(c.X - a.X) > 1e-4f, "The island has no width.");
            Assert.True(MathF.Abs(c.Y - a.Y) > 1e-4f, "The island has no height.");
        }
    }

    [Fact]
    public void NoTwoIslandsOverlap()
    {
        // The one thing an unwrap must never do: two faces painted from the same texels.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        Assert.True(atlas.Islands.Count > 100, "Not enough islands to be a real test.");

        // A coverage grid rather than a pairwise sweep: thousands of islands make that quadratic.
        var used = new int[atlas.Width * atlas.Height];
        int id = 1;

        foreach (UvIsland island in atlas.Islands)
        {
            for (int y = island.Y; y < island.Y + island.Height; y++)
            {
                for (int x = island.X; x < island.X + island.Width; x++)
                {
                    int offset = (y * atlas.Width) + x;
                    Assert.Equal(0, used[offset]);
                    used[offset] = id;
                }
            }

            id++;
        }
    }

    [Fact]
    public void ChartsKeepTheirPaddingClearOfEachOther()
    {
        // The gutter is what stops mip levels pulling a neighbour's colour across a seam. It belongs
        // between charts, not between the faces inside one — those are touching on purpose.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        var owner = new int[atlas.Width * atlas.Height];
        for (int i = 0; i < atlas.Charts.Count; i++)
        {
            UvChart chart = atlas.Charts[i];
            for (int y = chart.Y; y < chart.Y + chart.Height; y++)
            {
                for (int x = chart.X; x < chart.X + chart.Width; x++)
                {
                    Assert.Equal(0, owner[(y * atlas.Width) + x]);
                    owner[(y * atlas.Width) + x] = i + 1;
                }
            }
        }

        for (int i = 0; i < atlas.Charts.Count; i++)
        {
            UvChart chart = atlas.Charts[i];
            int left = Math.Max(chart.X - atlas.Padding, 0);
            int top = Math.Max(chart.Y - atlas.Padding, 0);
            int right = Math.Min(chart.X + chart.Width + atlas.Padding, atlas.Width);
            int bottom = Math.Min(chart.Y + chart.Height + atlas.Padding, atlas.Height);

            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    int found = owner[(y * atlas.Width) + x];
                    Assert.True(found is 0 || found == i + 1, "Another chart reaches into the gutter.");
                }
            }
        }
    }

    [Fact]
    public void AFlatWallArrivesAsOnePieceRatherThanAsItsFaces()
    {
        // The point of charting: a surface you can paint across. A 6x6 slab one voxel thick has six
        // sides, and each side is one connected run however many quads it merged from.
        var grid = new VoxelWorld();
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 6; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
            }
        }

        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(grid));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        Assert.Equal(6, atlas.Charts.Count);
    }

    [Fact]
    public void ColourAloneDoesNotSplitASurface()
    {
        // What prompted the charting: a face painted a different colour is still the same wall, and
        // splitting on colour would put a seam through the middle of it.
        var grid = new VoxelWorld();
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                grid.SetVoxel(x, 0, z, (byte)(30 + ((x + z) % 5)));
            }
        }

        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(grid));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        Assert.Equal(6, atlas.Charts.Count);
        Assert.True(mesh.QuadCount > 20, "The top should have merged into many differently coloured quads.");
    }

    [Fact]
    public void SurfacesFacingTheSameWayAtDifferentDepthsStayApart()
    {
        // Why this is not simply a projection per normal: two slabs facing the same way would land
        // on each other, and painting one would paint the other.
        var grid = new VoxelWorld();
        for (int x = 0; x < 4; x++)
        {
            for (int z = 0; z < 4; z++)
            {
                grid.SetVoxel(x, 0, z, Palette.WhiteIndex);
                grid.SetVoxel(x, 6, z, Palette.WhiteIndex);
            }
        }

        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(grid));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        // Twelve sides, not six: the two slabs never share a chart.
        Assert.Equal(12, atlas.Charts.Count);
    }

    [Fact]
    public void ASolidBodyComesOutAsAHandfulOfPieces()
    {
        // A solid shape is what a level is actually made of, and it is where charting earns itself:
        // six sides, however many colours they were painted in.
        var grid = new VoxelWorld();
        var random = new Random(7);
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                for (int z = 0; z < 10; z++)
                {
                    grid.SetVoxel(x, y, z, (byte)(30 + random.Next(6)));
                }
            }
        }

        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(grid));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        Assert.Equal(6, atlas.Charts.Count);
        Assert.True(mesh.QuadCount > 100, "The colours should have split the sides into many quads.");
    }

    [Fact]
    public void ScatteredVoxelsCannotBeGroupedAndAreStillLaidOutCorrectly()
    {
        // The other end of the range: loose voxels share no edges, so charting has nothing to join
        // and every face is its own piece. That is the right answer, not a failure — what matters is
        // that the layout is still valid.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        Assert.True(atlas.Charts.Count <= mesh.QuadCount);
        Assert.All(mesh.Uvs, uv =>
        {
            Assert.InRange(uv.X, 0f, 1f);
            Assert.InRange(uv.Y, 0f, 1f);
        });
    }

    [Fact]
    public void EveryUvStaysOnTheSheet()
    {
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvUnwrap.Apply(mesh);

        foreach (Vector2 uv in mesh.Uvs)
        {
            Assert.InRange(uv.X, 0f, 1f);
            Assert.InRange(uv.Y, 0f, 1f);
        }
    }

    [Fact]
    public void AnIslandIsShapedLikeTheFaceItCovers()
    {
        // A 4x4x4 cube greedy-meshes to six 4x4 quads, so every island is square and four voxels on
        // a side. A stretched island would paint stretched.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(Cube(4)));
        UvAtlas atlas = UvUnwrap.Apply(mesh, texelsPerVoxel: 8);

        Assert.Equal(6, atlas.Islands.Count);
        Assert.All(atlas.Islands, island =>
        {
            Assert.Equal(32, island.Width);
            Assert.Equal(32, island.Height);
        });
    }

    [Fact]
    public void ANonSquareFaceGetsANonSquareIsland()
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < 6; x++)
        {
            grid.SetVoxel(x, 0, 0, Palette.WhiteIndex);
        }

        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(grid));
        UvAtlas atlas = UvUnwrap.Apply(mesh, texelsPerVoxel: 4);

        // The four long sides are 6x1; the two caps are 1x1.
        Assert.Equal(4, atlas.Islands.Count(i => (i.Width, i.Height) is (24, 4) or (4, 24)));
        Assert.Equal(2, atlas.Islands.Count(i => i is { Width: 4, Height: 4 }));
    }

    [Fact]
    public void TheLayoutDoesNotMoveWhenTheLevelIsRescaled()
    {
        // Density is per voxel, not per world unit: changing a level's scale must not reshuffle a
        // texture that has already been painted on.
        ExportMesh unit = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlasA = UvUnwrap.Apply(unit);

        ExportMesh quarter = GreedyMesher.BuildScene(SceneOf(MixedWorld(), voxelSize: 0.25f));
        UvAtlas atlasB = UvUnwrap.Apply(quarter, voxelSize: 0.25f);

        Assert.Equal(atlasA.Width, atlasB.Width);
        Assert.Equal(atlasA.Height, atlasB.Height);
        Assert.Equal(atlasA.Islands, atlasB.Islands);

        for (int i = 0; i < unit.Uvs.Count; i++)
        {
            Assert.Equal(unit.Uvs[i], quarter.Uvs[i]);
        }
    }

    [Fact]
    public void TheSheetIsShapedLikeSomethingAToolWillAccept()
    {
        // Width a power of two, height a whole number of blocks, and not a sliver: a sheet eight
        // times as long as it is tall packs beautifully and is miserable to open.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        Assert.Equal(0, atlas.Width & (atlas.Width - 1));
        Assert.Equal(0, atlas.Height % 64);
        Assert.True(
            Math.Max(atlas.Width, atlas.Height) <= Math.Min(atlas.Width, atlas.Height) * 4,
            $"{atlas.Width}x{atlas.Height} is too far from square.");
    }

    [Fact]
    public void ThePackerLeavesLessThanHalfTheSheetEmpty()
    {
        // Not a tight bound, a regression guard: rows-only packing with square power-of-two sheets
        // came in under 15% on this model, and it would be easy to slip back there without noticing.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        long chartArea = atlas.Charts.Sum(c => (long)c.Width * c.Height);
        double occupancy = chartArea / (double)((long)atlas.Width * atlas.Height);

        Assert.True(occupancy > 0.5, $"Charts occupy only {occupancy:P0} of the sheet.");
    }

    [Fact]
    public void NoPieceIsMirrored()
    {
        // Charts get turned to pack, and a quarter turn and a transpose put a rectangle in the same
        // place — but the transpose is a reflection, and anything painted on those pieces would come
        // out backwards. Flat colours hide it completely, so it has to be checked here: the sign of
        // the UV cross product has to match the geometry's for every single face.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvUnwrap.Apply(mesh);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            int first = quad * 4;

            Vector2 uv0 = mesh.Uvs[first];
            Vector2 alongU = mesh.Uvs[first + 1] - uv0;
            Vector2 alongV = mesh.Uvs[first + 3] - uv0;

            // The mesher always emits corners so that the first edge crosses into the second the
            // same way round; a mirrored island flips that.
            float cross = (alongU.X * alongV.Y) - (alongU.Y * alongV.X);
            Assert.True(cross > 0f, $"Face {quad} was laid out mirrored.");
        }
    }

    [Fact]
    public void FacesNextToEachOtherOnTheModelStayNextToEachOtherOnTheSheet()
    {
        // The whole reason for charting. A wall split into many quads by colour has to come back as
        // one unbroken run of texture, or a brush stroke across it hits a seam at every colour
        // change — which is the confetti this replaced.
        var grid = new VoxelWorld();
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                grid.SetVoxel(x, 0, z, (byte)(30 + ((x + z) % 4)));
            }
        }

        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(grid));
        UvAtlas atlas = UvUnwrap.Apply(mesh, texelsPerVoxel: 4);

        // The top face: every island in its chart tiles the chart with no gap and no overlap.
        UvChart top = atlas.Charts.OrderByDescending(c => c.QuadCount).First();
        var covered = new bool[top.Width * top.Height];
        int filled = 0;

        foreach (UvIsland island in atlas.Islands)
        {
            if (island.X < top.X || island.Y < top.Y
                || island.X + island.Width > top.X + top.Width
                || island.Y + island.Height > top.Y + top.Height)
            {
                continue;
            }

            for (int y = island.Y - top.Y; y < island.Y - top.Y + island.Height; y++)
            {
                for (int x = island.X - top.X; x < island.X - top.X + island.Width; x++)
                {
                    Assert.False(covered[(y * top.Width) + x], "Two faces landed on the same texels.");
                    covered[(y * top.Width) + x] = true;
                    filled++;
                }
            }
        }

        Assert.Equal(top.Width * top.Height, filled);
    }

    [Fact]
    public void TheTextureCarriesEachQuadsColourWhereItsUvPoints()
    {
        // The end-to-end check: follow a quad's own UV into the generated sheet and land on the
        // colour that quad was built in.
        var grid = Cube(5, 40);
        grid.SetVoxel(2, 4, 2, 90);
        grid.SetFaceColor(new Int3(1, 4, 1), Face.PosY, 150);

        VoxelScene scene = SceneOf(grid);
        ExportMesh mesh = GreedyMesher.BuildScene(scene);
        UvAtlas atlas = UvUnwrap.Apply(mesh);

        byte[] pixels = AtlasTexture.CreateRgba(atlas, scene.Palette);

        for (int quad = 0; quad < mesh.QuadCount; quad++)
        {
            // The middle of the island, which is where a quad's own surface actually is.
            Vector2 a = mesh.Uvs[quad * 4];
            Vector2 c = mesh.Uvs[(quad * 4) + 2];

            int x = (int)(((a.X + c.X) * 0.5f) * atlas.Width);
            int y = (int)(((a.Y + c.Y) * 0.5f) * atlas.Height);
            int offset = ((y * atlas.Width) + x) * 4;

            Color32 expected = scene.Palette[mesh.QuadPaletteIndices[quad]];
            Assert.Equal(
                (expected.R, expected.G, expected.B),
                (pixels[offset], pixels[offset + 1], pixels[offset + 2]));
        }
    }

    [Fact]
    public void ADenserSheetIsRequestedAndAnOversizedOneIsRefused()
    {
        // Asking for more than will fit comes back coarser rather than failing or overflowing.
        ExportMesh mesh = GreedyMesher.BuildScene(SceneOf(MixedWorld()));
        UvAtlas atlas = UvUnwrap.Apply(mesh, texelsPerVoxel: 64, maxSize: 512);

        Assert.True(atlas.TexelsPerVoxel < 64, "The density should have been reduced to fit.");
        Assert.True(atlas.Width <= 512);
        Assert.True(atlas.Height <= 512);
    }

    [Fact]
    public void TransformedObjectsAreMeasuredInVoxelsAllTheSame()
    {
        // Sizes are read back off the geometry, after the object transforms are baked in, so a
        // rotation must not turn a 3x1 face into something else.
        var scene = new VoxelScene();
        var grid = new VoxelWorld();
        for (int x = 0; x < 3; x++)
        {
            grid.SetVoxel(x, 0, 0, Palette.WhiteIndex);
        }

        scene.Add(
            grid,
            new ObjectTransform(new Vector3(5f, 2f, 1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f)),
            "turned");

        ExportMesh mesh = GreedyMesher.BuildScene(scene);
        UvAtlas atlas = UvUnwrap.Apply(mesh, texelsPerVoxel: 4);

        Assert.Equal(4, atlas.Islands.Count(i => (i.Width, i.Height) is (12, 4) or (4, 12)));
    }
}
