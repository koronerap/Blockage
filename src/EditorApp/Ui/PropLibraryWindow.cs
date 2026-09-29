using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Import;
using EditorApp.Core.Project;
using EditorApp.Core.Voxels;
using ImGuiNET;
using Silk.NET.OpenGL;

namespace EditorApp.Ui;

/// <summary>
/// The prop library (Fullreleaseplan 6.3): your props as pictures, to click in where the view looks
/// or drag onto a surface in the viewport; the selection saved into it under a name.
/// </summary>
public sealed class PropLibraryWindow(GL gl) : IDisposable
{
    private const float Cell = 84f;

    private readonly Dictionary<string, (uint Texture, DateTime Written)> _pictures = [];

    private bool _open;
    private bool _stale = true;
    private IReadOnlyList<PropEntry> _entries = [];
    private string _name = "Prop";
    private PropEntry? _dragging;
    private PropEntry? _clicked;
    private (PropEntry Entry, Vector2 At)? _dropped;

    /// <summary>Where the props are kept; the user's own library unless a test says otherwise.</summary>
    public string Folder { get; init; } = PropLibrary.DefaultDirectory;

    public bool IsOpen => _open;

    public void Open()
    {
        _open = true;
        _stale = true;
    }

    /// <summary>A prop clicked, to go where the view looks; null when none was.</summary>
    public PropEntry? TakeClicked()
    {
        PropEntry? clicked = _clicked;
        _clicked = null;
        return clicked;
    }

    /// <summary>A prop let go of over the viewport, and where on screen; null when none was.</summary>
    public (PropEntry Entry, Vector2 At)? TakeDropped()
    {
        (PropEntry, Vector2)? dropped = _dropped;
        _dropped = null;
        return dropped;
    }

    public void Draw(EditorSession session)
    {
        // A prop dragged out of the window and let go over no window at all was let go over the viewport.
        if (_dragging is { } dragged && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
            {
                _dropped = (dragged, ImGui.GetMousePos());
            }

            _dragging = null;
        }

        if (!_open)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(Cell * 4.6f, Cell * 5f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Prop Library", ref _open))
        {
            ImGui.End();
            return;
        }

        if (_stale)
        {
            _entries = PropLibrary.List(Folder);
            _stale = false;
        }

        DrawSave(session);
        ImGui.Separator();

        if (_entries.Count == 0)
        {
            ImGui.TextDisabled("No props yet: select objects and save them here.");
        }

        float room = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)((room + ImGui.GetStyle().ItemSpacing.X) / (Cell + ImGui.GetStyle().ItemSpacing.X)));
        for (int i = 0; i < _entries.Count; i++)
        {
            if (i % columns != 0)
            {
                ImGui.SameLine();
            }

            DrawEntry(_entries[i]);
        }

        ImGui.End();
    }

    private void DrawSave(EditorSession session)
    {
        List<Core.Scene.VoxelObject> objects = [.. session.SelectedObjects.Where(o => !o.IsEmpty)];
        ImGui.SetNextItemWidth(MathF.Max(ImGui.GetContentRegionAvail().X - (ImGui.GetFontSize() * 8f), 60f));
        ImGui.InputText("##prop-name", ref _name, 64);
        ImGui.SameLine();

        ImGui.BeginDisabled(objects.Count == 0 || _name.Trim().Length == 0);
        if (ImGui.Button("Save Selection"))
        {
            try
            {
                PropEntry saved = PropLibrary.Save(Folder, _name.Trim(), session.Scene, objects);
                Forget(saved.ThumbnailPath);
                _stale = true;
                ReportLog.Shared.Post($"Saved {saved.Name} to the prop library.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReportLog.Shared.Post($"Could not save the prop: {exception.Message}", ReportKind.Error);
            }
        }

        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(objects.Count == 0 ? "Select the objects to keep as a prop first." : $"Keeps the {objects.Count} selected object{(objects.Count == 1 ? string.Empty : "s")} as a prop under this name.");
        }
    }

    private void DrawEntry(PropEntry entry)
    {
        ImGui.PushID(entry.LevelPath);
        ImGui.BeginGroup();

        uint picture = Picture(entry);
        bool clicked = picture != 0
            ? ImGui.ImageButton("##picture", (IntPtr)picture, new Vector2(Cell - 8f))
            : ImGui.Button(entry.Name, new Vector2(Cell));

        if (clicked)
        {
            _clicked = entry;
        }

        if (ImGui.BeginDragDropSource())
        {
            _dragging = entry;
            ImGui.SetDragDropPayload("PROP", IntPtr.Zero, 0);
            ImGui.TextUnformatted(entry.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{entry.Name}\nClick to add it where the view looks, or drag it onto a surface.");
        }

        if (ImGui.BeginPopupContextItem("##prop-menu"))
        {
            if (ImGui.MenuItem("Delete from Library"))
            {
                try
                {
                    PropLibrary.Delete(entry);
                    Forget(entry.ThumbnailPath);
                    _stale = true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ReportLog.Shared.Post($"Could not delete {entry.Name}: {exception.Message}", ReportKind.Error);
                }
            }

            ImGui.EndPopup();
        }

        // The name under the picture, cut short to the cell.
        string name = entry.Name;
        while (name.Length > 1 && ImGui.CalcTextSize(name).X > Cell)
        {
            name = name[..^2] + "…";
        }

        ImGui.TextDisabled(name);
        ImGui.EndGroup();
        ImGui.PopID();
    }

    /// <summary>A prop's picture on the GPU, loaded once and again only when the file changes; 0 without one.</summary>
    private unsafe uint Picture(PropEntry entry)
    {
        if (!File.Exists(entry.ThumbnailPath))
        {
            return 0;
        }

        DateTime written = File.GetLastWriteTimeUtc(entry.ThumbnailPath);
        if (_pictures.TryGetValue(entry.ThumbnailPath, out var known) && known.Written == written)
        {
            return known.Texture;
        }

        Forget(entry.ThumbnailPath);
        DecodedImage image;
        try
        {
            image = PngReader.Decode(entry.ThumbnailPath);
        }
        catch (Exception exception) when (exception is IOException or ImageDecodeException)
        {
            return 0;
        }

        uint texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        fixed (Color32* pixels = image.Pixels)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);
        _pictures[entry.ThumbnailPath] = (texture, written);
        return texture;
    }

    private void Forget(string path)
    {
        if (_pictures.Remove(path, out var known))
        {
            gl.DeleteTexture(known.Texture);
        }
    }

    public void Dispose()
    {
        foreach ((uint texture, _) in _pictures.Values)
        {
            gl.DeleteTexture(texture);
        }

        _pictures.Clear();
    }
}
