using System;
using System.IO;
using System.Text;

namespace ComfyShellExt.Core.Formats
{
    /// <summary>Small endian-aware helper over a seekable stream. Never throws on EOF.</summary>
    internal sealed class ByteReader
    {
        private readonly Stream _s;
        private readonly byte[] _buf = new byte[8];

        public ByteReader(Stream s) { _s = s; }

        public Stream BaseStream { get { return _s; } }

        public long Position { get { return _s.Position; } }

        public long Length
        {
            get { try { return _s.Length; } catch { return -1; } }
        }

        public bool Seek(long position)
        {
            try
            {
                if (position < 0) return false;
                _s.Position = position;
                return true;
            }
            catch { return false; }
        }

        public bool Skip(long count)
        {
            return Seek(Position + count);
        }

        /// <summary>Reads exactly count bytes, or returns null.</summary>
        public byte[] Read(int count)
        {
            if (count < 0 || count > 64 * 1024 * 1024) return null;
            var data = new byte[count];
            return Fill(data, count) ? data : null;
        }

        public bool Fill(byte[] data, int count)
        {
            int done = 0;
            while (done < count)
            {
                int n;
                try { n = _s.Read(data, done, count - done); }
                catch { return false; }
                if (n <= 0) return false;
                done += n;
            }
            return true;
        }

        public int ReadByteSafe()
        {
            try { return _s.ReadByte(); } catch { return -1; }
        }

        private bool Take(int n) { return Fill(_buf, n); }

        public long ReadU16BE() { return Take(2) ? (_buf[0] << 8) | _buf[1] : -1; }

        public long ReadU32BE()
        {
            if (!Take(4)) return -1;
            return ((long)_buf[0] << 24) | ((long)_buf[1] << 16) | ((long)_buf[2] << 8) | _buf[3];
        }

        public long ReadU64BE()
        {
            if (!Take(8)) return -1;
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | _buf[i];
            return v < 0 ? -1 : v;
        }

        public long ReadU16LE() { return Take(2) ? (_buf[1] << 8) | _buf[0] : -1; }

        public long ReadU32LE()
        {
            if (!Take(4)) return -1;
            return ((long)_buf[3] << 24) | ((long)_buf[2] << 16) | ((long)_buf[1] << 8) | _buf[0];
        }

        /// <summary>Reads a fixed length tag as ASCII, e.g. a PNG chunk type or a RIFF fourcc.</summary>
        public string ReadTag(int n)
        {
            var data = Read(n);
            if (data == null) return null;
            var sb = new StringBuilder(n);
            for (int i = 0; i < n; i++) sb.Append((char)data[i]);
            return sb.ToString();
        }
    }
}
