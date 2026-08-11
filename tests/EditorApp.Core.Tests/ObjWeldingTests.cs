using System.Globalization;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests;

/// <summary>
/// The exported mesh has to be one connected surface, not a pile of loose quads. Select-linked in a
/// DCC tool walks shared vertices; four unshared corners per quad means every face is its own island.
/// </summary>
public class ObjWeldingTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "editorapp-obj-weld", Guid.NewGuid().ToString("N"));

    public ObjWeldingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record Obj(List<float[]> Positions, List<float[]> Uvs, List<float[]> Normals, List<int[][]> Faces);

    private static Obj Parse(string path)
    {
        var obj = new Obj([], [], [], []);

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            static float N(string s) => float.Parse(s, CultureInfo.InvariantCulture);

            switch (parts[0])
            {
                case "v": obj.Positions.Add([N(parts[1]), N(parts[2]), N(parts[3])]); break;
                case "vt": obj.Uvs.Add([N(parts[1]), N(parts[2])]); break;
                case "vn": obj.Normals.Add([N(parts[1]), N(parts[2]), N(parts[3])]); break;
                case "f":
                    obj.Faces.Add([.. parts[1..].Select(c => c.Split('/').Select(int.Parse).ToArray())]);
                    break;
            }
        }

        return obj;
    }

    private static VoxelWorld Cube(int side)
    {
        var grid = new VoxelWorld();
        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                for (int z = 0; z < side; z++)
                {
                    grid.SetVoxel(x, y, z, Palette.WhiteIndex);
                }
            }
        }

        return grid;
    }

    private Obj Export(VoxelScene scene, string name = "level")
    {
        string path = Path.Combine(_directory, name + ".obj");
        new ObjExporter().Export(GreedyMesher.BuildScene(scene), scene.Palette, path, new ExportOptions());
        return Parse(path);
    }

    private static VoxelScene SceneOf(VoxelWorld grid)
    {
        var scene = new VoxelScene();
        scene.Add(grid, ObjectTransform.Identity, "cube");
        return scene;
    }

    [Fact]
    public void ASingleColourCubeHasEightVerticesNotTwentyFour()
    {
        // Six merged quads meeting at eight corners. Unwelded it would be 24.
        Obj obj = Export(SceneOf(Cube(4)));

        Assert.Equal(6, obj.Faces.Count);
        Assert.Equal(8, obj.Positions.Count);
    }

    [Fact]
    public void EveryFaceCornerKeepsItsOwnNormal()
    {
        // Welding positions must not weld normals: a cube corner belongs to three faces pointing
        // three different ways, and a shared normal there would round the cube off in the shading.
        Obj obj = Export(SceneOf(Cube(4)));

        Assert.Equal(6, obj.Normals.Count);

        foreach (int[][] face in obj.Faces)
        {
            int normal = face[0][2];
            Assert.All(face, corner => Assert.Equal(normal, corner[2]));
        }
    }

    [Fact]
    public void TheWholeSurfaceIsOneConnectedIsland()
    {
        // What select-linked actually does: walk faces through shared vertices and see how far it
        // gets. On the old output this reached exactly one face.
        Obj obj = Export(SceneOf(Cube(6)));

        var byVertex = new Dictionary<int, List<int>>();
        for (int f = 0; f < obj.Faces.Count; f++)
        {
            foreach (int[] corner in obj.Faces[f])
            {
                (byVertex.TryGetValue(corner[0], out List<int>? faces)
                    ? faces
                    : byVertex[corner[0]] = []).Add(f);
            }
        }

        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
        {
            foreach (int[] corner in obj.Faces[queue.Dequeue()])
            {
                foreach (int neighbour in byVertex[corner[0]])
                {
                    if (seen.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }

        Assert.Equal(obj.Faces.Count, seen.Count);
    }

    [Fact]
    public void EveryIndexPointsAtSomethingThatExists()
    {
        var grid = Cube(5);
        grid.SetFaceColor(new Int3(2, 4, 2), Face.PosY, 90);
        grid.SetVoxel(1, 4, 1, 40);

        Obj obj = Export(SceneOf(grid));

        foreach (int[][] face in obj.Faces)
        {
            Assert.Equal(4, face.Length);
            foreach (int[] corner in face)
            {
                Assert.InRange(corner[0], 1, obj.Positions.Count);
                Assert.InRange(corner[1], 1, obj.Uvs.Count);
                Assert.InRange(corner[2], 1, obj.Normals.Count);
            }
        }
    }

    [Fact]
    public void DifferentColoursStillGetDifferentUvsAtASharedCorner()
    {
        // The reason UVs are indexed separately. Two faces of different colours meet at a corner
        // that is one position; sharing its UV would repaint one of them.
        var grid = Cube(4);
        grid.SetVoxel(0, 3, 0, 40);

        Obj obj = Export(SceneOf(grid));

        Assert.True(obj.Uvs.Count > 1, "A two-colour level should not collapse to one UV.");

        var uvsPerPosition = new Dictionary<int, HashSet<int>>();
        foreach (int[][] face in obj.Faces)
        {
            foreach (int[] corner in face)
            {
                (uvsPerPosition.TryGetValue(corner[0], out HashSet<int>? set)
                    ? set
                    : uvsPerPosition[corner[0]] = []).Add(corner[1]);
            }
        }

        Assert.Contains(uvsPerPosition, entry => entry.Value.Count > 1);
    }

    [Fact]
    public void WeldingDoesNotMoveAnything()
    {
        // Same surface, same size — only the way it is written down changed.
        VoxelScene scene = SceneOf(Cube(5));
        ExportMesh mesh = GreedyMesher.BuildScene(scene);
        Obj obj = Export(scene);

        (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = mesh.Bounds();

        Assert.Equal(min.X, obj.Positions.Min(p => p[0]), 4);
        Assert.Equal(max.X, obj.Positions.Max(p => p[0]), 4);
        Assert.Equal(min.Y, obj.Positions.Min(p => p[1]), 4);
        Assert.Equal(max.Y, obj.Positions.Max(p => p[1]), 4);
        Assert.Equal(mesh.QuadCount, obj.Faces.Count);
    }

    [Fact]
    public void SeparateObjectsStaySeparate()
    {
        // Welding is not a licence to fuse pieces that merely touch. Two objects, no shared corner.
        var scene = new VoxelScene();
        scene.Add(Cube(3), ObjectTransform.Identity, "a");
        scene.Add(Cube(3), new ObjectTransform(new System.Numerics.Vector3(20f, 0f, 0f), System.Numerics.Quaternion.Identity), "b");

        Obj obj = Export(scene, "two");

        Assert.Equal(16, obj.Positions.Count);
    }
}
