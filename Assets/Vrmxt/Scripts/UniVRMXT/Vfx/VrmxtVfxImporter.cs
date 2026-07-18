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

            var emitters = new List<VrmxtVfxEmitterData>();
            foreach (var emitter in extension.Emitters)
            {
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

            var resolved = new List<VrmxtVfxResolvedEmitter>();
            foreach (var emitter in extension.Emitters)
            {
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
                Type = emitter.Type,
                Node = emitter.Node,
                LocalPosition = ToVector3(emitter.LocalPosition),
                LocalRotation = ToQuaternion(emitter.LocalRotation),
                Particle = ToParticleData(emitter.Particle),
            };
        }

        private static VrmxtVfxResolvedEmitter ToResolvedEmitter(
            VrmxtVfxEmitter emitter,
            Transform nodeTransform)
        {
            return new VrmxtVfxResolvedEmitter
            {
                Name = emitter.Name,
                Type = emitter.Type,
                Node = emitter.Node,
                NodeTransform = nodeTransform,
                LocalPosition = ToVector3(emitter.LocalPosition),
                LocalRotation = ToQuaternion(emitter.LocalRotation),
                Particle = ToParticleData(emitter.Particle),
            };
        }

        private static VrmxtVfxParticleData ToParticleData(VrmxtVfxParticle particle)
        {
            var startColor = particle.StartColor;
            return new VrmxtVfxParticleData
            {
                HasTexture = particle.Texture.HasValue,
                TextureIndex = particle.Texture ?? -1,
                EmissionRate = particle.EmissionRate,
                MaxParticles = particle.MaxParticles,
                Lifetime = particle.Lifetime,
                StartSize = particle.StartSize,
                StartSpeed = particle.StartSpeed,
                StartColor = new Color(
                    startColor[0],
                    startColor[1],
                    startColor[2],
                    startColor[3]),
            };
        }

        private static Vector3 ToVector3(IReadOnlyList<float> values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        private static Quaternion ToQuaternion(IReadOnlyList<float> values)
        {
            return new Quaternion(values[0], values[1], values[2], values[3]);
        }
    }
}
