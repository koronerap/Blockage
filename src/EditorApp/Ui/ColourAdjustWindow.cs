using System.Numerics;
using EditorApp.Core.Editing;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Hue, saturation and value shifts for the colours of what is selected (Fullreleaseplan 4.3): set
/// the three, then Apply — one undo step, the same colour shifted the same way wherever it is.
/// </summary>
public static class ColourAdjustWindow
{
    private static bool _open;
    private static float _hue;
    private static float _saturation;
    private static float _value;

    public static bool IsOpen => _open;

    public static void Open()
    {
        _open = true;
        _hue = 0f;
        _saturation = 0f;
        _value = 0f;
    }

    public static void Draw(EditorSession session)
    {
        if (!_open)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(ImGui.GetFontSize() * 20f, 0f), ImGuiCond.Appearing);
        if (ImGui.Begin("Adjust Colours", ref _open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextDisabled(session.InEditMode
                ? session.VoxelSelection.IsEmpty ? "All of the object being edited." : "The chosen voxels."
                : $"The colours of {ObjectMenu.SelectionSummary(session)}.");

            Props.Slider("Hue", "adjust-hue", ref _hue, -180f, 180f, "%.0f°");
            Props.Slider("Saturation", "adjust-saturation", ref _saturation, -1f, 1f, "%+.2f");
            Props.Slider("Value", "adjust-value", ref _value, -1f, 1f, "%+.2f");

            switch (Props.Buttons(string.Empty, "adjust-apply", "Apply", "Reset"))
            {
                case 0:
                    int changed = session.ShiftColours(_hue, _saturation, _value);
                    ReportLog.Shared.Post(changed > 0 ? $"Recoloured {changed:N0} voxels." : "Nothing changed colour.", changed > 0 ? ReportKind.Info : ReportKind.Warning);
                    break;

                case 1:
                    _hue = _saturation = _value = 0f;
                    break;
            }

            ImGui.TextDisabled("A new colour takes a custom slot where one is free,\nthe nearest in the palette where none is.");
        }

        ImGui.End();
    }
}
