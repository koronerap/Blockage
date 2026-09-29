using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Blockage.Importer
{
    /// <summary>
    /// Just enough JSON to read a level's manifest — which JsonUtility cannot, with its arrays of
    /// arrays and its optional numbers: objects as dictionaries, arrays as lists, numbers as doubles.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            int at = 0;
            object value = Value(text, ref at);
            Skip(text, ref at);
            if (at != text.Length)
            {
                throw new FormatException($"Unexpected text at {at} in the manifest.");
            }

            return value;
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("The manifest ends early.");
            }

            switch (s[i])
            {
                case '{':
                    return ParseObject(s, ref i);
                case '[':
                    return ParseArray(s, ref i);
                case '"':
                    return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (s[i] == '}')
            {
                i++;
                return result;
            }

            while (true)
            {
                Skip(s, ref i);
                string key = ParseString(s, ref i);
                Skip(s, ref i);
                if (s[i] != ':')
                {
                    throw new FormatException($"Expected ':' at {i} in the manifest.");
                }

                i++;
                result[key] = Value(s, ref i);
                Skip(s, ref i);
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (s[i] == '}')
                {
                    i++;
                    return result;
                }

                throw new FormatException($"Expected ',' or '}}' at {i} in the manifest.");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var result = new List<object>();
            i++;
            Skip(s, ref i);
            if (s[i] == ']')
            {
                i++;
                return result;
            }

            while (true)
            {
                result.Add(Value(s, ref i));
                Skip(s, ref i);
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (s[i] == ']')
                {
                    i++;
                    return result;
                }

                throw new FormatException($"Expected ',' or ']' at {i} in the manifest.");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"')
            {
                throw new FormatException($"Expected a string at {i} in the manifest.");
            }

            var text = new StringBuilder();
            i++;
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }

                char escaped = s[i++];
                switch (escaped)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': text.Append('\t'); break;
                    case 'r': text.Append('\r'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'u':
                        text.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: text.Append(escaped); break;
                }
            }

            i++;
            return text.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }

            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw new FormatException($"Expected {word} at {i} in the manifest.");
            }

            i += word.Length;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        // ---- Reading what was parsed ------------------------------------------------------------

        public static Dictionary<string, object> Obj(this Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;

        public static List<object> List(this Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out object value) && value is List<object> list ? list : new List<object>();

        public static string Text(this Dictionary<string, object> json, string key, string fallback = null) =>
            json.TryGetValue(key, out object value) && value is string text ? text : fallback;

        public static double Number(this Dictionary<string, object> json, string key, double fallback = 0) =>
            json.TryGetValue(key, out object value) && value is double number ? number : fallback;

        public static int? Int(this Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out object value) && value is double number ? (int)number : (int?)null;

        public static bool Bool(this Dictionary<string, object> json, string key, bool fallback) =>
            json.TryGetValue(key, out object value) && value is bool flag ? flag : fallback;

        public static float[] Floats(this Dictionary<string, object> json, string key)
        {
            if (!json.TryGetValue(key, out object value) || !(value is List<object> list))
            {
                return null;
            }

            var result = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                result[i] = list[i] is double number ? (float)number : 0f;
            }

            return result;
        }
    }
}
