using System;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// Matroska / WebM reader. ffmpeg stores "-metadata comment=..." as a Tags element with
    /// SimpleTag entries, which is how VideoHelperSuite embeds workflows in .webm and .mkv output.
    /// </summary>
    internal static class MatroskaReader
    {
        private const long IdSegment = 0x18538067;
        private const long IdInfo = 0x1549A966;
        private const long IdTags = 0x1254C367;
        private const long IdTag = 0x7373;
        private const long IdSimpleTag = 0x67C8;
        private const long IdTagName = 0x45A3;
        private const long IdTagString = 0x4487;
        private const long IdTagBinary = 0x4485;
        private const long IdTitle = 0x7BA9;
        private const long IdWritingApp = 0x5741;
        private const long IdMuxingApp = 0x4D80;

        private const int MaxValueBytes = 48 * 1024 * 1024;

        public static void Read(ByteReader r, MetaSink sink)
        {
            long end = r.Length;
            if (end <= 0) return;
            Walk(r, 0, end, 0, null, sink);
        }

        private static void Walk(ByteReader r, long start, long end, int depth, string tagName, MetaSink sink)
        {
            if (depth > 8 || sink.Stopped) return;
            long pos = start;
            while (pos + 2 <= end)
            {
                if (!r.Seek(pos)) return;
                long id, size;
                bool unknown;
                if (!ReadVint(r, true, out id)) return;
                if (!ReadSizeVint(r, out size, out unknown)) return;
                long contentStart = r.Position;
                long contentEnd = unknown ? end : Math.Min(contentStart + size, end);
                if (contentEnd < contentStart) return;

                if (id == IdSegment || id == IdTags || id == IdTag)
                {
                    Walk(r, contentStart, contentEnd, depth + 1, null, sink);
                }
                else if (id == IdSimpleTag)
                {
                    ReadSimpleTag(r, contentStart, contentEnd, depth + 1, sink);
                }
                else if (id == IdInfo)
                {
                    Walk(r, contentStart, contentEnd, depth + 1, "info", sink);
                }
                else if (tagName == "info" && (id == IdTitle || id == IdWritingApp || id == IdMuxingApp))
                {
                    var text = ReadText(r, contentStart, contentEnd);
                    string key = id == IdTitle ? "Title" : id == IdWritingApp ? "WritingApp" : "MuxingApp";
                    if (!sink.Add("mkv:info", key, text)) return;
                }
                if (sink.Stopped) return;
                if (unknown) return;
                pos = contentEnd;
            }
        }

        private static void ReadSimpleTag(ByteReader r, long start, long end, int depth, MetaSink sink)
        {
            string name = null;
            string value = null;
            long pos = start;
            while (pos + 2 <= end)
            {
                if (!r.Seek(pos)) break;
                long id, size;
                bool unknown;
                if (!ReadVint(r, true, out id)) break;
                if (!ReadSizeVint(r, out size, out unknown) || unknown) break;
                long contentStart = r.Position;
                long contentEnd = Math.Min(contentStart + size, end);
                if (id == IdTagName) name = ReadText(r, contentStart, contentEnd);
                else if (id == IdTagString || id == IdTagBinary) value = ReadText(r, contentStart, contentEnd);
                else if (id == IdSimpleTag) ReadSimpleTag(r, contentStart, contentEnd, depth + 1, sink);
                if (sink.Stopped) return;
                pos = contentEnd;
            }
            if (!string.IsNullOrEmpty(value)) sink.Add("mkv:tag", name ?? "", value);
        }

        private static string ReadText(ByteReader r, long start, long end)
        {
            long length = end - start;
            if (length <= 0 || length > MaxValueBytes) return null;
            if (!r.Seek(start)) return null;
            var data = r.Read((int)length);
            return TextCodec.TrimNuls(TextCodec.Decode(data));
        }

        /// <summary>Reads an EBML variable length integer. Element ids keep their marker bits.</summary>
        private static bool ReadVint(ByteReader r, bool keepMarker, out long value)
        {
            value = 0;
            int first = r.ReadByteSafe();
            if (first <= 0) return false;
            int width = 1;
            int mask = 0x80;
            while (width <= 8 && (first & mask) == 0)
            {
                mask >>= 1;
                width++;
            }
            if (width > 4 && keepMarker) return false;
            if (width > 8) return false;
            value = keepMarker ? first : first & (mask - 1);
            for (int i = 1; i < width; i++)
            {
                int b = r.ReadByteSafe();
                if (b < 0) return false;
                value = (value << 8) | (uint)b;
            }
            return true;
        }

        private static bool ReadSizeVint(ByteReader r, out long size, out bool unknown)
        {
            size = 0;
            unknown = false;
            long start = r.Position;
            if (!ReadVint(r, false, out size)) return false;
            int width = (int)(r.Position - start);
            long allOnes = (1L << (7 * width)) - 1;
            if (size == allOnes) { unknown = true; size = 0; }
            if (size < 0) return false;
            return true;
        }
    }
}
