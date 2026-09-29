// Checks what Blockage exported once Unity has imported it (Fullreleaseplan 8.6). Copied into a
// throwaway project's Assets/Editor by tools/validate-exports.ps1, with the exported files in
// Assets/Imported, and run there with:
//
//     Unity -batchmode -nographics -projectPath <project> -executeMethod ValidateImport.Run
//
// Every check logs PASS or FAIL; any FAIL makes Unity exit with 1. The results go to
// validate-result.txt beside the project's Assets as well, for the script to show.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ValidateImport
{
    private static readonly string[] Meshes = { "Demo", "Crate", "Crate copy", "Table", "Cup", "Small crate" };

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

        foreach ((string file, bool sceneGraph, string spaces) in new[] { ("Assets/Imported/sample.fbx", true, " "), ("Assets/Imported/sample.obj", false, "_") })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(file);
            Check(root != null, $"{file}: imports");
            if (root == null)
            {
                continue;
            }

            Transform Find(string name) =>
                root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name.Replace(" ", spaces));

            foreach (string name in Meshes)
            {
                Transform part = Find(name);
                MeshFilter filter = part != null ? part.GetComponent<MeshFilter>() : null;
                Check(filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0, $"{file}: {name} has a mesh");
                MeshRenderer renderer = part != null ? part.GetComponent<MeshRenderer>() : null;
                Check(renderer != null && renderer.sharedMaterial != null, $"{file}: {name} has a material");
            }

            // A metre a voxel: the table is six by three by four, whichever way the axes turn.
            Transform table = Find("Table");
            if (table != null && table.GetComponent<MeshFilter>() is { } tableFilter && tableFilter.sharedMesh != null)
            {
                Vector3 size = Vector3.Scale(tableFilter.sharedMesh.bounds.size, table.lossyScale);
                float[] sides = { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) };
                System.Array.Sort(sides);
                Check(Mathf.Abs(sides[0] - 3f) < 0.05f && Mathf.Abs(sides[1] - 4f) < 0.05f && Mathf.Abs(sides[2] - 6f) < 0.05f,
                    $"{file}: Table is 6 x 3 x 4 metres ({size.x:0.##} x {size.y:0.##} x {size.z:0.##})");
            }

            if (sceneGraph)
            {
                Check(table != null && table.TryGetComponent(out MeshFilter lit) && lit.sharedMesh.uv2.Length > 0, $"{file}: Table has lightmap UVs");
                Transform cup = Find("Cup");
                Check(cup != null && cup.parent != null && cup.parent.name == "Table", $"{file}: Cup is under Table");
                Transform crate = Find("Crate"), copy = Find("Crate copy");
                Check(crate != null && copy != null && crate.GetComponent<MeshFilter>().sharedMesh == copy.GetComponent<MeshFilter>().sharedMesh,
                    $"{file}: Crate and its copy share one mesh");
                Check(Find("Spawn") != null, $"{file}: Spawn is there");
            }
        }

        // Levels of detail, named as Unity makes an LOD group of: one, on the model's root, for the whole file.
        var detailed = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Imported/sample-lods.fbx");
        Check(detailed != null, "Assets/Imported/sample-lods.fbx: imports");
        if (detailed != null)
        {
            LODGroup group = detailed.GetComponent<LODGroup>();
            Check(group != null && group.lodCount == 3, "Assets/Imported/sample-lods.fbx: the model is an LOD group of three");
            Check(group != null && group.GetLODs()[0].renderers.Any(r => r.name == "Table_LOD0") && group.GetLODs()[2].renderers.Any(r => r.name == "Table_LOD2"),
                "Assets/Imported/sample-lods.fbx: Table's levels are in it");
            Check(group != null && group.GetLODs().All(lod => lod.renderers.Length > 0 && lod.renderers[0].GetComponent<MeshFilter>().sharedMesh.vertexCount > 0),
                "Assets/Imported/sample-lods.fbx: every level has a mesh");
            if (group != null && group.lodCount == 3)
            {
                int[] counts = group.GetLODs().Select(lod => lod.renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount)).ToArray();
                Check(counts[1] <= counts[0] && counts[2] <= counts[1], $"Assets/Imported/sample-lods.fbx: each level is coarser ({string.Join(", ", counts)} vertices)");
            }
        }

        lines.Add($"{failures} failure(s)");
        File.WriteAllLines(Path.Combine(Directory.GetCurrentDirectory(), "validate-result.txt"), lines);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
