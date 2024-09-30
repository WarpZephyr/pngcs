using System;
using System.IO;
using System.Collections.Generic;
using Hjg.Pngcs.Chunks;
using Hjg.Pngcs.Zlib;

namespace Hjg.Pngcs
{
    /// <summary>
    ///  Writes a PNG image, line by line.
    /// </summary>
    public class PngWriter
    {
        /// <summary>
        /// The output stream.
        /// </summary>
        private readonly Stream _baseStream;

        /// <summary>
        /// The pixel data stream.
        /// </summary>
        private PngIDatChunkOutputStream datStream;

        /// <summary>
        /// The compression stream.
        /// </summary>
        private ZlibOutputStream datStreamDeflated;

        /// <summary>
        /// The filtering strategy.
        /// </summary>
        private FilterWriteStrategy _filterStrategy;

        /// <summary>
        /// Basic image info, immutable.
        /// </summary>
        public readonly ImageInfo _imageInfo;

        /// <summary>
        /// A high level wrapper of a ChunksList : list of written/queued chunks
        /// </summary>
        private readonly PngMetadata _metadata;

        /// <summary>
        /// The written/queued chunks.
        /// </summary>
        private readonly ChunksListForWrite _chunksList;

        /// <summary>
        /// File name, or description, merely informative; can be empty.
        /// </summary>
        public readonly string FileName;

        /// <summary>
        /// Deflate algortithm compression strategy.
        /// </summary>
        public DeflateCompressStrategy CompressionStrategy { get; set; }

        /// <summary>
        /// Zip compression level (0 - 9).
        /// </summary>
        /// <remarks>
        /// Default is 6.<br/>
        /// Maximum is 9.
        /// </remarks>
        public int CompressionLevel { get; set; }

        /// <summary>
        /// Whether or not the base stream should be left open.
        /// </summary>
        public bool LeaveOpen { get; set; }

        /// <summary>
        /// The maximum size of IDAT chunks.
        /// </summary>
        /// <remarks>
        /// 0 uses default (PngIDatChunkOutputStream 32768).
        /// </remarks>
        public int IdatMaxSize { get; set; } 

        /// <summary>
        /// raw current row, as array of bytes,counting from 1 (index 0 is reserved for filter type)
        /// </summary>
        protected byte[] _rawRow;

        /// <summary>
        /// The previous raw row.
        /// </summary>
        protected byte[] _previousRawRow;

        /// <summary>
        /// The current raw row, after being filtered.
        /// </summary>
        protected byte[] _filteredRawRow;

        /// <summary>
        /// Number of chunk group (0-6) last writen, or currently writing.
        /// </summary>
        /// <remarks>see ChunksList.CHUNK_GROUP_NNN</remarks>
        public int CurrentChunkGroup { get; private set; }

        /// <summary>
        /// Current line number.
        /// </summary>
        private int _rowNum = -1;

        /// <summary>
        /// Auxiliar buffer, histogram, only used by <see cref="ReportResultsForFilter"/>.
        /// </summary>
        private readonly int[] histox = new int[256];

        /// <summary>
        /// This only influences the 1-2-4 bitdepth format; If we pass a ImageLine to WriteRow, this is ignored.
        /// </summary>
        private bool _unpackedMode;

        /// <summary>
        /// Whether or not values need to be packed, is auto computed.
        /// </summary>
        private bool needsPack;

        /// <summary>
        /// Creates a <see cref="PngWriter"/> from a <see cref="Stream"/> and <see cref="ImageInfo"/>.
        /// </summary>
        /// <param name="output">The output stream.</param>
        /// <param name="imgInfo">The image info.</param>
        public PngWriter(Stream output, ImageInfo imgInfo)
            : this(output, imgInfo, string.Empty) { }

