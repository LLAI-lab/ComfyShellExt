using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ComfyShellExt.Core.Json
{
    /// <summary>
    /// Minimal JSON reader. Objects become Dictionary&lt;string, object&gt;, arrays become
    /// List&lt;object&gt;, numbers become double. Returns false instead of throwing so it can also
    /// be used to validate a candidate substring cut out of a binary file.
    /// </summary>
    public static class MiniJson
    {
        private const int MaxDepth = 256;

        public static bool TryParse(string text, out object value)
        {
            value = null;
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0;
            object result;
            if (!ParseValue(text, ref i, 0, out result)) return false;
            SkipWs(text, ref i);
            if (i != text.Length) return false;
            value = result;
            return true;
        }

        public static object Parse(string text)
        {
            object v;
            return TryParse(text, out v) ? v : null;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        private static bool ParseValue(string s, ref int i, int depth, out object value)
        {
            value = null;
            if (depth > MaxDepth) return false;
            SkipWs(s, ref i);
            if (i >= s.Length) return false;
            switch (s[i])
            {
                case '{': return ParseObject(s, ref i, depth, out value);
                case '[': return ParseArray(s, ref i, depth, out value);
                case '"':
                    string str;
                    if (!ParseString(s, ref i, out str)) return false;
                    value = str;
                    return true;
                case 't':
                    if (!Literal(s, ref i, "true")) return false;
                    value = true;
                    return true;
                case 'f':
                    if (!Literal(s, ref i, "false")) return false;
                    value = false;
                    return true;
                case 'n':
                    if (!Literal(s, ref i, "null")) return false;
                    value = null;
                    return true;
                case 'N':
                    if (!Literal(s, ref i, "NaN")) return false;
                    value = double.NaN;
                    return true;
                default: return ParseNumber(s, ref i, out value);
            }
        }

        private static bool Literal(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static bool ParseObject(string s, ref int i, int depth, out object value)
        {
            value = null;
            var map = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; value = map; return true; }
            while (true)
            {
                SkipWs(s, ref i);
                string key;
                if (!ParseString(s, ref i, out key)) return false;
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') return false;
                i++;
                object item;
                if (!ParseValue(s, ref i, depth + 1, out item)) return false;
                map[key] = item;
                SkipWs(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; value = map; return true; }
                return false;
            }
        }

        private static bool ParseArray(string s, ref int i, int depth, out object value)
        {
            value = null;
            var list = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; value = list; return true; }
            while (true)
            {
                object item;
                if (!ParseValue(s, ref i, depth + 1, out item)) return false;
                list.Add(item);
                SkipWs(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; value = list; return true; }
                return false;
            }
        }

        private static bool ParseNumber(string s, ref int i, out object value)
        {
            value = null;
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            if (Literal(s, ref i, "Infinity")) { value = double.PositiveInfinity; return true; }
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
                else break;
            }
            if (i == start) return false;
            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out d)) return false;
            value = d;
            return true;
        }

        private static bool ParseString(string s, ref int i, out string value)
        {
            value = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) return false;
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
                        if (i + 4 > s.Length) return false;
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code)) return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }
    }
}
