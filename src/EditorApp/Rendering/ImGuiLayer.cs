// Adapted from Silk.NET's ImGuiController (Silk.NET.OpenGL.Extensions.ImGui 2.23.0),
// Copyright (c) .NET Foundation and Contributors, MIT license — see NOTICE.md.

using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace EditorApp.Rendering;

/// <summary>
/// Dear ImGui on the window: its input each frame, and its drawing after the scene. Silk.NET's own
/// controller, with one change that matters: a scale for the whole interface (Fullreleaseplan 9.11).
///
/// ImGui lays out in interface units, the window's size divided by the scale, and draws them at the
/// framebuffer's resolution, so a 150% display gets an interface half as large again: every panel,
/// icon and gizmo, since all of them are drawn through ImGui. The mouse comes in divided the same way.
/// Fonts are for whoever builds them to make sharp: at <see cref="PixelsPerUnit"/> times their size,
/// shown at their size.
/// </summary>
public sealed class ImGuiLayer : IDisposable
{
    private readonly GL _gl;
    private readonly IView _view;
    private readonly IInputContext _input;
    private readonly IKeyboard _keyboard;
    private readonly List<char> _pressedChars = [];
    private readonly ShaderProgram _shader;
    private readonly uint _vertexBuffer;
    private readonly uint _indexBuffer;
    private readonly nint _context;

    private uint _fontTexture;
    private bool _frameBegun;
    private int _windowWidth;
    private int _windowHeight;

    /// <summary>How many window pixels one interface unit is: 1.5 on a 150% display.</summary>
    public float UiScale { get; }

    /// <summary>How many framebuffer pixels one interface unit is: the scale, times the framebuffer's own on a Retina Mac.</summary>
    public float PixelsPerUnit => _windowWidth > 0 ? UiScale * _view.FramebufferSize.X / _windowWidth : UiScale;

    /// <param name="configure">Called once the context exists and before its first frame: fonts, settings.</param>
    public ImGuiLayer(GL gl, IView view, IInputContext input, float uiScale, Action? configure = null)
    {
        _gl = gl;
        _view = view;
        _input = input;
        UiScale = MathF.Max(uiScale, 0.5f);
        _windowWidth = view.Size.X;
        _windowHeight = view.Size.Y;

        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGui.StyleColorsDark();

        configure?.Invoke();
        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        _shader = new ShaderProgram(gl, VertexSource, FragmentSource);
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = gl.GenBuffer();
        CreateFontTexture();

        SetFrameData(1f / 60f);
        ImGui.NewFrame();
        _frameBegun = true;

        _keyboard = input.Keyboards[0];
        view.Resize += OnResize;
        _keyboard.KeyDown += OnKeyDown;
        _keyboard.KeyUp += OnKeyUp;
        _keyboard.KeyChar += OnKeyChar;
    }

    /// <summary>Ends the frame begun last time and begins this one, with this frame's input.</summary>
    public void Update(float deltaSeconds)
    {
        ImGui.SetCurrentContext(_context);
        if (_frameBegun)
        {
            ImGui.Render();
        }

        SetFrameData(deltaSeconds);
        UpdateInput();
        _frameBegun = true;
        ImGui.NewFrame();
    }

    /// <summary>Draws what the frame built, over whatever is in the framebuffer.</summary>
    public void Render()
    {
        if (!_frameBegun)
        {
            return;
        }

        ImGui.SetCurrentContext(_context);
        _frameBegun = false;
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private void SetFrameData(float deltaSeconds)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth, _windowHeight) / UiScale;
        if (_windowWidth > 0 && _windowHeight > 0)
        {
            io.DisplayFramebufferScale = new Vector2(_view.FramebufferSize.X, _view.FramebufferSize.Y) / io.DisplaySize;
        }