        /// <summary>
        /// Creates a <see cref="PngWriter"/> from a <see cref="Stream"/>, <see cref="ImageInfo"/>, and optional file name or description.
        /// </summary>
        /// <remarks>
        /// After construction nothing is written yet.<br/>
        /// You still can set some parameters (compression, filters) and queue chunks before you start writing the pixels.<br/>
        /// Also see <see cref="PngFileHelper.PngOpenWrite(string, ImageInfo, bool)"/>.
        /// </remarks>
        /// <param name="output">The output stream.</param>
        /// <param name="imageInfo">The image info.</param>
        /// <param name="filename">Optional, can be a filename or a description.</param>
        public PngWriter(Stream output, ImageInfo imageInfo, string filename)
        {
            FileName = filename;
            _baseStream = output;
            _imageInfo = imageInfo;

            // Default settings
            CompressionLevel = 6;
            LeaveOpen = false;
            IdatMaxSize = 0; // use default
            CompressionStrategy = DeflateCompressStrategy.Filtered;

            _rawRow = new byte[imageInfo.BytesPerRow + 1];
            _previousRawRow = new byte[_rawRow.Length];
            _filteredRawRow = new byte[_rawRow.Length];
            _chunksList = new ChunksListForWrite(_imageInfo);
            _metadata = new PngMetadata(_chunksList);
            _filterStrategy = new FilterWriteStrategy(_imageInfo, FilterType.FILTER_DEFAULT);
            _unpackedMode = false;
            needsPack = _unpackedMode && imageInfo.Packed;
        }

        #region Methods

        /// <summary>
        /// Gets the <see cref="PngMetadata"/>.
        /// </summary>
        /// <returns>A <see cref="PngMetadata"/>.</returns>
        public PngMetadata GetMetadata()
            => _metadata;

        /// <summary>
        /// Gets the chunks list to be written.
        /// </summary>
        /// <returns>A <see cref="ChunksListForWrite"/>.</returns>
        public ChunksListForWrite GetChunksList()
            => _chunksList;

        /// <summary>
        /// Whether or not unpacked mode is set.<br/>
        /// This determines whether or not packed values (bitdepths 1,2,4) will be written unpacked.
        /// </summary>
        /// <returns>Whether or not unpacked mode is set.</returns>
        public bool IsUnpackedMode()
            => _unpackedMode;

        /// <summary>
        /// Sets unpacked mode to the specified state.
        /// </summary>
        /// <param name="value">The value to set.</param>
        public void SetUnpackedMode(bool value)
        {
            _unpackedMode = value;
            needsPack = _unpackedMode && _imageInfo.Packed;
        }

        /// <summary>
        /// Computes compressed size/raw size, approximate
        /// </summary>
        /// <remarks>Actually: compressed size = total size of IDAT data , raw size = uncompressed pixel bytes = rows * (bytesPerRow + 1)
        /// </remarks>
        /// <returns></returns>
        public double ComputeCompressionRatio()
        {
            if (CurrentChunkGroup < ChunksList.CHUNK_GROUP_6_END)
                throw new InvalidOperationException($"{nameof(ComputeCompressionRatio)} can only be called after {nameof(End)}");

            return datStream.CountFlushed / (double)((_imageInfo.BytesPerRow + 1) * _imageInfo.Rows);
        }

        #endregion

        #region Write Chunks

        /// <summary>
        /// This is called automatically before writing the first row.
        /// </summary>
        private void Init()
        {
            datStream = new PngIDatChunkOutputStream(_baseStream, IdatMaxSize);
            datStreamDeflated = ZlibStreamFactory.CreateZlibOutputStream(datStream, CompressionLevel, CompressionStrategy, true);
            WriteSignatureAndIHDR();
            WriteFirstChunks();
        }

        private void WriteEndChunk()
            => new PngChunkIEND(_imageInfo).CreateRawChunk().WriteChunk(_baseStream);

