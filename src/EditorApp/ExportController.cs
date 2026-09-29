using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export;
using EditorApp.Core.Meshing;
using EditorApp.Core.Project;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp;

/// <summary>
/// The export dialog: pick a format and a path, see what greedy meshing actually saved, write the
/// files. The naive-versus-greedy counts are shown because they are the value this tool delivers
/// (EditorApp.md §4b) — and because a number that stops improving is the first sign of a bug.
/// </summary>
public sealed class ExportController(EditorSession session)
{
    /// <summary>
    /// Title and identity in one string, and the same string has to reach both <c>OpenPopup</c> and
    /// <c>BeginPopupModal</c>. ImGui hashes "Name###id" as "###id" rather than as "id", so opening
    /// by the bare id and beginning by the decorated one addresses two different popups and the
    /// dialog simply never appears.
    /// </summary>
    private const string PopupId = "Export mesh###export-level";

    private static readonly IMeshExporter[] Exporters =
    [
        new ObjExporter(),
        new GltfExporter(binary: true),
        new GltfExporter(binary: false),
    ];

    private readonly FileBrowserDialog _browser = new();

    private int _formatIndex;
    private string _outputPath = string.Empty;
    private bool _writeImportNotes = true;
    private bool _shouldOpenPopup;
    private bool _isOpen;

    /// <summary>0 = unwrapped, 1 = palette blocks.</summary>
    private int _layoutIndex;

    private int _texelsPerVoxel = UvUnwrap.DefaultTexelsPerVoxel;
    private bool _writeTexture = true;

    private UvAtlas? _atlas;
    private ExportMesh? _analysis;
    private int _naiveVertexCount;
    private double _analysisMilliseconds;
    private string _status = string.Empty;
    private bool _statusIsError;

    private IMeshExporter Current => Exporters[_formatIndex];

    public void Show()
    {
        // An empty level makes an empty file, which an engine takes without a word; better said here.
        if (!session.Scene.Objects.Any(o => o.Visible && !o.IsEmpty))
        {
            Ui.ReportLog.Shared.Post("Nothing to export - the level has no voxels that are shown.", Ui.ReportKind.Warning);
            return;
        }

        _isOpen = true;
        _shouldOpenPopup = true;
        _status = string.Empty;
        _statusIsError = false;

        // The folder is remembered; the file name always follows the level.
        //
        // It used to be set once and kept forever, which meant exporting one level and then opening
        // another left the first one's path in the box. Pressing Export then quietly overwrote the
        // first level's mesh and the file that was asked for never appeared — no error, because
        // nothing had gone wrong as far as the writer was concerned. A name that resets is visible
        // in the field; one that silently points at another level's file is not.
        string directory = _outputPath.Length > 0
            ? Path.GetDirectoryName(_outputPath) ?? DefaultDirectory
            : DefaultDirectory;

        _outputPath = Path.Combine(directory, session.ProjectName + Current.Extension);

        Analyze();
    }

    private string DefaultDirectory => session.ProjectPath is not null
        ? Path.GetDirectoryName(session.ProjectPath) ?? Directory.GetCurrentDirectory()
        : Directory.GetCurrentDirectory();

    /// <summary>Where Export will write. Exposed so the behaviour can be checked without a mouse.</summary>
    public string OutputPath => _outputPath;

    /// <summary>What the path field and the Browse button do, for the same reason.</summary>
    public void SetOutputPath(string path) => _outputPath = path;

    /// <summary>Opens the folder picker on the current output path.</summary>
    public void Browse() => _browser.Show(
        FileBrowserMode.Save,
        "Export to",
        Current.Extension,
        Path.GetDirectoryName(_outputPath),
        Path.GetFileName(_outputPath),
        SetOutputPath);

    /// <summary>
    /// Builds the greedy mesh and the naive reference once, so the dialog can show the reduction
    /// without meshing on every frame.
    /// </summary>
    private void Analyze()
    {
        var stopwatch = Stopwatch.StartNew();

        bool unwrapped = _layoutIndex == 0;

        // Colour stops constraining the merge once there is somewhere for it to go. Palette blocks
        // still need it: there, a merged quad samples one texel and so can only be one colour.
        _analysis = GreedyMesher.BuildScene(session.Scene, uvSelector: null, mergeAcrossColors: unwrapped);

        // Unwrapping rewrites the mesh's UVs, so it belongs here with the meshing rather than inside
        // an exporter — both formats have to be handed the same layout and the same sheet.
        _atlas = unwrapped
            ? UvUnwrap.Apply(_analysis, _texelsPerVoxel)
            : null;

        // The naive reference covers the whole scene too, so the reduction figure is the one that
        // actually applies to the file being written.
        _naiveVertexCount = 0;
        var naive = new MeshBuilder();
        foreach (Core.Scene.VoxelObject o in session.Scene.Objects)
        {
            if (!o.Visible)
            {
                continue;
            }

            EditMesher.BuildWorldNaive(o.Shown, naive);
            _naiveVertexCount += naive.VertexCount;
        }

        stopwatch.Stop();
        _analysisMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
    }

