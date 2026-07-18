using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Maps portable <c>VRMXT_vfx</c> particle fields onto Unity <see cref="ParticleSystem"/>.
    /// See <c>docs/vfx-particle-mapping.md</c> for the field table.
    /// </summary>
    public static class VrmxtVfxParticleSystemMapper
    {
        public const string EmitterObjectNamePrefix = "VRMXT_vfx_";
        public const string OwnedMaterialNamePrefix = "VRMXT_vfx_Particle";

        /// <summary>ShaderLab name of the packaged first-party particle shader.</summary>
        public const string PackagedShaderName = "UniVRMXT/Particles Unlit";

        /// <summary>
        /// <see cref="Resources.Load{T}(string)"/> path for the packaged particle material
        /// (<c>Runtime/Resources/UniVRMXT/ParticlesUnlit.mat</c>). Keeps the shader in builds.
        /// </summary>
        public const string PackagedMaterialResourcesPath = "UniVRMXT/ParticlesUnlit";

        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>
        /// Create a child under <see cref="VrmxtVfxResolvedEmitter.NodeTransform"/> with
        /// emitter local TR, then configure a <see cref="ParticleSystem"/>.
        /// </summary>
        /// <param name="texture">
        /// Optional glTF texture. When null, use the pipeline particle material tinted by
        /// <see cref="VrmxtVfxParticleData.StartColor"/>.
        /// </param>
        public static ParticleSystem Create(
            VrmxtVfxResolvedEmitter emitter,
            Texture texture = null)
        {
            if (emitter == null)
            {
                throw new ArgumentNullException(nameof(emitter));
            }

            if (emitter.NodeTransform == null)
            {
                throw new ArgumentException("Emitter NodeTransform is null.", nameof(emitter));
            }

            var go = new GameObject(BuildObjectName(emitter));
            var transform = go.transform;
            transform.SetParent(emitter.NodeTransform, false);
            transform.localPosition = emitter.LocalPosition;
            transform.localRotation = emitter.LocalRotation;
            transform.localScale = Vector3.one;

            var particleSystem = go.AddComponent<ParticleSystem>();
            Apply(particleSystem, emitter.Particle, texture);
            if (texture != null && emitter.Particle != null)
            {
                emitter.Particle.Texture = texture;
                emitter.Particle.HasTexture = true;
            }

            return particleSystem;
        }

        public static List<ParticleSystem> CreateAll(
            IReadOnlyList<VrmxtVfxResolvedEmitter> emitters,
            Func<int, Texture> resolveTexture = null)
        {
            var created = new List<ParticleSystem>();
            if (emitters == null)
            {
                return created;
            }

            for (var i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (emitter?.NodeTransform == null)
                {
                    continue;
                }

                var texture = ResolveTexture(emitter.Particle, resolveTexture);
                created.Add(Create(emitter, texture));
            }

            return created;
        }

        public static void Apply(
            ParticleSystem particleSystem,
            VrmxtVfxParticleData particle,
            Texture texture = null)
        {
            if (particleSystem == null)
            {
                throw new ArgumentNullException(nameof(particleSystem));
            }

            if (particle == null)
            {
                throw new ArgumentNullException(nameof(particle));
            }

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = Mathf.Max(1, particle.MaxParticles);
            main.startLifetime = particle.Lifetime;
            main.startSize = particle.StartSize;
            // Velocity comes from VelocityOverLifetime along local +Y (spec).
            main.startSpeed = 0f;
            main.startColor = particle.StartColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = particle.EmissionRate;

            var shape = particleSystem.shape;
            shape.enabled = false;

            var velocity = particleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(particle.StartSpeed);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            ApplyMaterial(renderer, texture);

            // Stop() above clears playOnAwake start; must Play after configure.
            particleSystem.Play(true);
        }

        /// <summary>
        /// Copy live <see cref="ParticleSystem"/> preview values back into portable
        /// <paramref name="emitter"/> fields (for Unity inspector edits before export).
        /// </summary>
        public static void ReadFromParticleSystem(
            ParticleSystem particleSystem,
            VrmxtVfxResolvedEmitter emitter)
        {
            if (particleSystem == null || emitter == null)
            {
                return;
            }

            emitter.Particle ??= new VrmxtVfxParticleData();
            ReadInto(particleSystem, emitter.Particle);

            var transform = particleSystem.transform;
            emitter.LocalPosition = transform.localPosition;
            emitter.LocalRotation = transform.localRotation;
        }

        /// <summary>
        /// Reverse of <see cref="Apply"/> for portable particle scalars.
        /// </summary>
        public static void ReadInto(ParticleSystem particleSystem, VrmxtVfxParticleData particle)
        {
            if (particleSystem == null)
            {
                throw new ArgumentNullException(nameof(particleSystem));
            }

            if (particle == null)
            {
                throw new ArgumentNullException(nameof(particle));
            }

            var main = particleSystem.main;
            particle.MaxParticles = Mathf.Max(1, main.maxParticles);
            particle.Lifetime = ReadCurveConstant(main.startLifetime);
            particle.StartSize = ReadCurveConstant(main.startSize);
            particle.StartColor = ReadStartColor(main.startColor);

            var emission = particleSystem.emission;
            particle.EmissionRate = ReadCurveConstant(emission.rateOverTime);

            var velocity = particleSystem.velocityOverLifetime;
            particle.StartSpeed = ReadCurveConstant(velocity.y);
        }

        public static Color ReadStartColor(ParticleSystem.MinMaxGradient gradient)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return gradient.color;
                case ParticleSystemGradientMode.TwoColors:
                    return Color.Lerp(gradient.colorMin, gradient.colorMax, 0.5f);
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.TwoGradients:
                    return gradient.Evaluate(0f);
                default:
                    return gradient.color;
            }
        }

        public static float ReadCurveConstant(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return (curve.constantMin + curve.constantMax) * 0.5f;
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    return curve.Evaluate(0f);
                default:
                    return curve.constant;
            }
        }

        public static string BuildObjectName(VrmxtVfxResolvedEmitter emitter)
        {
            if (emitter == null)
            {
                return EmitterObjectNamePrefix + "emitter";
            }

            if (!string.IsNullOrEmpty(emitter.Name))
            {
                return EmitterObjectNamePrefix + emitter.Name;
            }

            return EmitterObjectNamePrefix + emitter.Node;
        }

        public static bool IsOwnedParticleMaterial(Material material)
        {
            return material != null &&
                   material.name.StartsWith(OwnedMaterialNamePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Pick an unlit particle shader for the active pipeline (BIRP or URP).
        /// Always tries both host URP and BIRP particle shader names before the packaged
        /// <see cref="PackagedShaderName"/> — during ScriptedImporter / early boot
        /// <see cref="GraphicsSettings.currentRenderPipeline"/> is often null even in URP
        /// projects, so gating on that alone can persist a Built-in CG material that pinks
        /// under URP at runtime. Pipeline null/non-null only sets search <em>order</em>.
        /// If all <see cref="Shader.Find"/> calls miss, <see cref="CreateOwnedParticleMaterial"/>
        /// clones the Resources material (keeps packaged shader in builds).
        /// </summary>
        public static Shader ResolveParticleShader()
        {
            // Prefer likely pipeline first, but always probe both before packaged.
            Shader preferred;
            Shader secondary;
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                preferred = FindFirstShader(
                    "Particles/Standard Unlit",
                    "Particles/Standard Surface",
                    "Legacy Shaders/Particles/Alpha Blended Premultiply",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Mobile/Particles/Alpha Blended",
                    "Particles/Alpha Blended");
                secondary = FindFirstShader(
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Particles/Simple Lit");
            }
            else
            {
                preferred = FindFirstShader(
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Particles/Simple Lit");
                secondary = FindFirstShader(
                    "Particles/Standard Unlit",
                    "Particles/Standard Surface",
                    "Legacy Shaders/Particles/Alpha Blended Premultiply",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Mobile/Particles/Alpha Blended",
                    "Particles/Alpha Blended");
            }

            if (preferred != null)
            {
                return preferred;
            }

            if (secondary != null)
            {
                return secondary;
            }

            var packaged = FindFirstShader(PackagedShaderName);
            if (packaged != null)
            {
                return packaged;
            }

            return FindFirstShader(
                "Sprites/Default",
                "Unlit/Transparent",
                "Unlit/Color",
                "UI/Default");
        }

        /// <summary>
        /// Assign texture to BIRP (<c>_MainTex</c>) and URP (<c>_BaseMap</c>) slots when present.
        /// </summary>
        public static void ApplyTextureToMaterial(Material material, Texture texture)
        {
            if (material == null || texture == null)
            {
                return;
            }

            if (material.HasProperty(MainTexId))
            {
                material.SetTexture(MainTexId, texture);
            }

            if (material.HasProperty(BaseMapId))
            {
                material.SetTexture(BaseMapId, texture);
            }

            material.mainTexture = texture;
        }

        public static Texture ReadAssignedTexture(Material material)
        {
            if (material == null)
            {
                return null;
            }

            if (material.HasProperty(BaseMapId))
            {
                var baseMap = material.GetTexture(BaseMapId);
                if (baseMap != null)
                {
                    return baseMap;
                }
            }

            if (material.HasProperty(MainTexId))
            {
                var mainTex = material.GetTexture(MainTexId);
                if (mainTex != null)
                {
                    return mainTex;
                }
            }

            return material.mainTexture;
        }

        /// <summary>
        /// Configure blend state so texture / particle alpha is visible.
        /// URP <c>Particles/Unlit</c> defaults to Opaque when created from script.
        /// Texture decode already keeps PNG alpha; this is a material issue, not import.
        /// </summary>
        public static void ConfigureTransparentAlphaBlending(Material material)
        {
            if (material == null)
            {
                return;
            }

            // URP Lit/Particles Unlit surface type: 0 Opaque, 1 Transparent.
            if (material.HasProperty(SurfaceId))
            {
                material.SetFloat(SurfaceId, 1f);
            }

            // URP blend: 0 Alpha, 1 Premultiply, 2 Additive, 3 Multiply.
            if (material.HasProperty(BlendId))
            {
                material.SetFloat(BlendId, 0f);
            }

            // Built-in Particles/Standard Unlit rendering mode: 0 Opaque … 2 Fade, 3 Transparent.
            if (material.HasProperty(ModeId))
            {
                material.SetFloat(ModeId, 2f);
            }

            if (material.HasProperty(SrcBlendId))
            {
                material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty(DstBlendId))
            {
                material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty(ZWriteId))
            {
                material.SetFloat(ZWriteId, 0f);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            // Keep material tint white so ParticleSystem.startColor × texture alpha drives look.
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, Color.white);
            }

            if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, Color.white);
            }
        }

        private static void ApplyMaterial(ParticleSystemRenderer renderer, Texture texture)
        {
            if (renderer == null)
            {
                return;
            }

            var material = CreateOwnedParticleMaterial(renderer);
            if (material == null)
            {
                Debug.LogWarning(
                    "UniVRMXT: could not resolve a particle shader/material; particles may render pink.");
                return;
            }

            renderer.sharedMaterial = material;
            if (renderer.GetComponent<VrmxtVfxOwnedParticleMaterial>() == null)
            {
                renderer.gameObject.AddComponent<VrmxtVfxOwnedParticleMaterial>();
            }

            ApplyTextureToMaterial(material, texture);
        }

        private static Material CreateOwnedParticleMaterial(ParticleSystemRenderer renderer)
        {
            Material material = null;
            var shader = ResolveParticleShader();
            if (IsUsableShader(shader))
            {
                material = new Material(shader) { name = OwnedMaterialNamePrefix };
            }

            if (material == null)
            {
                // Resources material references the packaged shader → kept in player builds.
                var packaged = Resources.Load<Material>(PackagedMaterialResourcesPath);
                if (IsUsableMaterial(packaged))
                {
                    material = new Material(packaged) { name = OwnedMaterialNamePrefix };
                }
            }

            if (material == null)
            {
                // ScriptedImporter / early boot: Shader.Find often fails. Clone Unity's default
                // ParticleSystem material (usually Default-Particle) instead of leaving shader null.
                var defaultMaterial = renderer != null ? renderer.sharedMaterial : null;
                if (IsUsableMaterial(defaultMaterial))
                {
                    material = new Material(defaultMaterial) { name = OwnedMaterialNamePrefix };
                }
                else
                {
                    var builtinParticle = TryGetBuiltinParticleMaterial();
                    if (IsUsableMaterial(builtinParticle))
                    {
                        material = new Material(builtinParticle) { name = OwnedMaterialNamePrefix };
                    }
                }
            }

            if (material != null)
            {
                ConfigureTransparentAlphaBlending(material);
            }

            return material;
        }

        private static Material TryGetBuiltinParticleMaterial()
        {
            // May be null outside Editor; ParticleSystem.sharedMaterial clone is the primary fallback.
            return Resources.GetBuiltinResource<Material>("Default-Particle.mat");
        }

        private static Shader FindFirstShader(params string[] names)
        {
            if (names == null)
            {
                return null;
            }

            for (var i = 0; i < names.Length; i++)
            {
                var shader = Shader.Find(names[i]);
                if (IsUsableShader(shader))
                {
                    return shader;
                }
            }

            return null;
        }

        private static bool IsUsableShader(Shader shader)
        {
            return shader != null && shader.name != "Hidden/InternalErrorShader";
        }

        private static bool IsUsableMaterial(Material material)
        {
            return material != null && IsUsableShader(material.shader);
        }

        private static Texture ResolveTexture(
            VrmxtVfxParticleData particle,
            Func<int, Texture> resolveTexture)
        {
            if (particle == null || !particle.HasTexture || resolveTexture == null)
            {
                return null;
            }

            return resolveTexture(particle.TextureIndex);
        }
    }
}
