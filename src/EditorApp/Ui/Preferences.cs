using System.Text.Json;
using System.Text.Json.Serialization;
using EditorApp.Core.Editing;
using EditorApp.Input;
using EditorApp.Rendering;

namespace EditorApp.Ui;

/// <summary>
/// Settings of the editor itself rather than of any level: how it looks, how the view moves, what
/// the keys do. Kept beside the layout and the recent-files list, read at start, written when the
/// Preferences window closes and when the editor does.
///
/// Every value is clamped on the way in, from the window or from a file edited by hand, the way the
/// layout's are: a field of view of zero or a gizmo of no size must not be something a typo can do.
/// </summary>
public sealed class Preferences
{
    public const float MinFieldOfView = 30f;
    public const float MaxFieldOfView = 100f;

    /// <summary>The autosave intervals on offer, in minutes; zero is off.</summary>
    public static readonly int[] AutosaveChoices = [0, 1, 2, 5, 10];

    /// <summary>How much the undo stack may hold, in millions of cells.</summary>
    public static readonly int[] UndoChoices = [2, 8, 32, 64];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // ---- Interface ---------------------------------------------------------------------------

    public ThemeKind Theme { get; set; } = ThemeKind.Dark;

    public AccentKind Accent { get; set; } = AccentKind.Blue;

    public TextSize TextSize { get; set; } = TextSize.Normal;

    /// <summary>The mouse hints along the bottom, which say what the buttons do right now.</summary>
    public bool MouseHints { get; set; } = true;

    /// <summary>The welcome screen when the editor starts without a level to open.</summary>
    public bool ShowWelcome { get; set; } = true;

    // ---- Viewport ----------------------------------------------------------------------------

    /// <summary>Vertical field of view, in degrees.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set => _fieldOfView = Clamp(value, MinFieldOfView, MaxFieldOfView, _fieldOfView);
    }

    private float _fieldOfView = 60f;

    /// <summary>Overlay strokes — outlines, gizmos, the cut ring — against their designed width.</summary>
    public float LineWidth
    {
        get => _lineWidth;
        set => _lineWidth = Clamp(value, 0.5f, 2f, _lineWidth);
    }

    private float _lineWidth = 1f;

    /// <summary>The Transform gizmo against its designed size.</summary>
    public float GizmoSize
    {
        get => _gizmoSize;
        set => _gizmoSize = Clamp(value, 0.5f, 2f, _gizmoSize);
    }

    private float _gizmoSize = 1f;

    /// <summary>Frames wait for the screen. Off draws as fast as it can, and runs the fans for it.</summary>
    public bool VSync { get; set; } = true;

    /// <summary>Gizmos, overlays, X-Ray and shading, as the viewport header leaves them.</summary>
    public ViewportSettings Viewport { get; set; } = new();

    // ---- Navigation --------------------------------------------------------------------------

    /// <summary>Orbiting and looking, against their designed speed.</summary>
    public float OrbitSpeed
    {
        get => _orbitSpeed;
        set => _orbitSpeed = Clamp(value, 0.25f, 3f, _orbitSpeed);
    }

    private float _orbitSpeed = 1f;

    /// <summary>Flying with W A S D while looking, against its designed speed.</summary>
    public float FlySpeed
    {
        get => _flySpeed;
        set => _flySpeed = Clamp(value, 0.25f, 4f, _flySpeed);
    }

    private float _flySpeed = 1f;

    /// <summary>The wheel zooms out when rolled forward, as some programs have it.</summary>
    public bool InvertZoom { get; set; }

    // ---- Editing -----------------------------------------------------------------------------

    /// <summary>The magnet, what it snaps to, and the rotation increment — kept between sessions.</summary>
    public SnapSettings Snap { get; set; } = new();

    /// <summary>How much the undo stack holds before its oldest steps go, in millions of cells.</summary>
    public int UndoMemory
    {
        get => _undoMemory;
        set => _undoMemory = UndoChoices.Contains(value) ? value : _undoMemory;
    }

    private int _undoMemory = 8;

    /// <summary>Minutes of unsaved work before a copy is written for recovery; zero is off.</summary>
    public int AutosaveMinutes
    {
        get => _autosaveMinutes;
        set => _autosaveMinutes = AutosaveChoices.Contains(value) ? value : _autosaveMinutes;
    }

    private int _autosaveMinutes = 2;

    // ---- Keymap ------------------------------------------------------------------------------

    public KeymapPreset KeymapPreset { get; set; } = KeymapPreset.Default;

    /// <summary>What differs from the preset, as <see cref="Keymap.Changes"/> writes it.</summary>
    public Dictionary<string, string[]> KeymapChanges { get; set; } = [];

    // ---- Files -------------------------------------------------------------------------------

    public int RecentFilesKept
    {
        get => _recentFilesKept;
        set => _recentFilesKept = Math.Clamp(value, 1, RecentFiles.MaxCapacity);
    }

    private int _recentFilesKept = 10;

    // ---- Search ------------------------------------------------------------------------------

    /// <summary>The F3 search's commands last done, newest first, by id: what it offers before anything is typed.</summary>
    public List<string> RecentCommands { get; set; } = [];

    // ---- Keeping them ------------------------------------------------------------------------

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EditorApp",
        "preferences.json");

    /// <summary>The keymap these preferences describe.</summary>
    public Keymap BuildKeymap() => Keymap.With(KeymapPreset, KeymapChanges);

    /// <summary>Takes in the keymap as it now is.</summary>
    public void Remember(Keymap keymap)
    {
        KeymapPreset = keymap.Preset;
        KeymapChanges = keymap.Changes();
    }

    /// <summary>What was saved, or the defaults when there is nothing readable.</summary>
    public static Preferences Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), JsonOptions) is { } loaded)
            {
                // Enums from a file edited by hand can hold anything the number fits.
                loaded.Theme = Enum.IsDefined(loaded.Theme) ? loaded.Theme : ThemeKind.Dark;
                loaded.Accent = Enum.IsDefined(loaded.Accent) ? loaded.Accent : AccentKind.Blue;
                loaded.TextSize = Enum.IsDefined(loaded.TextSize) ? loaded.TextSize : TextSize.Normal;
                loaded.KeymapPreset = Enum.IsDefined(loaded.KeymapPreset) ? loaded.KeymapPreset : KeymapPreset.Default;
                loaded.KeymapChanges ??= [];
                loaded.Snap ??= new SnapSettings();
                loaded.Viewport ??= new ViewportSettings();
                loaded.RecentCommands ??= [];
                return loaded;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Preferences are a convenience; ones that cannot be read are the defaults.
        }

        return new Preferences();
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

            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a close over.
        }
    }

    public Preferences Clone() =>
        JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions) ?? new Preferences();

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
