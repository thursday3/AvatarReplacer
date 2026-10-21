using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AvatarReplacer
{
    /// <summary>
    /// Minimal JSON reader/writer for the mod's config files.
    ///
    /// Why not UnityEngine.JsonUtility: in this game's Unity 2022.3 runtime,
    /// JsonUtility silently drops every field whose value type lives in this
    /// plugin assembly. Observed in-game: expression-map templates lost their
    /// entire "mappings" list and avatar-profile templates lost
    /// positionOffset / rotationOffset, leaving 3-line skeleton files.
    /// Serializing these files ourselves keeps the on-disk format exactly
    /// what the docs promise, behaves identically outside Unity (the test
    /// harness exercises the real production code), and lets us accept
    /// hand-edited files JsonUtility would reject: "// comments", trailing
    /// commas, and unknown keys are all tolerated.
    ///
    /// Value model: Dictionary&lt;string, object&gt; | List&lt;object&gt; | string | double | bool | null
    /// </summary>
    internal static class SimpleJson
    {
        // ---------- writing ----------

        /// <summary>A JSON string literal with the quotes, escaping control characters.</summary>
        public static string Quote(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>A plain JSON number; whole values keep a ".0" suffix so files read cleanly.</summary>
        public static string Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                return d.ToString("0.0", CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        // ---------- reading ----------

        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON text is null");
            int i = 0;
            object value = ParseValue(json, ref i);
            SkipWhitespace(json, ref i);
            if (i < json.Length)
                throw new FormatException($"Unexpected trailing content at character {i}");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            var obj = Parse(json) as Dictionary<string, object>;
            if (obj == null) throw new FormatException("JSON root is not an object { }");
            return obj;
        }

        // ---- typed getters (missing keys return the fallback; never throw) ----

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v))
            {
                if (v is string) return (string)v;
                if (v == null) return fallback;
                // Tolerate hand-edited numbers/bools in string slots.
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
            return fallback;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v)) return v as List<object>;
            return null;
        }

        public static float GetFloat(Dictionary<string, object> obj, string key, float fallback)
        {
            object v;
            if (obj == null || !obj.TryGetValue(key, out v)) return fallback;
            if (v is double) return (float)(double)v;
            if (v is string)
            {
                float f;
                if (float.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
            }
            return fallback;
        }

        // ---------- parser ----------

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                    i = Math.Min(i + 2, s.Length);
                    continue;
                }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObjectBody(s, ref i);
                case '[': return ParseArrayBody(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    if (Match(s, ref i, "true")) return true;
                    break;
                case 'f':
                    if (Match(s, ref i, "false")) return false;
                    break;
                case 'n':
                    if (Match(s, ref i, "null")) return null;
                    break;
            }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObjectBody(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated object");
                if (s[i] == '}') { i++; return result; } // trailing comma tolerated
                if (s[i] != '"') throw new FormatException($"Expected object key at character {i}");
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"Expected ':' at character {i}");
                i++;
                result[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return result; }
                throw new FormatException($"Expected ',' or '}}' at character {i}");
            }
        }

        private static List<object> ParseArrayBody(string s, ref int i)
        {
            var result = new List<object>();
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated array");
                if (s[i] == ']') { i++; return result; } // trailing comma tolerated
                result.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return result; }
                throw new FormatException($"Expected ',' or ']' at character {i}");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Truncated \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException($"Invalid escape '\\{e}' at character {i}");
                }
            }
            throw new FormatException("Unterminated string");
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;
            string token = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException($"Invalid number '{token}' at character {start}");
            return d;
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }
    }
}
