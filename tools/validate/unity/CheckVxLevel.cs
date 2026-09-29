// Checks the Blockage Importer package (Fullreleaseplan 8.7) in a throwaway Unity project, with the
// package added from integrations/unity and the sample level at Assets/Levels/sample.vxlevel:
//
//     Unity -batchmode -nographics -projectPath <project> -executeMethod CheckVxLevel.Run
//
// The level is imported as a prefab and looked over; then the file is written again with an object
// renamed, as saving in Blockage would, and the prefab must follow. Every check logs PASS or FAIL, the lines
// go to vxlevel-result.txt, and any FAIL makes Unity exit with 1.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Blockage;
using UnityEditor;
using UnityEngine;

public static class CheckVxLevel
{
    private const string LevelPath = "Assets/Levels/sample.vxlevel";

    public static void Run()
    {
        var lines = new List<string>();
        int failures = 0;

        void Check(bool ok, string message)
        {
            string line = (ok ? "PASS " : "FAIL ") + message;
            lines.Add(line);
            Debug.Log(line);
            if (!ok)
            {
                failures++;
            }
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPath);
        Check(root != null, "the level imports as a prefab");

        if (root != null)
        {
            Transform Find(string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

            foreach (string name in new[] { "Demo", "Crate", "Crate copy", "Table", "Cup", "Small crate" })
            {
                Transform part = Find(name);
                bool meshed = part != null && part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0;
                Check(meshed, $"{name} has a mesh");
                Check(part != null && part.TryGetComponent(out MeshRenderer renderer) && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null, $"{name} has the palette material");
                Check(part != null && part.TryGetComponent(out MeshCollider collider) && collider.sharedMesh != null, $"{name} has a collider");
            }

            Transform table = Find("Table");
            if (table != null && table.TryGetComponent(out MeshFilter tableMesh))
            {
                Bounds local = tableMesh.sharedMesh.bounds;
                Vector3 size = Vector3.Scale(local.size, table.lossyScale);
                Vector3 low = table.TransformPoint(local.min), high = table.TransformPoint(local.max);
                Check(Mathf.Abs(size.x - 6f) < 0.01f && Mathf.Abs(size.y - 3f) < 0.01f && Mathf.Abs(size.z - 4f) < 0.01f, $"Table is 6 x 3 x 4 metres ({size.x:0.##} x {size.y:0.##} x {size.z:0.##})");
                Check(Mathf.Abs(Mathf.Min(low.x, high.x) + 26f) < 0.01f && Mathf.Abs(Mathf.Min(low.z, high.z)) < 0.01f, "Table stands where it stood, X turned round for Unity");
            }

            Transform cup = Find("Cup");
            Check(cup != null && cup.parent == table, "Cup is under Table");

            // Faces wound as Unity draws them: the normals Unity works out from the winding are the ones written.
            if (cup != null && cup.TryGetComponent(out MeshFilter cupMesh))
            {
                Mesh wound = Object.Instantiate(cupMesh.sharedMesh);
                wound.RecalculateNormals();
                Vector3[] written = cupMesh.sharedMesh.normals, worked = wound.normals;
                Check(written.Length > 0 && Enumerable.Range(0, written.Length).All(i => Vector3.Dot(written[i], worked[i]) > 0.9f), "faces are wound the way Unity draws them");
            }
            Transform crate = Find("Crate"), copy = Find("Crate copy");
            Check(crate != null && copy != null && crate.GetComponent<MeshFilter>().sharedMesh == copy.GetComponent<MeshFilter>().sharedMesh, "Crate and its copy share one mesh");

            Transform small = Find("Small crate");
            Check(small != null && Mathf.Abs(small.lossyScale.x - 0.5f) < 1e-4f, "Small crate keeps its half-size voxels");

            Transform spawn = Find("Spawn");
            Check(spawn != null && spawn.TryGetComponent(out BlockageMarker marker) && marker.kind == BlockageMarkerKind.Spawn, "Spawn is a spawn marker");
            Check(spawn != null && spawn.TryGetComponent(out BlockageProperties properties) && properties.GetString("team") == "red" && Mathf.Approximately(properties.GetNumber("lives"), 3f),
                "Spawn keeps its properties for the game");

            Transform sun = Find("Sun"), lamp = Find("Lamp");
            Check(sun != null && sun.TryGetComponent(out Light sunLight) && sunLight.type == LightType.Directional, "the sun is a directional light");
            Check(sun != null && Vector3.Dot(sun.forward, Vector3.down) > 0.3f, "the sun shines down");
            Check(lamp != null && lamp.TryGetComponent(out Light lampLight) && lampLight.type == LightType.Point && Mathf.Abs(lampLight.range - 20f) < 0.01f, "the lamp is a point light that reaches 20 metres");

            // Saved again in Blockage: the file changes under Unity, which imports it again.
            string edited = Path.Combine(Path.GetTempPath(), "blockage-check.vxlevel");
            File.Copy(LevelPath, edited, overwrite: true);
            using (ZipArchive zip = ZipFile.Open(edited, ZipArchiveMode.Update))
            {
                ZipArchiveEntry manifest = zip.GetEntry("manifest.json");
                string json;
                using (var reader = new StreamReader(manifest.Open()))
                {
                    json = reader.ReadToEnd();
                }

                manifest.Delete();
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                {
                    writer.Write(json.Replace("\"Crate copy\"", "\"Crate moved\""));
                }
            }

            File.Copy(edited, LevelPath, overwrite: true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var again = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPath);
            List<string> names = again != null ? again.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList() : new List<string>();
            Check(names.Contains("Crate moved") && !names.Contains("Crate copy"), "saved again, the level is imported again");
        }

        lines.Add($"{failures} failure(s)");
        File.WriteAllLines("vxlevel-result.txt", lines);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
