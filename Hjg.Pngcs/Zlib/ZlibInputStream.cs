using System;
using System.IO;

namespace Hjg.Pngcs.Zlib
{
    public abstract class ZlibInputStream : Stream
    {
        protected readonly Stream _baseStream;
        protected readonly bool _leaveOpen;

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override long Length
            => throw new NotImplementedException();

        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override bool CanTimeout => false;

        public ZlibInputStream(Stream stream, bool leaveOpen)
        {
            _baseStream = stream;
            _leaveOpen = leaveOpen;
        }

        public override void SetLength(long value)
            => throw new NotImplementedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("Cannot write in a input stream.");

        /// <summary>
        /// Mainly for debugging.
        /// </summary>
        /// <returns>The ImplementationId.</returns>
        public abstract string GetImplementationId();
    }
}

