using System;
using System.Collections.Generic;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// ISO base media file format walker (mp4, mov, m4v, avif, heic). Reads the metadata boxes only
    /// and seeks over mdat, so cost is independent of file size. Covers both layouts ffmpeg emits:
    /// moov/udta/meta/ilst with QuickTime keys such as (c)cmt, and moov/meta with an mdta keys table
    /// (what -movflags use_metadata_tags produces, which is how VideoHelperSuite stores workflows).
    /// </summary>
    internal static class IsoBmffReader
    {
        private const int MaxBoxBytes = 64 * 1024 * 1024;
        private const int MaxDepth = 8;

        private static readonly string[] Containers =
        {
            "moov", "udta", "trak", "mdia", "minf", "ilst", "edts", "mvex"
        };

        public static void Read(ByteReader r, MetaSink sink)
        {
            long end = r.Length;
            if (end <= 0) end = long.MaxValue;
            var keys = new List<string>();
            Walk(r, 0, end, 0, keys, sink);
        }

        private static void Walk(ByteReader r, long start, long end, int depth,
            List<string> keys, MetaSink sink)
        {
            if (depth > MaxDepth || sink.Stopped) return;
            long pos = start;
            while (pos + 8 <= end)
            {
                if (!r.Seek(pos)) return;
                long size = r.ReadU32BE();
                string type = r.ReadTag(4);
                if (size < 0 || type == null) return;
                long header = 8;
                if (size == 1)
                {
                    size = r.ReadU64BE();
                    header = 16;
                    if (size < 0) return;
                }
                else if (size == 0)
                {
                    size = end - pos;
                }
                if (size < header) return;
                long contentStart = pos + header;
                long contentEnd = Math.Min(pos + size, end);
                if (contentEnd <= contentStart) { pos = pos + size; continue; }

                Dispatch(r, type, contentStart, contentEnd, depth, keys, sink);
                if (sink.Stopped) return;
                pos = pos + size;
            }
        }

        private static void Dispatch(ByteReader r, string type, long contentStart, long contentEnd,
            int depth, List<string> keys, MetaSink sink)
        {
            if (Array.IndexOf(Containers, type) >= 0)
            {
                if (type == "ilst") IsoBmffTags.ReadItemList(r, contentStart, contentEnd, keys, sink);
                else Walk(r, contentStart, contentEnd, depth + 1, keys, sink);
                return;
            }
            if (type == "meta")
            {
                long offset = DetectMetaOffset(r, contentStart, contentEnd);
                Walk(r, offset, contentEnd, depth + 1, keys, sink);
                return;
            }
            if (type == "keys")
            {
                IsoBmffTags.ReadKeys(r, contentStart, contentEnd, keys);
                return;
            }
            if (type == "uuid")
            {
                IsoBmffTags.ReadUuid(r, contentStart, contentEnd, sink);
                return;
            }
            if (type.Length == 4 && (type[0] == '\u00A9' || type[0] == (char)0xA9))
            {
                IsoBmffTags.ReadPlainTag(r, type.Substring(1), contentStart, contentEnd, "mp4:udta", sink);
            }
            else if (type == "desc" || type == "cmt" || type == "titl" || type == "auth" ||
                     type == "info" || type == "name")
            {
                IsoBmffTags.ReadPlainTag(r, type, contentStart, contentEnd, "mp4:udta", sink);
            }
        }

        /// <summary>
        /// The ISO 'meta' box has a four byte version/flags header, the QuickTime one does not.
        /// Detect by checking whether a valid child box header sits right at the start.
        /// </summary>
        private static long DetectMetaOffset(ByteReader r, long contentStart, long contentEnd)
        {
            if (!r.Seek(contentStart)) return contentStart;
            long size = r.ReadU32BE();
            string tag = r.ReadTag(4);
            if (tag != null && size >= 8 && contentStart + size <= contentEnd && IsBoxType(tag))
                return contentStart;
            return contentStart + 4;
        }

        private static bool IsBoxType(string tag)
        {
            foreach (var c in tag)
            {
                if (c == '\u00A9' || c == (char)0xA9) continue;
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ')) return false;
            }
            return true;
        }

        internal static byte[] ReadBounded(ByteReader r, long start, long end)
        {
            long length = end - start;
            if (length <= 0 || length > MaxBoxBytes) return null;
            return r.Seek(start) ? r.Read((int)length) : null;
        }

        /// <summary>
        /// Locates the moov box without descending into it. All metadata lives there, so a brute
        /// force scan can be aimed at a few hundred KB instead of a multi gigabyte file.
        /// </summary>
        public static bool TryFindMoov(ByteReader r, out long start, out long end)
        {
            start = 0;
            end = 0;
            long limit = r.Length;
            if (limit <= 0) return false;
            long pos = 0;
            for (int guard = 0; guard < 4096 && pos + 8 <= limit; guard++)
            {
                if (!r.Seek(pos)) return false;
                long size = r.ReadU32BE();
                string type = r.ReadTag(4);
                if (size < 0 || type == null) return false;
                long header = 8;
                if (size == 1)
                {
                    size = r.ReadU64BE();
                    header = 16;
                    if (size < 0) return false;
                }
                else if (size == 0) size = limit - pos;
                if (size < header) return false;
                if (type == "moov")
                {
                    start = pos;
                    end = Math.Min(pos + size, limit);
                    return end > start;
                }
                pos += size;
            }
            return false;
        }
    }
}
