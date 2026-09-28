using System.Text.Json;
using System.Text.Json.Serialization;

namespace EditorApp.Ui;

/// <summary>
/// The Properties editor's tabs, in the order the strip shows them: by how often they are reached
/// for, the tool in hand first, as in Blender.
/// </summary>
public enum PropertiesTab
{
    /// <summary>Every setting of the active tool.</summary>
    Tool,

    /// <summary>The picked object's placement and voxels — or a light's settings, when one is picked.</summary>
    Object,

    Palette,

    /// <summary>The level as a whole: its size, the grid, ambient light and the lights.</summary>
    World,

    Reference,
}

/// <summary>
/// How the sidebar was left: how wide, where the divider between Outliner and Properties sat, which
/// tab was open. Kept beside the recent-files list and read back on the next start, so the layout a
/// user settled on is the one they come back to.
///
/// Every value is clamped on the way in, from a file or a drag alike: a sidebar dragged to nothing,
/// or a file edited by hand, must not leave the editor with no room for the model.
/// </summary>
public sealed class LayoutSettings
{
    public const float MinSidebarWidth = 260f;
    public const float MaxSidebarWidth = 640f;
    public const float DefaultSidebarWidth = 340f;

    public const float MinOutlinerHeight = 80f;
    public const float DefaultOutlinerHeight = 190f;

    /// <summary>Always left for Properties below the divider, so dragging it down cannot swallow them.</summary>
    public const float MinPropertiesHeight = 140f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public float SidebarWidth
    {
        get => _sidebarWidth;
        set => _sidebarWidth = float.IsFinite(value) ? Math.Clamp(value, MinSidebarWidth, MaxSidebarWidth) : _sidebarWidth;
    }

    private float _sidebarWidth = DefaultSidebarWidth;

    /// <summary>
    /// The Outliner's height. Clamped only against its own minimum here; how much of the column it may
    /// take depends on how tall the window is, which <see cref="OutlinerHeightWithin"/> decides.
    /// </summary>
    public float OutlinerHeight
    {
        get => _outlinerHeight;
        set => _outlinerHeight = float.IsFinite(value) ? MathF.Max(value, MinOutlinerHeight) : _outlinerHeight;
    }

    private float _outlinerHeight = DefaultOutlinerHeight;

    public PropertiesTab Tab { get; set; } = PropertiesTab.Object;

    /// <summary>The frame and mesh counters over the viewport.</summary>
    public bool StatisticsVisible { get; set; }

    /// <summary>The whole right-hand column; N hides it for more of the model.</summary>
    public bool SidebarVisible { get; set; } = true;

    /// <summary>The Outliner's height in a column this tall: never so much that Properties lose their minimum.</summary>
    public float OutlinerHeightWithin(float columnHeight) =>
        Math.Clamp(OutlinerHeight, MinOutlinerHeight, MathF.Max(columnHeight - MinPropertiesHeight, MinOutlinerHeight));

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EditorApp",
        "layout.json");

    /// <summary>What was saved, or the defaults when there is nothing readable.</summary>
    public static LayoutSettings Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), JsonOptions) is { } stored)
            {
                return new LayoutSettings
                {
                    SidebarWidth = stored.SidebarWidth,
                    OutlinerHeight = stored.OutlinerHeight,
                    Tab = Enum.IsDefined(stored.Tab) ? stored.Tab : PropertiesTab.Object,
                    StatisticsVisible = stored.StatisticsVisible,
                    SidebarVisible = stored.SidebarVisible ?? true,
                };
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A layout is a convenience; one that cannot be read is the default one.
        }

        return new LayoutSettings();
    }

    public void Save(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stored = new Stored(SidebarWidth, OutlinerHeight, Tab, StatisticsVisible, SidebarVisible);
            File.WriteAllText(path, JsonSerializer.Serialize(stored, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a close over.
        }
    }

    /// <param name="SidebarVisible">Absent from layouts saved before the sidebar could be hidden.</param>
    private sealed record Stored(float SidebarWidth, float OutlinerHeight, PropertiesTab Tab, bool StatisticsVisible, bool? SidebarVisible = null);
}
