using System;
using System.Text;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Minimal GLB chunk reader (JSON + BIN). Avoids UniGLTF types.
    /// </summary>
    public static class GlbChunks
    {
        private static readonly byte[] Magic = { (byte)'g', (byte)'l', (byte)'T', (byte)'F' };
        private static readonly byte[] JsonChunkType = { (byte)'J', (byte)'S', (byte)'O', (byte)'N' };
        private static readonly byte[] BinChunkType = { (byte)'B', (byte)'I', (byte)'N', 0 };

        /// <summary>
        /// Extract JSON text. GLB preferred; plain UTF-8 glTF JSON also accepted.
        /// </summary>
        public static bool TryExtractJson(byte[] glbOrJson, out string json)
        {
            return TryExtract(glbOrJson, out json, out _);
        }

        /// <summary>
        /// Extract JSON and optional BIN chunk payload (null when input is plain JSON).
        /// </summary>
        public static bool TryExtract(byte[] glbOrJson, out string json, out byte[] binChunk)
        {
            json = null;
            binChunk = null;
            if (glbOrJson == null || glbOrJson.Length < 12)
            {
                return false;
            }

            if (!IsGlb(glbOrJson))
            {
                try
                {
                    json = Encoding.UTF8.GetString(glbOrJson);
                    return json.IndexOf('{') >= 0;
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }
            }

            string foundJson = null;
            byte[] foundBin = null;
            var offset = 12;
            while (offset + 8 <= glbOrJson.Length)
            {
                var chunkLength = ReadUInt32Le(glbOrJson, offset);
                offset += 4;
                var chunkType0 = glbOrJson[offset];
                var chunkType1 = glbOrJson[offset + 1];
                var chunkType2 = glbOrJson[offset + 2];
                var chunkType3 = glbOrJson[offset + 3];
                offset += 4;

                if (offset + chunkLength > glbOrJson.Length)
                {
                    return false;
                }

                if (chunkType0 == JsonChunkType[0] &&
                    chunkType1 == JsonChunkType[1] &&
                    chunkType2 == JsonChunkType[2] &&
                    chunkType3 == JsonChunkType[3])
                {
                    var end = offset + (int)chunkLength;
                    while (end > offset &&
                           (glbOrJson[end - 1] == 0 || glbOrJson[end - 1] == (byte)' '))
                    {
                        end--;
                    }

                    foundJson = Encoding.UTF8.GetString(glbOrJson, offset, end - offset);
                }
                else if (chunkType0 == BinChunkType[0] &&
                         chunkType1 == BinChunkType[1] &&
                         chunkType2 == BinChunkType[2] &&
                         chunkType3 == BinChunkType[3])
                {
                    foundBin = new byte[chunkLength];
                    Buffer.BlockCopy(glbOrJson, offset, foundBin, 0, (int)chunkLength);
                }

                offset += (int)chunkLength;
            }

            if (string.IsNullOrWhiteSpace(foundJson))
            {
                return false;
            }

            json = foundJson;
            binChunk = foundBin;
            return true;
        }

        private static bool IsGlb(byte[] data)
        {
            for (var i = 0; i < 4; i++)
            {
                if (data[i] != Magic[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static uint ReadUInt32Le(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          (data[offset + 1] << 8) |
                          (data[offset + 2] << 16) |
                          (data[offset + 3] << 24));
        }
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="GlbChunks.TryExtractJson"/>.
    /// </summary>
    public static class GlbJson
    {
        public static bool TryExtract(byte[] glbOrJson, out string json)
        {
            return GlbChunks.TryExtractJson(glbOrJson, out json);
        }
    }
}
