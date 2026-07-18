using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    [CreateAssetMenu(fileName = "VrmxtVfxData", menuName = "UniVRMXT/VFX Data")]
    public sealed class VrmxtVfxData : ScriptableObject
    {
        [SerializeField]
        private List<VrmxtVfxEmitterData> emitters = new();

        public IReadOnlyList<VrmxtVfxEmitterData> Emitters => emitters;

        public void SetEmitters(IEnumerable<VrmxtVfxEmitterData> values)
        {
            emitters.Clear();
            if (values == null)
            {
                return;
            }

            emitters.AddRange(values);
        }
    }

    [Serializable]
    public sealed class VrmxtVfxEmitterData
    {
        public string Name;
        public string Type = "particle";
        public int Node;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public VrmxtVfxParticleData Particle = new();
    }

    [Serializable]
    public sealed class VrmxtVfxParticleData
    {
        public bool HasTexture;
        public int TextureIndex;

        /// <summary>
        /// Optional Unity texture for re-export. Set when building ParticleSystems or
        /// when import persists albedo onto the owned material.
        /// </summary>
        public Texture Texture;

        public float EmissionRate = VrmxtVfx.DefaultEmissionRate;
        public int MaxParticles = VrmxtVfx.DefaultMaxParticles;
        public float Lifetime = VrmxtVfx.DefaultLifetime;
        public float StartSize = VrmxtVfx.DefaultStartSize;
        public float StartSpeed = VrmxtVfx.DefaultStartSpeed;
        public Color StartColor = Color.white;
    }
}
