using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;

namespace EditorApp.Ui;

/// <summary>One choice of a pie: what it says, what it does, and whether it is what is on now.</summary>
public sealed record PieSlice(string Label, Action Run, bool Current = false);

/// <summary>
/// Blender's pie menus (Fullreleaseplan 7.7): up to eight choices round the mouse, the one it points
/// towards lit. Held down, the key is let go on a choice to take it; tapped, the pie stays open for a
/// click. Esc or a right click puts it away.
/// </summary>
public static class PieMenu
{
    /// <summary>Held longer than this, letting go of the key takes the choice pointed at.</summary>
    private const double HoldSeconds = 0.25;

    private const float Radius = 110f;
    private const float DeadZone = 22f;

    /// <summary>Blender's order: left, right, bottom, top, then the corners.</summary>
    private static readonly float[] Angles = [180f, 0f, 270f, 90f, 135f, 45f, 225f, 315f];

    private static PieSlice[]? _slices;
    private static string _title = string.Empty;
    private static Vector2 _centre;
    private static Key _key;
    private static double _openedAt;
    private static bool _sticky;
    private static bool _leftWasDown = true;

    /// <summary>Whether the key has been seen held while the pie was up: only then is letting go of it a choice.</summary>
    private static bool _seenHeld;

    public static bool IsOpen => _slices is not null;

    public static void Open(string title, PieSlice[] slices, Vector2 centre, Key key, double now)
    {
        _slices = [.. slices.Take(Angles.Length)];
        _title = title;
        _centre = centre;
        _key = key;
        _openedAt = now;
        _sticky = false;
        _leftWasDown = true;
        _seenHeld = false;
    }

    public static void Close() => _slices = null;

    /// <summary>Draws the pie and takes its input, every frame it is open.</summary>
    public static void Draw(IKeyboard keyboard, IMouse mouse, double now)
    {
        if (_slices is not { } slices)
        {
            return;
        }

        Vector2 pointer = mouse.Position;
        int pointed = Pointed(pointer);

        if (keyboard.IsKeyPressed(Key.Escape) || mouse.IsButtonPressed(MouseButton.Right))
        {
            Close();
            return;
        }

        // Held and let go: the choice pointed at, or nothing. Tapped — or let go before the pie was
        // even up — it stays for a click.
        bool held = keyboard.IsKeyPressed(_key);
        _seenHeld |= held;
        if (!_sticky && !held)
        {
            if (!_seenHeld || now - _openedAt < HoldSeconds)
            {
                _sticky = true;
            }
            else
            {
                Take(pointed);
                return;
            }
        }

        bool leftDown = mouse.IsButtonPressed(MouseButton.Left);
        if (leftDown && !_leftWasDown)
        {
            Take(pointed);
            return;
        }

        _leftWasDown = leftDown;

        ImDrawListPtr draw = ImGui.GetForegroundDrawList();
        uint ring = ImGui.GetColorU32(Theme.Surface with { W = 0.9f });
        draw.AddCircle(_centre, DeadZone, ImGui.GetColorU32(Theme.TextDim), 32, 2f);
        if (pointed >= 0)
        {
            float angle = Angles[pointed] * (MathF.PI / 180f);
            Vector2 towards = new(MathF.Cos(angle), -MathF.Sin(angle));
            draw.AddLine(_centre + (towards * DeadZone), _centre + (towards * (DeadZone + 10f)), ImGui.GetColorU32(Theme.Accent), 4f);
        }

        Vector2 titleSize = ImGui.CalcTextSize(_title);
        draw.AddText(_centre - new Vector2(titleSize.X * 0.5f, Radius + 42f), ImGui.GetColorU32(Theme.TextDim), _title);

        for (int i = 0; i < slices.Length; i++)
        {
            float angle = Angles[i] * (MathF.PI / 180f);
            Vector2 at = _centre + (new Vector2(MathF.Cos(angle), -MathF.Sin(angle)) * Radius);
            Vector2 size = ImGui.CalcTextSize(slices[i].Label) + new Vector2(22f, 10f);
            Vector2 min = at - (size * 0.5f);
            Vector2 max = at + (size * 0.5f);
            uint fill = i == pointed ? ImGui.GetColorU32(Theme.Accent)
                : slices[i].Current ? ImGui.GetColorU32(Theme.AccentActive with { W = 0.6f })
                : ring;
            draw.AddRectFilled(min, max, fill, 6f);
            draw.AddRect(min, max, ImGui.GetColorU32(Theme.Border), 6f);
            draw.AddText(min + new Vector2(11f, 5f), ImGui.GetColorU32(i == pointed ? Theme.TextOnAccent : Theme.Text), slices[i].Label);
        }
    }

    /// <summary>The slice the pointer points towards, past the dead zone in the middle; −1 for none.</summary>
    private static int Pointed(Vector2 pointer)
    {
        Vector2 offset = pointer - _centre;
        if (_slices is null || offset.Length() < DeadZone)
        {
            return -1;
        }

        float angle = MathF.Atan2(-offset.Y, offset.X) * (180f / MathF.PI);
        int best = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < _slices.Length; i++)
        {
            float difference = MathF.Abs((((angle - Angles[i]) % 360f) + 540f) % 360f - 180f);
            if (difference < nearest)
            {
                nearest = difference;
                best = i;
            }
        }

        return best;
    }

    private static void Take(int pointed)
    {
        PieSlice[]? slices = _slices;
        Close();
        if (slices is not null && pointed >= 0 && pointed < slices.Length)
        {
            slices[pointed].Run();
        }
    }
}