        private void WriteFirstChunks()
        {
            CurrentChunkGroup = ChunksList.CHUNK_GROUP_1_AFTERIDHR;
            _chunksList.WriteChunks(_baseStream, CurrentChunkGroup);

            CurrentChunkGroup = ChunksList.CHUNK_GROUP_2_PLTE;
            int nw = _chunksList.WriteChunks(_baseStream, CurrentChunkGroup);
            if (nw > 0 && _imageInfo.Grayscale)
                throw new InvalidOperationException("Cannot write palette for this format.");
            if (nw == 0 && _imageInfo.Indexed)
                throw new InvalidOperationException("Missing palette.");

            CurrentChunkGroup = ChunksList.CHUNK_GROUP_3_AFTERPLTE;
            _chunksList.WriteChunks(_baseStream, CurrentChunkGroup);

            CurrentChunkGroup = ChunksList.CHUNK_GROUP_4_IDAT;
        }

        private void WriteLastChunks()
        {
            // Not including end
            CurrentChunkGroup = ChunksList.CHUNK_GROUP_5_AFTERIDAT;
            _chunksList.WriteChunks(_baseStream, CurrentChunkGroup);

            // There should be no unwritten chunks.
            List<PngChunk> pending = _chunksList.GetQueuedChunks();
            if (pending.Count > 0)
                throw new Exception($"{pending.Count} chunks were not written.");

            CurrentChunkGroup = ChunksList.CHUNK_GROUP_6_END;
        }

        /// <summary>
        /// Write id signature and also "IHDR" chunk
        /// </summary>
        private void WriteSignatureAndIHDR()
        {
            CurrentChunkGroup = ChunksList.CHUNK_GROUP_0_IDHR;
            PngHelperInternal.WriteBytes(_baseStream, PngHelperInternal.PNG_ID_SIGNATURE); // signature

            // http://www.libpng.org/pub/png/spec/1.2/PNG-Chunks.html
            PngChunkIHDR ihdr = new PngChunkIHDR(_imageInfo)
            {
                Columns = _imageInfo.Columns,
                Rows = _imageInfo.Rows,
                BitsPerChannel = _imageInfo.BitDepth
            };

            int colormodel = 0;
            if (_imageInfo.HasAlpha)
                colormodel += 0x04;
            if (_imageInfo.Indexed)
                colormodel += 0x01;
            if (!_imageInfo.Grayscale)
                colormodel += 0x02;

            ihdr.ColorModel = colormodel;
            ihdr.CompressionMethod = 0; // compression method 0=deflate
            ihdr.FilterMethod = 0; // filter method (0)
            ihdr.Interlaced = 0; // never interlace
            ihdr.CreateRawChunk().WriteChunk(_baseStream);
        }

        #endregion

        #region Write Row

        protected void EncodeRowFromBytes(byte[] row)
        {
            if (row.Length == _imageInfo.SamplesPerRowPacked && !needsPack)
            {
                // some duplication of code - because this case is typical and it works faster this way
                int j = 1;
                if (_imageInfo.BitDepth <= 8)
                {
                    foreach (byte x in row)
                    { // optimized
                        _rawRow[j++] = x;
                    }
                }
                else
                { // 16 bitspc
                    foreach (byte x in row)
                    { // optimized
                        _rawRow[j] = x;
                        j += 2;
                    }
                }
            }
            else
            {
                // perhaps we need to pack?
                if (row.Length >= _imageInfo.SamplesPerRow && needsPack)
                    ImageLine.PackInplaceBytes(_imageInfo, row, row, false); // Row is packed in place!

                if (_imageInfo.BitDepth <= 8)
                {
                    for (int i = 0, j = 1; i < _imageInfo.SamplesPerRowPacked; i++)
                    {
                        _rawRow[j++] = row[i];
                    }
                }
                else
                {
                    // 16 bitspc
                    for (int i = 0, j = 1; i < _imageInfo.SamplesPerRowPacked; i++)
                    {
                        _rawRow[j++] = row[i];
                        _rawRow[j++] = 0;
                    }
                }

            }
        }

