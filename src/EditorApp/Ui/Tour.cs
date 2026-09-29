using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Input;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What the tour watches of the editor, read once a frame while it runs.</summary>
public readonly record struct TourState(
    float Yaw,
    float Pitch,
    long Voxels,
    int Objects,
    int? FocusId,
    int SelectedCount,
    EditorTool Tool,
    int Done,
    int Undone,
    bool Saved);

/// <summary>
/// A short tour for someone new to Blockage (Fullreleaseplan 9.8): seven steps on a fresh cube, each
/// told in a box at the top of the viewport and passed the moment it is done. The view turned, a
/// face pulled out, some paint, a shape added, the cube selected again, a change undone, the level
/// saved. The keys it names are read from the keymap, so a key changed in Preferences is the one it
/// tells. The welcome screen and Help start it; Skip moves on without doing a step, and End Tour
/// leaves it.
/// </summary>
public static class Tour
{
    private sealed record Step(string Title, Func<string> Text, Func<TourState, TourState, bool> Done);

    private static readonly Step[] Steps =
    [
        new("Look round",
            () => "Drag with the middle mouse button to turn the view round the cube. With Shift the middle button pans, and the wheel zooms.",
            (start, now) => MathF.Abs(now.Yaw - start.Yaw) + MathF.Abs(now.Pitch - start.Pitch) > 0.35f),
        new("Pull a face out",
            () => $"Press {Key(EditorAction.ToolExtrude, "the Extrude tool")} for Extrude. Click the top of the cube to take the whole face, then drag its arrow up.",
            (start, now) => now.Voxels > start.Voxels),
        new("Paint",
            () => $"Press {Key(EditorAction.ToolPaint, "the Paint tool")} for Paint, pick a colour from the palette, and click or drag over a few faces.",
            (start, now) => now.Tool == EditorTool.Paint && now.Done > start.Done),
        new("Add a shape",
            () => $"Press {Key(EditorAction.AddMenu, "Add in the menu bar")} and choose Mesh, then Sphere. It is set down on whatever the mouse points at.",
            (start, now) => now.Objects > start.Objects),
        new("Select",
            () => $"Every tool works on what is selected, which is the sphere now. Press {Key(EditorAction.ToolSelect, "the Select tool")} for Select, and click the cube.",
            (start, now) => now.FocusId == _cubeId && now.SelectedCount == 1),
        new("Undo",
            () => $"Press {Key(EditorAction.Undo, "Undo in the Edit menu")} to take the last change back. {Key(EditorAction.Redo, "Redo")} brings it back again.",
            (start, now) => now.Undone > start.Undone),
        new("Save",
            () => $"Press {Key(EditorAction.Save, "Save in the File menu")} and keep the level wherever you like.",
            (start, now) => now.Saved),
    ];

    private static int _step = -1;
    private static TourState _start;
    private static int? _cubeId;
    private static bool _requested;

    public static bool IsActive => _step >= 0;

    /// <summary>The step being shown, from 0; <see cref="StepCount"/> once the last is done, -1 when the tour is not running.</summary>
    public static int CurrentStep => _step;

    public static int StepCount => Steps.Length;

    /// <summary>Asks for the tour, from a menu: the editor starts it once it can give it a level.</summary>
    public static void Request() => _requested = true;

    /// <summary>Whether the tour was asked for since this was last called.</summary>
    public static bool TakeRequest()
    {
        bool requested = _requested;
        _requested = false;
        return requested;
    }

    /// <summary>Starts at the first step, on a level whose cube is <paramref name="cubeId"/>.</summary>
    public static void Start(TourState now, int? cubeId)
    {
        _step = 0;
        _start = now;
        _cubeId = cubeId;
    }

    public static void End() => _step = -1;

    /// <summary>Passes the step when what it asks is done.</summary>
    public static void Update(TourState now)
    {
        if (IsActive && _step < Steps.Length && Steps[_step].Done(_start, now))
        {
            Advance(now);
        }
    }

    /// <summary>Passes the step when it is done, and draws what to do at the top of the viewport.</summary>
    public static void Draw(Vector2 viewportPosition, Vector2 viewportSize, TourState now)
    {
        Update(now);
        if (!IsActive)
        {
            return;
        }

        float unit = ImGui.GetFontSize();
        float width = MathF.Min(unit * 27f, viewportSize.X - (unit * 2f));
        ImGui.SetNextWindowPos(new Vector2(viewportPosition.X + (viewportSize.X * 0.5f), viewportPosition.Y + (unit * 0.8f)), ImGuiCond.Always, new Vector2(0.5f, 0f));
        ImGui.SetNextWindowSize(new Vector2(width, 0f));
        ImGui.SetNextWindowBgAlpha(0.94f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
        if (ImGui.Begin("##tour", flags))
        {
            bool finished = _step >= Steps.Length;
            ImGui.TextDisabled(finished ? "Tour" : $"Tour · step {_step + 1} of {Steps.Length}");
            ImGui.Text(finished ? "That is the tour" : Steps[_step].Title);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - (unit * 1.2f));
            ImGui.TextWrapped(finished
                ? $"{Key(EditorAction.ShortcutSheet, "Help, then Keyboard Shortcuts")} lists every key, and the manual has the rest."
                : Steps[_step].Text());
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            if (finished)
            {
                if (ImGui.Button("Open the Manual"))
                {
                    Links.Open(Links.Manual);
                    End();
                }

                ImGui.SameLine();
                if (ImGui.Button("Close"))
                {
                    End();
                }
            }
            else
            {
                if (ImGui.Button("Skip"))
                {
                    Advance(now);
                }

                ImGui.SameLine();
                if (ImGui.Button("End Tour"))
                {
                    End();
                }
            }
        }

        ImGui.End();
    }

    private static void Advance(TourState now)
    {
        _step++;
        _start = now;
    }

    /// <summary>The key an action is on, or what to use instead when it has none.</summary>
    private static string Key(EditorAction action, string otherwise) =>
        Shortcut.Of(action) is { Length: > 0 } key ? key : otherwise;
}
