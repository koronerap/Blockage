# Blockage Importer for Unity

Drop a Blockage `.vxlevel` into your Assets and it arrives as a prefab. You don't need to export
first or keep an FBX next to it. Save the level again in Blockage and Unity imports it again on its
own.

What comes across:

- **Meshes.**
  - Each object is greedy-meshed in its palette's colours, painted faces included.
  - Mirror and Array modifiers are applied as Blockage shows them.
  - Linked copies share one mesh.
- **Materials.** One material for the whole level, with the palette as a strip of texels.
  - Where a colour glows or is metal or glossy, maps carry that too.
  - It works with the Built-in pipeline, URP or HDRP.
- **The hierarchy.** Every object is placed and turned where it stood, under its parent.
  - A metre is a voxel by default; set it with the importer's **Scale**.
- **Colliders.**
  - A mesh collider on each object, or boxes that fill its voxels, or none.
  - A trigger marker gets a trigger box.
- **Markers.** Spawn points, triggers, sounds and empties become empty objects with a
  `Blockage.BlockageMarker` telling their kind and size.
- **Custom properties.** On any object, as a `Blockage.BlockageProperties` the game can read:

  ```csharp
  var properties = GetComponent<Blockage.BlockageProperties>();
  string team = properties.GetString("team");
  float lives = properties.GetNumber("lives");
  ```

- **Lights.** The level's sun, point and spot lights come across as Unity lights.
- **Lightmap UVs.** A second UV set is laid out by Unity's own unwrapper, for baking light. It's on
  by default, and the objects are marked static to go with it.
- **Levels of detail.** Turn on **Lods** in the importer and each object gets half- and
  quarter-size copies in an LOD group of its own.

What stays behind:

- **Notes:** they are for the people working on the level.
- **Glass:** transparent colours are drawn opaque for now.
- **Objects that are hidden** or in a collection kept out of exports, as with Blockage's own exports.

## Installing

In Unity, open **Window › Package Manager**, choose **+ › Add package from git URL…**, and give:

```
https://github.com/koronerap/Blockage.git?path=/integrations/unity/com.blockage.importer
```

To install from a copy of the repository instead, choose **+ › Add package from disk…** and pick
`integrations/unity/com.blockage.importer/package.json`.

It needs Unity 2021.3 or later. It reads `.vxlevel` files up to version 6, the version Blockage
0.8 saves. A newer file says it wants a newer package.

## Checking it

`tools/validate-exports.ps1 -Unity` in the repository makes a throwaway project with this package
in it and imports the sample level. It checks every object, material, collider, parent, shared
mesh, marker, property and light, then saves the file again and checks the prefab follows.
