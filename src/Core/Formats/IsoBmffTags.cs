using System;
using System.Collections.Generic;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>Payload decoding for the metadata boxes found by <see cref="IsoBmffReader"/>.</summary>
    internal static class IsoBmffTags
    {
        private static readonly byte[] XmpUuid =
        {
            0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8,
            0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC
        };

        /// <summary>mdta 'keys' table: the ilst children are 1-based indexes into this list.</summary>
        public static void ReadKeys(ByteReader r, long start, long end, List<string> keys)
        {
            if (!r.Seek(start + 4)) return;
            long count = r.ReadU32BE();
            if (count < 0 || count > 512) return;
            for (long i = 0; i < count; i++)
            {
                long pos = r.Position;
                long size = r.ReadU32BE();
                string ns = r.ReadTag(4);
                if (size < 8 || ns == null || pos + size > end) return;
                var name = r.Read((int)(size - 8));
                keys.Add(name == null ? "" : TextCodec.TrimNuls(TextCodec.Decode(name)));
                if (!r.Seek(pos + size)) return;
            }
        }

        public static void ReadItemList(ByteReader r, long start, long end, List<string> keys, MetaSink sink)
        {
            long pos = start;
            while (pos + 8 <= end && !sink.Stopped)
            {
                if (!r.Seek(pos)) return;
                long size = r.ReadU32BE();
                string type = r.ReadTag(4);
                if (size < 8 || type == null) return;
                long contentEnd = Math.Min(pos + size, end);
                ReadItem(r, pos + 8, contentEnd, ResolveKey(type, keys), sink);
                pos += size;
            }
        }

        /// <summary>An ilst child holds 'data' payloads, optionally named by 'mean' and 'name'.</summary>
        private static void ReadItem(ByteReader r, long start, long end, string fallbackKey, MetaSink sink)
        {
            string name = null;
            var values = new List<string>();
            long pos = start;
            while (pos + 8 <= end)
            {
                if (!r.Seek(pos)) break;
                long size = r.ReadU32BE();
                string tag = r.ReadTag(4);
                if (size < 8 || tag == null) break;
                long bodyStart = r.Position;
                long bodyEnd = Math.Min(pos + size, end);
                if (tag == "name" || tag == "mean")
                {
                    var raw = IsoBmffReader.ReadBounded(r, bodyStart + 4, bodyEnd);
                    var text = TextCodec.TrimNuls(TextCodec.Decode(raw));
                    if (tag == "name" && !string.IsNullOrEmpty(text)) name = text;
                }
                else if (tag == "data" && size >= 16)
                {
                    long wellKnown = r.ReadU32BE() & 0xFFFFFF;
                    r.ReadU32BE();
                    var raw = IsoBmffReader.ReadBounded(r, bodyStart + 8, bodyEnd);
                    var text = wellKnown == 2
                        ? TextCodec.DecodeUtf16(raw, 0, raw == null ? 0 : raw.Length, true)
                        : TextCodec.Decode(raw);
                    text = TextCodec.TrimNuls(text);
                    if (!string.IsNullOrEmpty(text)) values.Add(text);
                }
                pos += size;
            }
            foreach (var v in values)
                if (!sink.Add("mp4:ilst", name ?? fallbackKey, v)) return;
        }

        /// <summary>QuickTime style udta child: either an iTunes data box or u16 length + u16 language.</summary>
        public static void ReadPlainTag(ByteReader r, string key, long start, long end,
            string source, MetaSink sink)
        {
            var raw = IsoBmffReader.ReadBounded(r, start, end);
            if (raw == null || raw.Length == 0) return;
            if (raw.Length >= 8)
            {
                long inner = ((long)raw[0] << 24) | ((long)raw[1] << 16) | ((long)raw[2] << 8) | raw[3];
                if (inner == raw.Length && raw[4] == 'd' && raw[5] == 'a' && raw[6] == 't' && raw[7] == 'a')
                {
                    ReadItem(r, start, end, key, sink);
                    return;
                }
            }
            if (raw.Length >= 4)
            {
                // QuickTime layout is u16 length + u16 language + text, so the declared length must
                // account for exactly the rest of the box. Anything else is a plain string payload,
                // and treating it as a length would silently truncate a large workflow.
                int len = (raw[0] << 8) | raw[1];
                if (len > 0 && len == raw.Length - 4)
                {
                    var text = TextCodec.TrimNuls(TextCodec.Decode(raw, 4, len));
                    if (!string.IsNullOrEmpty(text)) { sink.Add(source, key, text); return; }
                }
            }
            sink.Add(source, key, TextCodec.TrimNuls(TextCodec.Decode(raw)));
        }

        public static void ReadUuid(ByteReader r, long start, long end, MetaSink sink)
        {
            if (!r.Seek(start)) return;
            var id = r.Read(16);
            if (id == null) return;
            for (int i = 0; i < 16; i++) if (id[i] != XmpUuid[i]) return;
            var raw = IsoBmffReader.ReadBounded(r, start + 16, end);
            sink.Add("mp4:uuid", "XMP", TextCodec.Decode(raw));
        }

        /// <summary>Maps an ilst child type to a key name: (c)cmt, a keys index, or the raw fourcc.</summary>
        private static string ResolveKey(string type, List<string> keys)
        {
            bool numeric = true;
            long index = 0;
            foreach (var c in type)
            {
                if (c > 0x1F) { numeric = false; break; }
                index = (index << 8) | (byte)c;
            }
            if (numeric && index >= 1 && index <= keys.Count) return keys[(int)index - 1];
            if (type.Length == 4 && (type[0] == '\u00A9' || type[0] == (char)0xA9)) return type.Substring(1);
            return type;
        }
    }
}