        protected void EncodeRowFromInts(int[] row)
        {
            if (row.Length == _imageInfo.SamplesPerRowPacked && !needsPack)
            {
                // Some duplication of code - because this case is typical and it works faster this way.
                int j = 1;
                if (_imageInfo.BitDepth <= 8)
                {
                    foreach (int x in row)
                    {
                        // optimized
                        _rawRow[j++] = (byte)x;
                    }
                }
                else
                {
                    // 16 bitspc
                    foreach (int x in row)
                    {
                        // optimized
                        _rawRow[j++] = (byte)(x >> 8);
                        _rawRow[j++] = (byte)x;
                    }
                }
            }
            else
            {
                // Perhaps we need to pack?
                if (row.Length >= _imageInfo.SamplesPerRow && needsPack)
                    ImageLine.PackInplaceInts(_imageInfo, row, row, false); // Row is packed in place!

                if (_imageInfo.BitDepth <= 8)
                {
                    for (int i = 0, j = 1; i < _imageInfo.SamplesPerRowPacked; i++)
                    {
                        _rawRow[j++] = (byte)row[i];
                    }
                }
                else
                {
                    // 16 bitspc
                    for (int i = 0, j = 1; i < _imageInfo.SamplesPerRowPacked; i++)
                    {
                        _rawRow[j++] = (byte)(row[i] >> 8);
                        _rawRow[j++] = (byte)row[i];
                    }
                }

            }
        }

        private void PrepareEncodeRow(int rown)
        {
            if (datStream == null)
                Init();

            _rowNum++;
            if (rown >= 0 && _rowNum != rown)
                throw new InvalidOperationException($"Rows must be written in order: Expected: {_rowNum}; Passed: {rown}");

            // Swap
            byte[] tmp = _rawRow;
            _rawRow = _previousRawRow;
            _previousRawRow = tmp;
        }

        /// <summary>
        /// Write a <see cref="ImageLine"/>.<br/>
        /// This uses the row number from the <see cref="ImageLine"/>.
        /// </summary>
        public void WriteRow(ImageLine imgline, int rownumber)
        {
            SetUnpackedMode(imgline.SamplesUnpacked);
            if (imgline.LineSampleType == ImageLine.SampleType.Integer)
                WriteRowInt(imgline.ScanlineInts, rownumber);
            else
                WriteRowByte(imgline.ScanlineBytes, rownumber);
        }

        public void WriteRow(int[] newrow)
            => WriteRow(newrow, -1);

        public void WriteRow(int[] newrow, int rowNum)
            => WriteRowInt(newrow, rowNum);

        /// <summary>
        /// Writes a full image row.
        /// </summary>
        /// <remarks>
        /// This must be called sequentially from n=0 to n=rows-1.<br/>
        /// There must be one integer per sample.<br/>
        /// They must be in order: R G B R G B ... (or R G B A R G B A... if it has alpha).<br/>
        /// The values should be between 0 and 255 for 8 bitsperchannel images,<br/>
        /// and between 0-65535 form 16 bitsperchannel images (this applies also to the alpha channel if present)<br/>
        /// The array can be reused.
        /// </remarks>
        /// <param name="newrow">The pixel values to write.</param>
        /// <param name="rowNum">The number of the row, from 0 at the top, to rows - 1 at the bottom.</param>
        public void WriteRowInt(int[] newrow, int rowNum)
        {
            PrepareEncodeRow(rowNum);
            EncodeRowFromInts(newrow);
            FilterAndSend(rowNum);
        }

        /// <summary>
        /// Writes a full image row of bytes.
        /// </summary>
        /// <param name="newrow">The pixel values to write.</param>
        /// <param name="rowNum"></param>
        public void WriteRowByte(byte[] newrow, int rowNum)
        {
            PrepareEncodeRow(rowNum);
            EncodeRowFromBytes(newrow);
            FilterAndSend(rowNum);
        }

        /// <summary>
        /// Writes all the pixels, calling <see cref="WriteRowInt"/> for each image row
        /// </summary>
        /// <param name="image">The image to write.</param>
        public void WriteRowsInt(int[][] image)
        {
            for (int i = 0; i < _imageInfo.Rows; i++)
                WriteRowInt(image[i], i);
        }

        /// <summary>
        /// Writes all the pixels, calling <see cref="WriteRowByte"/> for each image row.
        /// </summary>
        /// <param name="image">The image to write.</param>
        public void WriteRowsByte(byte[][] image)
        {
            for (int i = 0; i < _imageInfo.Rows; i++)
                WriteRowByte(image[i], i);
        }