        io.DeltaTime = deltaSeconds;
    }

    private void UpdateInput()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        using MouseState mouse = _input.Mice[0].CaptureState();

        io.MouseDown[0] = mouse.IsButtonPressed(MouseButton.Left);
        io.MouseDown[1] = mouse.IsButtonPressed(MouseButton.Right);
        io.MouseDown[2] = mouse.IsButtonPressed(MouseButton.Middle);
        io.MousePos = mouse.Position / UiScale;

        ScrollWheel wheel = mouse.GetScrollWheels()[0];
        io.MouseWheel = wheel.Y;
        io.MouseWheelH = wheel.X;

        foreach (char c in _pressedChars)
        {
            io.AddInputCharacter(c);
        }

        _pressedChars.Clear();

        io.KeyCtrl = _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight);
        io.KeyAlt = _keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight);
        io.KeyShift = _keyboard.IsKeyPressed(Key.ShiftLeft) || _keyboard.IsKeyPressed(Key.ShiftRight);
        io.KeySuper = _keyboard.IsKeyPressed(Key.SuperLeft) || _keyboard.IsKeyPressed(Key.SuperRight);
    }

    private void OnResize(Vector2D<int> size)
    {
        _windowWidth = size.X;
        _windowHeight = size.Y;
    }

    private static void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => OnKey(key, scancode, down: true);

    private static void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => OnKey(key, scancode, down: false);

    private static void OnKey(Key key, int scancode, bool down)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        ImGuiKey imGuiKey = Translate(key);
        io.AddKeyEvent(imGuiKey, down);
        io.SetKeyEventNativeData(imGuiKey, (int)key, scancode);
    }

    private void OnKeyChar(IKeyboard keyboard, char c) => _pressedChars.Add(c);

    private unsafe void RenderDrawData(ImDrawDataPtr drawData)
    {
        int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return;
        }

        // What the scene left bound and enabled, to be put back after.
        _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
        _gl.ActiveTexture(GLEnum.Texture0);
        _gl.GetInteger(GLEnum.CurrentProgram, out int lastProgram);
        _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
        _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);
        _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);
        Span<int> lastPolygonMode = stackalloc int[2];
        _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);
        Span<int> lastScissorBox = stackalloc int[4];
        _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);
        _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
        _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);
        _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
        _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);
        _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
        _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);
        bool lastBlend = _gl.IsEnabled(GLEnum.Blend);
        bool lastCullFace = _gl.IsEnabled(GLEnum.CullFace);
        bool lastDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
        bool lastStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
        bool lastScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);
        bool lastPrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);

        _gl.Enable(GLEnum.Blend);
        _gl.BlendEquation(GLEnum.FuncAdd);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(GLEnum.CullFace);
        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Enable(GLEnum.ScissorTest);
        _gl.Disable(GLEnum.PrimitiveRestart);
        _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);

        // Interface units to clip space: the draw data's own rectangle, y down.
        Vector2 min = drawData.DisplayPos;
        Vector2 max = drawData.DisplayPos + drawData.DisplaySize;
        _shader.Use();
        _shader.SetInt("Texture", 0);
        _shader.SetMatrix4("ProjMtx", Matrix4x4.CreateOrthographicOffCenter(min.X, max.X, max.Y, min.Y, 0f, 1f));
        _gl.BindSampler(0, 0);

        // A vertex array of its own each time, as the scene's are not to be disturbed.
        uint vertexArray = _gl.GenVertexArray();
        _gl.BindVertexArray(vertexArray);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer);
        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
        _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
        _gl.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);

        Vector2 clipOffset = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(list.VtxBuffer.Size * sizeof(ImDrawVert)), (void*)list.VtxBuffer.Data, GLEnum.StreamDraw);
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(list.IdxBuffer.Size * sizeof(ushort)), (void*)list.IdxBuffer.Data, GLEnum.StreamDraw);

            for (int i = 0; i < list.CmdBuffer.Size; i++)
            {
                ImDrawCmdPtr command = list.CmdBuffer[i];
                if (command.UserCallback != IntPtr.Zero)
                {
                    throw new NotSupportedException("ImGui draw callbacks are not used here.");
                }

                var clip = new Vector4(
                    (command.ClipRect.X - clipOffset.X) * clipScale.X,
                    (command.ClipRect.Y - clipOffset.Y) * clipScale.Y,
                    (command.ClipRect.Z - clipOffset.X) * clipScale.X,
                    (command.ClipRect.W - clipOffset.Y) * clipScale.Y);
                if (clip.X >= framebufferWidth || clip.Y >= framebufferHeight || clip.Z < 0f || clip.W < 0f)
                {
                    continue;
                }

                _gl.Scissor((int)clip.X, (int)(framebufferHeight - clip.W), (uint)(clip.Z - clip.X), (uint)(clip.W - clip.Y));
                _gl.BindTexture(GLEnum.Texture2D, (uint)command.TextureId);
                _gl.DrawElementsBaseVertex(GLEnum.Triangles, command.ElemCount, GLEnum.UnsignedShort, (void*)(command.IdxOffset * sizeof(ushort)), (int)command.VtxOffset);
            }
        }

        _gl.DeleteVertexArray(vertexArray);

        _gl.UseProgram((uint)lastProgram);
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        _gl.BindSampler(0, (uint)lastSampler);
        _gl.ActiveTexture((GLEnum)lastActiveTexture);
        _gl.BindVertexArray((uint)lastVertexArray);
        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
        _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
        _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);
        Restore(GLEnum.Blend, lastBlend);
        Restore(GLEnum.CullFace, lastCullFace);
        Restore(GLEnum.DepthTest, lastDepthTest);
        Restore(GLEnum.StencilTest, lastStencilTest);
        Restore(GLEnum.ScissorTest, lastScissorTest);
        Restore(GLEnum.PrimitiveRestart, lastPrimitiveRestart);
        _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);
        _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
    }

    private void Restore(GLEnum capability, bool enabled)
    {
        if (enabled)
        {
            _gl.Enable(capability);
        }
        else
        {
            _gl.Disable(capability);
        }
    }

    private unsafe void CreateFontTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _fontTexture);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)InternalFormat.Rgba8, (uint)width, (uint)height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, (void*)pixels);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        io.Fonts.SetTexID((IntPtr)_fontTexture);
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
    }

    public void Dispose()
    {
        _view.Resize -= OnResize;
        _keyboard.KeyDown -= OnKeyDown;
        _keyboard.KeyUp -= OnKeyUp;
        _keyboard.KeyChar -= OnKeyChar;

        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteBuffer(_indexBuffer);
        _gl.DeleteTexture(_fontTexture);
        _shader.Dispose();
        ImGui.DestroyContext(_context);
    }

    private const string VertexSource = """
        #version 330
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
        }
        """;

    private const string FragmentSource = """
        #version 330
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;

    private static ImGuiKey Translate(Key key) => key switch
    {
        Key.Tab => ImGuiKey.Tab,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Right => ImGuiKey.RightArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.PageUp => ImGuiKey.PageUp,
        Key.PageDown => ImGuiKey.PageDown,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.Insert => ImGuiKey.Insert,
        Key.Delete => ImGuiKey.Delete,
        Key.Backspace => ImGuiKey.Backspace,
        Key.Space => ImGuiKey.Space,
        Key.Enter => ImGuiKey.Enter,
        Key.Escape => ImGuiKey.Escape,
        Key.Apostrophe => ImGuiKey.Apostrophe,
        Key.Comma => ImGuiKey.Comma,
        Key.Minus => ImGuiKey.Minus,
        Key.Period => ImGuiKey.Period,
        Key.Slash => ImGuiKey.Slash,
        Key.Semicolon => ImGuiKey.Semicolon,
        Key.Equal => ImGuiKey.Equal,
        Key.LeftBracket => ImGuiKey.LeftBracket,
        Key.BackSlash => ImGuiKey.Backslash,
        Key.RightBracket => ImGuiKey.RightBracket,
        Key.GraveAccent => ImGuiKey.GraveAccent,
        Key.CapsLock => ImGuiKey.CapsLock,
        Key.ScrollLock => ImGuiKey.ScrollLock,
        Key.NumLock => ImGuiKey.NumLock,
        Key.PrintScreen => ImGuiKey.PrintScreen,
        Key.Pause => ImGuiKey.Pause,
        >= Key.Keypad0 and <= Key.Keypad9 => ImGuiKey.Keypad0 + (key - Key.Keypad0),
        Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
        Key.KeypadDivide => ImGuiKey.KeypadDivide,
        Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
        Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
        Key.KeypadAdd => ImGuiKey.KeypadAdd,
        Key.KeypadEnter => ImGuiKey.KeypadEnter,
        Key.KeypadEqual => ImGuiKey.KeypadEqual,
        Key.ShiftLeft => ImGuiKey.LeftShift,
        Key.ControlLeft => ImGuiKey.LeftCtrl,
        Key.AltLeft => ImGuiKey.LeftAlt,
        Key.SuperLeft => ImGuiKey.LeftSuper,
        Key.ShiftRight => ImGuiKey.RightShift,
        Key.ControlRight => ImGuiKey.RightCtrl,
        Key.AltRight => ImGuiKey.RightAlt,
        Key.SuperRight => ImGuiKey.RightSuper,
        Key.Menu => ImGuiKey.Menu,
        >= Key.Number0 and <= Key.Number9 => ImGuiKey._0 + (key - Key.Number0),
        >= Key.A and <= Key.Z => ImGuiKey.A + (key - Key.A),
        >= Key.F1 and <= Key.F24 => ImGuiKey.F1 + (key - Key.F1),
        _ => ImGuiKey.None,
    };
}
