// ONLY FOR .NET 4.5
#if NET45

using System;
using System.IO;
using System.IO.Compression;

namespace Hjg.Pngcs.Zlib
{

    /// <summary>
    /// Zip input (deflater) based on Ms DeflateStream (.net 4.5)
    /// </summary>
    internal class MsZlibInputStream : ZlibInputStream
    {
        private DeflateStream _deflateStream; // lazily created, if real read is called
        private bool _initdone = false;
        private bool _closed = false;
        // private Adler32 _adler32; // we dont check adler32!
        private bool _fdict;// merely informational, not used
        private int _cmdinfo;// merely informational, not used
        private byte[] _dictid; // merely informational, not used
        private byte[] _crcread = null; // merely informational, not checked

        public MsZlibInputStream(Stream stream, bool leaveOpen) : base(stream, leaveOpen) { }

        public override int Read(byte[] array, int offset, int count)
        {
            if (!_initdone)
                DoInit();

            if (_deflateStream == null && count > 0)
                InitStream();

            // We dont't check CRC on reading
            int readCount = _deflateStream.Read(array, offset, count);
            if (readCount < 1 && _crcread == null)
            {
                // Deflater has ended
                // We try to read next 4 bytes from raw stream (crc)
                _crcread = new byte[4];
                for (int i = 0; i < 4; i++)
                    _crcread[i] = (byte)_baseStream.ReadByte(); // We don't really check/use this
            }

            return readCount;
        }

        public override void Close()
        {
            // Can happen if never called write
            if (!_initdone)
                DoInit(); 

            if (_closed)
                return;

            _closed = true;
            _deflateStream?.Close();

            if (_crcread == null)
            { // eat trailing 4 bytes
                _crcread = new byte[4];
                for (int i = 0; i < 4; i++)
                    _crcread[i] = (byte)_baseStream.ReadByte();
            }

            if (!_leaveOpen)
                _baseStream.Close();
        }

        private void InitStream()
        {
            if (_deflateStream != null)
                return;

            _deflateStream = new DeflateStream(_baseStream, CompressionMode.Decompress, true);
        }

        private void DoInit()
        {
            if (_initdone) return;
            _initdone = true;
            // read zlib header : http://www.ietf.org/rfc/rfc1950.txt
            int cmf = _baseStream.ReadByte();
            int flag = _baseStream.ReadByte();
            if (cmf == -1 || flag == -1) return;
            if ((cmf & 0x0f) != 8) throw new Exception("Bad compression method for ZLIB header: cmf=" + cmf);
            _cmdinfo = ((cmf & (0xf0)) >> 8);// not used?
            _fdict = (flag & 32) != 0;
            if (_fdict)
            {
                _dictid = new byte[4];
                for (int i = 0; i < 4; i++)
                {
                    _dictid[i] = (byte)_baseStream.ReadByte(); // we eat but don't use this
                }
            }
        }

        public override void Flush()
            => _deflateStream?.Flush();

        public override string GetImplementationId()
            => "Zlib Inflater: .Net CLR 4.5";
    }
}

#endif