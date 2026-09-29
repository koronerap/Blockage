using ImGuiNET;
using Silk.NET.Input;

namespace EditorApp.Input;

/// <summary>
/// The key the shortcuts are made with: Ctrl, and on a Mac Command as well, where a Mac's own programs
/// put their shortcuts — Ctrl+S is Command+S there too. The Mac's Control key still counts, so the
/// shortcuts read the same on every platform and work with either keyboard.
/// </summary>
public static class ControlKey
{
    public static bool IsHeld(IKeyboard keyboard) =>
        keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)
        || (OperatingSystem.IsMacOS() && (keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight)));

    /// <summary>The same, as ImGui saw it this frame.</summary>
    public static bool IsHeld(ImGuiIOPtr io) => io.KeyCtrl || (OperatingSystem.IsMacOS() && io.KeySuper);
}
