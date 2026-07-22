using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    public static class VrmxtVfxImporter
    {
        /// <summary>
        /// Parse extension JSON and keep emitters whose node resolves to a non-null name.
        /// Prefer <see cref="TryImport(string,IReadOnlyList{Transform},out List{VrmxtVfxResolvedEmitter})"/>
        /// for runtime attach.
        /// </summary>
        public static bool TryImport(
            string json,
            Func<int, string> resolveNode,
            out VrmxtVfxData data)
        {
            data = null;
            if (!VrmxtVfx.TryParse(json, out var extension))
            {
                return false;
            }

            var textureCount = TryGetTextureCount(json);
            var emitters = new List<VrmxtVfxEmitterData>();
            foreach (var emitter in extension.Emitters)
            {
                if (!IsTextureStructurallyValid(emitter, textureCount))
                {
                    continue;
                }

                if (!IsNodeNameResolved(emitter.Node, resolveNode))
                {
                    continue;
                }

                emitters.Add(ToEmitterData(emitter));
            }

            data = ScriptableObject.CreateInstance<VrmxtVfxData>();
            data.SetEmitters(emitters);
            return true;
        }

        public static bool TryImport(
            string json,
            IReadOnlyList<Transform> nodes,
            out List<VrmxtVfxResolvedEmitter> emitters)
        {
            emitters = null;
            if (nodes == null)
            {
                return false;
            }

            return TryImport(json, index => ResolveFromList(nodes, index), out emitters);
        }

        public static bool TryImport(
            string json,
            Func<int, Transform> resolveNode,
            out List<VrmxtVfxResolvedEmitter> emitters)
        {
            emitters = null;
            if (!VrmxtVfx.TryParse(json, out var extension))
            {
                return false;
            }

            var textureCount = TryGetTextureCount(json);
            var resolved = new List<VrmxtVfxResolvedEmitter>();
            foreach (var emitter in extension.Emitters)
            {
                if (!IsTextureStructurallyValid(emitter, textureCount))
                {
                    continue;
                }

                var nodeTransform = resolveNode?.Invoke(emitter.Node);
                if (nodeTransform == null)
                {
                    continue;
                }

                resolved.Add(ToResolvedEmitter(emitter, nodeTransform));
            }

            emitters = resolved;
            return true;
        }

        /// <summary>
        /// Read <c>textures[].Length</c> from a full glTF document.
        /// Returns null for bare extension JSON (unit tests / already-extracted objects)
        /// where range checks are deferred.
        /// </summary>
        public static int? TryGetTextureCount(string gltfJson)
        {
            if (string.IsNullOrWhiteSpace(gltfJson))
            {
                return null;
            }

            try
            {
                var root = Newtonsoft.Json.Linq.JToken.Parse(gltfJson) as Newtonsoft.Json.Linq.JObject;
                if (root == null)
                {
                    return null;
                }

                if (root.TryGetValue("textures", StringComparison.Ordinal, out var texturesToken) &&
                    texturesToken is Newtonsoft.Json.Linq.JArray textures)
                {
                    return textures.Count;
                }

                // Full glTF without textures[] → any texture index is out of range.
                if (root.ContainsKey("asset") || root.ContainsKey("nodes"))
                {
                    return 0;
                }

                // Bare extension object — range unknown.
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Structurally invalid texture index (out of <c>textures[]</c>) skips the emitter.
        /// When texture count is unknown, accept non-negative indices (decode failure → white
        /// solid fallback at runtime).
        /// </summary>
        public static bool IsTextureStructurallyValid(VrmxtVfxEmitter emitter, int? textureCount)
        {
            if (emitter == null)
            {
                return false;
            }

            if (!emitter.Texture.HasValue)
            {
                return true;
            }

            var index = emitter.Texture.Value;
            if (index < 0)
            {
                return false;
            }

            if (!textureCount.HasValue)
            {
                return true;
            }

            return index < textureCount.Value;
        }

        private static Transform ResolveFromList(IReadOnlyList<Transform> nodes, int index)
        {
            if (index < 0 || index >= nodes.Count)
            {
                return null;
            }

            return nodes[index];
        }

        private static bool IsNodeNameResolved(int nodeIndex, Func<int, string> resolveNode)
        {
            if (resolveNode == null)
            {
                return false;
            }

            var nodeName = resolveNode(nodeIndex);
            return !string.IsNullOrEmpty(nodeName);
        }

        private static VrmxtVfxEmitterData ToEmitterData(VrmxtVfxEmitter emitter)
        {
            return new VrmxtVfxEmitterData
            {
                Name = emitter.Name,
                Node = emitter.Node,
                Particle = ToParticleData(emitter),
            };
        }

        private static VrmxtVfxResolvedEmitter ToResolvedEmitter(
            VrmxtVfxEmitter emitter,
            Transform nodeTransform)
        {
            return new VrmxtVfxResolvedEmitter
            {
                Name = emitter.Name,
                Node = emitter.Node,
                NodeTransform = nodeTransform,
                Particle = ToParticleData(emitter),
            };
        }

        private static VrmxtVfxParticleData ToParticleData(VrmxtVfxEmitter emitter)
        {
            var color = emitter.Color;
            var size = emitter.Size;
            return new VrmxtVfxParticleData
            {
                HasTexture = emitter.Texture.HasValue,
                TextureIndex = emitter.Texture ?? -1,
                EmissionRate = emitter.EmissionRate,
                MaxParticles = emitter.MaxParticles,
                Lifetime = emitter.Lifetime,
                SizeX = size[0],
                SizeY = size[1],
                StartSpeed = emitter.StartSpeed,
                Color = new Color(color[0], color[1], color[2], color[3]),
            };
        }
    }
}
