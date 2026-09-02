using System;
using System.IO;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell.Interop
{
    /// <summary>Adapts the COM IStream the shell hands us to a seekable System.IO.Stream.</summary>
    internal sealed class ComStream : Stream
    {
        private const int StatflagNoname = 1;
        private ComTypes.IStream _stream;
        private long _length = -1;

        public ComStream(ComTypes.IStream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            _stream = stream;
        }

        public override bool CanRead { get { return _stream != null; } }
        public override bool CanSeek { get { return _stream != null; } }
        public override bool CanWrite { get { return false; } }

        public override long Length
        {
            get
            {
                if (_length < 0)
                {
                    ComTypes.STATSTG stat;
                    _stream.Stat(out stat, StatflagNoname);
                    _length = stat.cbSize;
                }
                return _length;
            }
        }

        public override long Position
        {
            get { return Seek(0, SeekOrigin.Current); }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException("count");
            if (count == 0) return 0;
            var target = offset == 0 ? buffer : new byte[count];
            var read = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                _stream.Read(target, count, read);
                int n = Marshal.ReadInt32(read);
                if (n > 0 && offset != 0) Buffer.BlockCopy(target, 0, buffer, offset, n);
                return n < 0 ? 0 : n;
            }
            finally { Marshal.FreeCoTaskMem(read); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = Marshal.AllocCoTaskMem(sizeof(long));
            try
            {
                _stream.Seek(offset, (int)origin, position);
                return Marshal.ReadInt64(position);
            }
            finally { Marshal.FreeCoTaskMem(position); }
        }

        public override void Flush() { }

        public override void SetLength(long value) { throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>The shell owns the IStream, so only drop the reference.</summary>
        protected override void Dispose(bool disposing)
        {
            _stream = null;
            base.Dispose(disposing);
        }
    }
}
