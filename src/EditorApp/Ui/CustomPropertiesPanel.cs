using System.Globalization;
using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Scene;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// An object's marker settings (Fullreleaseplan 6.4) and custom properties (6.5), in its Properties:
/// what it marks and how big, and named values for the game — text, a number, or on and off. A drag
/// or an edit typed is one undo step once it is let go.
/// </summary>
public static class CustomPropertiesPanel
{
    private static VoxelObject? _editing;
    private static ObjectMarker? _markerBefore;
    private static IReadOnlyList<CustomProperty> _propertiesBefore = [];

    /// <summary>Puts an unfinished edit into history — when another object is shown, for one.</summary>
    public static void Flush(EditorSession session)
    {
        if (_editing is { } target)
        {
            session.PushObjectDataEdit(target, _markerBefore, _propertiesBefore, $"Edit {target.Name}");
        }

        _editing = null;
    }

    public static void DrawMarker(EditorSession session, VoxelObject target)
    {
        if (target.Marker is not { } marker)
        {
            return;
        }

        Begin(session, target);

        if (Props.BeginCombo("Marks", "marker-kind", ObjectMarker.NameOf(marker.Kind)))
        {
            foreach (MarkerKind kind in Enum.GetValues<MarkerKind>())
            {
                if (ImGui.Selectable(ObjectMarker.NameOf(kind), kind == marker.Kind) && kind != marker.Kind)
                {
                    Change(session, target);
                    session.SetMarkerLive(target, ObjectMarker.Default(kind));
                    Flush(session);
                }
            }

            ImGui.EndCombo();
        }

        Vector3 size = marker.Size;
        string label = marker.Kind switch
        {
            MarkerKind.Trigger => "Box",
            MarkerKind.Sound => "Reach",
            _ => "Drawn size",
        };

        bool changed = marker.Kind == MarkerKind.Sound
            ? Props.Float(label, "marker-reach", ref size.X, 0.05f, 0.01f, 10_000f, "%.2f")
            : Props.Vector(label, "marker-size", ref size, 0.05f);

        if (changed)
        {
            Change(session, target);
            session.SetMarkerLive(target, marker with { Size = marker.Kind == MarkerKind.Sound ? new Vector3(size.X) : size });
        }

        Tooltip(marker.Kind switch
        {
            MarkerKind.Trigger => "The box the trigger fills, in world units, standing on the marker.",
            MarkerKind.Sound => "How far the sound carries, in world units.",
            _ => "How big it is drawn. The game reads its place and turn.",
        });

        End(session);
    }

    public static void DrawProperties(EditorSession session, VoxelObject target)
    {
        Begin(session, target);
        List<CustomProperty> properties = [.. target.Properties];
        int removed = -1;

        for (int i = 0; i < properties.Count; i++)
        {
            ImGui.PushID(i);
            CustomProperty property = properties[i];

            // Laid out by position, not by what came before: a checkbox is narrower than a field.
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float start = ImGui.GetCursorPosX();
            float room = ImGui.GetContentRegionAvail().X;
            float button = ImGui.GetFrameHeight();
            float kindWidth = button * 2.2f;
            float keyWidth = room * 0.38f;
            float valueWidth = MathF.Max(room - keyWidth - kindWidth - button - (spacing * 3f), button);

            string key = property.Key;
            ImGui.SetNextItemWidth(keyWidth);
            if (ImGui.InputText("##key", ref key, CustomProperty.MaxKeyLength))
            {
                Change(session, target);
                properties[i] = property with { Key = key.Trim() };
            }

            ImGui.SameLine(start + keyWidth + spacing);
            ImGui.SetNextItemWidth(valueWidth);
            properties[i] = DrawValue(session, target, properties[i]);

            ImGui.SameLine(start + keyWidth + valueWidth + (spacing * 2f));
            ImGui.SetNextItemWidth(kindWidth);
            if (ImGui.BeginCombo("##kind", property.Kind switch { PropertyKind.Number => "#", PropertyKind.Toggle => "on", _ => "abc" }, ImGuiComboFlags.NoArrowButton))
            {
                foreach (PropertyKind kind in Enum.GetValues<PropertyKind>())
                {
                    if (ImGui.Selectable(kind.ToString(), kind == property.Kind) && kind != property.Kind)
                    {
                        Flush(session);
                        properties[i] = properties[i].As(kind);
                        session.SetProperties(target.Id, properties, $"Make {property.Key} {kind}");
                        ImGui.EndCombo();
                        ImGui.PopID();
                        return;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine(start + keyWidth + valueWidth + kindWidth + (spacing * 3f));
            if (ImGui.Button("x", new Vector2(button)))
            {
                removed = i;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Take {property.Key} away");
            }

            ImGui.PopID();
        }

        if (removed >= 0)
        {
            Flush(session);
            string name = properties[removed].Key;
            properties.RemoveAt(removed);
            session.SetProperties(target.Id, properties, $"Remove {name}");
            return;
        }

        if (!properties.SequenceEqual(target.Properties))
        {
            session.SetPropertiesLive(target, properties);
        }

        End(session);

        if (ImGui.Button("Add Property", new Vector2(-1f, 0f)))
        {
            Flush(session);
            string key = "property";
            for (int n = 2; target.Properties.Any(p => p.Key == key); n++)
            {
                key = $"property{n}";
            }

            session.SetProperties(target.Id, [.. target.Properties, new CustomProperty(key, PropertyKind.Text, string.Empty)], $"Add {key}");
        }

        Tooltip("A named value for the game to read, sent in the glTF node's extras.");
    }

    private static CustomProperty DrawValue(EditorSession session, VoxelObject target, CustomProperty property)
    {
        switch (property.Kind)
        {
            case PropertyKind.Number:
                double number = property.Number;
                if (ImGui.InputDouble("##value", ref number, 0d, 0d, "%.6g"))
                {
                    Change(session, target);
                    return property with { Value = number.ToString(CultureInfo.InvariantCulture) };
                }

                return property;

            case PropertyKind.Toggle:
                bool on = property.Toggle;
                if (ImGui.Checkbox("##value", ref on))
                {
                    Change(session, target);
                    return property with { Value = on ? "true" : "false" };
                }

                return property;

            default:
                string text = property.Value;
                if (ImGui.InputText("##value", ref text, CustomProperty.MaxValueLength))
                {
                    Change(session, target);
                    return property with { Value = text };
                }

                return property;
        }
    }

    private static void Begin(EditorSession session, VoxelObject target)
    {
        if (_editing is not null && !ReferenceEquals(_editing, target))
        {
            Flush(session);
        }
    }

    /// <summary>The first change of a gesture remembers how things were before it.</summary>
    private static void Change(EditorSession session, VoxelObject target)
    {
        if (_editing is null)
        {
            _editing = target;
            _markerBefore = target.Marker;
            _propertiesBefore = target.Properties;
        }
    }

    /// <summary>The gesture is over once nothing is held.</summary>
    private static void End(EditorSession session)
    {
        if (_editing is not null && !ImGui.IsAnyItemActive())
        {
            Flush(session);
        }
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }
}
