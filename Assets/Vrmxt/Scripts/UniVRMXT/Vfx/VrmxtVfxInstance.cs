using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Runtime holder for portable <c>VRMXT_sprite_particle</c> emitters on a loaded avatar root.
    /// Optional <see cref="BuildParticleSystems"/> maps fields onto Unity
    /// <see cref="ParticleSystem"/> children under each resolved node.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class VrmxtVfxInstance : MonoBehaviour
    {
        private const double EmitterPullSuppressSeconds = 0.25;

        [SerializeField]
        private List<VrmxtVfxResolvedEmitter> emitters = new();

        [SerializeField]
        private List<ParticleSystem> particleSystems = new();

        [NonSerialized]
        private double _suppressEmitterPullUntil;

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
        /// unresolved indices use the solid tint fallback (<see cref="VrmxtVfxParticleData.Color"/>).
        /// </summary>
        public void BuildParticleSystems(Func<int, Texture> resolveTexture = null)
        {
            ClearParticleSystems();
            BindTexturesFromResolver(emitters, resolveTexture);
            particleSystems.AddRange(
                VrmxtVfxParticleSystemMapper.CreateAll(emitters, resolveTexture));
            SuppressEmitterPull();
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

        /// <param name="destroyOwnedMaterials">
        /// When false (export copy path), do not <c>DestroyImmediate</c> particle materials —
        /// <see cref="GameObject.Instantiate"/> may still share them with the scene original.
        /// </param>
        public void ClearParticleSystems(bool destroyOwnedMaterials = true)
        {
            for (var i = 0; i < particleSystems.Count; i++)
            {
                var particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                if (destroyOwnedMaterials)
                {
                    DestroyOwnedMaterial(particleSystem);
                }
                else
                {
                    // Detach marker + slot so OnDestroy cannot DestroyImmediate a shared mat.
                    var marker = particleSystem.GetComponent<VrmxtVfxOwnedParticleMaterial>();
                    if (marker != null)
                    {
                        DestroyOwnedObject(marker);
                    }

                    var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = null;
                    }
                }

                DestroyOwned(particleSystem.gameObject);
            }

            particleSystems.Clear();
        }

        /// <summary>
        /// Push emitter portable fields onto existing preview <see cref="ParticleSystem"/>
        /// children. Does not create or destroy systems.
        /// </summary>
        public void SyncParticleSystemsFromEmitters()
        {
            SuppressEmitterPull();

            for (var i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter?.Particle == null)
                {
                    continue;
                }

                var particleSystem = FindParticleSystemChild(emitter);
                if (particleSystem == null)
                {
                    continue;
                }

                VrmxtVfxParticleSystemMapper.Apply(
                    particleSystem,
                    emitter.Particle,
                    emitter.Particle.Texture);
            }
        }

        /// <summary>
        /// Pull live preview <see cref="ParticleSystem"/> values back into emitter fields
        /// (same path export uses). Skips briefly after Instance→PS push to avoid a loop.
        /// </summary>
        public void SyncEmittersFromParticleSystems()
        {
            if (Time.realtimeSinceStartupAsDouble < _suppressEmitterPullUntil)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter == null)
                {
                    continue;
                }

                var particleSystem = FindParticleSystemChild(emitter);
                if (particleSystem == null)
                {
                    continue;
                }

                if (!EmitterDiffersFromParticleSystem(emitter, particleSystem))
                {
                    continue;
                }

                VrmxtVfxParticleSystemMapper.ReadFromParticleSystem(particleSystem, emitter);
                changed = true;
            }

            if (changed)
            {
                // Avoid immediately pushing the same values back through OnValidate Apply.
                SuppressEmitterPull();
            }
        }

        private void Update()
        {
            // Edit-mode + play: watch preview PS inspector edits → Instance fields.
            SyncEmittersFromParticleSystems();
        }

        private void OnValidate()
        {
            SyncParticleSystemsFromEmitters();
        }

        private void OnDestroy()
        {
            ClearParticleSystems();
        }

        private void SuppressEmitterPull()
        {
            _suppressEmitterPullUntil =
                Time.realtimeSinceStartupAsDouble + EmitterPullSuppressSeconds;
        }

        private static bool EmitterDiffersFromParticleSystem(
            VrmxtVfxResolvedEmitter emitter,
            ParticleSystem particleSystem)
        {
            emitter.Particle ??= new VrmxtVfxParticleData();
            var probe = new VrmxtVfxResolvedEmitter { Particle = new VrmxtVfxParticleData() };
            VrmxtVfxParticleSystemMapper.ReadFromParticleSystem(particleSystem, probe);

            var a = emitter.Particle;
            var b = probe.Particle;
            if (!Mathf.Approximately(a.EmissionRate, b.EmissionRate) ||
                a.MaxParticles != b.MaxParticles ||
                !Mathf.Approximately(a.Lifetime, b.Lifetime) ||
                !Mathf.Approximately(a.SizeX, b.SizeX) ||
                !Mathf.Approximately(a.SizeY, b.SizeY) ||
                !Mathf.Approximately(a.StartSpeed, b.StartSpeed) ||
                !ColorsApproximatelyEqual(a.Color, b.Color))
            {
                return true;
            }

            return false;
        }

        private static bool ColorsApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) &&
                   Mathf.Approximately(a.g, b.g) &&
                   Mathf.Approximately(a.b, b.b) &&
                   Mathf.Approximately(a.a, b.a);
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
        public int Node;
        public Transform NodeTransform;
        public VrmxtVfxParticleData Particle = new();
    }
}
