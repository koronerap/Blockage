using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockage.Importer
{
    /// <summary>
    /// A grid as a Unity mesh: every face between a voxel and empty space, merged into the largest
    /// rectangles of one colour a chunk at a time, each corner's UV on its colour's texel of the
    /// palette strip. Blockage is right-handed and Unity left-handed: X is turned round — and since a
    /// face counter-clockwise from outside stays so in the turned world, each is wound the other way
    /// round for Unity, which draws the clockwise ones.
    /// </summary>
    internal static class VoxelMesher
    {
        private const int S = VoxelGrid.Size;

        public static Mesh Build(VoxelGrid grid, string name)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var mask = new byte[S * S];
            var cell = new int[3];

            foreach (KeyValuePair<Vector3Int, byte[]> pair in grid.Chunks)
            {
                Vector3Int origin = pair.Key * S;
                byte[] cells = pair.Value;

                for (int face = 0; face < 6; face++)
                {
                    int axis = face / 2;
                    int sign = face % 2 == 0 ? 1 : -1;
                    int u = (axis + 1) % 3;
                    int v = (axis + 2) % 3;
                    Vector3Int step = ModifierStack.Offsets[face];

                    for (int d = 0; d < S; d++)
                    {
                        // The faces of this slice that look out on empty space, by colour.
                        for (int j = 0; j < S; j++)
                        {
                            for (int i = 0; i < S; i++)
                            {
                                cell[axis] = d;
                                cell[u] = i;
                                cell[v] = j;
                                byte colour = 0;
                                if (cells[VoxelGrid.Index(cell[0], cell[1], cell[2])] != 0)
                                {
                                    int nx = cell[0] + step.x, ny = cell[1] + step.y, nz = cell[2] + step.z;
                                    bool inside = nx >= 0 && ny >= 0 && nz >= 0 && nx < S && ny < S && nz < S;
                                    byte beyond = inside
                                        ? cells[VoxelGrid.Index(nx, ny, nz)]
                                        : grid.Get(origin.x + nx, origin.y + ny, origin.z + nz);
                                    if (beyond == 0)
                                    {
                                        colour = grid.FaceColour(origin.x + cell[0], origin.y + cell[1], origin.z + cell[2], face);
                                    }
                                }

                                mask[(j * S) + i] = colour;
                            }
                        }

                        // The largest rectangles of one colour, row by row.
                        for (int j = 0; j < S; j++)
                        {
                            for (int i = 0; i < S;)
                            {
                                byte colour = mask[(j * S) + i];
                                if (colour == 0)
                                {
                                    i++;
                                    continue;
                                }

                                int width = 1;
                                while (i + width < S && mask[(j * S) + i + width] == colour)
                                {
                                    width++;
                                }

                                int height = 1;
                                while (j + height < S && RowIs(mask, i, width, j + height, colour))
                                {
                                    height++;
                                }

                                for (int jj = j; jj < j + height; jj++)
                                {
                                    for (int ii = i; ii < i + width; ii++)
                                    {
                                        mask[(jj * S) + ii] = 0;
                                    }
                                }

                                AddQuad(vertices, normals, uvs, triangles, origin, axis, u, v, sign, d, i, j, width, height, colour);
                                i += width;
                            }
                        }
                    }
                }
            }

            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool RowIs(byte[] mask, int i, int width, int j, byte colour)
        {
            for (int ii = i; ii < i + width; ii++)
            {
                if (mask[(j * S) + ii] != colour)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddQuad(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            Vector3Int origin, int axis, int u, int v, int sign, int d, int i, int j, int width, int height, byte colour)
        {
            float plane = sign > 0 ? d + 1 : d;
            Vector3 Corner(int du, int dv)
            {
                var p = new float[3];
                p[axis] = plane;
                p[u] = i + du;
                p[v] = j + dv;
                return new Vector3(origin.x + p[0], origin.y + p[1], origin.z + p[2]);
            }

            Vector3 normal = Vector3.zero;
            normal[axis] = sign;
            Vector3[] corners = { Corner(0, 0), Corner(width, 0), Corner(width, height), Corner(0, height) };

            // Counter-clockwise seen from outside, in Blockage's right-handed axes.
            if (Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[3] - corners[0]), normal) < 0f)
            {
                corners = new[] { corners[0], corners[3], corners[2], corners[1] };
            }

            int first = vertices.Count;
            var uv = new Vector2((colour + 0.5f) / 256f, 0.5f);
            foreach (Vector3 corner in corners)
            {
                vertices.Add(Mirror(corner));
                normals.Add(Mirror(normal));
                uvs.Add(uv);
            }

            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first);
            triangles.Add(first + 3);
            triangles.Add(first + 2);
        }

        /// <summary>
        /// Boxes that fill the voxels, for colliders: from each voxel not yet in one, grown along X,
        /// then Z, then Y, a chunk at a time — in Unity's axes, as the mesh is.
        /// </summary>
        public static List<Bounds> Boxes(VoxelGrid grid)
        {
            var boxes = new List<Bounds>();
            var taken = new bool[S * S * S];
            foreach (KeyValuePair<Vector3Int, byte[]> pair in grid.Chunks)
            {
                byte[] cells = pair.Value;
                System.Array.Clear(taken, 0, taken.Length);
                bool Open(int x, int y, int z) => cells[VoxelGrid.Index(x, y, z)] != 0 && !taken[VoxelGrid.Index(x, y, z)];

                for (int y = 0; y < S; y++)
                {
                    for (int z = 0; z < S; z++)
                    {
                        for (int x = 0; x < S; x++)
                        {
                            if (!Open(x, y, z))
                            {
                                continue;
                            }

                            int x1 = x + 1;
                            while (x1 < S && Open(x1, y, z))
                            {
                                x1++;
                            }

                            int z1 = z + 1;
                            while (z1 < S && Span(x, x1, y, y + 1, z1, z1 + 1, Open))
                            {
                                z1++;
                            }

                            int y1 = y + 1;
                            while (y1 < S && Span(x, x1, y1, y1 + 1, z, z1, Open))
                            {
                                y1++;
                            }

                            for (int yy = y; yy < y1; yy++)
                            {
                                for (int zz = z; zz < z1; zz++)
                                {
                                    for (int xx = x; xx < x1; xx++)
                                    {
                                        taken[VoxelGrid.Index(xx, yy, zz)] = true;
                                    }
                                }
                            }

                            Vector3 low = pair.Key * S + new Vector3Int(x, y, z);
                            Vector3 high = pair.Key * S + new Vector3Int(x1, y1, z1);
                            var box = new Bounds();
                            box.SetMinMax(Vector3.Min(Mirror(low), Mirror(high)), Vector3.Max(Mirror(low), Mirror(high)));
                            boxes.Add(box);
                        }
                    }
                }
            }

            return boxes;
        }

        private static bool Span(int x0, int x1, int y0, int y1, int z0, int z1, System.Func<int, int, int, bool> open)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int z = z0; z < z1; z++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        if (!open(x, y, z))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>Blockage's right-handed axes to Unity's left-handed ones: X turned round.</summary>
        public static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        public static Quaternion Mirror(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
    }
}
