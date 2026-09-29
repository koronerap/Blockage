<p align="center">
  <img src="docs/images/editor.png" alt="Blockage: a small voxel scene of props and shapes, with the Outliner and the Object properties" width="100%">
</p>

# Blockage

**A voxel level editor with Blender's habits.** You shape volume by pulling faces out and pushing
them in — there is no place-a-block tool — then paint faces, cut objects in two, parent, light, and
export clean meshes for a game engine.

It grew out of the level and prop pipeline for the game *Mimic Busters*, and writes that game's
`.character` and `.weapons` files as well as OBJ and glTF.

> **Status: early — 0.1.** In daily use, but expect rough edges, and file-format changes before 1.0.

## Download

Get the latest build from [**Releases**](https://github.com/koronerap/Blockage/releases).

| Platform | File | Notes |
|---|---|---|
| Windows 10/11, x64 | `Blockage-0.1.0-win-x64.zip` | Unzip and run `Blockage.exe`. No .NET install needed. The build is not code-signed, so SmartScreen may stop it the first time: **More info → Run anyway**. |
| macOS, Apple Silicon | `Blockage-0.1.0-osx-arm64.tar.gz` | **Experimental: built, but not yet run on a Mac.** Unpack, clear the download quarantine with `xattr -dr com.apple.quarantine Blockage-0.1.0-osx-arm64`, and start `./Blockage` from a terminal inside it. |
| Android 8+ | `Blockage-0.1.0-android.apk` | Sideload it. A lighter companion on the same core: the four tools, the palette, undo, open and save. |

## What it does

- **Shape by extruding.** Drag across a surface to select it — a box, or a whole flat patch in one
  click — then pull the arrow out to add voxels or push it in to carve. Loop Cut splits an object in
  two along a plane.
- **Objects as a whole.** A Transform gizmo with Blender-style snapping (increments, corners, edge
  centres, surfaces), parenting, join, duplicate, subdivide, quarter turns and mirrors, hide and lock.
- **Paint face by face.** Brush, bucket and pattern fills, an eyedropper on Alt, a 256-colour palette.
- **Add with Shift+A.** Cubes, spheres, cylinders, cones, stairs, arches and more; ready-made props
  — crates, barrels, tables, trees — and lights, set down on the surface under the cursor and sized
  afterwards in an *Adjust* panel.
- **Blender where it helps.** F3 command search, a right-click context menu, overlays, X-Ray and
  wireframe, an Outliner with hierarchy, rebindable keys (a *Default* and a *Mimic Busters* preset), a
  welcome screen with templates.
- **Export.** OBJ and glTF/GLB, greedy-meshed, with a generated palette texture and automatic UVs;
  Mimicraft `.character` and `.weapons`.
- **Your work is kept.** Autosave with crash recovery, and the last session is kept at every exit, so
  a wrong click on "don't save" can be undone.

<p align="center">
  <img src="docs/images/add-menu.png" alt="The Shift+A menu, with the Mesh list open" width="54%">
  <img src="docs/images/welcome.png" alt="The welcome screen: templates, recent files, recovery" width="44%">
</p>

## Getting around

With the *Default* keymap — **F1** in the editor lists every key, and **Edit › Preferences › Keymap**
changes them.

| | |
|---|---|
| Look round, fly | hold the right mouse button; W A S D, Q E |
| Orbit, pan, zoom | middle mouse; Shift + middle; the wheel |
| Frame | Home for the level, F for the object |
| Tools | G move · R rotate · E extrude · B paint · Ctrl+R loop cut · V view |
| Add | Shift+A |
| Search for any command | F3 |
| Context menu | right click |
| Snap | hold Shift while dragging; Shift+Tab keeps it on |
| Undo, redo | Ctrl+Z, Ctrl+Shift+Z |

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and OpenGL 3.3. Windows is the
tested platform.

```
git clone https://github.com/koronerap/Blockage.git
cd Blockage
dotnet run --project src/EditorApp                            # the editor
dotnet test                                                   # the test suites
dotnet publish src/EditorApp -p:PublishProfile=win-x64        # a standalone build; or osx-arm64
```

The Android app needs the .NET Android workload, a JDK and the Android SDK; `build-android.ps1`
finds them and builds, installs and starts it. [TODO.md](TODO.md#android) has the details.

## Inside

| | |
|---|---|
| `src/EditorApp.Core` | the model, the tools, meshing, the file formats and the exporters — no graphics, no UI |
| `src/EditorApp` | the desktop editor: Silk.NET, OpenGL, Dear ImGui |
| `src/EditorApp.Mobile` | the Android app on the same core |
| `tests/` | the xUnit suites for both |
| [`EditorApp.md`](EditorApp.md) | the design, in Turkish |
| [`TODO.md`](TODO.md) | the build log: what was done and why, and what is left |

Levels are saved as `.vxlevel`: a zip holding a JSON manifest and run-length-encoded chunks of 32³
voxels.

## License

MIT — see [LICENSE](LICENSE). The libraries and assets it builds on, and their licenses, are listed in
[NOTICE.md](NOTICE.md).
