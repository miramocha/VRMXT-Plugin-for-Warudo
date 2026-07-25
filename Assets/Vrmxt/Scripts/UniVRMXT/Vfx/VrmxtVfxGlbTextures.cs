using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Decode glTF <c>textures[]</c> from a second read of GLB bytes (UniVRM only imports
    /// material/meta textures — VFX-only images are otherwise missing).
    /// </summary>
    public sealed class VrmxtVfxGlbTextures : IDisposable
    {
        private readonly string _json;
        private readonly byte[] _binChunk;
        private readonly Dictionary<int, Texture2D> _cache = new();
        private bool _disposed;

        public VrmxtVfxGlbTextures(string gltfJson, byte[] binChunk)
        {
            _json = gltfJson ?? throw new ArgumentNullException(nameof(gltfJson));
            _binChunk = binChunk;
        }

        public static bool TryCreate(byte[] glbBytes, out VrmxtVfxGlbTextures textures)
        {
            textures = null;
            if (!GlbChunks.TryExtract(glbBytes, out var json, out var bin))
            {
                return false;
            }

            textures = new VrmxtVfxGlbTextures(json, bin);
            return true;
        }

        public string Json => _json;

        public IReadOnlyCollection<Texture2D> CachedTextures => _cache.Values;

        public Texture Get(int textureIndex)
        {
            if (_disposed || textureIndex < 0)
            {
                return null;
            }

            if (_cache.TryGetValue(textureIndex, out var cached))
            {
                return cached;
            }

            if (!GltfImageBytes.TryGetTextureImage(
                    _json,
                    _binChunk,
                    textureIndex,
                    out var imageBytes,
                    out _))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "VRMXT_sprite_particle_tex_" + textureIndex,
            };

            if (!texture.LoadImage(imageBytes, markNonReadable: true))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            ApplyGltfSampler(texture, textureIndex);
            _cache[textureIndex] = texture;
            return texture;
        }

        /// <summary>
        /// Honor glTF <c>samplers[]</c> wrap/filter (default repeat). Hardcoding Clamp
        /// broke tiled emission/dissolve masks that ship with non-identity ST.
        /// </summary>
        private void ApplyGltfSampler(Texture2D texture, int textureIndex)
        {
            if (texture == null)
            {
                return;
            }

            if (!GltfImageBytes.TryGetTextureSampler(
                    _json,
                    textureIndex,
                    out var wrapS,
                    out var wrapT,
                    out var magFilter,
                    out _))
            {
                texture.wrapModeU = TextureWrapMode.Repeat;
                texture.wrapModeV = TextureWrapMode.Repeat;
                texture.filterMode = FilterMode.Bilinear;
                return;
            }

            texture.wrapModeU = ToUnityWrap(wrapS);
            texture.wrapModeV = ToUnityWrap(wrapT);
            texture.filterMode = ToUnityFilter(magFilter);
        }

        private static TextureWrapMode ToUnityWrap(int gltfWrap)
        {
            switch (gltfWrap)
            {
                case GltfImageBytes.WrapClampToEdge:
                    return TextureWrapMode.Clamp;
                case GltfImageBytes.WrapMirroredRepeat:
                    return TextureWrapMode.Mirror;
                case GltfImageBytes.WrapRepeat:
                default:
                    return TextureWrapMode.Repeat;
            }
        }

        private static FilterMode ToUnityFilter(int gltfMagFilter)
        {
            return gltfMagFilter == GltfImageBytes.FilterNearest
                ? FilterMode.Point
                : FilterMode.Bilinear;
        }

        public Func<int, Texture> AsResolver()
        {
            return Get;
        }

        /// <summary>
        /// Stop owning cached textures (e.g. after they are saved as prefab sub-assets).
        /// </summary>
        public void ReleaseOwnership()
        {
            _cache.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var kv in _cache)
            {
                var texture = kv.Value;
                if (texture == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            _cache.Clear();
        }
    }
}
