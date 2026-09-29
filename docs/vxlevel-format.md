# The `.vxlevel` format

A Blockage level is one `.vxlevel` file. This page describes version 7, the format of Blockage 1.0,
closely enough to read and write it without Blockage. Version 7 is frozen: a 1.x build writes it
and reads every version before it.

## Container

The file is a zip archive, deflate-compressed, with these entries:

| Entry | What it holds |
|---|---|
| `manifest.json` | the level: its palette, its objects and where they stand, lights, cameras and settings |
| `objects/<id>/chunks/<x>_<y>_<z>.bin` | one chunk of an object's voxels, run-length coded |
| `objects/<id>/faces/<x>_<y>_<z>.bin` | the faces of that chunk painted another colour than their voxel; only when there are any |

`<id>` is the object's `id` in the manifest. `<x>_<y>_<z>` is the chunk's coordinate, which may be
negative, as in `objects/1/chunks/1_0_-1.bin`. A reader should ignore entries it does not know.

## Space and units

- The world is right-handed, with Y up. One world unit is whatever the game makes it, usually a metre.
- Each object is an integer lattice of voxels in its own space. Voxel `(x, y, z)` fills the cube from
  `(x, y, z)` to `(x + 1, y + 1, z + 1)`.
- An object's `voxelSize` is the world size of one voxel. Its `rotation` turns it about its own origin,
  and its `position` then places it. A local point `p` is at `rotation × (p × voxelSize) + position` in
  the world.
- Positions are world positions, a child's too. A child keeps its world position when it is parented;
  the manifest only says whose child it is.

## Chunks

An object's voxels come in chunks of 32 × 32 × 32. Voxel `(x, y, z)` is in chunk
`(floor(x / 32), floor(y / 32), floor(z / 32))`, at local `(x mod 32, y mod 32, z mod 32)`, each of
those from 0 to 31.

A chunk is 32768 palette indices, one byte each. Index 0 is empty space and 1 to 255 are colours of
the palette. They are ordered x fastest, then z, then y:

```
linear = (y << 10) | (z << 5) | x
```

The chunk entry codes them as runs of three bytes: a 16-bit little-endian count from 1 to 65535, then
the index repeated that many times. The counts add up to exactly 32768, so an empty or a uniform chunk
is a single run of three bytes. A chunk that is empty is not written at all.

## Painted faces

A voxel's six faces are the colour of the voxel unless painted otherwise. The faces entry of a chunk
lists the exceptions as records of five bytes:

| Bytes | Value |
|---|---|
| 0–3 | `linear × 6 + face`, a 32-bit little-endian integer |
| 4 | the palette index the face is painted |

`face` is 0 for +X, 1 for −X, 2 for +Y, 3 for −Y, 4 for +Z and 5 for −Z. Records are sorted by that
integer. A painted face belongs to its voxel: when the voxel is recoloured or removed, its paint goes.

## The manifest

`manifest.json` is UTF-8 JSON. A field written as optional below is left out when it has nothing to
say, and a reader takes its default. A reader should ignore fields it does not know.

### The level

| Field | Type | Meaning |
|---|---|---|
| `version` | integer | the format version; `7` |
| `name` | string | the level's name |
| `chunkSize` | integer | always `32` |
| `palette` | 256 strings | each colour as `#RRGGBBAA`; entry 0 is empty space, its value unused |
| `savedCustomSlots` | integers, optional | the entries 192–255 the user kept in the palette's custom row |
| `materials` | array, optional | palette entries that are not plain colour; see below |
| `boundsMin`, `boundsMax` | 3 integers each, optional | the world box round every voxel, rounded out to whole units; left out for an empty level |
| `chunks` | array | always empty; version 1's single grid |
| `objects` | array | the objects, in the level's order |
| `active` | integer, optional | the `id` of the active object |
| `collections` | array, optional | the collections |
| `lights` | array, optional | the lights; an empty array is a level left unlit |
| `ambient` | number, optional | how bright the shadows are, from 0 to 1 |
| `cameras` | array, optional | the cameras |
| `activeCamera` | integer, optional | the `id` of the camera renders are seen from |
| `render` | object, optional | render settings |
| `referenceImages` | array, optional | pictures to model over |
| `savedUtc` | string | when it was saved, ISO 8601 |

### Objects

| Field | Type | Meaning |
|---|---|---|
| `id` | integer | unique among the objects |
| `name` | string | its name |
| `position` | 3 numbers | where it is, in world units |
| `rotation` | 4 numbers | a unit quaternion, `[x, y, z, w]` |
| `voxelSize` | number | the world size of one voxel, from 0.001 to 1000 |
| `chunks` | array of `[x, y, z]` | the chunks written under `objects/<id>/` |
| `linkedTo` | integer, optional | a linked copy: the `id` of the object whose voxels it shares, which alone has them written |
| `visible` | boolean | whether it is shown and exported |
| `locked` | boolean, optional | whether it is locked; default `false` |
| `parent` | integer, optional | the `id` of the object it is a child of |
| `collection` | integer, optional | the `id` of the collection it is in |
| `selected` | boolean, optional | whether it was selected; default `false` |
| `modifiers` | array, optional | its modifiers, in the order they apply |
| `marker` | string, optional | a marker: `empty`, `spawn`, `trigger`, `sound` or `note` |
| `markerSize` | 3 numbers, optional | a marker's size in world units: the box a trigger fills, how far a sound carries (x), how big the others are drawn |
| `markerText` | string, optional | what a note says |
| `properties` | array, optional | custom properties for the game, each `{ "key", "type", "value" }`, `type` being `text`, `number` or `toggle` and `value` always a string |

