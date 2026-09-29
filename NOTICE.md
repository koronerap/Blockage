# Third-party notices

Blockage itself is under the MIT License (see `LICENSE`). It is built on, and its release builds
carry, the work of others, each under its own license.

## Libraries in the builds

| Component | Used for | License |
|---|---|---|
| [.NET](https://github.com/dotnet/runtime) | the runtime; the release builds are self-contained | MIT |
| [Silk.NET](https://github.com/dotnet/Silk.NET) | windowing, input and OpenGL on the desktop | MIT |
| [Dear ImGui](https://github.com/ocornut/imgui), through [cimgui](https://github.com/cimgui/cimgui) and [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) | the desktop interface | MIT |
| [GLFW](https://www.glfw.org/) | the desktop window and its GL context (`glfw3.dll`) | zlib/libpng |
| [SharpGLTF](https://github.com/vpenades/SharpGLTF) | glTF and GLB export | MIT |

Their full license texts are in their own repositories, linked above.

## Material Symbols

The Android app's icons are Material Symbols (Outlined) from
[google/material-design-icons](https://github.com/google/material-design-icons), used under the
Apache License 2.0.

The SVGs were converted to Android vector drawables and live in
`src/EditorApp.Mobile/Resources/drawable/`. Nothing about the artwork was changed: the conversion
moves the source `0 -960 960 960` viewBox into a drawable's own box and nothing else. Each file
carries the same notice at the top.

The desktop editor does not use them. Its icons are drawn from primitives in
`src/EditorApp/Ui/Icons.cs` and are the project's own.

## Inter

The desktop editor's interface font is Inter, under the SIL Open Font License. It lives in
`assets/fonts/` and travels with the build so the editor looks the same on every machine.
