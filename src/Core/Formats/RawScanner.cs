using System;
using System.Text;
using ComfyShellExt.Core.Json;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>
    /// Last resort detector for payloads the container parsers miss: AVIF item storage, exotic
    /// muxers, custom nodes, or metadata a tool wrote somewhere unusual. Scans in overlapping
    /// windows so a marker that straddles a boundary is still found, and never buffers the whole
    /// file.
    /// </summary>
    internal static class RawScanner
    {
        private const int WindowBytes = 8 * 1024 * 1024;
        private const int OverlapBytes = 256 * 1024;
        private const int BackLimit = 8 * 1024 * 1024;
        /// <summary>Upper bound for an unbalanced slice, so a bad match cannot cost 8 MB of memory.</summary>
        private const int MaxPartialBytes = 4 * 1024 * 1024;

        /// <summary>
        /// Scans up to <paramref name="budget"/> bytes: the whole file when it fits, otherwise the
        /// head and the tail, which is where containers keep their metadata.
        /// </summary>
        public static bool Scan(ByteReader r, long budget, MetaSink sink)
        {
            long length = r.Length;
            if (length <= 0) return false;
            if (budget >= length) return ScanRange(r, 0, length, "raw:full", sink);
            long half = Math.Max(64 * 1024, budget / 2);
            if (ScanRange(r, 0, Math.Min(half, length), "raw:head", sink)) return true;
            return ScanRange(r, Math.Max(0, length - half), length, "raw:tail", sink);
        }

        /// <summary>Scans an explicit byte range, for example just the moov box of a video.</summary>
        public static bool ScanRange(ByteReader r, long start, long end, string label, MetaSink sink)
        {
            if (start < 0) start = 0;
            long length = r.Length;
            if (length > 0 && end > length) end = length;
            bool hit = false;
            long position = start;
            while (position < end && !sink.Stopped)
            {
                int count = (int)Math.Min(WindowBytes, end - position);
                if (count <= 0) break;
                if (!r.Seek(position)) break;
                var data = r.Read(count);
                if (data == null) break;
                if (Probe(data, label, sink)) hit = true;
                if (position + count >= end) break;
                position += count - Math.Min(OverlapBytes, count / 2);
            }
            return hit;
        }

        private static bool Probe(byte[] data, string source, MetaSink sink)
        {
            // Latin-1 keeps a 1:1 byte/char mapping so offsets stay usable for the UTF-8 re-decode.
            var flat = Latin1(data);
            bool hit = false;
            bool partialSent = false;
            foreach (var marker in JsonScan.Markers)
            {
                int at = flat.IndexOf(marker, StringComparison.Ordinal);
                while (at >= 0)
                {
                    hit = true;
                    int next = at + marker.Length;
                    int start = FindStart(flat, at);
                    if (start >= 0)
                    {
                        var body = JsonScan.ExtractBalanced(flat, start);
                        if (body != null && start + body.Length > at)
                        {
                            if (!sink.Add(source, "scan", TextCodec.Decode(data, start, body.Length)))
                                return true;
                            next = start + body.Length;
                        }
                        else if (!partialSent)
                        {
                            // Nothing balanced here, but the signature is real: hand over a bounded
                            // slice from the brace on so the caller still reports the file.
                            partialSent = true;
                            int count = Math.Min(data.Length - start, MaxPartialBytes);
                            if (!sink.Add(source, "partial", TextCodec.Decode(data, start, count)))
                                return true;
                            break;
                        }
                        else break;
                    }
                    at = flat.IndexOf(marker, Math.Min(next, flat.Length), StringComparison.Ordinal);
                }
            }
            return hit;
        }

        /// <summary>
        /// Walks back to the nearest control byte, which is how JSON payloads are delimited inside
        /// binary containers, then returns the first brace at or after that boundary.
        /// </summary>
        private static int FindStart(string flat, int marker)
        {
            int lo = Math.Max(0, marker - BackLimit);
            int b = marker;
            while (b > lo)
            {
                char c = flat[b - 1];
                if (c < 0x09 || (c > 0x0D && c < 0x20)) break;
                b--;
            }
            for (int i = b; i <= marker; i++)
                if (flat[i] == '{' || flat[i] == '[') return i;
            return -1;
        }

        private static string Latin1(byte[] data)
        {
            var sb = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++) sb.Append((char)data[i]);
            return sb.ToString();
        }
    }
}
