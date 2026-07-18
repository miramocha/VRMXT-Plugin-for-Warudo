using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Runtime holder for portable <c>VRMXT_vfx</c> emitters on a loaded avatar root.
    /// Optional <see cref="BuildParticleSystems"/> maps fields onto Unity
    /// <see cref="ParticleSystem"/> children under each resolved node.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmxtVfxInstance : MonoBehaviour
    {
        [SerializeField]
        private List<VrmxtVfxResolvedEmitter> emitters = new();

        [SerializeField]
        private List<ParticleSystem> particleSystems = new();

        public IReadOnlyList<VrmxtVfxResolvedEmitter> Emitters => emitters;

        public IReadOnlyList<ParticleSystem> ParticleSystems => particleSystems;

        public void SetEmitters(IEnumerable<VrmxtVfxResolvedEmitter> values)
        {
            ClearParticleSystems();
            emitters.Clear();
            if (values == null)
            {
                return;
            }

            emitters.AddRange(values);
        }

        /// <summary>
        /// Spawn <see cref="ParticleSystem"/> children for current emitters.
        /// <paramref name="resolveTexture"/> maps glTF <c>textures[]</c> indices; null or
        /// unresolved indices use the solid tint fallback (<see cref="VrmxtVfxParticleData.StartColor"/>).
        /// </summary>
        public void BuildParticleSystems(Func<int, Texture> resolveTexture = null)
        {
            ClearParticleSystems();
            BindTexturesFromResolver(emitters, resolveTexture);
            particleSystems.AddRange(
                VrmxtVfxParticleSystemMapper.CreateAll(emitters, resolveTexture));
        }

        /// <summary>
        /// Store resolved Unity textures on emitters so export can re-embed them even after
        /// preview <see cref="ParticleSystem"/> children are cleared.
        /// </summary>
        public static void BindTexturesFromResolver(
            IReadOnlyList<VrmxtVfxResolvedEmitter> emitters,
            Func<int, Texture> resolveTexture)
        {
            if (emitters == null || resolveTexture == null)
            {
                return;
            }

            for (var i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter?.Particle == null || !emitter.Particle.HasTexture)
                {
                    continue;
                }

                if (emitter.Particle.Texture != null)
                {
                    continue;
                }

                var texture = resolveTexture(emitter.Particle.TextureIndex);
                if (texture != null)
                {
                    emitter.Particle.Texture = texture;
                }
            }
        }

        /// <summary>
        /// Copy albedo from live owned particle materials onto
        /// <see cref="VrmxtVfxParticleData.Texture"/> (import persist / pre-export).
        /// </summary>
        public void SyncTexturesFromParticleMaterials()
        {
            for (var i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter?.Particle == null)
                {
                    continue;
                }

                if (emitter.Particle.Texture != null)
                {
                    continue;
                }

                var particleSystem = FindParticleSystemChild(emitter);
                if (particleSystem == null)
                {
                    continue;
                }

                var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                var texture = VrmxtVfxParticleSystemMapper.ReadAssignedTexture(
                    renderer != null ? renderer.sharedMaterial : null);
                if (texture == null)
                {
                    continue;
                }

                emitter.Particle.Texture = texture;
                emitter.Particle.HasTexture = true;
            }
        }

        private ParticleSystem FindParticleSystemChild(VrmxtVfxResolvedEmitter emitter)
        {
            if (emitter.NodeTransform == null)
            {
                return null;
            }

            var expectedName = VrmxtVfxParticleSystemMapper.BuildObjectName(emitter);
            for (var i = 0; i < emitter.NodeTransform.childCount; i++)
            {
                var child = emitter.NodeTransform.GetChild(i);
                if (child == null || child.name != expectedName)
                {
                    continue;
                }

                var particleSystem = child.GetComponent<ParticleSystem>();
                if (particleSystem != null)
                {
                    return particleSystem;
                }
            }

            for (var i = 0; i < particleSystems.Count; i++)
            {
                var particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                if (particleSystem.transform.parent == emitter.NodeTransform &&
                    particleSystem.gameObject.name == expectedName)
                {
                    return particleSystem;
                }
            }

            return null;
        }

        public void ClearParticleSystems()
        {
            for (var i = 0; i < particleSystems.Count; i++)
            {
                var particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                DestroyOwnedMaterial(particleSystem);
                DestroyOwned(particleSystem.gameObject);
            }

            particleSystems.Clear();
        }

        private void OnDestroy()
        {
            ClearParticleSystems();
        }

        private static void DestroyOwnedMaterial(ParticleSystem particleSystem)
        {
            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                return;
            }

            var material = renderer.sharedMaterial;
            if (!VrmxtVfxParticleSystemMapper.IsOwnedParticleMaterial(material))
            {
                return;
            }

            renderer.sharedMaterial = null;
            DestroyOwnedObject(material);
        }

        private static void DestroyOwned(GameObject go)
        {
            DestroyOwnedObject(go);
        }

        private static void DestroyOwnedObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }

    [Serializable]
    public sealed class VrmxtVfxResolvedEmitter
    {
        public string Name;
        public string Type = "particle";
        public int Node;
        public Transform NodeTransform;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public VrmxtVfxParticleData Particle = new();
    }
}
