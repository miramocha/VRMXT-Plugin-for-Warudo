using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Maps portable <c>VRMXT_sprite_particle</c> fields onto Unity
    /// <see cref="ParticleSystem"/>.
    /// </summary>
    public static class VrmxtVfxParticleSystemMapper
    {
        public const string EmitterObjectNamePrefix = "VRMXT_sprite_particle_";
        public const string OwnedMaterialNamePrefix = "VRMXT_sprite_particle_Particle";

        /// <summary>ShaderLab name of the packaged first-party particle shader.</summary>
        public const string PackagedShaderName = "VRMXT/Particles Unlit";

        /// <summary>
        /// <see cref="Resources.Load{T}(string)"/> path for the packaged particle material
        /// (<c>Runtime/Resources/UniVRMXT/ParticlesUnlit.mat</c>). Keeps the shader in builds.
        /// </summary>
        public const string PackagedMaterialResourcesPath = "UniVRMXT/ParticlesUnlit";

        /// <summary>
        /// Optional host override for the packaged particle material template (e.g. Warudo
        /// <c>ModHost.LoadAsset</c>). When null, uses <see cref="Resources.Load{T}(string)"/>.
        /// </summary>
        public static Func<Material> PackagedMaterialProvider { get; set; }

        /// <summary>
        /// When true, clone the packaged material before probing host
        /// <see cref="Shader.Find"/> names. Use for hosts where Unity
        /// <c>Resources.Load</c> cannot see mod assets (Warudo/UMod).
        /// </summary>
        public static bool PreferPackagedParticleMaterial { get; set; }

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
        /// identity local transform, then configure a <see cref="ParticleSystem"/>.
        /// </summary>
        /// <param name="texture">
        /// Optional glTF texture. When null, use the pipeline particle material tinted by
        /// <see cref="VrmxtVfxParticleData.Color"/>.
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
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
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
            ApplyWorldSpaceSize(main, particleSystem.transform, particle.SizeX, particle.SizeY);
            // Velocity comes from VelocityOverLifetime along local +Y (spec).
            main.startSpeed = 0f;
            main.startColor = particle.Color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            // Local scaling: sizes are world meters on an identity-scale PS child and do not
            // track the referenced node's live hierarchy scale (spec).
            main.scalingMode = ParticleSystemScalingMode.Local;

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
        /// Map world-space sprite width/height (meters) onto <see cref="ParticleSystem.MainModule"/>.
        /// Callers must use <see cref="ParticleSystemScalingMode.Local"/> on an identity-scale
        /// particle child so dimensions do not inherit the referenced node scale.
        /// </summary>
        public static void ApplyWorldSpaceSize(
            ParticleSystem.MainModule main,
            Transform particleTransform,
            float worldWidth,
            float worldHeight)
        {
            // particleTransform retained for call-site clarity / future guards; Local scaling
            // means start sizes are not multiplied by parent lossyScale.
            _ = particleTransform;

            main.startSize3D = true;
            main.startSizeX = Mathf.Max(worldWidth, 1e-6f);
            main.startSizeY = Mathf.Max(worldHeight, 1e-6f);
            main.startSizeZ = 1f;
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
            ReadWorldSpaceSize(main, particleSystem.transform, out particle.SizeX, out particle.SizeY);
            particle.Color = ReadStartColor(main.startColor);

            var emission = particleSystem.emission;
            particle.EmissionRate = ReadCurveConstant(emission.rateOverTime);

            var velocity = particleSystem.velocityOverLifetime;
            particle.StartSpeed = ReadCurveConstant(velocity.y);
        }

        public static void ReadWorldSpaceSize(
            ParticleSystem.MainModule main,
            Transform particleTransform,
            out float worldWidth,
            out float worldHeight)
        {
            _ = particleTransform;

            if (main.startSize3D)
            {
                worldWidth = ReadCurveConstant(main.startSizeX);
                worldHeight = ReadCurveConstant(main.startSizeY);
                return;
            }

            var uniform = ReadCurveConstant(main.startSize);
            worldWidth = uniform;
            worldHeight = uniform;
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
        /// </summary>
        public static Shader ResolveParticleShader()
        {
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

        public static void ConfigureTransparentAlphaBlending(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty(SurfaceId))
            {
                material.SetFloat(SurfaceId, 1f);
            }

            if (material.HasProperty(BlendId))
            {
                material.SetFloat(BlendId, 0f);
            }

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

            if (PreferPackagedParticleMaterial)
            {
                material = ClonePackagedMaterial();
            }

            if (material == null)
            {
                var shader = ResolveParticleShader();
                if (IsUsableShader(shader))
                {
                    material = new Material(shader) { name = OwnedMaterialNamePrefix };
                }
            }

            if (material == null)
            {
                material = ClonePackagedMaterial();
            }

            if (material == null)
            {
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

        private static Material ClonePackagedMaterial()
        {
            var packaged = TryGetPackagedMaterialTemplate();
            if (!IsUsableMaterial(packaged))
            {
                return null;
            }

            return new Material(packaged) { name = OwnedMaterialNamePrefix };
        }

        private static Material TryGetPackagedMaterialTemplate()
        {
            if (PackagedMaterialProvider != null)
            {
                try
                {
                    var provided = PackagedMaterialProvider();
                    if (IsUsableMaterial(provided))
                    {
                        return provided;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("UniVRMXT: PackagedMaterialProvider failed: " + e.Message);
                }
            }

            return Resources.Load<Material>(PackagedMaterialResourcesPath);
        }

        private static Material TryGetBuiltinParticleMaterial()
        {
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
