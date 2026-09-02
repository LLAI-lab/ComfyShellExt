using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ComfyShellExt.Core.Json
{
    /// <summary>Just enough JSON writing for the index file; avoids any external dependency.</summary>
    public static class JsonText
    {
        public static string Escape(string value)
        {
            if (value == null) return "";
            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == 0x7F) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static void Member(StringBuilder sb, ref bool first, string key, string value)
        {
            if (value == null) return;
            Comma(sb, ref first);
            sb.Append('"').Append(key).Append("\":\"").Append(Escape(value)).Append('"');
        }

        public static void Member(StringBuilder sb, ref bool first, string key, long value)
        {
            Comma(sb, ref first);
            sb.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public static void Member(StringBuilder sb, ref bool first, string key, bool value)
        {
            Comma(sb, ref first);
            sb.Append('"').Append(key).Append("\":").Append(value ? "true" : "false");
        }

        public static void Member(StringBuilder sb, ref bool first, string key, List<string> values)
        {
            if (values == null || values.Count == 0) return;
            Comma(sb, ref first);
            sb.Append('"').Append(key).Append("\":[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(values[i])).Append('"');
            }
            sb.Append(']');
        }

        private static void Comma(StringBuilder sb, ref bool first)
        {
            if (first) first = false;
            else sb.Append(',');
        }

        public static string Str(Dictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) ? v as string : null;
        }

        public static long Num(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v)) return 0;
            return v is double ? (long)(double)v : 0;
        }

        public static bool Flag(Dictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        public static List<string> Array(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v)) return null;
            var list = v as List<object>;
            if (list == null) return null;
            var result = new List<string>(list.Count);
            foreach (var item in list)
            {
                var s = item as string;
                if (s != null) result.Add(s);
            }
            return result.Count == 0 ? null : result;
        }
    }
}
