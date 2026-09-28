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

    /// <summary>The file browser is a popup, so it is drawn at the top level, not inside a panel.</summary>
    public void DrawDialogs() => _browser.Draw();

    public void DrawContent(EditorSession session, ReferenceModelRenderer reference)
    {
        int pressed = Props.Buttons("Model", "reference-file", "Import...", "Remove");
        if (pressed == 0)
        {
            _browser.Show(
                FileBrowserMode.Open,
                "Import a reference model (.obj, .gltf, .glb)",
                ".obj",
                _browser.CurrentDirectory,
                suggestedName: null,
                path => Load(reference, path));
        }
        else if (pressed == 1 && reference.Mesh is not null)
        {
            reference.SetMesh(null);
            _status = string.Empty;
        }

        if (_status.Length > 0)
        {
            Props.Note(string.Empty, _status, _statusIsError ? Theme.Danger : Theme.Success);
        }

        if (reference.Mesh is not { } mesh)
        {
            Props.Label(string.Empty);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled(
                $"A model to build against - {string.Join(" ", ReferenceMeshLoader.SupportedExtensions)}. "
                + "A guide only: never voxelised, saved or exported.");
            ImGui.PopTextWrapPos();
            return;
        }

        (Vector3 min, Vector3 max) = mesh.Bounds();
        Vector3 size = max - min;
        Props.Value("File", mesh.Name);
        Props.Value("Triangles", $"{mesh.TriangleCount:N0}");
        Props.Value("Size", $"{size.X:0.##} × {size.Y:0.##} × {size.Z:0.##}");

        if (Props.Section("Display"))
        {
            bool visible = reference.Visible;
            if (Props.Check(string.Empty, "reference-visible", "Show", ref visible))
            {
                reference.Visible = visible;
            }

            bool wireframe = reference.Wireframe;
            if (Props.Check(string.Empty, "reference-wireframe", "Wireframe", ref wireframe))
            {
                reference.Wireframe = wireframe;
            }

            float opacity = reference.Opacity * 100f;
            if (Props.Slider("Opacity", "reference-opacity", ref opacity, 5f, 100f, "%.0f%%"))
            {
                reference.Opacity = opacity / 100f;
            }
        }

        if (Props.Section("Placement"))
        {
            float scale = reference.Scale;
            if (Props.Float("Scale", "reference-scale", ref scale, 0.01f, 0.001f, 1000f, "%.3gx"))
            {
                reference.Scale = scale;
            }

            Vector3 offset = reference.Offset;
            if (Props.Vector("Offset", "reference-offset", ref offset, 0.25f))
            {
                reference.Offset = offset;
            }

            int action = Props.Buttons(string.Empty, "reference-placement", "Fit to level", "Reset");
            if (action == 0)
            {
                FitToLevel(session, reference);
            }
            else if (action == 1)
            {
                reference.Scale = 1f;
                reference.Offset = Vector3.Zero;
            }
        }
    }

    private void FitToLevel(EditorSession session, ReferenceModelRenderer reference)
    {
        // The whole level as it stands in the world — every object, at its own place and voxel size —
        // not the focused object's own grid.
        if (!session.Scene.TryGetWorldBounds(out Vector3 min, out Vector3 max))
        {
            _status = "The level is empty, so there is nothing to fit to.";
            _statusIsError = true;
            return;
        }

        reference.FitTo(min, max);
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
