using System;
using System.IO;
using System.IO.Compression;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// Reads PNG tEXt / zTXt / iTXt chunks. ComfyUI's SaveImage node stores the API prompt under
    /// the "prompt" keyword and the editor graph under "workflow".
    /// </summary>
    internal static class PngReader
    {
        public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private const int MaxChunkBytes = 64 * 1024 * 1024;

        public static void Read(ByteReader r, MetaSink sink)
        {
            if (!r.Seek(Signature.Length)) return;
            while (true)
            {
                long length = r.ReadU32BE();
                if (length < 0) return;
                string type = r.ReadTag(4);
                if (type == null || type == "IEND") return;
                bool interesting = type == "tEXt" || type == "zTXt" || type == "iTXt" || type == "eXIf";
                // Step over anything we do not read, including chunks too big to buffer: a workflow
                // with embedded base64 images can be enormous and must not end the scan early.
                if (!interesting || length > MaxChunkBytes)
                {
                    if (!r.Skip(length + 4)) return;
                    continue;
                }
                var data = r.Read((int)length);
                if (data == null) return;
                if (!r.Skip(4)) return;
                if (type == "eXIf")
                {
                    ExifReader.Read(data, TextCodec.StartsWith(data, "Exif\0\0") ? 6 : 0, "png:eXIf", sink);
                    if (sink.Stopped) return;
                    continue;
                }
                if (!Emit(type, data, sink)) return;
            }
        }

        private static bool Emit(string type, byte[] data, MetaSink sink)
        {
            int nul = TextCodec.IndexOfNul(data, 0);
            if (nul <= 0) return true;
            string keyword = TextCodec.Decode(data, 0, nul);
            int p = nul + 1;
            string value;
            if (type == "tEXt")
            {
                value = TextCodec.Decode(data, p, data.Length - p);
            }
            else if (type == "zTXt")
            {
                if (p >= data.Length) return true;
                p++; // compression method
                value = Inflate(data, p);
            }
            else
            {
                if (p + 2 > data.Length) return true;
                byte compressed = data[p];
                p += 2; // compression flag + method
                int lang = TextCodec.IndexOfNul(data, p);
                if (lang < 0) return true;
                int translated = TextCodec.IndexOfNul(data, lang + 1);
                if (translated < 0) return true;
                p = translated + 1;
                value = compressed == 1 ? Inflate(data, p) : TextCodec.Decode(data, p, data.Length - p);
            }
            return sink.Add("png:" + type, keyword, value);
        }

        /// <summary>
        /// PNG uses zlib, but tools do get this wrong, and a stream that fails part way through has
        /// still given us most of the workflow. Try zlib, then raw deflate, then zlib with padding:
        /// the managed inflater emits nothing at all when its input simply stops mid block, and a
        /// tail of filler lets it flush the symbols it had already decoded.
        /// </summary>
        private static string Inflate(byte[] data, int offset)
        {
            var text = Inflate(data, offset, 2, 0);
            if (string.IsNullOrEmpty(text)) text = Inflate(data, offset, 0, 0);
            if (string.IsNullOrEmpty(text)) text = Inflate(data, offset, 2, 64 * 1024);
            return text;
        }

        private static string Inflate(byte[] data, int offset, int skipHeader, int padding)
        {
            int start = offset + skipHeader;
            if (start >= data.Length) return null;
            try
            {
                var source = data;
                int count = data.Length - start;
                if (padding > 0)
                {
                    source = new byte[count + padding];
                    Buffer.BlockCopy(data, start, source, 0, count);
                    start = 0;
                    count = source.Length;
                }
                using (var src = new MemoryStream(source, start, count, false))
                using (var inflate = new DeflateStream(src, CompressionMode.Decompress))
                using (var dst = new MemoryStream())
                {
                    var buf = new byte[64 * 1024];
                    long total = 0;
                    while (true)
                    {
                        int n;
                        try { n = inflate.Read(buf, 0, buf.Length); }
                        catch { break; }
                        if (n <= 0) break;
                        total += n;
                        if (total > MaxChunkBytes) break;
                        dst.Write(buf, 0, n);
                    }
                    return dst.Length == 0 ? null : TextCodec.Decode(dst.ToArray());
                }
            }
            catch { return null; }
        }
    }
}
