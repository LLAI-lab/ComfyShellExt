using System;
using System.Text;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// Text decoding for container metadata. Most ComfyUI payloads are ASCII because json.dumps
    /// escapes non-ASCII, but custom nodes that pass ensure_ascii=False produce real UTF-8, and
    /// legacy tags are Latin-1. Strict UTF-8 first, Latin-1 as the fallback.
    /// </summary>
    internal static class TextCodec
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static string Decode(byte[] data)
        {
            return data == null ? null : Decode(data, 0, data.Length);
        }

        public static string Decode(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0 || offset < 0 || offset + count > data.Length) return null;
            try { return StrictUtf8.GetString(data, offset, count); }
            catch { }
            try { return Latin1.GetString(data, offset, count); }
            catch { return null; }
        }

        public static string DecodeUtf16(byte[] data, int offset, int count, bool bigEndian)
        {
            if (data == null || count <= 0 || offset < 0 || offset + count > data.Length) return null;
            try
            {
                var enc = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
                return enc.GetString(data, offset, count & ~1);
            }
            catch { return null; }
        }

        /// <summary>Removes trailing NUL padding that fixed size tags leave behind.</summary>
        public static string TrimNuls(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            int end = value.Length;
            while (end > 0 && (value[end - 1] == '\0' || value[end - 1] == '\uFEFF')) end--;
            int start = 0;
            while (start < end && (value[start] == '\0' || value[start] == '\uFEFF')) start++;
            return start == 0 && end == value.Length ? value : value.Substring(start, end - start);
        }

        /// <summary>Index of the first NUL at or after offset, or -1.</summary>
        public static int IndexOfNul(byte[] data, int offset)
        {
            for (int i = offset; i < data.Length; i++) if (data[i] == 0) return i;
            return -1;
        }

        public static bool StartsWith(byte[] data, string ascii)
        {
            if (data == null || data.Length < ascii.Length) return false;
            for (int i = 0; i < ascii.Length; i++) if (data[i] != (byte)ascii[i]) return false;
            return true;
        }

        /// <summary>
        /// Undoes XML escaping. XMP packets carry JSON as element text, so the quotes that make a
        /// workflow recognisable arrive as &amp;quot; and have to be restored before matching.
        /// </summary>
        public static string UnescapeXml(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('&') < 0) return value;
            return value
                .Replace("&quot;", "\"")
                .Replace("&#34;", "\"")
                .Replace("&#x22;", "\"")
                .Replace("&apos;", "'")
                .Replace("&#39;", "'")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&#10;", "\n")
                .Replace("&#13;", "\r")
                .Replace("&amp;", "&");
        }
    }
}