        #endregion

        #region Filter

        private void FilterAndSend(int rown)
        {
            FilterRow(rown);
            datStreamDeflated.Write(_filteredRawRow, 0, _imageInfo.BytesPerRow + 1);
        }

        private void FilterRow(int rown)
        {
            // Warning: filters operation rely on: "previous row" (rowbprev) is initialized to 0 the first time
            if (_filterStrategy.ShouldTestAll(rown))
            {
                FilterRowNone();
                ReportResultsForFilter(rown, FilterType.FILTER_NONE, true);
                FilterRowSubtract();
                ReportResultsForFilter(rown, FilterType.FILTER_SUB, true);
                FilterRowUp();
                ReportResultsForFilter(rown, FilterType.FILTER_UP, true);
                FilterRowAverage();
                ReportResultsForFilter(rown, FilterType.FILTER_AVERAGE, true);
                FilterRowPaeth();
                ReportResultsForFilter(rown, FilterType.FILTER_PAETH, true);
            }

            FilterType filterType = _filterStrategy.GimmeFilterType(rown, true);
            _filteredRawRow[0] = (byte)(int)filterType;
            switch (filterType)
            {
                case FilterType.FILTER_NONE:
                    FilterRowNone();
                    break;
                case FilterType.FILTER_SUB:
                    FilterRowSubtract();
                    break;
                case FilterType.FILTER_UP:
                    FilterRowUp();
                    break;
                case FilterType.FILTER_AVERAGE:
                    FilterRowAverage();
                    break;
                case FilterType.FILTER_PAETH:
                    FilterRowPaeth();
                    break;
                default:
                    throw new NotImplementedException($"Filter type {filterType} not implemented.");
            }

            ReportResultsForFilter(rown, filterType, false);
        }

        private void FilterRowAverage()
        {
            int i, j, imax;
            imax = _imageInfo.BytesPerRow;
            for (j = 1 - _imageInfo.BytesPerPixel, i = 1; i <= imax; i++, j++)
                _filteredRawRow[i] = (byte)(_rawRow[i] - (_previousRawRow[i] + (j > 0 ? _rawRow[j] : 0)) / 2);
        }

        private void FilterRowNone()
        {
            for (int i = 1; i <= _imageInfo.BytesPerRow; i++)
                _filteredRawRow[i] = _rawRow[i];
        }


        private void FilterRowPaeth()
        {
            int i, j, imax;
            imax = _imageInfo.BytesPerRow;
            for (j = 1 - _imageInfo.BytesPerPixel, i = 1; i <= imax; i++, j++)
                _filteredRawRow[i] = (byte)(_rawRow[i] - PngHelperInternal.FilterPaethPredictor(
                    (j > 0) ? _rawRow[j] : 0, _previousRawRow[i], (j > 0) ? _previousRawRow[j] : 0));
        }

        private void FilterRowSubtract()
        {
            int i;
            int j;

            for (i = 1; i <= _imageInfo.BytesPerPixel; i++)
                _filteredRawRow[i] = _rawRow[i];

            for (j = 1, i = _imageInfo.BytesPerPixel + 1; i <= _imageInfo.BytesPerRow; i++, j++)
                _filteredRawRow[i] = (byte)(_rawRow[i] - _rawRow[j]);
        }

        private void FilterRowUp()
        {
            for (int i = 1; i <= _imageInfo.BytesPerRow; i++)
                _filteredRawRow[i] = (byte)(_rawRow[i] - _previousRawRow[i]);
        }

        private long SumFilteredRawRow()
        {
            // Sums absolute value.
            long sum = 0;
            for (int i = 1; i <= _imageInfo.BytesPerRow; i++)
            {
                if (_filteredRawRow[i] < 0)
                {
                    sum -= _filteredRawRow[i];
                }
                else
                {
                    sum += _filteredRawRow[i];
                }
            }
            return sum;
        }

