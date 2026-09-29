---
title: Export and other tools
nav_order: 7
---

# Export and other tools

## Exporting a mesh

**File › Export › Mesh** (**Ctrl+E**) writes the level as OBJ, glTF, GLB or FBX. The mesh is
greedy-merged, with a generated palette texture and automatic UVs.

- Each object is its own mesh, placed under its parent. Linked copies share one mesh.
- Markers go out as empties, with their custom properties for the game.
- **Lightmap UVs** add a second UV set, for baking light.
- glTF can also carry the level's **lights**, and **collision boxes** that Godot takes as collision.
- FBX can also carry **levels of detail**, half- and quarter-size copies that Unity makes an LOD
  group of.
- Hidden objects, and what is in a collection kept out of exports, stay behind.

**File › Export** also writes MagicaVoxel `.vox` files, and Mimicraft `.character` and `.weapons`.

## Bringing work in

**File › Import** reads:

- **MagicaVoxel `.vox`**: its models, its scene and layers, its palette and materials.
- **Image as a Sprite**: a pixel-art image stood up with a thickness.
- **Heightmap as Terrain**: a greyscale image raised into ground.
- **Mesh as Voxels**: an OBJ, glTF or GLB made into voxels at the size you choose, in its colours and
  textures, solid or as a shell.

A MagicaVoxel file comes in as a level of its own, named after it until you save it. The others are
added to the level you are in, where the view looks, ready to be moved into place.

## Straight into Unity

The **Blockage Importer** package imports `.vxlevel` files as prefabs, and imports them again
whenever Blockage saves them. It brings in the meshes, colliders and hierarchy, markers, properties
and lights, lightmap UVs, and levels of detail.

To install it, open **Window › Package Manager** in Unity, choose **+ › Add package from git URL...**,
and give:

```
https://github.com/koronerap/Blockage.git?path=/integrations/unity/com.blockage.importer
```

It needs Unity 2021.3 or later. Its
[README](https://github.com/koronerap/Blockage/tree/main/integrations/unity/com.blockage.importer)
has the details.
