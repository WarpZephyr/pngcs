using System;
using System.IO;

namespace Hjg.Pngcs.Zlib
{
    public abstract class ZlibOutputStream : Stream
    {
        readonly protected Stream _baseStream;
        readonly protected bool _leaveOpen;
        protected int _compressLevel;
        protected DeflateCompressStrategy _strategy;

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override long Length
            => throw new NotImplementedException();

        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override bool CanTimeout => false;

        public ZlibOutputStream(Stream stream, int compressLevel, DeflateCompressStrategy strategy, bool leaveOpen)
        {
            _baseStream = stream;
            _leaveOpen = leaveOpen;
            _strategy = strategy;
            _compressLevel = compressLevel;
        }

        public override void SetLength(long value)
            => throw new NotImplementedException();


        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotImplementedException();


        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("Cannot read in an output stream.");

        /// <summary>
        /// Mainly for debugging.
        /// </summary>
        /// <returns>The ImplementationId.</returns>
        public abstract string GetImplementationId();
    }
}