        private void ReportResultsForFilter(int rown, FilterType type, bool tentative)
        {
            for (int i = 0; i < histox.Length; i++)
                histox[i] = 0;
            int s = 0, v;
            for (int i = 1; i <= _imageInfo.BytesPerRow; i++)
            {
                v = _filteredRawRow[i];
                if (v < 0)
                    s -= v;
                else
                    s += v;
                histox[v & 0xFF]++;
            }
            _filterStrategy.FillResultsForFilter(rown, type, s, histox, tentative);
        }

        /// <summary>
        /// Sets internal prediction filter type, or a strategy to choose it.
        /// </summary>
        /// <remarks>
        /// This must be called just after constructor, before starting writing.<br/>
        /// Recommended values: DEFAULT (default) or AGGRESSIVE.
        /// </remarks>
        /// <param name="filterType">One of the five prediction types or strategy to choose it</param>
        public void SetFilterType(FilterType filterType)
            => _filterStrategy = new FilterWriteStrategy(_imageInfo, filterType);

        #endregion

        #region Copy Chunks

        /// <summary>
        /// Copy chunks from reader - copyMask : see ChunksToWrite.COPY_XXX.<br/>
        /// If we are after idat, only considers those chunks after IDAT in <see cref="PngReader"/>.<br/>
        /// TODO: this should be more customizable.
        /// </summary>
        private void CopyChunks(PngReader reader, int copyMask, bool onlyAfterIdat)
        {
            bool idatDone = CurrentChunkGroup >= ChunksList.CHUNK_GROUP_4_IDAT;
            if (onlyAfterIdat && reader.CurrentChunkGroup < ChunksList.CHUNK_GROUP_6_END)
                throw new InvalidOperationException("Tried to copy last chunks but reader has not ended.");

            foreach (PngChunk chunk in reader.GetChunksList().GetChunks())
            {
                if (chunk.ChunkGroup < ChunksList.CHUNK_GROUP_4_IDAT && idatDone)
                    continue;

                bool copy = false;
                if (chunk.Critical && chunk.Id.Equals(ChunkHelper.PLTE))
                {
                    copy |= _imageInfo.Indexed && ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_PALETTE);
                    copy |= !_imageInfo.Grayscale && ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_ALL);
                }
                else
                {
                    // Ancillary
                    bool text = chunk is PngChunkTextVar;
                    bool safe = chunk.Safe;

                    // Notice that these if are not exclusive
                    copy |= ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_ALL);
                    copy |= safe && ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_ALL_SAFE);
                    copy |= chunk.Id.Equals(ChunkHelper.tRNS) && ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_TRANSPARENCY);
                    copy |= chunk.Id.Equals(ChunkHelper.pHYs) && ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_PHYS);
                    copy |= text && ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_TEXTUAL);
                    copy |= ChunkHelper.MaskMatch(copyMask, ChunkCopyBehaviour.COPY_ALMOSTALL)
                        && !(ChunkHelper.IsUnknown(chunk) || text || chunk.Id.Equals(ChunkHelper.hIST) || chunk.Id.Equals(ChunkHelper.tIME));
                    copy |= !(chunk is PngChunkSkipped);
                }

                if (copy)
                {
                    _chunksList.Queue(PngChunk.CloneChunk(chunk, _imageInfo));
                }
            }
        }

        public void CopyChunksFirst(PngReader reader, int copy_mask)
            => CopyChunks(reader, copy_mask, false);

        public void CopyChunksLast(PngReader reader, int copy_mask)
            => CopyChunks(reader, copy_mask, true);

        #endregion

        #region End

        /// <summary>
        /// Finalizes the image creation and closes the file stream.
        /// </summary>
        /// <remarks>This must be called after writing all lines.</remarks>
        public void End()
        {
            if (_rowNum != _imageInfo.Rows - 1)
                throw new InvalidOperationException("Not all rows have been written.");

            datStreamDeflated.Dispose();
            datStream.Dispose();
            WriteLastChunks();
            WriteEndChunk();

            if (!LeaveOpen)
            {
                _baseStream.Dispose();
            }
        }

        #endregion
    }
}
