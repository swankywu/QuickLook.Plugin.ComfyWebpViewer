using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QuickLook.Plugin.ComfyWebpViewer
{
    /// <summary>
    /// Minimal, dependency-free JSON reader + pretty printer.
    /// Only what the metadata panel needs: no external packages, no reflection tricks.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var i = 0;
            SkipWs(text, ref i);
            return ParseValue(text, ref i);
        }

        public static bool TryParse(string text, out object node)
        {
            try
            {
                node = Parse(text);
                return node != null;
            }
            catch
            {
                node = null;
                return false;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length)
                throw new FormatException("Unexpected end of JSON");

            var c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWs(s, ref i);

            if (i < s.Length && s[i] == '}')
            {
                i++;
                return map;
            }

            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("Expected object key at " + i);

                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("Expected ':' at " + i);
                i++;

                map[key] = ParseValue(s, ref i);

                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return map;
                }

                throw new FormatException("Expected ',' or '}' at " + i);
            }

            throw new FormatException("Unterminated object");
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            SkipWs(s, ref i);

            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }

            while (i < s.Length)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);

                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }

                throw new FormatException("Expected ',' or ']' at " + i);
            }

            throw new FormatException("Unterminated array");
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();

            while (i < s.Length)
            {
                var c = s[i];
                if (c == '"')
                {
                    i++;
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                i++;
                if (i >= s.Length)
                    break;

                var e = s[i++];
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
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }

            throw new FormatException("Unterminated string");
        }

        private static object ParseNumber(string s, ref int i)
        {
            var start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
                i++;

            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+'))
                i++;

            var raw = s.Substring(start, i - start);
            double d;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                // Keep integral values readable: 20 instead of 20.0
                if (Math.Abs(d % 1) < double.Epsilon && Math.Abs(d) < 9.2e18)
                    return (long)d;
                return d;
            }

            throw new FormatException("Bad number: " + raw);
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("Expected " + word);
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        // ---------------------------------------------------------------- helpers

        public static Dictionary<string, object> AsObject(object node)
        {
            return node as Dictionary<string, object>;
        }

        public static List<object> AsArray(object node)
        {
            return node as List<object>;
        }

        public static string AsText(object node)
        {
            if (node == null)
                return null;
            if (node is string s)
                return s;
            if (node is long l)
                return l.ToString(CultureInfo.InvariantCulture);
            if (node is double d)
                return d.ToString(CultureInfo.InvariantCulture);
            if (node is bool b)
                return b ? "true" : "false";
            return null;
        }

        public static object Get(object node, string key)
        {
            var map = AsObject(node);
            object v;
            return map != null && map.TryGetValue(key, out v) ? v : null;
        }

        // ------------------------------------------------------------ pretty print

        public static string Pretty(object node)
        {
            var sb = new StringBuilder();
            Write(node, sb, 0);
            return sb.ToString();
        }

        private static void Write(object node, StringBuilder sb, int indent)
        {
            var pad = new string(' ', indent * 2);
            var pad2 = new string(' ', (indent + 1) * 2);

            if (node == null)
            {
                sb.Append("null");
                return;
            }

            if (node is string s)
            {
                sb.Append(Quote(s));
                return;
            }

            if (node is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (node is long || node is double)
            {
                sb.Append(node is long ln
                    ? ln.ToString(CultureInfo.InvariantCulture)
                    : ((double)node).ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            var map = node as Dictionary<string, object>;
            if (map != null)
            {
                if (map.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }

                sb.Append("{\n");
                var first = true;
                foreach (var kv in map)
                {
                    if (!first)
                        sb.Append(",\n");
                    first = false;
                    sb.Append(pad2).Append(Quote(kv.Key)).Append(": ");
                    Write(kv.Value, sb, indent + 1);
                }

                sb.Append('\n').Append(pad).Append('}');
                return;
            }

            var list = node as List<object>;
            if (list != null)
            {
                if (list.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }

                // Keep link tuples such as ["6", 0] on a single line.
                var flat = true;
                foreach (var item in list)
                    if (item is Dictionary<string, object> || item is List<object>)
                        flat = false;

                if (flat)
                {
                    sb.Append('[');
                    for (var n = 0; n < list.Count; n++)
                    {
                        if (n > 0)
                            sb.Append(", ");
                        Write(list[n], sb, 0);
                    }

                    sb.Append(']');
                    return;
                }

                sb.Append("[\n");
                for (var n = 0; n < list.Count; n++)
                {
                    if (n > 0)
                        sb.Append(",\n");
                    sb.Append(pad2);
                    Write(list[n], sb, indent + 1);
                }

                sb.Append('\n').Append(pad).Append(']');
            }
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
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
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
