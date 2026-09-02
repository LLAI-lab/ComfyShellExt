using System;
using System.Collections.Generic;
using System.IO;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// Prints the structure of a container. When a file "should" have a workflow but does not show
    /// one, this answers the only question that matters: is the metadata absent, or is it present in
    /// a place we did not read?
    /// </summary>
    public static class ContainerOutline
    {
        public static List<string> Describe(Stream stream)
        {
            var lines = new List<string>();
            var reader = new ByteReader(stream);
            string name;
            var type = ContainerSniffer.Sniff(reader, out name);
            lines.Add("container: " + name);
            switch (type)
            {
                case ContainerType.Png: Png(reader, lines); break;
                case ContainerType.IsoBmff: Boxes(reader, 0, reader.Length, 0, lines); break;
                case ContainerType.WebP:
                case ContainerType.Avi:
                case ContainerType.Wave: Riff(reader, lines); break;
                case ContainerType.Matroska: lines.Add("  (matroska: use --dump to see tag values)"); break;
                default: lines.Add("  (no structure listing for this container)"); break;
            }
            return lines;
        }

        private static void Png(ByteReader r, List<string> lines)
        {
            if (!r.Seek(8)) return;
            for (int guard = 0; guard < 4096; guard++)
            {
                long offset = r.Position;
                long length = r.ReadU32BE();
                string type = r.ReadTag(4);
                if (length < 0 || type == null) return;
                lines.Add(string.Format("  @{0,-10} {1,-6} {2} bytes", offset, type, length));
                if (type == "IEND") return;
                if (!r.Skip(length + 4)) return;
            }
        }

        private static void Boxes(ByteReader r, long start, long end, int depth, List<string> lines)
        {
            if (depth > 4 || end <= start) return;
            long pos = start;
            for (int guard = 0; guard < 4096 && pos + 8 <= end; guard++)
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
                else if (size == 0) size = end - pos;
                if (size < header) return;
                lines.Add(string.Format("  {0}@{1,-10} {2,-6} {3} bytes",
                    new string(' ', depth * 2), pos, Printable(type), size));
                if (Descend(type)) Boxes(r, pos + header + MetaSkip(r, type, pos + header, pos + size),
                    Math.Min(pos + size, end), depth + 1, lines);
                pos += size;
            }
        }

        /// <summary>The ISO flavour of 'meta' has a version/flags word before its children.</summary>
        private static long MetaSkip(ByteReader r, string type, long contentStart, long contentEnd)
        {
            if (type != "meta") return 0;
            if (!r.Seek(contentStart)) return 0;
            long size = r.ReadU32BE();
            string tag = r.ReadTag(4);
            bool looksLikeBox = tag != null && size >= 8 && contentStart + size <= contentEnd;
            return looksLikeBox ? 0 : 4;
        }

        private static bool Descend(string type)
        {
            switch (type)
            {
                case "moov": case "udta": case "meta": case "ilst": case "trak":
                case "mdia": case "minf": case "----": return true;
                default: return false;
            }
        }

        private static void Riff(ByteReader r, List<string> lines)
        {
            if (!r.Seek(12)) return;
            for (int guard = 0; guard < 4096; guard++)
            {
                long offset = r.Position;
                string id = r.ReadTag(4);
                long size = r.ReadU32LE();
                if (id == null || size < 0) return;
                lines.Add(string.Format("  @{0,-10} {1,-6} {2} bytes", offset, Printable(id), size));
                if (id == "LIST")
                {
                    string listType = r.ReadTag(4);
                    lines.Add("    list type " + Printable(listType ?? "?"));
                    if (!r.Seek(offset + 12)) return;
                    continue;
                }
                if (!r.Seek(offset + 8 + size + (size & 1))) return;
            }
        }

        private static string Printable(string tag)
        {
            var chars = tag.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] < 0x20 || chars[i] > 0x7E) chars[i] = '.';
            return new string(chars);
        }
    }
}
