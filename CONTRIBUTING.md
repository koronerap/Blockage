# Contributing to Blockage

Thank you for wanting to help. Bug reports, ideas and pull requests are all welcome.

## Reporting a bug or asking for something

Use the issue templates. For a bug, the steps that make it happen and a small `.vxlevel` that shows
it are worth more than anything else. If Blockage crashed, the end of its `crash.log` says where; the
bug template says where to find the file.

For anything large, open an issue first: a new tool, a change to how something works, or a change to
the file format. Then the shape can be agreed before you spend time on it.

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and OpenGL 3.3.

```
dotnet run --project src/EditorApp      # the editor
dotnet test                             # both test suites
```

`src/EditorApp.Core` holds the model, the tools, meshing, the file formats and the exporters, with no
graphics and no UI. Most changes land there, and there they can be tested.
`tests/EditorApp.Core.Tests` covers the core, and `tests/EditorApp.Tests` the editor's own logic. CI
runs both on Windows, Linux and macOS for every push and pull request.

A few switches help while working:

| | |
|---|---|
| `--level=<file>` | opens that level |
| `--smoke=<frames>` | opens the window, draws that many frames, and reports how long meshing and the frames took |
| `--stress` | measures building, meshing, saving and opening large levels, with no window |
| `--export-to=<folder>` | runs every exporter with no window; `tools/validate-exports.ps1` then opens the results in Blender and Unity, if you have them |

The Android app in `src/EditorApp.Mobile` needs the .NET `android` workload, JDK 17 and the Android
SDK; `build-android.ps1` builds it.

## Making the change

- Match the code around it: its names, its structure, and how much it comments. A comment says why,
  not what.
- New behaviour in `EditorApp.Core` comes with tests.
- Levels saved by any earlier version must keep opening. A change to `.vxlevel` comes with a test
  that opens a file saved before it.
- Keep a pull request to one change, and say what it is for. Add a line to `CHANGELOG.md` for
  anything a user would notice.

## License

Blockage is MIT licensed. By contributing, you agree that your contribution is released under the same
license.
