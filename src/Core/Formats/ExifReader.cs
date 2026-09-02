using System;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// TIFF/EXIF IFD walker shared by JPEG APP1 and WebP EXIF chunks. Every ASCII-ish tag is
    /// reported, not just the well known ones, because ComfyUI's SaveAnimatedWEBP node writes the
    /// workflow into whatever tag ids happen to be free (0x0110, 0x010f, 0x010e ...).
    /// </summary>
    internal static class ExifReader
    {
        private const int MaxEntries = 512;

        public static void Read(byte[] blob, int offset, string source, MetaSink sink)
        {
            if (blob == null || offset < 0 || offset + 8 > blob.Length) return;
            bool big;
            if (blob[offset] == 0x49 && blob[offset + 1] == 0x49) big = false;
            else if (blob[offset] == 0x4D && blob[offset + 1] == 0x4D) big = true;
            else return;
            if (U16(blob, offset + 2, big) != 42) return;
            long first = U32(blob, offset + 4, big);
            ReadIfd(blob, offset, (int)first, big, source, sink, 0);
        }

        private static void ReadIfd(byte[] b, int tiff, int ifd, bool big, string source,
            MetaSink sink, int depth)
        {
            if (depth > 3 || ifd <= 0) return;
            int pos = tiff + ifd;
            if (pos + 2 > b.Length) return;
            int count = (int)U16(b, pos, big);
            if (count <= 0 || count > MaxEntries) return;
            pos += 2;
            for (int i = 0; i < count; i++, pos += 12)
            {
                if (pos + 12 > b.Length) return;
                int tag = (int)U16(b, pos, big);
                int type = (int)U16(b, pos + 2, big);
                long n = U32(b, pos + 4, big);
                int unit = TypeSize(type);
                if (unit == 0 || n < 0 || n > 64 * 1024 * 1024) continue;
                long size = n * unit;
                int valuePos = size <= 4 ? pos + 8 : tiff + (int)U32(b, pos + 8, big);
                if (valuePos < 0 || valuePos + size > b.Length) continue;

                if (tag == 0x8769 || tag == 0xA005)
                {
                    ReadIfd(b, tiff, (int)U32(b, pos + 8, big), big, source, sink, depth + 1);
                    continue;
                }
                string text = Decode(b, valuePos, (int)size, tag, type, big);
                if (string.IsNullOrEmpty(text)) continue;
                if (!sink.Add(source, TagName(tag), text)) return;
            }
            if (pos + 4 <= b.Length)
                ReadIfd(b, tiff, (int)U32(b, pos, big), big, source, sink, depth + 1);
        }

        private static string Decode(byte[] b, int pos, int size, int tag, int type, bool big)
        {
            if (size < 4) return null;
            if (tag >= 0x9C9B && tag <= 0x9C9F)
                return TextCodec.TrimNuls(TextCodec.DecodeUtf16(b, pos, size, big));
            if (type != 1 && type != 2 && type != 7) return null;
            if (tag == 0x9286 && size > 8)
            {
                // UserComment: 8 byte character code prefix
                if (b[pos] == 0x55 && b[pos + 1] == 0x4E) // "UNICODE\0"
                    return TextCodec.TrimNuls(TextCodec.DecodeUtf16(b, pos + 8, size - 8, big));
                return TextCodec.TrimNuls(TextCodec.Decode(b, pos + 8, size - 8));
            }
            var text = TextCodec.TrimNuls(TextCodec.Decode(b, pos, size));
            return LooksPrintable(text) ? text : null;
        }

        private static bool LooksPrintable(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int bad = 0;
            int limit = Math.Min(s.Length, 256);
            for (int i = 0; i < limit; i++)
            {
                char c = s[i];
                if (c < 0x09 || (c > 0x0D && c < 0x20)) bad++;
            }
            return bad * 8 < limit;
        }

        private static int TypeSize(int type)
        {
            switch (type)
            {
                case 1: case 2: case 6: case 7: return 1;
                case 3: case 8: return 2;
                case 4: case 9: case 11: return 4;
                case 5: case 10: case 12: return 8;
                default: return 0;
            }
        }

        private static string TagName(int tag)
        {
            switch (tag)
            {
                case 0x010E: return "ImageDescription";
                case 0x010F: return "Make";
                case 0x0110: return "Model";
                case 0x0131: return "Software";
                case 0x013B: return "Artist";
                case 0x8298: return "Copyright";
                case 0x9286: return "UserComment";
                case 0x9C9B: return "XPTitle";
                case 0x9C9C: return "XPComment";
                case 0x9C9D: return "XPAuthor";
                case 0x9C9E: return "XPKeywords";
                case 0x9C9F: return "XPSubject";
                case 0xA420: return "ImageUniqueID";
                default: return "0x" + tag.ToString("X4");
            }
        }

        private static long U16(byte[] b, int p, bool big)
        {
            return big ? (b[p] << 8) | b[p + 1] : (b[p + 1] << 8) | b[p];
        }

        private static long U32(byte[] b, int p, bool big)
        {
            return big
                ? ((long)b[p] << 24) | ((long)b[p + 1] << 16) | ((long)b[p + 2] << 8) | b[p + 3]
                : ((long)b[p + 3] << 24) | ((long)b[p + 2] << 16) | ((long)b[p + 1] << 8) | b[p];
        }
    }
}
