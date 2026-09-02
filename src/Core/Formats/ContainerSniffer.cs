using System;

namespace ComfyShellExt.Core.Formats
{
    internal enum ContainerType
    {
        Unknown = 0, Png, Jpeg, Gif, WebP, Avi, Wave, IsoBmff, Matroska, Flac
    }

    /// <summary>Identifies the container from its magic bytes rather than trusting the extension.</summary>
    internal static class ContainerSniffer
    {
        public static ContainerType Sniff(ByteReader r, out string name)
        {
            name = "unknown";
            if (!r.Seek(0)) return ContainerType.Unknown;
            var head = r.Read(16);
            if (head == null || head.Length < 12) return ContainerType.Unknown;

            if (Match(head, 0, PngReader.Signature)) { name = "png"; return ContainerType.Png; }
            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) { name = "jpeg"; return ContainerType.Jpeg; }
            if (Ascii(head, 0, "GIF8")) { name = "gif"; return ContainerType.Gif; }
            if (Ascii(head, 0, "fLaC")) { name = "flac"; return ContainerType.Flac; }
            if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3)
            {
                name = "matroska";
                return ContainerType.Matroska;
            }
            if (Ascii(head, 0, "RIFF"))
            {
                if (Ascii(head, 8, "WEBP")) { name = "webp"; return ContainerType.WebP; }
                if (Ascii(head, 8, "AVI ")) { name = "avi"; return ContainerType.Avi; }
                if (Ascii(head, 8, "WAVE")) { name = "wave"; return ContainerType.Wave; }
                name = "riff";
                return ContainerType.Avi;
            }
            if (Ascii(head, 4, "ftyp") || Ascii(head, 4, "styp") || Ascii(head, 4, "moov") ||
                Ascii(head, 4, "mdat") || Ascii(head, 4, "free") || Ascii(head, 4, "skip") ||
                Ascii(head, 4, "wide"))
            {
                name = Ascii(head, 4, "ftyp") ? BrandName(head) : "mp4";
                return ContainerType.IsoBmff;
            }
            return ContainerType.Unknown;
        }

        private static string BrandName(byte[] head)
        {
            var brand = new string(new[] { (char)head[8], (char)head[9], (char)head[10], (char)head[11] });
            brand = brand.Trim().ToLowerInvariant();
            if (brand.StartsWith("avif") || brand.StartsWith("avis")) return "avif";
            if (brand.StartsWith("heic") || brand.StartsWith("heix") || brand.StartsWith("mif1")) return "heif";
            if (brand.StartsWith("qt")) return "mov";
            return "mp4";
        }

        public static void Dispatch(ContainerType type, ByteReader r, MetaSink sink)
        {
            switch (type)
            {
                case ContainerType.Png: PngReader.Read(r, sink); break;
                case ContainerType.Jpeg: JpegReader.Read(r, sink); break;
                case ContainerType.Gif: GifReader.Read(r, sink); break;
                case ContainerType.WebP:
                case ContainerType.Avi:
                case ContainerType.Wave: RiffReader.Read(r, sink); break;
                case ContainerType.IsoBmff: IsoBmffReader.Read(r, sink); break;
                case ContainerType.Matroska: MatroskaReader.Read(r, sink); break;
                case ContainerType.Flac: FlacReader.Read(r, sink); break;
            }
        }

        private static bool Match(byte[] data, int offset, byte[] pattern)
        {
            if (offset + pattern.Length > data.Length) return false;
            for (int i = 0; i < pattern.Length; i++) if (data[offset + i] != pattern[i]) return false;
            return true;
        }

        private static bool Ascii(byte[] data, int offset, string text)
        {
            if (offset + text.Length > data.Length) return false;
            for (int i = 0; i < text.Length; i++) if (data[offset + i] != (byte)text[i]) return false;
            return true;
        }
    }
}
