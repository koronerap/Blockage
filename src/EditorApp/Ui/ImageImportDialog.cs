using System.Numerics;
using EditorApp.Core.Import;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>What an image is made into.</summary>
public enum ImageImport
{
    /// <summary>A pixel-art sprite, stood up with a thickness.</summary>
    Sprite,

    /// <summary>A heightmap, raised into terrain.</summary>
    Heightmap,
}

/// <summary>
/// An image made into voxels (Fullreleaseplan 8.2): the image chosen first, then how it is made into
/// voxels, then set down in the level where the view looks, as a prop is.
/// </summary>
public static class ImageImportDialog
{
    private const string PopupId = "Image to voxels###image-import";

    private static readonly FileBrowserDialog Browser = new();

    private static ImageImport _kind;
    private static DecodedImage? _image;
    private static string _name = string.Empty;
    private static bool _openRequested;
    private static string? _error;

    private static int _thickness = 1;
    private static int _cutoff = 128;
    private static int _width = 128;
    private static int _height = 24;
    private static int _water;

    /// <summary>What was made, waiting to be set down in the level.</summary>
    private static (VoxelScene Scene, string Name)? _made;

    /// <summary>Asks for the image.</summary>
    public static void Show(ImageImport kind)
    {
        _kind = kind;
        Browser.Show(
            FileBrowserMode.Open,
            kind == ImageImport.Sprite ? "Import an image as a sprite" : "Import a heightmap as terrain",
            ".png",
            null,
            null,
            Load);
    }

    /// <summary>Reads the image and asks how to make it, the way choosing it in the dialog does.</summary>
    public static void Load(string path)
    {
        try
        {
            _image = PngReader.Decode(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageDecodeException or InvalidDataException)
        {
            ReportLog.Shared.Post($"Could not read {Path.GetFileName(path)}: {exception.Message}", ReportKind.Error);
            return;
        }

        _name = Path.GetFileNameWithoutExtension(path);
        _width = Math.Min(_image.Width, 256);
        _error = null;
        _openRequested = true;
    }

    /// <summary>What was made since last asked, once: the host sets it down where the view looks.</summary>
    public static (VoxelScene Scene, string Name)? TakeMade()
    {
        (VoxelScene Scene, string Name)? made = _made;
        _made = null;
        return made;
    }

    public static void Draw()
    {
        Browser.Draw();

        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        if (_image is not { } image)
        {
            return;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            if (!open)
            {
                _image = null;
            }

            return;
        }

        ImGui.TextDisabled($"{_name}.png - {image.Width} x {image.Height} pixels");
        ImGui.Spacing();
        ImGui.PushItemWidth(ImGui.GetFontSize() * 12f);

        if (_kind == ImageImport.Sprite)
        {
            ImGui.SliderInt("Thickness", ref _thickness, 1, ImageVoxels.MaxThickness);
            Hint("How many voxels deep each pixel goes.");
            ImGui.SliderInt("See-through below", ref _cutoff, 1, 255);
            Hint("Pixels less solid than this are left out: 128 keeps what is more than half there.");
        }
        else
        {
            ImGui.SliderInt("Width", ref _width, 8, Math.Min(ImageVoxels.MaxWidth, Math.Max(image.Width * 2, 64)));
            Hint("How many voxels across; the depth follows the image's shape.");
            ImGui.SliderInt("Height", ref _height, 2, ImageVoxels.MaxHeight);
            Hint("How high white stands. Black is one voxel of ground.");
            ImGui.SliderInt("Water", ref _water, 0, _height);
            Hint("Water fills the low ground up to here; 0 for none.");
        }

        ImGui.PopItemWidth();

        if (_error is not null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.Highlight, _error);
        }

        ImGui.Spacing();
        if (ImGui.Button("Import", Theme.ModalButton))
        {
            try
            {
                VoxelScene made = _kind == ImageImport.Sprite
                    ? ImageVoxels.Sprite(image, _thickness, (byte)_cutoff, _name)
                    : ImageVoxels.Heightmap(image, _width, _height, _water, _name);
                _made = (made, _name);
                _image = null;
                ImGui.CloseCurrentPopup();
            }
            catch (InvalidDataException exception)
            {
                _error = exception.Message;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", Theme.ModalButton))
        {
            _image = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();

        if (!open)
        {
            _image = null;
        }
    }

    private static void Hint(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
