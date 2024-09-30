using System.IO;

namespace Hjg.Pngcs.Chunks
{
    /// <summary>
    /// sTER chunk: http://www.libpng.org/pub/png/spec/register/pngext-1.3.0-pdg.html#C.sTER
    /// </summary>
    public class PngChunkSTER : PngChunkSingle
    {
        public const string ID = "sTER";

        /// <summary>
        /// 0: cross-fuse layout 1: diverging-fuse layout
        /// </summary>
        public byte Mode { get; set; }

        public PngChunkSTER(ImageInfo info)
            : base(ID, info) { }


        public override ChunkOrderingConstraint GetOrderingConstraint()
            => ChunkOrderingConstraint.BEFORE_IDAT;

        public override ChunkRaw CreateRawChunk()
        {
            ChunkRaw c = CreateEmptyChunk(1, true);
            c.Data[0] = Mode;
            return c;
        }

        public override void ParseFromRaw(ChunkRaw chunk)
        {
            if (chunk.Length != 1)
                throw new InvalidDataException($"Invalid chunk length: {chunk}");

            Mode = chunk.Data[0];
        }

        public override void CloneDataFromRead(PngChunk other)
        {
            PngChunkSTER otherx = (PngChunkSTER)other;
            Mode = otherx.Mode;
        }
    }
}
