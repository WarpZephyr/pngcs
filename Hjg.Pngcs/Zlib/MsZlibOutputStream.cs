// ONLY FOR .NET 4.5
#if NET45

using System.IO.Compression;
using System.IO;

namespace Hjg.Pngcs.Zlib
{
    internal class MsZlibOutputStream : ZlibOutputStream
    {
        private DeflateStream _deflateStream; // lazily created, if real read/write is called
        private readonly Adler32 _adler32 = new Adler32();
        private bool _initdone = false;
        private bool _closed = false;

        public MsZlibOutputStream(Stream stream, int compressLevel, DeflateCompressStrategy strategy, bool leaveOpen)
            : base(stream, compressLevel, strategy, leaveOpen) { }

        public override void WriteByte(byte value)
        {
            if (!_initdone)
                DoInit();

            if (_deflateStream == null)
                InitStream();

            base.WriteByte(value);
            _adler32.Update(value);
        }

        public override void Write(byte[] array, int offset, int count)
        {
            if (count == 0)
                return;

            if (!_initdone)
                DoInit();

            if (_deflateStream == null)
                InitStream();

            _deflateStream.Write(array, offset, count);
            _adler32.Update(array, offset, count);
        }

        public override void Close()
        {
            // Can happen if never called write
            if (!_initdone)
                DoInit();

            if (_closed)
                return;

            _closed = true;
            // Sigh ... no only must I close the parent stream to force a flush, but I must save a reference to
            // raw stream because (apparently) Close() sets it to null (shame on you, MS developers)
            if (_deflateStream != null)
            {
                _deflateStream.Close();
            }
            else
            {         // Second hack: empty input?
                _baseStream.WriteByte(3);
                _baseStream.WriteByte(0);
            }

            // Add crc
            uint crcv = _adler32.GetValue();
            _baseStream.WriteByte((byte)((crcv >> 24) & 0xFF));
            _baseStream.WriteByte((byte)((crcv >> 16) & 0xFF));
            _baseStream.WriteByte((byte)((crcv >> 8) & 0xFF));
            _baseStream.WriteByte((byte)((crcv) & 0xFF));

            if (!_leaveOpen)
                _baseStream.Close();
        }

        private void InitStream()
        {
            if (_deflateStream != null)
                return;

            // I must create the DeflateStream only if necessary, because of its bug with empty input (sigh)
            // I must create with leaveopen=true always and do the closing myself,
            // because MS moronic implementation of DeflateStream: I cant force a flush of the underlying stream witouth closing (sigh bis)
            CompressionLevel clevel = CompressionLevel.Optimal;

            // thanks for the granularity, MS!
            if (_compressLevel >= 1 && _compressLevel <= 5)
            {
                clevel = CompressionLevel.Fastest;
            }
            else if (_compressLevel == 0)
            {
                clevel = CompressionLevel.NoCompression;
            }

            _deflateStream = new DeflateStream(_baseStream, clevel, true);
        }

        private void DoInit()
        {
            if (_initdone)
                return;

            // http://stackoverflow.com/a/2331025/277304
            int cmf = 0x78;
            int flg = 218;

            // Sorry about the following lines
            if (_compressLevel >= 5 && _compressLevel <= 6)
            {
                flg = 156;
            }
            else if (_compressLevel >= 3 && _compressLevel <= 4)
            {
                flg = 94;
            }
            else if (_compressLevel <= 2)
            {
                flg = 1;
            }

            flg -= ((cmf * 256 + flg) % 31); // just in case
            if (flg < 0) flg += 31;
            _baseStream.WriteByte((byte)cmf);
            _baseStream.WriteByte((byte)flg);
            _initdone = true;
        }

        public override void Flush()
            => _deflateStream?.Flush();

        public override string GetImplementationId()
            => "Zlib Deflater: .Net CLR 4.5";

    }
}

#endif