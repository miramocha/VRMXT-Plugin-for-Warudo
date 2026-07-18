using System;
using Newtonsoft.Json.Linq;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Read embedded glTF image bytes for <c>textures[i]</c> without UniGLTF.
    /// Supports bufferView-backed images in the GLB BIN chunk (typical <c>.vrm</c>).
    /// </summary>
    public static class GltfImageBytes
    {
        public static bool TryGetTextureImage(
            string gltfJson,
            byte[] binChunk,
            int textureIndex,
            out byte[] imageBytes,
            out string mimeType)
        {
            imageBytes = null;
            mimeType = null;
            if (string.IsNullOrWhiteSpace(gltfJson) || textureIndex < 0)
            {
                return false;
            }

            try
            {
                var root = JToken.Parse(gltfJson) as JObject;
                if (root == null)
                {
                    return false;
                }

                if (!TryGetArray(root, "textures", out var textures) ||
                    textureIndex >= textures.Count ||
                    textures[textureIndex] is not JObject textureObject)
                {
                    return false;
                }

                if (!TryGetInt(textureObject, "source", out var imageIndex) || imageIndex < 0)
                {
                    return false;
                }

                if (!TryGetArray(root, "images", out var images) ||
                    imageIndex >= images.Count ||
                    images[imageIndex] is not JObject imageObject)
                {
                    return false;
                }

                if (TryGetProperty(imageObject, "mimeType", out var mimeToken) &&
                    mimeToken.Type == JTokenType.String)
                {
                    mimeType = mimeToken.Value<string>();
                }

                // External URI images are out of scope for the .vrm re-read path.
                if (TryGetProperty(imageObject, "uri", out var uriToken) &&
                    uriToken.Type == JTokenType.String &&
                    !string.IsNullOrEmpty(uriToken.Value<string>()))
                {
                    var uri = uriToken.Value<string>();
                    if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        return TryDecodeDataUri(uri, out imageBytes, ref mimeType);
                    }

                    return false;
                }

                if (!TryGetInt(imageObject, "bufferView", out var bufferViewIndex) ||
                    bufferViewIndex < 0)
                {
                    return false;
                }

                return TryReadBufferView(root, binChunk, bufferViewIndex, out imageBytes);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryReadBufferView(
            JObject root,
            byte[] binChunk,
            int bufferViewIndex,
            out byte[] imageBytes)
        {
            imageBytes = null;
            if (binChunk == null)
            {
                return false;
            }

            if (!TryGetArray(root, "bufferViews", out var bufferViews) ||
                bufferViewIndex >= bufferViews.Count ||
                bufferViews[bufferViewIndex] is not JObject view)
            {
                return false;
            }

            if (!TryGetInt(view, "buffer", out var bufferIndex) || bufferIndex != 0)
            {
                // Only GLB buffer 0 (BIN chunk) supported.
                return false;
            }

            var byteOffset = 0;
            if (TryGetInt(view, "byteOffset", out var offset))
            {
                byteOffset = offset;
            }

            if (!TryGetInt(view, "byteLength", out var byteLength) || byteLength < 1)
            {
                return false;
            }

            if (byteOffset < 0 || byteOffset + byteLength > binChunk.Length)
            {
                return false;
            }

            imageBytes = new byte[byteLength];
            Buffer.BlockCopy(binChunk, byteOffset, imageBytes, 0, byteLength);
            return true;
        }

        private static bool TryDecodeDataUri(string uri, out byte[] imageBytes, ref string mimeType)
        {
            imageBytes = null;
            // data:image/png;base64,....
            var comma = uri.IndexOf(',');
            if (comma < 0)
            {
                return false;
            }

            var header = uri.Substring(0, comma);
            var payload = uri.Substring(comma + 1);
            if (header.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(mimeType))
            {
                const string prefix = "data:";
                var mimeEnd = header.IndexOf(';');
                if (mimeEnd > prefix.Length)
                {
                    mimeType = header.Substring(prefix.Length, mimeEnd - prefix.Length);
                }
            }

            try
            {
                imageBytes = Convert.FromBase64String(payload);
                return imageBytes.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool TryGetArray(JObject parent, string name, out JArray array)
        {
            array = null;
            if (!TryGetProperty(parent, name, out var token) || token is not JArray found)
            {
                return false;
            }

            array = found;
            return true;
        }

        private static bool TryGetProperty(JObject parent, string name, out JToken token)
        {
            return parent.TryGetValue(name, StringComparison.Ordinal, out token);
        }

        private static bool TryGetInt(JObject parent, string name, out int value)
        {
            value = 0;
            if (!TryGetProperty(parent, name, out var token))
            {
                return false;
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                return false;
            }

            var number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) ||
                number != Math.Truncate(number) ||
                number < int.MinValue || number > int.MaxValue)
            {
                return false;
            }

            value = (int)number;
            return true;
        }
    }
}
