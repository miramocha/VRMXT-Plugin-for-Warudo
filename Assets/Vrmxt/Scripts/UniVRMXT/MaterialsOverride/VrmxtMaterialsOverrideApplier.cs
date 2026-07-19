using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Shared apply logic for Editor and Warudo-style hosts. Resolves the <c>unity</c>
    /// override per material, resolves MToon-sourced <c>bindings</c> from the sibling
    /// <c>VRMC_materials_mtoon</c> extension, and writes <c>properties</c> then
    /// <c>bindings</c> onto matching <see cref="Renderer"/> materials (bindings win on
    /// overlap, per base-spec rule 23). Never throws on a per-material failure — that
    /// material is left on stock import.
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
            Func<string, Shader> resolveShader = null)
        {
            VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store);
            return Apply(
                root,
                store,
                gltfJson,
                activePipeline,
                resolveTexture,
                isProviderMismatch,
                resolveShader);
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
            Func<string, Shader> resolveShader = null)
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

                if (!UnityOverrideSelector.TrySelectUnityEngineOverride(
                        extension, activePipeline, out var engineOverride))
                {
                    continue;
                }

                var unityOverride = engineOverride.Material as UnityMaterialOverride;
                if (unityOverride == null)
                {
                    continue;
                }

                var shader = ResolveShader(unityOverride.ShaderName, resolveShader);
                if (shader == null)
                {
                    // Shader not present in this build — keep / restore stock import.
                    Debug.LogWarning(
                        $"VRMXT_materials_override: shader '{unityOverride.ShaderName}' unresolved for " +
                        $"material '{entry.MaterialName}'. Leaving stock material.");
                    if (entry.SourceMaterial != null)
                    {
                        VrmxtMaterialsOverrideAuthoring.RestoreSourceMaterial(
                            root,
                            entry.MaterialName,
                            entry.SourceMaterial);
                    }

                    continue;
                }

                WarnOnProviderMismatch(entry.MaterialName, unityOverride.Provider, isProviderMismatch);

                var hasMtoon = TryFindSiblingMtoon(gltfRoot, entry.MaterialName, out var mtoon);

                // Drop stale DontSave authoring previews so we apply onto stock import mats.
                if (entry.SourceMaterial != null)
                {
                    VrmxtMaterialsOverrideAuthoring.RestoreSourceMaterial(
                        root,
                        entry.MaterialName,
                        entry.SourceMaterial);
                }

                var appliedToAny = false;
                foreach (var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                             root, entry.MaterialName))
                {
                    if (material == null || (material.hideFlags & HideFlags.DontSave) != 0)
                    {
                        continue;
                    }

                    // Import / runtime: mutate materials the host already built. Scene
                    // authoring uses DontSave clones via Authoring instead — those must not
                    // be written onto imported assets (they do not serialize → pink/missing).
                    material.shader = shader;
                    ApplyProperties(material, engineOverride.Properties, resolveTexture);
                    ApplyBindings(material, engineOverride.Bindings, hasMtoon, mtoon, resolveTexture);
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
        /// Resolve a shader by name: per-call <paramref name="resolveShader"/>, else
        /// <see cref="ShaderResolveProvider"/>, else <see cref="Shader.Find"/>.
        /// Each step is tried only when the previous returns null.
        /// </summary>
        public static Shader ResolveShader(string shaderName, Func<string, Shader> resolveShader = null)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return null;
            }

            if (resolveShader != null)
            {
                var fromCaller = resolveShader(shaderName);
                if (fromCaller != null)
                {
                    return fromCaller;
                }
            }

            if (ShaderResolveProvider != null &&
                !ReferenceEquals(resolveShader, ShaderResolveProvider))
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

            if (label.IndexOf("HDRenderPipeline", StringComparison.Ordinal) >= 0 ||
                label.IndexOf("HDRender", StringComparison.Ordinal) >= 0)
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
        public static IEnumerable<Material> FindMaterialsByName(GameObject root, string materialName)
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
            Func<MaterialProvider, bool> isProviderMismatch)
        {
            if (provider == null || isProviderMismatch == null)
            {
                return;
            }

            if (isProviderMismatch(provider))
            {
                Debug.LogWarning(
                    $"VRMXT_materials_override: provider '{provider.Id}'" +
                    (string.IsNullOrEmpty(provider.Version) ? string.Empty : $" {provider.Version}") +
                    $" for material '{materialName}' does not match the resolved package. Applying anyway (provider is advisory).");
            }
        }

        private static void ApplyProperties(
            Material material,
            IReadOnlyList<VrmxtMaterialProperty> properties,
            Func<int, Texture> resolveTexture)
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
                        ApplyTexture(material, property.Name, property.TextureIndex, resolveTexture);
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
            Func<int, Texture> resolveTexture)
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
            Func<int, Texture> resolveTexture)
        {
            if (binding == null || string.IsNullOrEmpty(binding.Target))
            {
                return;
            }

            if (!TryResolveMtoonSource(binding.Source, mtoon, out var scalar, out var vector, out var textureIndex, out var category))
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

        private static void ApplyVector(Material material, string target, IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            if (values.Count == 3)
            {
                material.SetColor(target, new Color(values[0], values[1], values[2], 1f));
                return;
            }

            if (values.Count == 4)
            {
                material.SetColor(target, new Color(values[0], values[1], values[2], values[3]));
                return;
            }

            material.SetVector(target, new Vector4(
                values.Count > 0 ? values[0] : 0f,
                values.Count > 1 ? values[1] : 0f,
                values.Count > 2 ? values[2] : 0f,
                values.Count > 3 ? values[3] : 0f));
        }

        private static void ApplyTexture(
            Material material,
            string target,
            int? textureIndex,
            Func<int, Texture> resolveTexture)
        {
            if (!textureIndex.HasValue || resolveTexture == null)
            {
                return;
            }

            var texture = resolveTexture(textureIndex.Value);
            if (texture != null)
            {
                material.SetTexture(target, texture);
            }
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
            out MtoonSourceCategory category)
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

        private static bool TryFindSiblingMtoon(JObject gltfRoot, string materialName, out JObject mtoon)
        {
            mtoon = null;
            if (gltfRoot == null ||
                !gltfRoot.TryGetValue("materials", StringComparison.Ordinal, out var materialsToken) ||
                materialsToken is not JArray materials)
            {
                return false;
            }

            for (var i = 0; i < materials.Count; i++)
            {
                if (materials[i] is not JObject materialObject)
                {
                    continue;
                }

                var name = VrmxtMaterialsOverrideRuntime.GetMaterialName(materialObject, i);
                if (!string.Equals(name, materialName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (materialObject.TryGetValue("extensions", StringComparison.Ordinal, out var extensionsToken) &&
                    extensionsToken is JObject extensions &&
                    extensions.TryGetValue("VRMC_materials_mtoon", StringComparison.Ordinal, out var mtoonToken) &&
                    mtoonToken is JObject mtoonObject)
                {
                    mtoon = mtoonObject;
                    return true;
                }

                return false;
            }

            return false;
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
            if (parent == null ||
                !parent.TryGetValue(propertyName, StringComparison.Ordinal, out var token) ||
                (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            {
                return defaultValue;
            }

            return token.Value<float>();
        }

        private static float ReadNestedFloat(
            JObject parent,
            string objectPropertyName,
            string nestedPropertyName,
            float defaultValue)
        {
            if (parent == null ||
                !parent.TryGetValue(objectPropertyName, StringComparison.Ordinal, out var objectToken) ||
                objectToken is not JObject nested)
            {
                return defaultValue;
            }

            return ReadFloat(nested, nestedPropertyName, defaultValue);
        }

        private static float[] ReadFloatArray(JObject parent, string propertyName, float[] defaults)
        {
            if (parent == null ||
                !parent.TryGetValue(propertyName, StringComparison.Ordinal, out var token) ||
                token is not JArray array ||
                array.Count != defaults.Length)
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
            if (parent == null ||
                !parent.TryGetValue(textureInfoPropertyName, StringComparison.Ordinal, out var token) ||
                token is not JObject textureInfo ||
                !textureInfo.TryGetValue("index", StringComparison.Ordinal, out var indexToken) ||
                (indexToken.Type != JTokenType.Integer && indexToken.Type != JTokenType.Float))
            {
                return null;
            }

            var index = indexToken.Value<int>();
            return index >= 0 ? index : (int?)null;
        }
    }
}
