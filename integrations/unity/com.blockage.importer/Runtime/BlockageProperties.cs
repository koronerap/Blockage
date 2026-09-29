using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Blockage
{
    /// <summary>
    /// An object's custom properties, as they were set in Blockage: named values for the game to read
    /// — a door's key, a crate's loot table — each text, a number, or on and off.
    /// </summary>
    public sealed class BlockageProperties : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string key;

            /// <summary>"text", "number" or "toggle".</summary>
            public string type;

            /// <summary>The value as text; a number in the invariant culture, a toggle "true" or "false".</summary>
            public string value;
        }

        public List<Entry> entries = new List<Entry>();

        public bool Has(string key) => entries.Exists(entry => entry.key == key);

        public bool TryGetValue(string key, out string value)
        {
            foreach (Entry entry in entries)
            {
                if (entry.key == key)
                {
                    value = entry.value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public string GetString(string key, string fallback = "") =>
            TryGetValue(key, out string value) ? value : fallback;

        public float GetNumber(string key, float fallback = 0f) =>
            TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number : fallback;

        public bool GetBool(string key, bool fallback = false) =>
            TryGetValue(key, out string value) ? value == "true" : fallback;
    }
}
