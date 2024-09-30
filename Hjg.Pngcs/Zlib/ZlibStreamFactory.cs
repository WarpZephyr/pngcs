using System.IO;

namespace Hjg.Pngcs.Zlib
{
    public class ZlibStreamFactory
    {
        public static ZlibInputStream CreateZlibInputStream(Stream stream, bool leaveOpen)
        {
#if SHARPZIPLIB
            return new ICSZlibInputStream(stream, leaveOpen);
#endif
#if NET45
                return new MsZlibInputStream(stream, leaveOpen);
#endif
        }

        public static ZlibInputStream CreateZlibInputStream(Stream stream)
            => CreateZlibInputStream(stream, false);

        public static ZlibOutputStream CreateZlibOutputStream(Stream stream, int compressLevel, DeflateCompressStrategy strat, bool leaveOpen)
        {
#if SHARPZIPLIB
            return new ICSZlibOutputStream(stream, compressLevel, strat, leaveOpen);
#endif
#if NET45
                return new MsZlibOutputStream(stream, compressLevel, strategy, leaveOpen);
#endif
        }

        public static ZlibOutputStream CreateZlibOutputStream(Stream stream)
            => CreateZlibOutputStream(stream, false);

        public static ZlibOutputStream CreateZlibOutputStream(Stream stream, bool leaveOpen)
            => CreateZlibOutputStream(stream, DeflateCompressLevel.DEFAULT, DeflateCompressStrategy.Default, leaveOpen);
    }
}
