using System.IO;

#if SHARPZIPLIB

using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
// ONLY IF SHARPZIPLIB IS AVAILABLE

namespace Hjg.Pngcs.Zlib
{
    /// <summary>
    /// Zip input (inflater) based on SharpZipLib.
    /// </summary>
    internal class ICSZlibInputStream : ZlibInputStream
    {
        private readonly InflaterInputStream _inflaterInputStream;

        public ICSZlibInputStream(Stream stream, bool leaveOpen) : base(stream, leaveOpen)
        {
            _inflaterInputStream = new InflaterInputStream(stream)
            {
                IsStreamOwner = !leaveOpen
            };
        }

        public override int Read(byte[] array, int offset, int count)
            => _inflaterInputStream.Read(array, offset, count);

        public override int ReadByte()
            => _inflaterInputStream.ReadByte();

        public override void Close()
            => _inflaterInputStream.Close();

        public override void Flush()
            => _inflaterInputStream.Flush();

        public override string GetImplementationId()
            => "Zlib Inflater: SharpZipLib";
    }
}

#endif
