using System;
using Newtonsoft.Json.Linq;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Swap stock MToon to the pipeline MToonXT shader and write stencil extras.
    /// Skips when <c>VRMXT_materials_override</c> would apply (spec rule 14).
    /// </summary>
    public static class VrmcMaterialsMtoonxtApplier
    {
        private static readonly VrmcMaterialsMtoonxtStencil StencilOff =
            new VrmcMaterialsMtoonxtStencil(false, 0, 255, 255, "always", "keep", "keep", "keep");

        public static int Apply(
            GameObject root,
            string gltfJson,
            Func<string, Shader> resolveShader = null)
        {
            VrmcMaterialsMtoonxtRuntime.TryAttachFromGltfJson(root, gltfJson, out var store);
            return Apply(root, store, gltfJson, resolveShader);
        }

        public static int Apply(
            GameObject root,
            VrmcMaterialsMtoonxtInstance store,
            string gltfJson,
            Func<string, Shader> resolveShader = null)
        {
            if (root == null || store == null || string.IsNullOrWhiteSpace(gltfJson))
            {
                return 0;
            }

            JObject gltfRoot;
            try
            {
                gltfRoot = JToken.Parse(gltfJson) as JObject;
            }
            catch
            {
                return 0;
            }

            if (gltfRoot == null)
            {
                return 0;
            }

            var pipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
            var shaderName = ShaderNameForPipeline(pipeline);
            if (string.IsNullOrEmpty(shaderName))
            {
                return 0;
            }

            var shader = VrmxtMaterialsOverrideApplier.ResolveShader(
                shaderName,
                resolveShader);
            if (shader == null)
            {
                return 0;
            }

            var applied = 0;
            var pairs = store.Pairs;
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair == null || string.IsNullOrEmpty(pair.ExtensionJson))
                {
                    continue;
                }

                if (!VrmcMaterialsMtoonxt.TryParse(pair.ExtensionJson, out var xt))
                {
                    continue;
                }

                if (!TryGetMaterialObject(gltfRoot, pair.GltfMaterialIndex, out var materialObject))
                {
                    continue;
                }

                if (!HasSiblingMtoon(materialObject))
                {
                    continue;
                }

                if (WouldMaterialsOverrideApply(materialObject, pipeline, resolveShader))
                {
                    continue;
                }

                var swappedAny = false;
                foreach (var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                             root,
                             pair.MaterialName))
                {
                    if (material == null || (material.hideFlags & HideFlags.DontSave) != 0)
                    {
                        continue;
                    }

                    material.shader = shader;
                    // Shader switch leaves new stencil floats at 0 (Disabled). GPU Comp 0
                    // fails the test. Write stencil-off, then overlay JSON extras.
                    ApplyStencilOffDefaults(material);
                    ApplyStencil(material, xt.Stencil, outline: false);
                    ApplyStencil(material, xt.OutlineStencil, outline: true);
                    swappedAny = true;
                }

                if (swappedAny)
                {
                    applied++;
                }
            }

            return applied;
        }

        private static bool TryGetMaterialObject(
            JObject gltfRoot,
            int index,
            out JObject materialObject)
        {
            materialObject = null;
            if (gltfRoot == null ||
                !gltfRoot.TryGetValue("materials", StringComparison.Ordinal, out var materialsToken))
            {
                return false;
            }

            var materials = materialsToken as JArray;
            if (materials == null || index < 0 || index >= materials.Count)
            {
                return false;
            }

            materialObject = materials[index] as JObject;
            return materialObject != null;
        }

        private static bool HasSiblingMtoon(JObject materialObject)
        {
            if (materialObject == null ||
                !materialObject.TryGetValue("extensions", StringComparison.Ordinal, out var extensionsToken))
            {
                return false;
            }

            var extensions = extensionsToken as JObject;
            if (extensions == null ||
                !extensions.TryGetValue(
                    VrmcMaterialsMtoonxt.SiblingMtoonExtensionName,
                    StringComparison.Ordinal,
                    out var mtoonToken))
            {
                return false;
            }

            return mtoonToken as JObject != null;
        }

        private static bool WouldMaterialsOverrideApply(
            JObject materialObject,
            RenderPipelineVariant pipeline,
            Func<string, Shader> resolveShader)
        {
            if (materialObject == null ||
                !materialObject.TryGetValue("extensions", StringComparison.Ordinal, out var extensionsToken))
            {
                return false;
            }

            var extensions = extensionsToken as JObject;
            if (extensions == null ||
                !extensions.TryGetValue(
                    VrmxtMaterialsOverride.ExtensionName,
                    StringComparison.Ordinal,
                    out var overrideToken))
            {
                return false;
            }

            if (!VrmxtMaterialsOverride.TryParse(overrideToken, out var extension))
            {
                return false;
            }

            if (!UnityOverrideSelector.TrySelectUnityEngineOverride(
                    extension,
                    pipeline,
                    out var engineOverride))
            {
                return false;
            }

            var unity = engineOverride.Material as UnityMaterialOverride;
            if (unity == null || string.IsNullOrEmpty(unity.ShaderName))
            {
                return false;
            }

            return VrmxtMaterialsOverrideApplier.ResolveShader(unity.ShaderName, resolveShader) != null;
        }

        public static string ShaderNameForPipeline(RenderPipelineVariant pipeline)
        {
            switch (pipeline)
            {
                case RenderPipelineVariant.Urp:
                    return VrmcMaterialsMtoonxt.UrpShaderName;
                case RenderPipelineVariant.Builtin:
                    return VrmcMaterialsMtoonxt.BuiltinShaderName;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Spec stencil-off: Always / Keep. Comp 0 is Unity Disabled and hides the mesh.
        /// </summary>
        public static void ApplyStencilOffDefaults(Material material)
        {
            ApplyStencil(material, StencilOff, outline: false);
            ApplyStencil(material, StencilOff, outline: true);
        }

        /// <summary>
        /// Shader switch / Unity enum Disabled leave Comp at 0. Restore that pass only.
        /// </summary>
        public static void EnsureStencilOffIfUninitialized(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (!IsEnabled(material, VrmcMaterialsMtoonxt.StencilPropEnabled))
            {
                ApplyStencil(material, StencilOff, outline: false);
            }
            else if (IsUninitializedComp(material, VrmcMaterialsMtoonxt.StencilPropComp))
            {
                TrySetFloat(material, VrmcMaterialsMtoonxt.StencilPropComp, 8f);
            }

            if (!IsEnabled(material, VrmcMaterialsMtoonxt.OutlineStencilPropEnabled))
            {
                ApplyStencil(material, StencilOff, outline: true);
            }
            else if (IsUninitializedComp(material, VrmcMaterialsMtoonxt.OutlineStencilPropComp))
            {
                TrySetFloat(material, VrmcMaterialsMtoonxt.OutlineStencilPropComp, 8f);
            }
        }

        private static bool IsEnabled(Material material, string propertyName)
        {
            return material.HasProperty(propertyName)
                && material.GetFloat(propertyName) >= 0.5f;
        }

        private static bool IsUninitializedComp(Material material, string propertyName)
        {
            return material.HasProperty(propertyName)
                && Mathf.Approximately(material.GetFloat(propertyName), 0f);
        }

        private static void ApplyStencil(Material material, VrmcMaterialsMtoonxtStencil stencil, bool outline)
        {
            if (material == null || stencil == null)
            {
                return;
            }

            var applied = stencil.Enabled ? stencil : StencilOff;
            var prefix = outline
                ? "_M_OutlineStencil"
                : "_M_Stencil";

            TrySetFloat(material, prefix + "Enabled", applied.Enabled ? 1f : 0f);
            TrySetFloat(material, prefix + "Ref", applied.Ref);
            TrySetFloat(material, prefix + "ReadMask", applied.ReadMask);
            TrySetFloat(material, prefix + "WriteMask", applied.WriteMask);
            TrySetFloat(material, prefix + "Comp", applied.CompUnityInt);
            TrySetFloat(material, prefix + "Pass", applied.PassUnityInt);
            TrySetFloat(material, prefix + "Fail", applied.FailUnityInt);
            TrySetFloat(material, prefix + "ZFail", applied.ZFailUnityInt);
        }

        private static void TrySetFloat(Material material, string name, float value)
        {
            if (material.HasProperty(name))
            {
                material.SetFloat(name, value);
            }
        }
    }
}
