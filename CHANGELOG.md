# Changelog

What each version of Blockage brought. Only 0.1.0 has been published as a release so far; the
versions after it are the milestones of the development builds. Until 1.0 the `.vxlevel` format can
still change, but every version opens the levels saved by the ones before it.

## [0.9.0] — in development

Performance, platforms and the road to 1.0.

### Added
- Continuous integration: both test suites run on Windows, Linux and macOS for every push and pull
  request, and every run measures a large level.
- A release pipeline. A version tag builds the Windows, macOS and Linux packages and puts them, with
  their SHA-256 sums, in a draft release. The Windows build is signed once a certificate is set up.
- A Linux build, as a tarball with an icon and `install.sh`, which puts Blockage in the applications
  menu for you alone (`./install.sh --remove` takes it out again).
- A Mac app. The macOS build comes as `Blockage.app`, with its icon, signed ad hoc; once a Developer
  ID is set up it is signed with that and notarized. On a Mac, Command works wherever Blockage says
  Ctrl.
- This changelog, notes for contributors, and templates for issues and pull requests.
- Samples on the welcome screen: an island, a village and a cave, made from the generators and
  props, to look round and take apart.
- A tour, from the welcome screen or **Help › Take the Tour**: seven steps on a fresh cube, each
  passed as soon as it is done, naming the keys as the keymap has them.
- News of a newer release, on the welcome screen and in the Help menu. Blockage asks GitHub once as it
  starts and sends nothing else; **Preferences › Startup › New versions** turns it off.
- After a crash, Blockage offers to report it the next time it starts: GitHub's bug form, filled in
  with the version and the crash log, to read over before sending. **Help › Report a Problem**
  opens the form at any time.
- A manual, published from `docs/` by GitHub Pages and linked from the welcome screen and the Help
  menu. Its page of keys is written by `--write-shortcuts` from the Default keymap.

### Fixed
- The welcome screen said the project's page asked to sign in; it is public.
- **File › Export** named OBJ and glTF but not FBX.

### Changed
- Large levels are much faster. On a 512 × 128 × 512 level of hills and caves, every part is ready
  to work on in a second or so instead of 16, and the whole level draws two to four times faster.
  - Faces that look alike are merged into one.
  - Parts of the level are meshed on worker threads.
  - The panels no longer walk every voxel in every frame.
- The `.vxlevel` format is frozen for 1.0 as version 7, and described in
  [docs/vxlevel-format.md](docs/vxlevel-format.md). Version 7 marks what came since 6 was first
  written — linked copies, modifiers, collections, markers and more — so an older build refuses the
  file rather than opening it with those missing. Files of every earlier version still open, checked
  against files saved by each version's own writer. The Unity package (0.9.0) reads version 7.
- ImGui's memory of its windows (`imgui.ini`) is kept with the layout in the settings folder, not in
  whatever folder Blockage was started from. Started from the Finder, Blockage browses and exports
  from the home folder rather than from `/`.

## [0.8.0] — 2026-09-29

Working with other tools.

### Added
- MagicaVoxel `.vox` in and out: models, the scene, layers, linked copies, the palette and its
  materials.
- Images into voxels: pixel-art sprites stood up with a thickness, and heightmaps raised into
  terrain.
- Meshes into voxels: OBJ (with its MTL), glTF and GLB, at the size you choose, in their colours and
  textures, solid or as a shell.
- glTF as a scene: objects under their parents, lights (`KHR_lights_punctual`), and the collision
  boxes Godot takes as collision.
- FBX export, with levels of detail that Unity makes an LOD group of.
- Lightmap UVs, as a second set in glTF and FBX.
- A Unity package in `integrations/unity`. It imports `.vxlevel` files as prefabs, and imports them
  again whenever Blockage saves: meshes, colliders, hierarchy, markers, properties, lights, lightmap
  UVs and levels of detail.
- `tools/validate-exports.ps1`, which opens every export in Blender and Unity and checks what
  arrives.

### Fixed
- Text glTF files named their metallic and emissive textures without writing them.
- Unity welded an OBJ export into one mesh.

## [0.7.0] — 2026-09-29

Viewing and workflow.

