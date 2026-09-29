using EditorApp.Core.Import;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// A mesh made into voxels (Fullreleaseplan 8.3): the model chosen, then how many voxels along its
/// longest side and whether to fill it, then made — away from the frame, since a large model takes a
/// moment — and set down in the level where the view looks, as a prop is.
/// </summary>
public static class MeshImportDialog
{
    private const string PopupId = "Mesh to voxels###mesh-import";

    private static readonly FileBrowserDialog Browser = new();

    private static ColouredMesh? _mesh;
    private static bool _openRequested;
    private static int _resolution = 64;
    private static bool _solid = true;
    private static Task<VoxelScene>? _making;
    private static string? _error;

    /// <summary>What was made, waiting to be set down in the level.</summary>
    private static (VoxelScene Scene, string Name)? _made;

    public static bool IsBusy => _making is { IsCompleted: false };

    public static void Show() => Browser.Show(
        FileBrowserMode.Open,
        "Import a mesh as voxels (.obj, .gltf, .glb)",
        string.Join(";", ColouredMesh.SupportedExtensions),
        null,
        null,
        Load);

    /// <summary>Reads the model and asks how to make it, the way choosing it in the dialog does.</summary>
    public static void Load(string path)
    {
        try
        {
            _mesh = ColouredMesh.Load(path);
        }
        catch (Exception exception) when (exception is ReferenceImportException or IOException or UnauthorizedAccessException)
        {
            ReportLog.Shared.Post(exception.Message, ReportKind.Error);
            return;
        }

        _error = null;
        _openRequested = true;
    }

    /// <summary>What was made since last asked, once: the host sets it down where the view looks.</summary>
    public static (VoxelScene Scene, string Name)? TakeMade()
    {
        (VoxelScene Scene, string Name)? made = _made;
        _made = null;
        return made;
    }

    public static void Draw()
    {
        Browser.Draw();

        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        if (_mesh is not { } mesh)
        {
            return;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            if (!open && !IsBusy)
            {
                _mesh = null;
            }

            return;
        }

        (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = mesh.Bounds();
        System.Numerics.Vector3 size = max - min;
        ImGui.TextDisabled($"{mesh.Name} - {mesh.Triangles.Count:N0} triangles, {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##}");
        foreach (string warning in mesh.Warnings.Distinct().Take(4))
        {
            ImGui.TextColored(Theme.Highlight, warning);
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(IsBusy);
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12f);
        ImGui.SliderInt("Voxels along the longest side", ref _resolution, 4, MeshVoxelizer.MaxResolution);
        ImGui.Checkbox("Fill the inside", ref _solid);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Everything the surface closes in, coloured from the surface nearest it. A model with holes in it closes nothing in.");
        }

        ImGui.EndDisabled();

        if (_error is not null)
        {
            ImGui.TextColored(Theme.Highlight, _error);
        }

        TakeFinished(mesh);

        ImGui.Spacing();
        ImGui.BeginDisabled(IsBusy);
        if (ImGui.Button(IsBusy ? "Making voxels..." : "Import", Theme.ModalButton))
        {
            int resolution = _resolution;
            bool solid = _solid;
            _error = null;
            _making = Task.Run(() => MeshVoxelizer.Voxelize(mesh, resolution, solid, mesh.Name));
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", Theme.ModalButton))
        {
            _mesh = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();

        if (_made is not null)
        {
            _mesh = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>Picks up the voxels once they are made, or what stopped them.</summary>
    private static void TakeFinished(ColouredMesh mesh)
    {
        if (_making is not { IsCompleted: true } finished)
        {
            return;
        }

        _making = null;
        if (finished.IsCompletedSuccessfully)
        {
            _made = (finished.Result, mesh.Name);
        }
        else
        {
            Exception? problem = finished.Exception?.GetBaseException();
            _error = problem?.Message ?? "The voxels could not be made.";
            if (problem is not InvalidDataException)
            {
                CrashLog.Record($"voxelising {mesh.Name}", problem ?? new InvalidOperationException(_error));
            }
        }
    }
}
