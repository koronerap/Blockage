using System.Numerics;
using EditorApp.Core.Export;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Meshing;

/// <summary>
/// The export mesher (EditorApp.md §4b) — the thing this tool exists to deliver. A flat wall leaves
/// as one quad instead of hundreds; a single-color 32³ cube leaves as 6 quads instead of 6144.
///
/// Two faces merge only when all three hold: same plane, same normal, same palette index. The third
/// is what separates this from textbook greedy meshing — with color in the mask, merging needs an
/// equality test rather than a boolean one.
///
/// This never runs on the editing mesh. A merged quad belongs to a hundred voxels at once, which
/// destroys the triangle-to-voxel mapping selection would need; that is why there are two meshers
/// and not one.
/// </summary>
public static class GreedyMesher
{
    /// <param name="uvSelector">
    /// UV for a palette index; all four corners of a quad get the same one. Defaults to the palette
    /// texture's block center, which is why merging is completely unconstrained here (§6).
    /// </param>
    /// <param name="mergeAcrossColors">
    /// Merge on shape alone, letting one quad span voxels of different colours.
    ///
    /// Colour has always had to match because of where the colour lived: with the palette texture a
    /// quad's four corners all sample one texel, so a merged quad could only ever be one colour, and
    /// every painted edge became a cut in the geometry. Give the mesh a real unwrap and that stops
    /// being true — the quad owns a rectangle of the sheet large enough to hold each of its cells, so
    /// the paint goes into the texture instead of into the triangle count. Off for palette exports,
    /// which still need it.
    /// </param>
    public static ExportMesh Build(
        VoxelWorld world,
        Func<byte, Vector2>? uvSelector = null,
        bool mergeAcrossColors = false)
    {
        var mesh = new ExportMesh();
        if (!world.TryGetBounds(out Int3 min, out Int3 max))
        {
            return mesh;
        }

        uvSelector ??= PaletteTexture.TexelCenterUv;

        Span<int> minimum = [min.X, min.Y, min.Z];
        Span<int> maximum = [max.X, max.Y, max.Z];

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            SweepDirection(world, mesh, (Face)f, minimum, maximum, uvSelector, mergeAcrossColors);
        }

