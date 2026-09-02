using System;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// RIFF containers: WebP (EXIF / XMP chunks, used by ComfyUI's SaveAnimatedWEBP) and
    /// AVI / WAVE (LIST INFO chunks such as ICMT written by ffmpeg -metadata comment=).
    /// </summary>
    internal static class RiffReader
    {
        private const int MaxChunkBytes = 64 * 1024 * 1024;

        public static void Read(ByteReader r, MetaSink sink)
        {
            if (!r.Seek(0)) return;
            if (r.ReadTag(4) != "RIFF") return;
            long total = r.ReadU32LE();
            string form = r.ReadTag(4);
            if (form == null) return;
            long end = Math.Min(r.Length < 0 ? long.MaxValue : r.Length, 12 + Math.Max(0, total - 4));
            ReadChunks(r, end, form, sink);
        }

        private static void ReadChunks(ByteReader r, long end, string form, MetaSink sink)
        {
            while (r.Position + 8 <= end)
            {
                string id = r.ReadTag(4);
                long size = r.ReadU32LE();
                if (id == null || size < 0 || size > MaxChunkBytes) return;
                long next = r.Position + size + (size & 1);
                if (id == "LIST")
                {
                    string listType = r.ReadTag(4);
                    if (listType == "INFO")
                    {
                        if (!ReadInfo(r, Math.Min(next, end), sink)) return;
                    }
                }
                else if (id == "EXIF" || id == "Exif")
                {
                    var data = r.Read((int)size);
                    if (data == null) return;
                    int off = TextCodec.StartsWith(data, "Exif\0\0") ? 6 : 0;
                    ExifReader.Read(data, off, "webp:exif", sink);
                    if (sink.Stopped) return;
                }
                else if (id == "XMP ")
                {
                    var data = r.Read((int)size);
                    if (data == null) return;
                    if (!sink.Add("webp:xmp", "XMP", TextCodec.Decode(data))) return;
                }
                if (!r.Seek(next)) return;
            }
        }

        private static bool ReadInfo(ByteReader r, long end, MetaSink sink)
        {
            while (r.Position + 8 <= end)
            {
                string id = r.ReadTag(4);
                long size = r.ReadU32LE();
                if (id == null || size < 0 || size > MaxChunkBytes) return true;
                long next = r.Position + size + (size & 1);
                var data = r.Read((int)Math.Min(size, MaxChunkBytes));
                if (data == null) return true;
                var text = TextCodec.TrimNuls(TextCodec.Decode(data));
                if (!string.IsNullOrEmpty(text) && !sink.Add("riff:INFO", id, text)) return false;
                if (!r.Seek(next)) return true;
            }
            return true;
        }
    }
}
