using System;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>Walks JPEG marker segments and reports EXIF, XMP and COM comment payloads.</summary>
    internal static class JpegReader
    {
        private const string XmpHeader = "http://ns.adobe.com/xap/1.0/\0";
        private const int MaxSegments = 2000;

        public static void Read(ByteReader r, MetaSink sink)
        {
            if (!r.Seek(2)) return;
            for (int guard = 0; guard < MaxSegments; guard++)
            {
                int b = r.ReadByteSafe();
                while (b == 0xFF) b = r.ReadByteSafe();
                if (b < 0) return;
                // b is the marker code; markers are 0xFF followed by the code
                int marker = b;
                if (marker == 0xD9) return;                       // EOI
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8)) continue;
                long length = r.ReadU16BE();
                if (length < 2) return;
                int payload = (int)length - 2;
                if (marker == 0xDA) return;                       // start of scan, no metadata after
                if (marker == 0xE1 || marker == 0xFE)
                {
                    var data = r.Read(payload);
                    if (data == null) return;
                    if (!Emit(marker, data, sink)) return;
                }
                else if (!r.Skip(payload)) return;
            }
        }

        private static bool Emit(int marker, byte[] data, MetaSink sink)
        {
            if (marker == 0xFE)
                return sink.Add("jpeg:COM", "Comment", TextCodec.TrimNuls(TextCodec.Decode(data)));
            if (TextCodec.StartsWith(data, "Exif\0\0"))
            {
                ExifReader.Read(data, 6, "jpeg:exif", sink);
                return !sink.Stopped;
            }
            if (TextCodec.StartsWith(data, XmpHeader))
            {
                int p = XmpHeader.Length;
                return sink.Add("jpeg:xmp", "XMP", TextCodec.Decode(data, p, data.Length - p));
            }
            return true;
        }
    }
}
