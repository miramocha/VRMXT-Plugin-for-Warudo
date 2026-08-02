using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UniVRMXT.Format;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Shared apply logic for Editor and Warudo-style hosts. Resolves the <c>unity</c>
    /// override per material, resolves MToon-sourced <c>bindings</c> from the sibling
    /// <c>VRMC_materials_mtoon</c> extension, and writes <c>properties</c> then
    /// <c>bindings</c> onto matching <see cref="Renderer"/> materials (bindings win on
    /// overlap, per base-spec rule 23). When the override shader differs from the live
    /// material shader, texture slots on the target shader that are not listed in
    /// <c>properties</c> or <c>bindings</c> are cleared so stock import textures (e.g.
    /// MToon <c>_MainTex</c>) do not survive a shader swap. Never throws on a
    /// per-material failure — that material is left on stock import.
    /// </summary>
    public static class VrmxtMaterialsOverrideApplier
    {
        /// <summary>
        /// Optional host shader lookup (e.g. Warudo <c>ModHost.Assets.Load</c> cache).
        /// Used when <c>Apply(..., resolveShader)</c> omits a per-call resolver.
        /// uMod / restricted players often have shaders loaded but <see cref="Shader.Find"/>
        /// returns null — same pattern as VFX <c>PackagedMaterialProvider</c>.
        /// </summary>
        public static Func<string, Shader> ShaderResolveProvider { get; set; }

        /// <summary>
        /// Optional host pipeline detection (e.g. Warudo
        /// <c>DetectActivePipelineForWarudo</c>). Used by Transfer / authoring paths that
        /// call <see cref="DetectActivePipeline"/> so hosts can match Apply's RP choice.
        /// </summary>
        public static Func<RenderPipelineVariant> ActivePipelineProvider { get; set; }

        /// <summary>
        /// Attach (if needed) and apply in one call. Prefer the
        /// <see cref="Apply(GameObject,VrmxtMaterialsOverrideInstance,string,RenderPipelineVariant,Func{int,Texture},Func{MaterialProvider,bool},Func{string,Shader})"/>
        /// overload when a <see cref="VrmxtMaterialsOverrideInstance"/> already exists (e.g.
        /// applied later than attach, without keeping the original glTF JSON in memory).
        /// </summary>
        public static int Apply(
            GameObject root,
            string gltfJson,
            RenderPipelineVariant activePipeline,
            Func<int, Texture> resolveTexture = null,
            Func<MaterialProvider, bool> isProviderMismatch = null,
            Func<string, Shader> resolveShader = null
        )
        {
            VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store);
            return Apply(
                root,
                store,
                gltfJson,
                activePipeline,
                resolveTexture,
                isProviderMismatch,
                resolveShader
            );
        }

        /// <summary>
        /// Apply from an existing store. <paramref name="gltfJson"/> is still required to
        /// resolve sibling <c>VRMC_materials_mtoon</c> values for <c>bindings</c> — the
        /// store only keeps the <c>VRMXT_materials_override</c> object itself.
        /// Returns the number of glTF materials that received an override.
        /// </summary>
        public static int Apply(
            GameObject root,
            VrmxtMaterialsOverrideInstance store,
            string gltfJson,
            RenderPipelineVariant activePipeline,
            Func<int, Texture> resolveTexture = null,
            Func<MaterialProvider, bool> isProviderMismatch = null,
            Func<string, Shader> resolveShader = null
        )
        {
            if (root == null || store == null)
            {
                return 0;
            }

            // Prefer caller resolver; else use textures decoded/persisted on the Instance
            // (Editor import hook path after GLB ReleaseOwnership).
            if (resolveTexture == null)
            {
                resolveTexture = index =>
                    store.TryGetImportedTexture(index, out var texture) ? texture : null;
            }

            var gltfRoot = TryParseGltfRoot(gltfJson);

            var applied = 0;
            foreach (var entry in store.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.MaterialName))
                {
                    continue;
                }

                if (!VrmxtMaterialsOverride.TryParse(entry.ExtensionJson, out var extension))
                {
                    continue;
                }

                if (
                    !UnityOverrideSelector.TrySelectUnityEngineOverride(
                        extension,
                        activePipeline,
                        out var engineOverride
                    )
                )
                {
                    continue;
                }

                var unityOverride = engineOverride.Material as UnityMaterialOverride;
                if (unityOverride == null)
                {
                    continue;
                }

                if (ResolveShader(unityOverride.ShaderName, resolveShader) == null)
                {
                    // Shader not present in this build — keep / restore stock import.
                    Debug.LogWarning(
                        $"VRMXT_materials_override: shader '{unityOverride.ShaderName}' unresolved for "
                            + $"material '{entry.MaterialName}'. Leaving stock material."
                    );
                    if (entry.SourceMaterial != null)
                    {
                        VrmxtMaterialsOverrideAuthoring.RestoreSourceMaterial(
                            root,
                            entry.MaterialName,
                            entry.SourceMaterial,
                            destroyPreviewMaterials: true,
                            overrideMaterial: entry.OverrideMaterial,
                            liveAppliedOverride: entry.LiveAppliedOverride
                        );
                    }

                    continue;
                }

                WarnOnProviderMismatch(
                    entry.MaterialName,
                    unityOverride.Provider,
                    isProviderMismatch
                );

                var hasMtoon = TryFindSiblingMtoonForPair(gltfRoot, entry, out var mtoon);

                // Put Source back on slots (drops authoring Override Material assets or
                // leftover DontSave previews) so runtime Apply mutates stock import mats.
                if (entry.SourceMaterial != null)
                {
                    VrmxtMaterialsOverrideAuthoring.RestoreSourceMaterial(
                        root,
                        entry.MaterialName,
                        entry.SourceMaterial,
                        destroyPreviewMaterials: true,
                        overrideMaterial: entry.OverrideMaterial,
                        liveAppliedOverride: entry.LiveAppliedOverride
                    );
                }

                var appliedToAny = false;
                foreach (
                    var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                        root,
                        entry.MaterialName
                    )
                )
                {
                    if (material == null || (material.hideFlags & HideFlags.DontSave) != 0)
                    {
                        continue;
                    }

                    // Runtime / Player: mutate host-built materials in place. Editor
                    // authoring assigns Override Material *assets* onto slots via
                    // VrmxtMaterialsOverrideAuthoring — skip DontSave leftovers here.
                    if (
                        !TryWriteUnityOverrideOntoMaterial(
                            material,
                            engineOverride,
                            hasMtoon,
                            mtoon,
                            resolveTexture,
                            resolveShader
                        )
                    )
                    {
                        continue;
                    }

                    appliedToAny = true;
                }

                if (appliedToAny)
                {
                    applied++;
                }
            }

            return applied;
        }

        /// <summary>
        /// Write the active-pipeline unity override from <paramref name="pair"/> onto
        /// <paramref name="material"/> (shader, properties, bindings, render state).
        /// Does not touch renderers. Returns false when the slot is unselectable or the
        /// shader cannot resolve.
        /// </summary>
        public static bool TryWritePairOverrideOntoMaterial(
            Material material,
            VrmxtMaterialsOverridePair pair,
            string gltfJson,
            RenderPipelineVariant activePipeline,
            Func<int, Texture> resolveTexture = null,
            Func<string, Shader> resolveShader = null
        )
        {
            if (material == null || pair == null)
            {
                return false;
            }

            if (!VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension))
            {
                return false;
            }

            if (
                !UnityOverrideSelector.TrySelectUnityEngineOverride(
                    extension,
                    activePipeline,
                    out var engineOverride
                )
            )
            {
                return false;
            }

            var gltfRoot = TryParseGltfRoot(gltfJson);
            var hasMtoon = TryFindSiblingMtoonForPair(gltfRoot, pair, out var mtoon);
            return TryWriteUnityOverrideOntoMaterial(
                material,
                engineOverride,
                hasMtoon,
                mtoon,
                resolveTexture,
                resolveShader
            );
        }

        /// <summary>
        /// Write a selected unity engine override onto an existing <see cref="Material"/>.
        /// Sets shader, clears unlisted textures when the shader changes, applies
        /// <c>properties</c> then <c>bindings</c>, then Unity render state from
        /// <c>_Mode</c>. Returns false when the shader cannot resolve.
        /// </summary>
        public static bool TryWriteUnityOverrideOntoMaterial(
            Material material,
            VrmxtMaterialEngineOverride engineOverride,
            bool hasMtoon,
            JObject mtoon,
            Func<int, Texture> resolveTexture,
            Func<string, Shader> resolveShader = null
        )
        {
            if (material == null || engineOverride == null)
            {
                return false;
            }

            var unityOverride = engineOverride.Material as UnityMaterialOverride;
            if (unityOverride == null)
            {
                return false;
            }

            var shader = ResolveShader(unityOverride.ShaderName, resolveShader);
            if (shader == null)
            {
                return false;
            }

            var previousShader = material.shader;
            material.shader = shader;
            if (!ReferenceEquals(previousShader, shader))
            {
                ClearUnlistedTextureProperties(
                    material,
                    shader,
                    engineOverride.Properties,
                    engineOverride.Bindings,
                    hasMtoon,
                    mtoon
                );
            }

            ApplyProperties(material, engineOverride.Properties, resolveTexture);
            ApplyBindings(material, engineOverride.Bindings, hasMtoon, mtoon, resolveTexture);
            // Thry/Poiyomi _Mode on_value_actions (render_queue / RenderType) only run
            // in the Editor inspector — runtime SetFloat("_Mode") alone leaves the
            // stock MToon queue. Additive glitter/emission then draws wrong / invisible.
            ApplyUnityRenderStateFromMode(material);
            return true;
        }

        /// <summary>
        /// Portable Unity <c>idType: shaderName</c> for a live material. Prefer the
        /// <c>OriginalShader</c> override tag when set (Unity material tags; used by
        /// optimizers such as Thry Lock to remember the unlocked shader). Otherwise
        /// <see cref="Shader.name"/>. Does not parse locked shader path strings.
        /// </summary>
        public static string GetPortableShaderName(Material material)
        {
            if (material == null || material.shader == null)
            {
                return null;
            }

            var original = material.GetTag(OriginalShaderTag, false, string.Empty);
            if (!string.IsNullOrEmpty(original))
            {
                return original;
            }

            return material.shader.name;
        }

        /// <summary>
        /// Unity material override tag used by Thry Shader Optimizer (and compatible
        /// lockers) to store the unlocked shader name while the active shader is a
        /// generated locked variant.
        /// </summary>
        public const string OriginalShaderTag = "OriginalShader";

        /// <summary>
        /// Resolve a shader by name.
        /// When <paramref name="resolveShader"/> is provided, its result is final
        /// (including null = deny; no provider or <see cref="Shader.Find"/> fallback).
        /// When omitted: <see cref="ShaderResolveProvider"/>, else <see cref="Shader.Find"/>.
        /// </summary>
        public static Shader ResolveShader(
            string shaderName,
            Func<string, Shader> resolveShader = null
        )
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return null;
            }

            if (resolveShader != null)
            {
                return resolveShader(shaderName);
            }

            if (ShaderResolveProvider != null)
            {
                var fromProvider = ShaderResolveProvider(shaderName);
                if (fromProvider != null)
                {
                    return fromProvider;
                }
            }

            return Shader.Find(shaderName);
        }

        /// <summary>
        /// Best-effort active pipeline detection from
        /// <see cref="UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline"/>.
        /// Unrecognized SRPs fall back to <see cref="RenderPipelineVariant.Builtin"/> so
        /// variant-gated overrides stay conservative (no false-positive match).
        /// Does not use <c>Object.GetType()</c> / Reflection — Warudo UMod code security
        /// rejects those APIs. Distinguishes URP vs HDRP via
        /// <see cref="UnityEngine.Object.ToString"/> (<c>name (TypeName)</c>).
        /// </summary>
        public static RenderPipelineVariant DetectActivePipeline()
        {
            if (ActivePipelineProvider != null)
            {
                return ActivePipelineProvider();
            }

            var pipelineAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                return RenderPipelineVariant.Builtin;
            }

            // UnityEngine.Object.ToString → "assetName (TypeName)". Prefer this over
            // GetType().Name so restricted hosts (Warudo/UMod) can vendor this file.
            var label = pipelineAsset.ToString();
            if (label.IndexOf("Universal", StringComparison.Ordinal) >= 0)
            {
                return RenderPipelineVariant.Urp;
            }

            if (
                label.IndexOf("HDRenderPipeline", StringComparison.Ordinal) >= 0
                || label.IndexOf("HDRender", StringComparison.Ordinal) >= 0
            )
            {
                return RenderPipelineVariant.Hdrp;
            }

            return RenderPipelineVariant.Builtin;
        }

        /// <summary>
        /// All distinct <see cref="Material"/> instances on <paramref name="root"/>'s
        /// renderers whose name matches <paramref name="materialName"/> (glTF material
        /// name, with a defensive check for Unity's " (Instance)" suffix).
        /// </summary>
        public static IEnumerable<Material> FindMaterialsByName(
            GameObject root,
            string materialName
        )
        {
            if (root == null || string.IsNullOrEmpty(materialName))
            {
                yield break;
            }

            var seen = new HashSet<Material>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var sharedMaterials = renderers[i].sharedMaterials;
                for (var j = 0; j < sharedMaterials.Length; j++)
                {
                    var material = sharedMaterials[j];
                    if (material == null || !seen.Add(material))
                    {
                        continue;
                    }

                    if (MaterialNameMatches(material.name, materialName))
                    {
                        yield return material;
                    }
                }
            }
        }

        private static bool MaterialNameMatches(string unityMaterialName, string gltfMaterialName)
        {
            var unity = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(unityMaterialName);
            var gltf = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(gltfMaterialName);
            return string.Equals(unity, gltf, StringComparison.Ordinal);
        }

        private static void WarnOnProviderMismatch(
            string materialName,
            MaterialProvider provider,
            Func<MaterialProvider, bool> isProviderMismatch
        )
        {
            if (provider == null || isProviderMismatch == null)
            {
                return;
            }

            if (isProviderMismatch(provider))
            {
                Debug.LogWarning(
                    $"VRMXT_materials_override: provider '{provider.Id}'"
                        + (
                            string.IsNullOrEmpty(provider.Version)
                                ? string.Empty
                                : $" {provider.Version}"
                        )
                        + $" for material '{materialName}' does not match the resolved package. Applying anyway (provider is advisory)."
                );
            }
        }

        private static void ApplyProperties(
            Material material,
            IReadOnlyList<VrmxtMaterialProperty> properties,
            Func<int, Texture> resolveTexture
        )
        {
            if (properties == null)
            {
                return;
            }

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                if (property == null || string.IsNullOrEmpty(property.Name))
                {
                    continue;
                }

                switch (property.Type)
                {
                    case VrmxtMaterialsOverride.TargetTypeScalar:
                        if (property.ScalarValue.HasValue)
                        {
                            material.SetFloat(property.Name, property.ScalarValue.Value);
                        }

                        break;

                    case VrmxtMaterialsOverride.TargetTypeVector:
                        ApplyVector(material, property.Name, property.VectorValue);
                        break;

                    case VrmxtMaterialsOverride.TargetTypeTexture:
                        ApplyTexture(
                            material,
                            property.Name,
                            property.TextureIndex,
                            property.VectorValue,
                            resolveTexture
                        );
                        break;

                    case VrmxtMaterialsOverride.TargetTypeShaderFeature:
                        ApplyShaderFeature(material, property.Name, property.BoolValue);
                        break;
                }
            }
        }

        private static void ApplyBindings(
            Material material,
            IReadOnlyList<VrmxtMaterialBinding> bindings,
            bool hasMtoon,
            JObject mtoon,
            Func<int, Texture> resolveTexture
        )
        {
            // Base-spec rule 16: no sibling VRMC_materials_mtoon extension at all means
            // every binding on this material is ignored, not defaulted.
            if (bindings == null || bindings.Count == 0 || !hasMtoon)
            {
                return;
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                ApplyBinding(material, bindings[i], mtoon, resolveTexture);
            }
        }

        private static void ApplyBinding(
            Material material,
            VrmxtMaterialBinding binding,
            JObject mtoon,
            Func<int, Texture> resolveTexture
        )
        {
            if (binding == null || string.IsNullOrEmpty(binding.Target))
            {
                return;
            }

            if (
                !TryResolveMtoonSource(
                    binding.Source,
                    mtoon,
                    out var scalar,
                    out var vector,
                    out var textureIndex,
                    out var category
                )
            )
            {
                // Unknown or unresolvable source (e.g. no texture set): ignore per rules 16/24.
                return;
            }

            switch (binding.TargetType)
            {
                case VrmxtMaterialsOverride.TargetTypeScalar:
                    if (category == MtoonSourceCategory.Scalar && scalar.HasValue)
                    {
                        material.SetFloat(binding.Target, scalar.Value);
                    }

                    break;

                case VrmxtMaterialsOverride.TargetTypeVector:
                    if (category == MtoonSourceCategory.Vector)
                    {
                        ApplyVector(material, binding.Target, vector);
                    }

                    break;

                case VrmxtMaterialsOverride.TargetTypeTexture:
                    if (category == MtoonSourceCategory.Texture)
                    {
                        ApplyTexture(material, binding.Target, textureIndex, resolveTexture);
                    }

                    break;

                default:
                    // shaderFeature has no boolean MToon source in this draft; ignore.
                    break;
            }
        }

        private static void ApplyVector(
            Material material,
            string target,
            IReadOnlyList<float> values
        )
        {
            if (
                values == null
                || values.Count == 0
                || material == null
                || string.IsNullOrEmpty(target)
            )
            {
                return;
            }

            // Colors were captured via GetColor; vectors (UV pans, scroll directions with
            // out-of-0..1 / negative components like _EmissiveScroll_Direction) via GetVector.
            // SetColor applies color-space conversion and must not be used for Vector props —
            // that kills Poiyomi scrolling emission / glitter pans.
            if (IsShaderColorProperty(material, target))
            {
                if (values.Count >= 4)
                {
                    material.SetColor(
                        target,
                        new Color(values[0], values[1], values[2], values[3])
                    );
                }
                else if (values.Count == 3)
                {
                    material.SetColor(target, new Color(values[0], values[1], values[2], 1f));
                }
                else
                {
                    material.SetColor(
                        target,
                        new Color(
                            values[0],
                            values.Count > 1 ? values[1] : 0f,
                            values.Count > 2 ? values[2] : 0f,
                            values.Count > 3 ? values[3] : 1f
                        )
                    );
                }

                return;
            }

            material.SetVector(
                target,
                new Vector4(
                    values[0],
                    values.Count > 1 ? values[1] : 0f,
                    values.Count > 2 ? values[2] : 0f,
                    values.Count > 3 ? values[3] : 0f
                )
            );
        }

        private static bool IsShaderColorProperty(Material material, string propertyName)
        {
            var shader = material != null ? material.shader : null;
            if (shader == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (
                    !string.Equals(
                        shader.GetPropertyName(i),
                        propertyName,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                return shader.GetPropertyType(i) == ShaderPropertyType.Color;
            }

            return false;
        }

        private static void ApplyTexture(
            Material material,
            string target,
            int? textureIndex,
            Func<int, Texture> resolveTexture
        )
        {
            ApplyTexture(material, target, textureIndex, null, resolveTexture);
        }

        private static void ApplyTexture(
            Material material,
            string target,
            int? textureIndex,
            IReadOnlyList<float> textureTransform,
            Func<int, Texture> resolveTexture
        )
        {
            if (!textureIndex.HasValue || resolveTexture == null)
            {
                return;
            }

            var texture = resolveTexture(textureIndex.Value);
            if (texture != null)
            {
                if (!CanAssignTextureToProperty(material, target, texture))
                {
                    Debug.LogWarning(
                        "VRMXT_materials_override: skip texture '"
                            + target
                            + "' on '"
                            + (material != null ? material.name : "?")
                            + "' — dimension mismatch (tex="
                            + texture.dimension
                            + ")."
                    );
                    return;
                }

                material.SetTexture(target, texture);
                ApplyTextureTransform(material, target, textureTransform);
                return;
            }

            Debug.LogWarning(
                "VRMXT_materials_override: texture property '"
                    + target
                    + "' index="
                    + textureIndex.Value
                    + " unresolved on '"
                    + (material != null ? material.name : "?")
                    + "' (RememberTextures miss / out of range)."
            );
        }

        /// <summary>
        /// Apply optional Unity texture ST from <c>properties[].value</c>
        /// <c>[scale.x, scale.y, offset.x, offset.y]</c> (2-float scale-only also accepted).
        /// </summary>
        private static void ApplyTextureTransform(
            Material material,
            string target,
            IReadOnlyList<float> textureTransform
        )
        {
            if (
                material == null
                || string.IsNullOrEmpty(target)
                || textureTransform == null
                || textureTransform.Count < 2
                || !material.HasProperty(target)
            )
            {
                return;
            }

            material.SetTextureScale(target, new Vector2(textureTransform[0], textureTransform[1]));

            if (textureTransform.Count >= 4)
            {
                material.SetTextureOffset(
                    target,
                    new Vector2(textureTransform[2], textureTransform[3])
                );
            }
        }

        /// <summary>
        /// Avoid Unity's "Error assigning 2D texture to CUBE…" when override JSON
        /// points a Cube/3D slot at a packed 2D glTF image (common for unused
        /// reflection cube props on toon presets).
        /// </summary>
        private static bool CanAssignTextureToProperty(
            Material material,
            string propertyName,
            Texture texture
        )
        {
            if (material == null || texture == null || string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            if (!material.HasProperty(propertyName))
            {
                return false;
            }

            var shader = material.shader;
            if (shader == null)
            {
                return true;
            }

            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (
                    !string.Equals(
                        shader.GetPropertyName(i),
                        propertyName,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                {
                    return true;
                }

                var expected = shader.GetPropertyTextureDimension(i);
                return expected == TextureDimension.Any || expected == texture.dimension;
            }

            return true;
        }

        /// <summary>
        /// After a shader swap, drop stock-import textures that the override JSON does not
        /// mention — but only when the override claims texture ownership (texture
        /// <c>properties</c> and/or texture <c>bindings</c>). When textures are omitted on
        /// purpose (Character / Warudo Material Properties owns them), leave slots alone.
        /// </summary>
        private static void ClearUnlistedTextureProperties(
            Material material,
            Shader shader,
            IReadOnlyList<VrmxtMaterialProperty> properties,
            IReadOnlyList<VrmxtMaterialBinding> bindings,
            bool hasMtoon,
            JObject mtoon
        )
        {
            if (material == null || shader == null)
            {
                return;
            }

            if (!OverrideClaimsTextureOwnership(properties, bindings))
            {
                return;
            }

            var covered = CollectCoveredTextureTargets(properties, bindings, hasMtoon, mtoon);
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                {
                    continue;
                }

                var name = shader.GetPropertyName(i);
                if (
                    string.IsNullOrEmpty(name)
                    || !material.HasProperty(name)
                    || covered.Contains(name)
                )
                {
                    continue;
                }

                material.SetTexture(name, null);
            }
        }

        private static bool OverrideClaimsTextureOwnership(
            IReadOnlyList<VrmxtMaterialProperty> properties,
            IReadOnlyList<VrmxtMaterialBinding> bindings
        )
        {
            if (properties != null)
            {
                for (var i = 0; i < properties.Count; i++)
                {
                    var property = properties[i];
                    if (
                        property != null
                        && string.Equals(
                            property.Type,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            if (bindings == null)
            {
                return false;
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (
                    binding != null
                    && string.Equals(
                        binding.TargetType,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Texture slots the override JSON will write. Binding targets are covered only when
        /// <see cref="ApplyBindings"/> would apply them (sibling MToon + resolvable texture
        /// source); otherwise they must stay clearable after a shader swap.
        /// </summary>
        private static HashSet<string> CollectCoveredTextureTargets(
            IReadOnlyList<VrmxtMaterialProperty> properties,
            IReadOnlyList<VrmxtMaterialBinding> bindings,
            bool hasMtoon,
            JObject mtoon
        )
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            if (properties != null)
            {
                for (var i = 0; i < properties.Count; i++)
                {
                    var property = properties[i];
                    if (
                        property == null
                        || !string.Equals(
                            property.Type,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            StringComparison.Ordinal
                        )
                        || string.IsNullOrEmpty(property.Name)
                    )
                    {
                        continue;
                    }

                    covered.Add(property.Name);
                }
            }

            // Match ApplyBindings: no sibling MToon means every binding is ignored.
            if (bindings == null || !hasMtoon || mtoon == null)
            {
                return covered;
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (
                    binding == null
                    || !string.Equals(
                        binding.TargetType,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                    || string.IsNullOrEmpty(binding.Target)
                )
                {
                    continue;
                }

                // Match ApplyBinding: unresolvable / non-texture MToon sources are ignored.
                if (
                    !TryResolveMtoonSource(
                        binding.Source,
                        mtoon,
                        out _,
                        out _,
                        out _,
                        out var category
                    )
                    || category != MtoonSourceCategory.Texture
                )
                {
                    continue;
                }

                covered.Add(binding.Target);
            }

            return covered;
        }

        private static void ApplyShaderFeature(Material material, string target, bool? enabled)
        {
            if (!enabled.HasValue)
            {
                return;
            }

            if (enabled.Value)
            {
                material.EnableKeyword(target);
            }
            else
            {
                material.DisableKeyword(target);
            }
        }

        /// <summary>
        /// Map Poiyomi/Thry <c>_Mode</c> rendering preset to Unity <see cref="Material.renderQueue"/>
        /// and <c>RenderType</c> tag. Values match Poiyomi Toon 9.3 <c>_Mode</c> on_value_actions
        /// (0 Opaque, 1 Cutout, 9 TransClipping, 2 Fade, 3 Transparent, 4 Additive, …).
        /// </summary>
        public static void ApplyUnityRenderStateFromMode(Material material)
        {
            if (material == null || !material.HasProperty("_Mode"))
            {
                return;
            }

            var mode = Mathf.RoundToInt(material.GetFloat("_Mode"));
            switch (mode)
            {
                case 0: // Opaque
                    material.renderQueue = 2000;
                    material.SetOverrideTag("RenderType", "Opaque");
                    break;
                case 1: // Cutout
                    material.renderQueue = 2450;
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    break;
                case 9: // TransClipping
                    material.renderQueue = 2460;
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    break;
                case 2: // Fade
                case 3: // Transparent
                case 4: // Additive — TestPoi
                case 5: // Soft Additive
                case 6: // Multiplicative
                case 7: // 2x Multiplicative
                    material.renderQueue = 3000;
                    material.SetOverrideTag("RenderType", "Transparent");
                    break;
            }
        }

        private enum MtoonSourceCategory
        {
            Scalar,
            Vector,
            Texture,
        }

        private static readonly float[] DefaultShadeColorFactor = { 0f, 0f, 0f };

        private static bool TryResolveMtoonSource(
            string source,
            JObject mtoon,
            out float? scalar,
            out float[] vector,
            out int? textureIndex,
            out MtoonSourceCategory category
        )
        {
            scalar = null;
            vector = null;
            textureIndex = null;
            category = MtoonSourceCategory.Scalar;

            switch (source)
            {
                case "shadeColorFactor":
                    category = MtoonSourceCategory.Vector;
                    vector = ReadFloatArray(mtoon, "shadeColorFactor", DefaultShadeColorFactor);
                    return true;

                case "shadeMultiplyTexture":
                    category = MtoonSourceCategory.Texture;
                    textureIndex = ReadTextureIndex(mtoon, "shadeMultiplyTexture");
                    return textureIndex.HasValue;

                case "shadingShiftFactor":
                    category = MtoonSourceCategory.Scalar;
                    scalar = ReadFloat(mtoon, "shadingShiftFactor", 0f);
                    return true;

                case "shadingShiftTexture":
                    category = MtoonSourceCategory.Texture;
                    textureIndex = ReadTextureIndex(mtoon, "shadingShiftTexture");
                    return textureIndex.HasValue;

                case "shadingShiftTexture.scale":
                    category = MtoonSourceCategory.Scalar;
                    scalar = ReadNestedFloat(mtoon, "shadingShiftTexture", "scale", 1f);
                    return true;

                case "shadingToonyFactor":
                    category = MtoonSourceCategory.Scalar;
                    scalar = ReadFloat(mtoon, "shadingToonyFactor", 0.9f);
                    return true;

                case "giEqualizationFactor":
                    category = MtoonSourceCategory.Scalar;
                    scalar = ReadFloat(mtoon, "giEqualizationFactor", 0.9f);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Resolve sibling <c>VRMC_materials_mtoon</c> for a store pair. Prefers
        /// <see cref="VrmxtMaterialsOverridePair.GltfMaterialIndex"/> when set; otherwise
        /// matches by material name (with <c> (Instance)</c> strip and <c>Name#N</c>
        /// occurrence among override-bearing slots). Continues past same-name slots that
        /// lack MToon when resolving a plain (non-disambiguated) key.
        /// </summary>
        public static bool TryFindSiblingMtoonForPair(
            JObject gltfRoot,
            VrmxtMaterialsOverridePair pair,
            out JObject mtoon
        )
        {
            mtoon = null;
            if (pair == null)
            {
                return false;
            }

            if (
                gltfRoot == null
                || !gltfRoot.TryGetValue(
                    "materials",
                    StringComparison.Ordinal,
                    out var materialsToken
                )
                || materialsToken is not JArray materials
            )
            {
                return false;
            }

            if (pair.GltfMaterialIndex >= 0 && pair.GltfMaterialIndex < materials.Count)
            {
                return TryGetMtoonExtension(
                    materials[pair.GltfMaterialIndex] as JObject,
                    out mtoon
                );
            }

            return TryFindSiblingMtoon(gltfRoot, pair.MaterialName, out mtoon);
        }

        /// <summary>
        /// Texture index for a MToon binding source (e.g. <c>shadeMultiplyTexture</c>), if set.
        /// </summary>
        public static bool TryGetMtoonBindingTextureIndex(
            string bindingSource,
            JObject mtoon,
            out int textureIndex
        )
        {
            textureIndex = 0;
            if (
                !TryResolveMtoonSource(
                    bindingSource,
                    mtoon,
                    out _,
                    out _,
                    out var index,
                    out var category
                )
                || category != MtoonSourceCategory.Texture
                || !index.HasValue
            )
            {
                return false;
            }

            textureIndex = index.Value;
            return true;
        }

        private static bool TryFindSiblingMtoon(
            JObject gltfRoot,
            string materialName,
            out JObject mtoon
        )
        {
            mtoon = null;
            if (
                gltfRoot == null
                || string.IsNullOrEmpty(materialName)
                || !gltfRoot.TryGetValue(
                    "materials",
                    StringComparison.Ordinal,
                    out var materialsToken
                )
                || materialsToken is not JArray materials
            )
            {
                return false;
            }

            var isDisambiguated = VrmxtMaterialsOverrideRuntime.TryGetDisambiguatedStoreKey(
                materialName,
                out var baseName,
                out var occurrence
            );
            if (!isDisambiguated)
            {
                baseName = materialName;
            }

            var overrideOccurrence = 0;
            for (var i = 0; i < materials.Count; i++)
            {
                if (materials[i] is not JObject materialObject)
                {
                    continue;
                }

                var name = VrmxtMaterialsOverrideRuntime.GetMaterialName(materialObject, i);
                if (!MaterialNameMatches(name, baseName))
                {
                    continue;
                }

                if (isDisambiguated)
                {
                    if (!HasVrmxtMaterialsOverrideExtension(materialObject))
                    {
                        continue;
                    }

                    overrideOccurrence++;
                    if (overrideOccurrence != occurrence)
                    {
                        continue;
                    }

                    return TryGetMtoonExtension(materialObject, out mtoon);
                }

                // Plain key: first same-name slot that actually has MToon (skip empties).
                if (TryGetMtoonExtension(materialObject, out mtoon))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasVrmxtMaterialsOverrideExtension(JObject materialObject)
        {
            if (
                materialObject == null
                || !materialObject.TryGetValue(
                    "extensions",
                    StringComparison.Ordinal,
                    out var extensionsToken
                )
                || extensionsToken is not JObject extensions
                || !extensions.TryGetValue(
                    "VRMXT_materials_override",
                    StringComparison.Ordinal,
                    out var overrideToken
                )
                || overrideToken is not JObject
            )
            {
                return false;
            }

            return VrmxtMaterialsOverride.TryParse(overrideToken, out _);
        }

        private static bool TryGetMtoonExtension(JObject materialObject, out JObject mtoon)
        {
            mtoon = null;
            if (
                materialObject == null
                || !materialObject.TryGetValue(
                    "extensions",
                    StringComparison.Ordinal,
                    out var extensionsToken
                )
                || extensionsToken is not JObject extensions
                || !extensions.TryGetValue(
                    "VRMC_materials_mtoon",
                    StringComparison.Ordinal,
                    out var mtoonToken
                )
                || mtoonToken is not JObject mtoonObject
            )
            {
                return false;
            }

            mtoon = mtoonObject;
            return true;
        }

        private static JObject TryParseGltfRoot(string gltfJson)
        {
            if (string.IsNullOrWhiteSpace(gltfJson))
            {
                return null;
            }

            try
            {
                return JToken.Parse(gltfJson) as JObject;
            }
            catch (JsonReaderException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static float ReadFloat(JObject parent, string propertyName, float defaultValue)
        {
            if (
                parent == null
                || !parent.TryGetValue(propertyName, StringComparison.Ordinal, out var token)
                || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            )
            {
                return defaultValue;
            }

            return token.Value<float>();
        }

        private static float ReadNestedFloat(
            JObject parent,
            string objectPropertyName,
            string nestedPropertyName,
            float defaultValue
        )
        {
            if (
                parent == null
                || !parent.TryGetValue(
                    objectPropertyName,
                    StringComparison.Ordinal,
                    out var objectToken
                )
                || objectToken is not JObject nested
            )
            {
                return defaultValue;
            }

            return ReadFloat(nested, nestedPropertyName, defaultValue);
        }

        private static float[] ReadFloatArray(JObject parent, string propertyName, float[] defaults)
        {
            if (
                parent == null
                || !parent.TryGetValue(propertyName, StringComparison.Ordinal, out var token)
                || token is not JArray array
                || array.Count != defaults.Length
            )
            {
                return defaults;
            }

            var values = new float[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.Float && array[i].Type != JTokenType.Integer)
                {
                    return defaults;
                }

                values[i] = array[i].Value<float>();
            }

            return values;
        }

        private static int? ReadTextureIndex(JObject parent, string textureInfoPropertyName)
        {
            if (
                parent == null
                || !parent.TryGetValue(
                    textureInfoPropertyName,
                    StringComparison.Ordinal,
                    out var token
                )
                || token is not JObject textureInfo
                || !textureInfo.TryGetValue("index", StringComparison.Ordinal, out var indexToken)
                || (indexToken.Type != JTokenType.Integer && indexToken.Type != JTokenType.Float)
            )
            {
                return null;
            }

            var index = indexToken.Value<int>();
            return index >= 0 ? index : (int?)null;
        }
    }
}