        return mesh;
    }

    /// <summary>
    /// Meshes every visible object and bakes its placement into the result, so the export is a
    /// single mesh with one material regardless of how many pieces the level was built from. Each
    /// object is merged in its own space, which is what keeps greedy merging working after a
    /// rotation.
    /// </summary>
    public static ExportMesh BuildScene(
        Scene.VoxelScene scene,
        Func<byte, Vector2>? uvSelector = null,
        bool mergeAcrossColors = false)
    {
        var combined = new ExportMesh();

        foreach (Scene.VoxelObject o in scene.Objects)
        {
            if (!o.IsExported || o.IsEmpty)
            {
                continue;
            }

            // Where this object's quads start, so the export can put it back as its own object
            // rather than welding the whole level into one lump.
            // Each object's own voxel size comes in with its transform: voxels become world units
            // object by object, since two objects need not share a size.
            int first = combined.QuadCount;
            combined.Append(Build(o.Shown, uvSelector, mergeAcrossColors), o.Transform);
            combined.BeginPart(o.Name, first, o.VoxelSize);
        }

        return combined;
    }

    private static void SweepDirection(
        VoxelWorld world,
        ExportMesh mesh,
        Face face,
        ReadOnlySpan<int> minimum,
        ReadOnlySpan<int> maximum,
        Func<byte, Vector2> uvSelector,
        bool mergeAcrossColors)
    {
        int axis = FaceInfo.Axis(face);
        // The two axes spanning the slice, always in ascending order so the quad corner offsets
        // from FaceInfo scale onto the right one.
        int uAxis = axis == 0 ? 1 : 0;
        int vAxis = axis == 2 ? 1 : 2;

        int uCount = maximum[uAxis] - minimum[uAxis] + 1;
        int vCount = maximum[vAxis] - minimum[vAxis] + 1;

        Vector3 normal = FaceInfo.Normal(face);
        Int3 neighbourOffset = FaceInfo.Offset(face);

        // One mask buffer for every slice of this direction.
        var mask = new byte[uCount * vCount];
        Span<int> position = stackalloc int[3];

        // Rows run along one axis, so this cursor stays inside the same chunk for 32 cells at a
        // time. Nothing writes to the world during a sweep, so the cache cannot go stale.
        var reader = new VoxelReader(world);

        for (int slice = minimum[axis]; slice <= maximum[axis]; slice++)
        {
            BuildMask(ref reader, mask, face, axis, uAxis, vAxis, slice, minimum, uCount, vCount, neighbourOffset, position);
            EmitQuads(mesh, mask, face, axis, uAxis, vAxis, slice, minimum, uCount, vCount, normal, uvSelector, mergeAcrossColors);
        }
    }

    /// <summary>
    /// mask[u, v] = the palette index of a voxel whose face in this direction is exposed, or 0.
    /// Storing the index rather than a flag is what makes same-color merging an equality test.
    /// </summary>
    private static void BuildMask(
        ref VoxelReader reader,
        byte[] mask,
        Face face,
        int axis,
        int uAxis,
        int vAxis,
        int slice,
        ReadOnlySpan<int> minimum,
        int uCount,
        int vCount,
        Int3 neighbourOffset,
        Span<int> position)
    {
        Array.Clear(mask);

        position[axis] = slice;

        for (int v = 0; v < vCount; v++)
        {
            position[vAxis] = minimum[vAxis] + v;
            int rowBase = v * uCount;

            for (int u = 0; u < uCount; u++)
            {
                // Only the u component varies down a row; the other two are already set.
                position[uAxis] = minimum[uAxis] + u;

                var voxel = new Int3(position[0], position[1], position[2]);
                if (!reader.IsSolid(voxel))
                {
                    continue;
                }

                if (reader.IsSolid(voxel + neighbourOffset))
                {
                    continue;   // interior face, never visible
                }

                // The mask has always held a colour per face; now that colour can differ from the
                // voxel's own, which changes nothing about how merging works.
                byte index = reader.GetFace(voxel, face);
                if (index == Palette.EmptyIndex)
                {
                    continue;
                }

                mask[rowBase + u] = index;
            }
        }
    }

    private static void EmitQuads(
        ExportMesh mesh,
        byte[] mask,
        Face face,
        int axis,
        int uAxis,
        int vAxis,
        int slice,
        ReadOnlySpan<int> minimum,
        int uCount,
        int vCount,
        Vector3 normal,
        Func<byte, Vector2> uvSelector,
        bool mergeAcrossColors)
    {
        // Either "the same colour as the run started with", or "covered at all". The second is what
        // lets a painted surface stay one quad.
        bool Matches(byte cell, byte index) =>
            mergeAcrossColors ? cell != Palette.EmptyIndex : cell == index;

        // Whether this face's first quad edge runs along the mask's u axis or its v axis.
        bool firstEdgeAlongU =
            Component(FaceInfo.Corner(face, 1), uAxis) != Component(FaceInfo.Corner(face, 0), uAxis);

        for (int v = 0; v < vCount; v++)
        {
            for (int u = 0; u < uCount; u++)
            {
                byte index = mask[v * uCount + u];
                if (index == Palette.EmptyIndex)
                {
                    continue;
                }

                // Width: run right while the run holds.
                int width = 1;
                while (u + width < uCount && Matches(mask[v * uCount + u + width], index))
                {
                    width++;
                }

                // Height: grow down only while the *entire* next row matches.
                int height = 1;
                while (v + height < vCount)
                {
                    bool rowMatches = true;
                    for (int i = 0; i < width; i++)
                    {
                        if (!Matches(mask[(v + height) * uCount + u + i], index))
                        {
                            rowMatches = false;
                            break;
                        }
                    }

                    if (!rowMatches)
                    {
                        break;
                    }

                    height++;
                }

                QuadColors? cells = null;
                if (mergeAcrossColors && (width > 1 || height > 1))
                {
                    // Stored along the quad's own first and second edges, which is not always the
                    // mask's u and v: the corner tables wind the other way round on half the faces,
                    // so for those the run's width lies along the quad's second edge. Getting this
                    // backwards is invisible on a single-coloured quad and paints nonsense on a
                    // merged one.
                    int gridWidth = firstEdgeAlongU ? width : height;
                    int gridHeight = firstEdgeAlongU ? height : width;
                    var grid = new byte[gridWidth * gridHeight];

                    for (int b = 0; b < gridHeight; b++)
                    {
                        for (int a = 0; a < gridWidth; a++)
                        {
                            int alongU = firstEdgeAlongU ? a : b;
                            int alongV = firstEdgeAlongU ? b : a;
                            grid[(b * gridWidth) + a] = mask[((v + alongV) * uCount) + u + alongU];
                        }
                    }

                    cells = new QuadColors(gridWidth, gridHeight, grid);
                }

                AddQuad(mesh, face, axis, uAxis, vAxis, slice, minimum, u, v, width, height, normal, uvSelector(index), index, cells);

                // Consumed: clear the rectangle so it is not emitted again.
                for (int j = 0; j < height; j++)
                {
                    Array.Clear(mask, (v + j) * uCount + u, width);
                }

                u += width - 1;
            }
        }
    }

    private static void AddQuad(
        ExportMesh mesh,
        Face face,
        int axis,
        int uAxis,
        int vAxis,
        int slice,
        ReadOnlySpan<int> minimum,
        int u,
        int v,
        int width,
        int height,
        Vector3 normal,
        Vector2 uv,
        byte paletteIndex,
        QuadColors? cells)
    {
        int originU = minimum[uAxis] + u;
        int originV = minimum[vAxis] + v;

        mesh.AddQuad(
            Corner(face, 0, axis, uAxis, vAxis, slice, originU, originV, width, height),
            Corner(face, 1, axis, uAxis, vAxis, slice, originU, originV, width, height),
            Corner(face, 2, axis, uAxis, vAxis, slice, originU, originV, width, height),
            Corner(face, 3, axis, uAxis, vAxis, slice, originU, originV, width, height),
            normal,
            uv,
            paletteIndex,
            cells);
    }

    /// <summary>
    /// The unit-cube corner offsets in <see cref="FaceInfo"/> already wind counter-clockwise seen
    /// from outside. Scaling the u component by the run width and the v component by the run height
    /// stretches that same square over the merged rectangle, so winding stays correct for free and
    /// a merged quad is indistinguishable from an unmerged one apart from its size.
    /// </summary>
    private static Vector3 Corner(
        Face face,
        int cornerIndex,
        int axis,
        int uAxis,
        int vAxis,
        int slice,
        int originU,
        int originV,
        int width,
        int height)
    {
        Int3 offset = FaceInfo.Corner(face, cornerIndex);

        Span<float> result = stackalloc float[3];
        result[axis] = slice + Component(offset, axis);
        result[uAxis] = originU + Component(offset, uAxis) * width;
        result[vAxis] = originV + Component(offset, vAxis) * height;

        return new Vector3(result[0], result[1], result[2]);
    }

    private static int Component(Int3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };
}