### Added
- Ambient occlusion and sun shadows in the viewport.
- A section box to see inside a level (Alt+B).
- Reference images on planes, to model over.
- A quad view: top, front and right beside the perspective (Ctrl+Alt+Q).
- Walking through the level at a player's size, with collision.
- An undo history to click back to any step (Ctrl+Alt+Z).
- Pie menus for shading (Z) and the view (`), and Quick Favorites (Shift+Q).
- Several levels open at once in tabs, with copy and paste between them.
- A measure tool (Shift+M), and notes left in the level.

### Changed
- The selection outline follows the object's real silhouette, not its box.

## [0.6.0] — 2026-09-29

Level design and layout.

### Added
- Collections, nested as in Blender, each with show, lock and export switches.
- Linked copies that share their voxels (Alt+D), exported as one shared mesh.
- A prop library of your own, and Append from another level.
- Markers (empties, spawn points, triggers, sound sources) and custom properties, sent to the game
  in glTF.
- Align and distribute.
- Scattering props over the ground.
- Generated terrain, caves, trees, rocks and buildings; the same seed gives the same result.

## [0.5.0] — 2026-09-29

Rendering.

### Added
- A path tracer for the level, on every CPU core or on the graphics card: soft shadows, light bounced
  off walls, glowing colours, metal and glass.
- Rendered shading in the viewport, and a Render window on F12: resolution, samples, exposure, sky,
  sun size, fog, bloom, depth of field and backdrop.
- Cameras kept with the level: perspective, orthographic or isometric. Look through one with
  Numpad 0.
- Turntables as GIF, PNGs or MP4 (with ffmpeg), sprite sheets, and a picture from every camera at
  once.
- A screenshot of the viewport without the overlays.

## [0.4.0] — 2026-09-29

Paint and materials.

### Added
- Voxel materials: glow, metal, roughness and glass, shown in the viewport and exported to glTF.
- Gradient, noise and dither fills in two colours, and Fill Enclosed.
- Replacing a colour everywhere, and shifting hue, saturation and value.
- Palettes in and out as `.gpl`, `.hex` and `.png` (Lospec's too), ramps, and a palette library.
- A stencil: an image laid over the view and painted onto the faces.

## [0.3.0] — 2026-09-29

Modelling.

### Added
- Edit Mode (Tab). Select voxels by click, box, magic wand or colour, and grow or shrink the
  selection. Move, turn and mirror it, separate it into a new object (P), fill it or delete it.
- Boolean union, difference and intersection between objects.
- Volume filters: hollow, thicken, thin, smooth, and clearing away small loose pieces.
- Resampling to half the resolution, or by any factor.
- A Sculpt tool (S): build up, carve, raise, flatten and smooth.
- Extrude draws a line, rectangle or ellipse on a face.
- Voxel lettering in the Add menu.
- Non-destructive Mirror and Array modifiers.

## [0.2.0] — 2026-09-29

Selection.

### Changed
- Selection works as in Blender: a set of selected objects and an active one. Hovering only
  highlights. The tools work on the selection, and the gizmo stays with it.
- Click, Shift+click and box select; A, Alt+A and Ctrl+I.
- Delete, duplicate, hide, lock, copy and paste act on the whole selection in one undo step.
  Ctrl+P parents it to the active object, and Ctrl+J joins it into it.
- Several objects move together, about their middle, the active object, or each its own centre.
- The Outliner and Properties follow the selection. With several objects selected, Alt sets a field
  on all of them.
- The last object can be deleted too, and an empty level saved.

## [0.1.0] — 2026-09-29

The first public release.

- Modelling by extrusion: select a surface as a box or a whole flat patch, and pull it out or push it
  in. Loop Cut splits an object in two.
- Painting per face: brush, bucket and pattern fills, an eyedropper on Alt, a 256-colour palette.
- Objects: a Transform gizmo with Blender-style snapping, parenting, join, duplicate, subdivide,
  quarter turns and mirrors, hide and lock.
- Shift+A: shapes, props and lights set down on the surface under the cursor.
- F3 command search, a right-click menu, overlays, X-Ray and wireframe, an Outliner, and rebindable
  keys with a *Default* and a *Mimic Busters* preset.
- A welcome screen with templates, recent files and Recover Last Session; autosave with crash
  recovery.
- Export to OBJ and glTF/GLB, greedy-meshed with a palette texture and automatic UVs, and to
  Mimicraft `.character` and `.weapons`.
- Windows and Android builds; an experimental macOS build.
