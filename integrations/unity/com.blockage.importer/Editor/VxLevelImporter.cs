using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockage.Importer
{
    /// <summary>
    /// Blockage's .vxlevel, straight into Unity (Fullreleaseplan 8.7): a prefab of the level — each
    /// object a mesh in the palette's colours and materials, under its parent as in Blockage, linked
    /// copies sharing one mesh; colliders; markers and custom properties for the game to read; the
    /// level's lights. Saved again in Blockage, it is imported again by itself.
    /// </summary>
    [ScriptedImporter(3, "vxlevel")]
    public sealed class VxLevelImporter : ScriptedImporter
    {
        public enum ColliderMode
        {
            None,

            /// <summary>A mesh collider on each object's own mesh.</summary>
            Mesh,

            /// <summary>Boxes filling each object's voxels: cheaper to collide with, and fine for moving bodies too.</summary>
            Boxes,
        }

        [Tooltip("How many metres a voxel of size 1 is in Blockage.")]
        public float scale = 1f;

        public ColliderMode colliders = ColliderMode.Mesh;

        [Tooltip("The level's sun, point and spot lights.")]
        public bool lights = true;

        [Tooltip("Marks the level's objects static, for lightmapping and batching. Markers stay movable.")]
        public bool markStatic = true;

        [Tooltip("A second set of UVs for baking light, laid out by Unity's own unwrapper.")]
        public bool lightmapUVs = true;

        [Tooltip("Half and quarter-size copies of each object in an LOD group, to be seen from further off.")]
        public bool lods;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            Level level;
            try
            {
                level = VxLevel.Read(ctx.assetPath);
            }
            catch (Exception problem)
            {
                ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)} could not be read: {problem.Message}");
                return;
            }

            var root = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));
            ctx.AddObjectToAsset("level", root);
            ctx.SetMainObject(root);

            Material material = PaletteMaterial(ctx, level);
            var made = new Dictionary<int, GameObject>();
            var meshes = new Dictionary<VoxelGrid, Mesh>();
            var levels = new Dictionary<(VoxelGrid, int), Mesh>();

            foreach (LevelObject o in level.Objects)
            {
                // What Blockage's own exports take; notes are for the people working on the level.
                if (!level.IsExported(o.Collection, o.Visible) || o.Marker == "note")
                {
                    continue;
                }

                var node = new GameObject(o.Name);
                node.transform.SetParent(root.transform, false);
                node.transform.localPosition = VoxelMesher.Mirror(o.Position) * scale;
                node.transform.localRotation = VoxelMesher.Mirror(o.Rotation);
                node.transform.localScale = Vector3.one * (o.VoxelSize * scale);
                made[o.Id] = node;

                if (o.Grid.Chunks.Count > 0 && !o.Grid.IsEmpty)
                {
                    VoxelGrid shown = o.Modifiers.Count > 0 ? ModifierStack.Apply(o.Grid, o.Modifiers) : o.Grid;
                    if (!meshes.TryGetValue(shown, out Mesh mesh))
                    {
                        mesh = VoxelMesher.Build(shown, o.Name);
                        if (lightmapUVs)
                        {
                            Unwrapping.GenerateSecondaryUVSet(mesh);
                        }

                        ctx.AddObjectToAsset($"mesh-{o.Id}", mesh);
                        meshes[shown] = mesh;
                    }

                    node.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer renderer = node.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    AddColliders(node, shown, mesh);
                    if (lods)
                    {
                        AddLevelsOfDetail(ctx, node, o, shown, renderer, material, levels);
                    }
                    if (markStatic)
                    {
                        GameObjectUtility.SetStaticEditorFlags(node,
                            StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic
                            | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                    }
                }

                if (o.Marker != null)
                {
                    AddMarker(node, o);
                }

                if (o.Properties.Count > 0)
                {
                    node.AddComponent<BlockageProperties>().entries = new List<BlockageProperties.Entry>(o.Properties);
                }
            }

            // Under their parents, each where it stood: Blockage keeps where things are in the world.
            foreach (LevelObject o in level.Objects)
            {
                if (made.TryGetValue(o.Id, out GameObject child) && o.Parent != 0 && made.TryGetValue(o.Parent, out GameObject parent) && parent != child)
                {
                    child.transform.SetParent(parent.transform, true);
                }
            }

            if (lights)
            {
                foreach (LevelLight light in level.Lights)
                {
                    GameObject node = AddLight(root, light);
                    if (light.Parent != 0 && made.TryGetValue(light.Parent, out GameObject parent))
                    {
                        node.transform.SetParent(parent.transform, true);
                    }
                }
            }
        }

        /// <summary>How much coarser each level after the first is, and the share of the screen below which it takes over.</summary>
        private static readonly (int Factor, float Below)[] Levels = { (2, 0.08f), (4, 0.01f) };

        /// <summary>
        /// The object's coarser copies as children, each block of voxels one voxel, scaled back up to
        /// its size — and an LOD group choosing between them by how much of the screen it fills.
        /// </summary>
        private void AddLevelsOfDetail(AssetImportContext ctx, GameObject node, LevelObject o, VoxelGrid shown, MeshRenderer full, Material material, Dictionary<(VoxelGrid, int), Mesh> made)
        {
            var steps = new List<LOD> { new LOD(0.25f, new Renderer[] { full }) };
            for (int level = 0; level < Levels.Length; level++)
            {
                (int factor, float below) = Levels[level];
                if (!made.TryGetValue((shown, factor), out Mesh coarse))
                {
                    coarse = VoxelMesher.Build(VoxelMesher.Downsample(shown, factor), $"{o.Name}_LOD{level + 1}");
                    ctx.AddObjectToAsset($"mesh-{o.Id}-lod{level + 1}", coarse);
                    made[(shown, factor)] = coarse;
                }

                var child = new GameObject($"{o.Name}_LOD{level + 1}");
                child.transform.SetParent(node.transform, false);
                child.transform.localScale = Vector3.one * factor;
                child.AddComponent<MeshFilter>().sharedMesh = coarse;
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                steps.Add(new LOD(below, new Renderer[] { renderer }));
                if (markStatic)
                {
                    GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(node));
                }
            }

            // Full detail while it fills a quarter of the screen's height; the coarsest until it is a speck.
            LODGroup group = node.AddComponent<LODGroup>();
            group.SetLODs(steps.ToArray());
            group.RecalculateBounds();
        }

        private void AddColliders(GameObject node, VoxelGrid grid, Mesh mesh)
        {
            switch (colliders)
            {
                case ColliderMode.Mesh:
                    node.AddComponent<MeshCollider>().sharedMesh = mesh;
                    break;
                case ColliderMode.Boxes:
                    foreach (Bounds box in VoxelMesher.Boxes(grid))
                    {
                        BoxCollider collider = node.AddComponent<BoxCollider>();
                        collider.center = box.center;
                        collider.size = box.size;
                    }

                    break;
            }
        }

        private void AddMarker(GameObject node, LevelObject o)
        {
            BlockageMarker marker = node.AddComponent<BlockageMarker>();
            switch (o.Marker)
            {
                case "spawn": marker.kind = BlockageMarkerKind.Spawn; break;
                case "trigger": marker.kind = BlockageMarkerKind.Trigger; break;
                case "sound": marker.kind = BlockageMarkerKind.Sound; break;
                default: marker.kind = BlockageMarkerKind.Empty; break;
            }

            marker.size = o.MarkerSize * scale;

            // A trigger's box, standing on the marker, as a trigger collider in the node's own units.
            if (marker.kind == BlockageMarkerKind.Trigger)
            {
                float unit = Mathf.Max(o.VoxelSize, 1e-6f);
                BoxCollider box = node.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = o.MarkerSize / unit;
                box.center = new Vector3(0f, o.MarkerSize.y * 0.5f / unit, 0f);
            }
        }

        private GameObject AddLight(GameObject root, LevelLight light)
        {
            var node = new GameObject(light.Name);
            node.transform.SetParent(root.transform, false);
            node.transform.localPosition = VoxelMesher.Mirror(light.Position) * scale;

            // Blockage's lights shine along their own −Y; Unity's along their forward.
            Vector3 shines = VoxelMesher.Mirror(light.Rotation * Vector3.down);
            Vector3 up = VoxelMesher.Mirror(light.Rotation * Vector3.forward);
            node.transform.localRotation = Quaternion.LookRotation(shines, up);
            node.SetActive(light.Visible);

            Light lamp = node.AddComponent<Light>();
            lamp.color = light.Colour;
            lamp.intensity = light.Intensity;
            switch (light.Kind)
            {
                case "directional":
                    lamp.type = LightType.Directional;
                    lamp.shadows = LightShadows.Soft;
                    break;
                case "spot":
                    lamp.type = LightType.Spot;
                    lamp.range = light.Range * scale;
                    lamp.spotAngle = light.SpotAngle;
                    lamp.innerSpotAngle = light.SpotAngle * Mathf.Clamp01(1f - light.SpotBlend);
                    break;
                default:
                    lamp.type = LightType.Point;
                    lamp.range = light.Range * scale;
                    break;
            }

            return node;
        }

        /// <summary>
        /// One material for the whole level: the palette as a strip of texels, and where any colour
        /// shines, is metal or glossy, the maps that say so. Built-in, URP or HDRP, whichever is in use.
        /// </summary>
        private static Material PaletteMaterial(AssetImportContext ctx, Level level)
        {
            Texture2D colours = Strip("palette", i => level.Palette[i]);
            ctx.AddObjectToAsset("palette", colours);

            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = (urp ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") : null) ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "palette", mainTexture = colours };

            bool shiny = false, glowing = false;
            foreach (LevelMaterial entry in level.Materials.Values)
            {
                shiny |= entry.Metallic > 0f || entry.Roughness < 1f;
                glowing |= entry.Emission > 0f;
            }

            if (shiny)
            {
                Texture2D metal = Strip("palette-metallic", i => level.Materials.TryGetValue(i, out LevelMaterial m)
                    ? new Color(m.Metallic, 0f, 0f, 1f - m.Roughness)
                    : new Color(0f, 0f, 0f, 0f));
                ctx.AddObjectToAsset("palette-metallic", metal);
                material.SetTexture("_MetallicGlossMap", metal);
                material.EnableKeyword(urp ? "_METALLICSPECGLOSSMAP" : "_METALLICGLOSSMAP");
                material.SetFloat("_GlossMapScale", 1f);
                material.SetFloat("_Smoothness", 1f);
            }

            if (glowing)
            {
                Texture2D glow = Strip("palette-emission", i => level.Materials.TryGetValue(i, out LevelMaterial m)
                    ? (Color)level.Palette[i] * m.Emission
                    : Color.black);
                ctx.AddObjectToAsset("palette-emission", glow);
                material.SetTexture("_EmissionMap", glow);
                material.SetColor("_EmissionColor", Color.white);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            ctx.AddObjectToAsset("material", material);
            return material;
        }

        private static Texture2D Strip(string name, Func<int, Color> texel)
        {
            var texture = new Texture2D(256, 1, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                pixels[i] = texel(i);
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }
    }
}
