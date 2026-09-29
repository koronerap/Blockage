using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Import;
using EditorApp.Core.Scene;
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

    private readonly FileBrowserDialog _imageBrowser = new();

    /// <summary>Tells whether a picture could be read, and how tall it is for its width: the renderer's.</summary>
    public static ReferenceImageRenderer? Images { get; set; }

    public void DrawContent(EditorSession session, ReferenceModelRenderer reference)
    {
        _imageBrowser.Draw();
        if (Props.Section("Images"))
        {
            DrawImages(session);
        }

        if (!Props.Section("Model"))
        {
            return;
        }

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

    /// <summary>
    /// Pictures to model over (Fullreleaseplan 7.3): each on a plane, see-through as far as wanted,
    /// behind the model or among it, and — as a background image — shown only in the view that
    /// looks straight at it. Saved with the level as where the picture is on disk.
    /// </summary>
    private void DrawImages(EditorSession session)
    {
        VoxelScene scene = session.Scene;
        if (Props.Buttons(string.Empty, "reference-image-add", "Add Image...") == 0)
        {
            _imageBrowser.Show(FileBrowserMode.Open, "Add a picture to model over (.png)", ".png", _imageBrowser.CurrentDirectory, suggestedName: null, path =>
            {
                // Standing in the middle of what there is, as wide as it: on the ground plane for a top view.
                (Vector3 min, Vector3 max) = SectionViewport.Bounds(scene);
                Vector3 centre = (min + max) * 0.5f;
                scene.AddReferenceImage(new ReferenceImage(path)
                {
                    Centre = centre with { Y = MathF.Max(centre.Y, (max.Y - min.Y) * 0.5f) },
                    Width = MathF.Max(max.X - min.X, 8f),
                });
                session.HasUnsavedChanges = true;
            });
        }

        if (scene.ReferenceImages.Count == 0)
        {
            ImGui.TextDisabled("A drawing or photo to model over, as a PNG.");
            return;
        }

        ReferenceImage? removed = null;
        for (int i = 0; i < scene.ReferenceImages.Count; i++)
        {
            ReferenceImage image = scene.ReferenceImages[i];
            ImGui.PushID(i);

            bool visible = image.Visible;
            if (ImGui.Checkbox($"##shown", ref visible))
            {
                image.Visible = visible;
                session.HasUnsavedChanges = true;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(image.Name);
            if (Images?.CannotRead(image.Path) == true)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Danger, "(cannot be read)");
            }

            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
            if (ImGui.Button("x", new Vector2(ImGui.GetFrameHeight())))
            {
                removed = image;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Take {image.Name} away. The picture itself stays where it is.");
            }

            int plane = Props.Choice("Plane", "image-plane", [(null, "Front"), (null, "Side"), (null, "Top")], (int)image.Plane);
            if (plane != (int)image.Plane)
            {
                image.Plane = (ImagePlane)plane;
                session.HasUnsavedChanges = true;
            }

            Vector3 centre = image.Centre;
            if (Props.Vector("Centre", "image-centre", ref centre, 0.1f))
            {
                image.Centre = centre;
                session.HasUnsavedChanges = true;
            }

            float width = image.Width;
            if (Props.Float("Width", "image-width", ref width, 0.1f, ReferenceImage.MinWidth, ReferenceImage.MaxWidth, "%.1f"))
            {
                image.Width = width;
                session.HasUnsavedChanges = true;
            }

            float opacity = image.Opacity;
            if (Props.Slider("Opacity", "image-opacity", ref opacity, 0f, 1f, "%.2f"))
            {
                image.Opacity = opacity;
                session.HasUnsavedChanges = true;
            }

            bool behind = image.Behind;
            if (Props.Check(string.Empty, "image-behind", "Behind the model", ref behind))
            {
                image.Behind = behind;
                session.HasUnsavedChanges = true;
            }

            bool aligned = image.OnlyAligned;
            if (Props.Check(string.Empty, "image-aligned", "Only in its own view", ref aligned))
            {
                image.OnlyAligned = aligned;
                session.HasUnsavedChanges = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Shown only while the view looks straight at its plane: Numpad 1 for the front, 3 for the side, 7 for the top.");
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (removed is not null)
        {
            scene.RemoveReferenceImage(removed);
            session.HasUnsavedChanges = true;
        }
    }
}