    public void Draw()
    {
        if (_shouldOpenPopup)
        {
            ImGui.OpenPopup(PopupId);
            _shouldOpenPopup = false;
        }

        if (_isOpen)
        {
            DrawPopup();
        }
    }

    private void DrawPopup()
    {
        // A fixed width, and no AlwaysAutoResize.
        //
        // The two together were a feedback loop: the output path field asks for the width available
        // minus room for the Browse button, an auto-resizing window takes its width from its widest
        // item, and each frame handed the other a little more. The dialog walked off the side of the
        // screen. Zero height still means fit to the content, which has nothing feeding back into it.
        //
        ImGui.SetNextWindowSize(new Vector2(720f, 0f), ImGuiCond.Always);

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        DrawFormatPicker();
        ImGui.Spacing();
        DrawTexturePicker();
        ImGui.Spacing();
        DrawPathPicker();

        ImGui.SeparatorText("Result");
        DrawAnalysis();

        ImGui.Spacing();
        DrawActions();

        // Drawn inside this popup, not beside it. A popup opened while none is being drawn goes to
        // the top level, and opening a second one there closes the first: pressing Browse made the
        // export dialog vanish, and picking a file then set a path on a dialog that was no longer
        // on screen. From in here it nests instead, and the export dialog is still there underneath
        // when the browser closes.
        _browser.Draw();

        ImGui.EndPopup();

        if (!open)
        {
            _isOpen = false;
        }
    }

    private void DrawFormatPicker()
    {
        ImGui.Text("Format");

        for (int i = 0; i < Exporters.Length; i++)
        {
            if (ImGui.RadioButton(Exporters[i].DisplayName, _formatIndex == i) && _formatIndex != i)
            {
                _formatIndex = i;
                _outputPath = Path.ChangeExtension(_outputPath, Exporters[i].Extension);
            }
        }
    }

