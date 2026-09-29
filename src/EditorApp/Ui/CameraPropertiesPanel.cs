using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// A camera's settings, when one is picked in the outliner (Fullreleaseplan 5.4): what kind it is,
/// how wide it sees, whether renders are seen from it, and where it stands. A drag is one undo step.
/// </summary>
public static class CameraPropertiesPanel
{
    private static SceneCamera? _before;

    /// <summary>Looks through a camera: the application's, which has the view.</summary>
    public static Action<SceneCamera> LookThrough { get; set; } = _ => { };

    /// <summary>Moves a camera to where the view stands.</summary>
    public static Action<SceneCamera> MoveToView { get; set; } = _ => { };

    /// <summary>Puts an unfinished edit into history — when the camera is no longer the one shown, for one.</summary>
    public static void Flush(EditorSession session)
    {
        if (_before is { } before)
        {
            session.PushCameraEdit(before, $"Edit {before.Name}");
        }

        _before = null;
    }

    public static void DrawContent(EditorSession session, SceneCamera camera)
    {
        if (_before is not null && _before.Id != camera.Id)
        {
            Flush(session);
        }

        ObjectPropertiesPanel.DrawName(camera.Id, camera.Name, Icons.Camera, name => session.RenameCamera(camera.Id, name));
        SceneCamera edited = camera;

        if (Props.Section("Camera"))
        {
            if (Props.BeginCombo("Type", "camera-kind", KindName(camera.Kind)))
            {
                foreach (CameraKind kind in Enum.GetValues<CameraKind>())
                {
                    if (ImGui.Selectable(KindName(kind), kind == camera.Kind) && kind != camera.Kind)
                    {
                        Flush(session);
                        session.SetCameraKind(camera.Id, kind);
                    }
                }

                ImGui.EndCombo();
            }

            Tooltip("Isometric is orthographic at the 2:1 angle isometric sprites are drawn at.");

            if (camera.Kind == CameraKind.Perspective)
            {
                float fov = camera.FieldOfView;
                if (Props.Slider("Field of view", "camera-fov", ref fov, 5f, 120f, "%.0f°"))
                {
                    edited = edited with { FieldOfView = fov };
                }
            }
            else
            {
                float height = camera.OrthographicHeight;
                if (Props.Float("View height", "camera-height", ref height, 0.1f, 0.01f, 100_000f, "%.1f"))
                {
                    edited = edited with { OrthographicHeight = height };
                }

                Tooltip("How much of the world the picture spans top to bottom, in world units.");
            }

            bool active = session.Scene.ActiveCameraId == camera.Id;
            if (Props.Check(string.Empty, "camera-active", "Render from it", ref active))
            {
                Flush(session);
                session.SetActiveCamera(active ? camera.Id : 0);
            }

            Tooltip($"Render Image{Shortcut.Hint(EditorAction.RenderImage)} renders from this camera; with none, it renders the view.");

            if (Props.Buttons(string.Empty, "camera-look", "Look Through") == 0)
            {
                LookThrough(camera);
            }

            Tooltip($"The view put where the camera stands{Shortcut.Hint(EditorAction.ViewCamera)} - the same key goes back.");

            if (Props.Buttons(string.Empty, "camera-move", "Move to View") == 0)
            {
                Flush(session);
                MoveToView(camera);
            }

            Tooltip($"The camera put where the view stands{Shortcut.Hint(EditorAction.CameraToView)}.");
        }

        if (Props.Section("Transform"))
        {
            Vector3 position = camera.Position;
            if (Props.Vector("Location", "camera-location", ref position, 0.05f))
            {
                edited = edited with { Position = position };
            }

            // An isometric camera keeps its angle: turning it would make it something else.
            ImGui.BeginDisabled(camera.Kind == CameraKind.Isometric);
            float yaw = camera.Yaw * (180f / MathF.PI);
            if (Props.Float("Turn", "camera-yaw", ref yaw, 0.5f, -720f, 720f, "%.1f°"))
            {
                edited = edited with { Yaw = yaw * (MathF.PI / 180f) };
            }

            float pitch = camera.Pitch * (180f / MathF.PI);
            if (Props.Float("Tilt", "camera-pitch", ref pitch, 0.5f, -89f, 89f, "%.1f°"))
            {
                edited = edited with { Pitch = pitch * (MathF.PI / 180f) };
            }

            ImGui.EndDisabled();
        }

        if (edited != camera)
        {
            _before ??= camera;
            session.SetCameraLive(edited);
        }

        // The gesture is over once nothing is held.
        if (_before is not null && !ImGui.IsAnyItemActive())
        {
            Flush(session);
        }
    }

    private static string KindName(CameraKind kind) => kind switch
    {
        CameraKind.Orthographic => "Orthographic",
        CameraKind.Isometric => "Isometric",
        _ => "Perspective",
    };

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
