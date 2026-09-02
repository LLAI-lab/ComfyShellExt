using System;
using System.IO;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>Reads GIF comment extension blocks, which is where PIL writes a GIF comment.</summary>
    internal static class GifReader
    {
        private const int MaxBlockBytes = 32 * 1024 * 1024;

        public static void Read(ByteReader r, MetaSink sink)
        {
            if (!r.Seek(6)) return;
            if (r.Skip(4) == false) return;               // logical screen width/height
            int packed = r.ReadByteSafe();
            if (packed < 0) return;
            if (!r.Skip(2)) return;                       // background colour + aspect ratio
            if ((packed & 0x80) != 0 && !r.Skip(3 * (1 << ((packed & 7) + 1)))) return;

            for (int guard = 0; guard < 4096; guard++)
            {
                int b = r.ReadByteSafe();
                if (b < 0 || b == 0x3B) return;           // trailer
                if (b == 0x2C)
                {
                    if (!r.Skip(8)) return;               // left, top, width, height
                    int flags = r.ReadByteSafe();
                    if (flags < 0) return;
                    if ((flags & 0x80) != 0 && !r.Skip(3 * (1 << ((flags & 7) + 1)))) return;
                    if (!r.Skip(1)) return;               // LZW minimum code size
                    if (SkipSubBlocks(r) == null) return;
                    continue;
                }
                if (b != 0x21) return;                    // not an extension: give up
                int label = r.ReadByteSafe();
                if (label < 0) return;
                if (label == 0xFF && !r.Skip(12)) return; // application id block (1 + 11 bytes)
                var payload = SkipSubBlocks(r);
                if (payload == null) return;
                if (label == 0xFE)
                {
                    var text = TextCodec.TrimNuls(TextCodec.Decode(payload));
                    if (!string.IsNullOrEmpty(text) && !sink.Add("gif:comment", "Comment", text)) return;
                }
                else if (label == 0xFF && payload.Length > 32)
                {
                    var text = TextCodec.TrimNuls(TextCodec.Decode(payload));
                    if (!string.IsNullOrEmpty(text) && !sink.Add("gif:appext", "Application", text)) return;
                }
            }
        }

        /// <summary>Reads length prefixed sub blocks until the terminator. Returns the payload.</summary>
        private static byte[] SkipSubBlocks(ByteReader r)
        {
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    int n = r.ReadByteSafe();
                    if (n < 0) return null;
                    if (n == 0) return ms.ToArray();
                    var data = r.Read(n);
                    if (data == null) return null;
                    if (ms.Length < MaxBlockBytes) ms.Write(data, 0, data.Length);
                }
            }
        }
    }
}