A marker usually has no voxels. Hidden and locked are the object's own: a collection that is hidden
or locked hides or locks what is in it as well.

### Modifiers

A modifier changes what an object shows and exports, while its own voxels stay as they are.

| Field | Type | Meaning |
|---|---|---|
| `kind` | string | `mirror` or `array` |
| `axis` | string | `x`, `y` or `z` |
| `plane` | integer | mirror: the lattice line it mirrors across, counted in voxels from the object's origin |
| `count` | integer | array: how many in the row, the original included |
| `step` | integer | array: how many voxels one copy stands from the next; negative runs the other way |
| `enabled` | boolean | whether it applies |

### Collections

Each collection is listed after the one it is inside.

| Field | Type | Meaning |
|---|---|---|
| `id` | integer | unique among the collections |
| `name` | string | its name |
| `parent` | integer, optional | the `id` of the collection it is inside |
| `visible`, `locked`, `export` | booleans | its three switches; what is in a collection with `export` off is left out of exports |

### Materials

| Field | Type | Meaning |
|---|---|---|
| `index` | integer | the palette entry, 1 to 255 |
| `emission` | number | how much it glows, from 0 |
| `metallic` | number | 0 to 1 |
| `roughness` | number | 0 to 1; default `1` |
| `opacity` | number | 0 to 1; default `1`, and below 1 it is glass |

### Lights

| Field | Type | Meaning |
|---|---|---|
| `name` | string | its name |
| `kind` | string | `directional`, `point` or `spot` |
| `position` | 3 numbers | where it is |
| `rotation` | 4 numbers | a quaternion `[x, y, z, w]`; a light shines along its own −Y |
| `colour` | string | `#RRGGBB` |
| `intensity` | number | how strong it is |
| `range` | number | how far a point or spot light reaches |
| `spotAngle`, `spotBlend` | numbers | a spot's cone in degrees, and how soft its edge is from 0 to 1 |
| `visible`, `locked`, `selected` | booleans | as for objects; `locked` and `selected` optional |
| `parent`, `collection` | integers, optional | as for objects |

### Cameras

| Field | Type | Meaning |
|---|---|---|
| `id` | integer | unique among the cameras |
| `name` | string | its name |
| `position` | 3 numbers | where it is |
| `yaw` | number | degrees about +Y; 0 looks along +Z |
| `pitch` | number | degrees above the horizon |
| `kind` | string | `perspective`, `orthographic` or `isometric` |
| `fov` | number | perspective: the view's height in degrees |
| `orthographicHeight` | number | orthographic and isometric: the view's height in world units |
| `pivotDistance` | number | how far ahead the point it turns about is |

### Render settings

`width`, `height`, `samples`, `bounces` and `seed` are integers. `skyStrength`, `exposure`,
`emission`, `fog`, `aperture`, `focus`, `bloom` and `sunSize` are numbers, and `skyTop` and
`skyHorizon` are colours as three numbers from 0 to 1. `transparent` is a boolean. The optional
`backgroundColour` is a plain backdrop in place of the sky, and the optional `engine` is `gpu`,
or `cpu` when left out. The same level, settings and seed render the same picture.

### Reference images

| Field | Type | Meaning |
|---|---|---|
| `path` | string | the picture's path on disk, as it was when added |
| `plane` | string | `front`, `side` or `top` |
| `centre` | 3 numbers | the middle of the picture in the world |
| `width` | number | its width in world units |
| `opacity` | number | 0 to 1 |
| `onlyAligned`, `behind`, `visible` | booleans | shown only in the view that looks straight at it; drawn behind the model; shown at all |

The picture itself is not in the file.

## Versions

Every version stays readable. The Blockage tests open a file that each version's own writer saved.

| Version | Came with | What changed |
|---|---|---|
| 1 | the first saves | one grid, its chunks at `chunks/<x>_<y>_<z>.bin` and listed in the top-level `chunks` |
| 2 | objects | `objects`, each with a position and rotation, its chunks under `objects/<id>/` |
| 3 | painted faces | the `faces` entries |
| 4 | voxel size | a top-level `voxelSize` for the whole level; positions counted in those voxels |
| 5 | hiding | `visible` on objects; before it, everything was visible |
| 6 | a voxel size per object | `voxelSize` on each object, positions in world units; then lights and `ambient` |
| 7 | 1.0 | frozen: linked copies, modifiers, collections, markers and properties, materials, cameras, render settings and reference images, all of which came while 6 was still written |

Reading an older file:

- **Version 1** has no `objects`. Its one grid becomes an object named `Object 1` at the origin.
- **Versions 4 and 5** give every object the level's `voxelSize` and multiply each position by it.
  Before version 4 it is 1.
- **No `lights`**, as in files from before lights were saved, means the sun the viewport always had.
- A **`version` above the reader's own** means a newer Blockage wrote the file. Refuse it rather than
  guess: its fields could mean things the reader cannot know.

## Changing the format after 1.0

- A new field that an older reader can ignore and still be right needs no new version.
- A field an older reader would get wrong by ignoring it, or a field whose meaning changes, needs a
  new version. For example, a linked copy an older reader opened would be empty.
- Every version stays readable, and each new version gets a file its own writer saved in
  `tests/EditorApp.Core.Tests/Fixtures`.
