using System;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>FLAC Vorbis comment blocks. ComfyUI's SaveAudio node writes workflows there.</summary>
    internal static class FlacReader
    {
        public static void Read(ByteReader r, MetaSink sink)
        {
            if (!r.Seek(4)) return;
            for (int guard = 0; guard < 64; guard++)
            {
                int header = r.ReadByteSafe();
                if (header < 0) return;
                bool last = (header & 0x80) != 0;
                int type = header & 0x7F;
                long a = r.ReadU16BE();
                int c = r.ReadByteSafe();
                if (a < 0 || c < 0) return;
                long length = (a << 8) | (uint)c;
                long next = r.Position + length;
                if (type == 4 && length > 8 && length < 48 * 1024 * 1024)
                {
                    var data = r.Read((int)length);
                    if (data == null) return;
                    if (!Emit(data, sink)) return;
                }
                if (last || !r.Seek(next)) return;
            }
        }

        private static bool Emit(byte[] data, MetaSink sink)
        {
            int p = 0;
            long vendor = U32(data, ref p);
            if (vendor < 0 || p + vendor > data.Length) return true;
            p += (int)vendor;
            long count = U32(data, ref p);
            if (count < 0 || count > 4096) return true;
            for (long i = 0; i < count; i++)
            {
                long len = U32(data, ref p);
                if (len < 0 || p + len > data.Length) return true;
                var entry = TextCodec.Decode(data, p, (int)len);
                p += (int)len;
                if (string.IsNullOrEmpty(entry)) continue;
                int eq = entry.IndexOf('=');
                string key = eq > 0 ? entry.Substring(0, eq) : "";
                string value = eq > 0 ? entry.Substring(eq + 1) : entry;
                if (!sink.Add("flac:vorbis", key, value)) return false;
            }
            return true;
        }

        private static long U32(byte[] data, ref int p)
        {
            if (p + 4 > data.Length) { p = data.Length; return -1; }
            long v = data[p] | ((long)data[p + 1] << 8) | ((long)data[p + 2] << 16) | ((long)data[p + 3] << 24);
            p += 4;
            return v;
        }
    }
}
