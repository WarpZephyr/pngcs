using System.IO;

#if SHARPZIPLIB
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
// ONLY IF SHARPZIPLIB IS AVAILABLE

namespace Hjg.Pngcs.Zlib
{
    /// <summary>
    /// Zlib output (deflater) based on SharpZipLib.
    /// </summary>
    internal class ICSZlibOutputStream : ZlibOutputStream
    {
        private readonly DeflaterOutputStream _deflaterOutputStream;
        private readonly Deflater _deflater;

        public ICSZlibOutputStream(Stream st, int compressLevel, DeflateCompressStrategy strategy, bool leaveOpen) : base(st, compressLevel, strategy, leaveOpen)
        {
            _deflater = new Deflater(compressLevel);
            SetStrategy(strategy);
            _deflaterOutputStream = new DeflaterOutputStream(st, _deflater)
            {
                IsStreamOwner = !leaveOpen
            };
        }

        public void SetStrategy(DeflateCompressStrategy strat)
        {
            if (strat == DeflateCompressStrategy.Filtered)
                _deflater.SetStrategy(DeflateStrategy.Filtered);
            else if (strat == DeflateCompressStrategy.Huffman)
                _deflater.SetStrategy(DeflateStrategy.HuffmanOnly);
            else _deflater.SetStrategy(DeflateStrategy.Default);
        }

        public override void Write(byte[] buffer, int offset, int count)
            => _deflaterOutputStream.Write(buffer, offset, count);

        public override void WriteByte(byte value)
            => _deflaterOutputStream.WriteByte(value);


        public override void Close()
            => _deflaterOutputStream.Close();


        public override void Flush()
            => _deflaterOutputStream.Flush();

        public override string GetImplementationId()
            => "Zlib Deflater: SharpZipLib";
    }
}

#endif