    /// <summary>
    /// The two ways colour can leave this tool, and they are genuinely different jobs rather than a
    /// preference. Unwrapped gives every face its own patch of a sheet, which is the only way the
    /// model can be textured anywhere else. Palette blocks put the whole level on one 128x128 image
    /// shared by every export — smaller and unbleedable, but with no surface to paint on.
    /// </summary>
    private void DrawTexturePicker()
    {
        ImGui.Text("Texture");

        int layout = _layoutIndex;
        if (ImGui.RadioButton("Unwrapped UVs  -  paintable in an external tool", ref layout, 0) |
            ImGui.RadioButton("Palette blocks  -  one shared 128x128, no UV space", ref layout, 1))
        {
            if (layout != _layoutIndex)
            {
                _layoutIndex = layout;
                Analyze();
            }
        }

        if (_layoutIndex == 0)
        {
            ImGui.SetNextItemWidth(200f);
            int texels = _texelsPerVoxel;
            if (ImGui.SliderInt("Texels per voxel", ref texels, 1, 64) && texels != _texelsPerVoxel)
            {
                _texelsPerVoxel = texels;
                Analyze();
            }

            if (_atlas is { } atlas)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"-> {atlas.Width} x {atlas.Height}");

                ImGui.TextDisabled(
                    $"  {atlas.Charts.Count:N0} pieces from {atlas.Islands.Count:N0} faces, "
                    + $"{atlas.Coverage:P0} of the sheet used");

                if (atlas.TexelsPerVoxel != _texelsPerVoxel)
                {
                    ImGui.TextColored(
                        Theme.Highlight,
                        $"Reduced to {atlas.TexelsPerVoxel} texels per voxel to fit the sheet.");
                }
            }
        }

        ImGui.Checkbox("Write the texture file", ref _writeTexture);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The UVs are written either way.\nTurn this off to paint from a blank sheet.");
        }
    }

    private void DrawPathPicker()
    {
        ImGui.SetNextItemWidth(-120f);
        string path = _outputPath;
        if (ImGui.InputText("Output", ref path, 512))
        {
            _outputPath = path;
        }

        ImGui.SameLine();
        if (ImGui.Button("Browse..."))
        {
            Browse();
        }

        ImGui.Checkbox("Write texture import notes beside the mesh", ref _writeImportNotes);
    }

    private void DrawAnalysis()
    {
        if (_analysis is null)
        {
            ImGui.TextDisabled("Not analysed.");
            return;
        }

        int greedyVertices = _analysis.VertexCount;

        ImGui.Text("Greedy meshing");
        ImGui.Text($"  Quads        {_analysis.QuadCount:N0}");
        ImGui.Text($"  Triangles    {_analysis.TriangleCount:N0}");
        ImGui.Text($"  Vertices     {greedyVertices:N0}   (naive: {_naiveVertexCount:N0})");

        if (greedyVertices > 0)
        {
            float reduction = 100f * (1f - greedyVertices / (float)Math.Max(_naiveVertexCount, 1));
            ImGui.SameLine();
            ImGui.TextColored(Theme.Success, $"  -{reduction:0.0}%");
        }

        ImGui.Text($"  Colors used  {_analysis.UsedPaletteIndices().Count}  ->  1 material, 1 texture");

        // The size the file will actually be, read off the mesh that is about to be written rather
        // than recomputed from the level. A wrong scale is cheap to fix here and expensive to find
        // once the model is in an engine.
        (Vector3 min, Vector3 max) = _analysis.Bounds();
        Vector3 extent = max - min;
        ImGui.Text($"  Size         {extent.X:0.###} x {extent.Y:0.###} x {extent.Z:0.###}");

        if (session.Scene.SharedVoxelSize is { } shared)
        {
            if (MathF.Abs(shared - 1f) > 1e-6f)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Highlight, $"   at {shared:0.####} per voxel");
            }
        }
        else if (session.Scene.Objects.Count(o => o.Visible && !o.IsEmpty) > 1)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Highlight, "   voxel sizes differ by object");
        }

        ImGui.TextDisabled($"  Meshed in {_analysisMilliseconds:0} ms");

        if (ImGui.Button("Re-analyse"))
        {
            Analyze();
        }

        ImGui.SameLine();
        if (ImGui.Button("Texture import settings"))
        {
            ImGui.OpenPopup("texture-notes");
        }

        if (ImGui.BeginPopup("texture-notes"))
        {
            ImGui.TextUnformatted(PaletteTexture.ImportNotes);
            ImGui.EndPopup();
        }
    }

    private void DrawActions()
    {
        if (_status.Length > 0)
        {
            ImGui.TextColored(
                _statusIsError ? Theme.Danger : Theme.Success,
                _status);
        }

        ImGui.BeginDisabled(_analysis is null || _outputPath.Trim().Length == 0);
        if (ImGui.Button("Export", Theme.ModalButton))
        {
            RunExport();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Close", Theme.ModalButton))
        {
            _isOpen = false;
            ImGui.CloseCurrentPopup();
        }
    }

    private void RunExport()
    {
        try
        {
            // Always mesh fresh at the moment of export: the analysis may predate recent edits.
            Analyze();

            string path = _outputPath.Trim();
            if (!path.EndsWith(Current.Extension, StringComparison.OrdinalIgnoreCase))
            {
                path = Path.ChangeExtension(path, Current.Extension);
                _outputPath = path;
            }

            ExportResult result = Current.Export(
                _analysis!,
                session.Scene.Palette,
                path,
                new ExportOptions
                {
                    Atlas = _atlas,
                    WriteTexture = _writeTexture,
                    WriteImportNotes = _writeImportNotes,
                });

            // Names the file, because "wrote 4 files" does not answer the question people actually
            // have when they cannot find them.
            _status = $"Wrote {Path.GetFileName(path)} and {result.FilesWritten.Count - 1} more to "
                + $"{Path.GetDirectoryName(path)} - {result.QuadCount:N0} quads, {result.VertexCount:N0} vertices.";
            _statusIsError = false;
        }
        catch (Exception exception)
        {
            // Everything, as for opening and saving. An export that fails silently is worse than one
            // that fails loudly, and the list of ways a mesh writer can be surprised is not short.
            _status = $"Export failed: {exception.Message}";
            _statusIsError = true;
            CrashLog.Record($"exporting {_outputPath}", exception);
        }
    }
}
