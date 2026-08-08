using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Import;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Imports a model to build against — a blockout to trace, or the thing the level has to fit
/// around. It is a guide only: never voxelised, never saved into the project, never exported.
/// </summary>
public sealed class ReferencePanel
{
    private readonly FileBrowserDialog _browser = new();

    private string _status = string.Empty;
    private bool _statusIsError;

    public void Draw(EditorSession session, ReferenceModelRenderer reference)
    {
        ImGui.SetNextWindowPos(new Vector2(430f, 590f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420f, 260f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Reference model"))
        {
            DrawContents(session, reference);
        }

        ImGui.End();
        _browser.Draw();
    }

    private void DrawContents(EditorSession session, ReferenceModelRenderer reference)
    {
        if (ImGui.Button("Import..."))
        {
            _browser.Show(
                FileBrowserMode.Open,
                "Import a reference model (.obj, .gltf, .glb)",
                ".obj",
                _browser.CurrentDirectory,
                suggestedName: null,
                path => Load(reference, path));
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(reference.Mesh is null);
        if (ImGui.Button("Remove"))
        {
            reference.SetMesh(null);
            _status = string.Empty;
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled(string.Join(" ", ReferenceMeshLoader.SupportedExtensions));

        if (_status.Length > 0)
        {
            ImGui.TextColored(
                _statusIsError ? Theme.Danger : Theme.Success,
                _status);
        }

        if (reference.Mesh is not { } mesh)
        {
            ImGui.TextDisabled("Nothing imported. A reference is a visual guide only —");
            ImGui.TextDisabled("it is never voxelised, saved or exported.");
            return;
        }

        ImGui.Separator();
        ImGui.Text($"{mesh.Name} — {mesh.TriangleCount:N0} triangles");

        (Vector3 min, Vector3 max) = mesh.Bounds();
        Vector3 size = max - min;
        ImGui.TextDisabled($"Source size {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##}");

        bool visible = reference.Visible;
        if (ImGui.Checkbox("Visible", ref visible))
        {
            reference.Visible = visible;
        }

        ImGui.SameLine();
        bool wireframe = reference.Wireframe;
        if (ImGui.Checkbox("Wireframe", ref wireframe))
        {
            reference.Wireframe = wireframe;
        }

        float opacity = reference.Opacity;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.SliderFloat("Opacity", ref opacity, 0.05f, 1f))
        {
            reference.Opacity = opacity;
        }

        float scale = reference.Scale;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.DragFloat("Scale", ref scale, 0.01f, 0.001f, 1000f))
        {
            reference.Scale = scale;
        }

        Vector3 offset = reference.Offset;
        ImGui.SetNextItemWidth(280f);
        if (ImGui.DragFloat3("Offset", ref offset, 0.25f))
        {
            reference.Offset = offset;
        }

        if (ImGui.Button("Fit to level"))
        {
            FitToLevel(session, reference);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Scales and moves the guide so it sits inside the level's current bounds.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset transform"))
        {
            reference.Scale = 1f;
            reference.Offset = Vector3.Zero;
        }
    }

    private void FitToLevel(EditorSession session, ReferenceModelRenderer reference)
    {
        if (!session.World.TryGetBounds(out Int3 min, out Int3 max))
        {
            _status = "The level is empty, so there is nothing to fit to.";
            _statusIsError = true;
            return;
        }

        reference.FitTo(min.ToVector3(), max.ToVector3() + Vector3.One);
        _status = string.Empty;
    }

    private void Load(ReferenceModelRenderer reference, string path)
    {
        try
        {
            ReferenceMesh mesh = ReferenceMeshLoader.Load(path);
            reference.SetMesh(mesh);
            _status = $"Loaded {mesh.TriangleCount:N0} triangles.";
            _statusIsError = false;
        }
        catch (Exception exception) when (exception is ReferenceImportException or IOException or UnauthorizedAccessException)
        {
            reference.SetMesh(null);
            _status = exception.Message;
            _statusIsError = true;
        }
    }
}
