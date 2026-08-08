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
    private const string PopupId = "export-level";

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

    private ExportMesh? _analysis;
    private int _naiveVertexCount;
    private double _analysisMilliseconds;
    private string _status = string.Empty;
    private bool _statusIsError;

    private IMeshExporter Current => Exporters[_formatIndex];

    public void Show()
    {
        _isOpen = true;
        _shouldOpenPopup = true;
        _status = string.Empty;
        _statusIsError = false;

        if (_outputPath.Length == 0)
        {
            string directory = session.ProjectPath is not null
                ? Path.GetDirectoryName(session.ProjectPath) ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();

            _outputPath = Path.Combine(directory, session.ProjectName + Current.Extension);
        }

        Analyze();
    }

    /// <summary>
    /// Builds the greedy mesh and the naive reference once, so the dialog can show the reduction
    /// without meshing on every frame.
    /// </summary>
    private void Analyze()
    {
        var stopwatch = Stopwatch.StartNew();

        _analysis = GreedyMesher.BuildScene(session.Scene);

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

            EditMesher.BuildWorldNaive(o.Grid, naive);
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

        _browser.Draw();
    }

    private void DrawPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(720f, 0f), ImGuiCond.Appearing);

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        ImGui.SeparatorText("Export mesh");
        DrawFormatPicker();
        ImGui.Spacing();
        DrawPathPicker();

        ImGui.SeparatorText("Result");
        DrawAnalysis();

        ImGui.Spacing();
        DrawActions();

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
            _browser.Show(
                FileBrowserMode.Save,
                "Export to",
                Current.Extension,
                Path.GetDirectoryName(_outputPath),
                Path.GetFileName(_outputPath),
                selected => _outputPath = selected);
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

        if (session.Scene.HasCustomVoxelSize)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Highlight, $"   at {session.Scene.VoxelSize:0.####} per voxel");
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
                new ExportOptions { WriteImportNotes = _writeImportNotes });

            _status = $"Wrote {result.FilesWritten.Count} file(s): {result.QuadCount:N0} quads, "
                + $"{result.VertexCount:N0} vertices.";
            _statusIsError = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or VxLevelFormatException)
        {
            _status = $"Export failed: {exception.Message}";
            _statusIsError = true;
        }
    }
}
