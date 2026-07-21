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
        public int Node;
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

        /// <summary>Sprite width (meters). World-space; does not inherit node scale.</summary>
        public float SizeX = VrmxtVfx.DefaultSize[0];

        /// <summary>Sprite height (meters). World-space; does not inherit node scale.</summary>
        public float SizeY = VrmxtVfx.DefaultSize[1];

        public float StartSpeed = VrmxtVfx.DefaultStartSpeed;
        public Color Color = UnityEngine.Color.white;
    }
}
