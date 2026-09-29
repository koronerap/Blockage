using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace EditorApp.Core.Scene;

/// <summary>What a marker marks, for the game that reads the level.</summary>
public enum MarkerKind
{
    /// <summary>A named point, as Blender's plain empty: a place and a turn for the game to use.</summary>
    Empty,

    /// <summary>Where a player or a creature appears, facing the way it points.</summary>
    Spawn,

    /// <summary>A box something happens in when it is entered.</summary>
    Trigger,

    /// <summary>Where a sound comes from, heard as far as its size.</summary>
    Sound,
}

/// <summary>
/// An object as a marker (Fullreleaseplan 6.4): a point, a spawn, a trigger box or a sound, with no
/// voxels needed — Blender's empties. <paramref name="Size"/> is the box a trigger fills, how far a
/// sound carries (its X), or how big the others are drawn, in world units.
/// </summary>
public sealed record ObjectMarker(MarkerKind Kind, Vector3 Size)
{
    public static ObjectMarker Default(MarkerKind kind) => kind switch
    {
        MarkerKind.Trigger => new(kind, new Vector3(4f, 3f, 4f)),
        MarkerKind.Sound => new(kind, new Vector3(8f)),
        _ => new(kind, new Vector3(1f)),
    };

    public static string NameOf(MarkerKind kind) => kind switch
    {
        MarkerKind.Spawn => "Spawn point",
        MarkerKind.Trigger => "Trigger",
        MarkerKind.Sound => "Sound",
        _ => "Empty",
    };

    /// <summary>How it is written in files and exports.</summary>
    public static string KeyOf(MarkerKind kind) => kind.ToString().ToLowerInvariant();

    public static MarkerKind? Parse(string? key) =>
        Enum.GetValues<MarkerKind>().Cast<MarkerKind?>().FirstOrDefault(kind => KeyOf(kind!.Value) == key);

    public ObjectMarker Clamped() => this with { Size = Vector3.Clamp(Size, new Vector3(0.01f), new Vector3(10_000f)) };
}

/// <summary>What a custom property holds.</summary>
public enum PropertyKind
{
    Text,
    Number,
    Toggle,
}

/// <summary>
/// A custom property (Fullreleaseplan 6.5), as Blender has them: a named value on an object for the
/// game to read — a door's key, a crate's loot table, a light's flicker — kept with the level and
/// sent out in the glTF node's extras. The value is kept as text; a number in the invariant culture.
/// </summary>
public sealed record CustomProperty(string Key, PropertyKind Kind, string Value)
{
    public const int MaxKeyLength = 64;

    public const int MaxValueLength = 256;

    public double Number => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0d;

    public bool Toggle => Value == "true";

    /// <summary>The value as JSON: a string, a number or a boolean.</summary>
    public JsonNode? ToJson() => Kind switch
    {
        PropertyKind.Number => JsonValue.Create(Number),
        PropertyKind.Toggle => JsonValue.Create(Toggle),
        _ => JsonValue.Create(Value),
    };

    /// <summary>The same property holding a different kind of value, its value carried over where it can be.</summary>
    public CustomProperty As(PropertyKind kind) => kind switch
    {
        PropertyKind.Number => this with { Kind = kind, Value = Number.ToString(CultureInfo.InvariantCulture) },
        PropertyKind.Toggle => this with { Kind = kind, Value = Value is "true" or "1" ? "true" : "false" },
        _ => this with { Kind = kind },
    };

    public CustomProperty Clamped() => this with
    {
        Key = Key.Length > MaxKeyLength ? Key[..MaxKeyLength] : Key,
        Value = Value.Length > MaxValueLength ? Value[..MaxValueLength] : Value,
    };
}
