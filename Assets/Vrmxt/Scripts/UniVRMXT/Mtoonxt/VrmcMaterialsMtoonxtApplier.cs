using System;
using Newtonsoft.Json.Linq;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Swap stock MToon to <c>VRMXT/MToon10</c> and write stencil extras.
    /// Skips when <c>VRMXT_materials_override</c> would apply (spec rule 14).
    /// </summary>
    public static class VrmcMaterialsMtoonxtApplier
    {
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

            var shader = VrmxtMaterialsOverrideApplier.ResolveShader(
                VrmcMaterialsMtoonxt.BuiltinShaderName,
                resolveShader);
            if (shader == null)
            {
                return 0;
            }

            var pipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
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

        private static void ApplyStencil(Material material, VrmcMaterialsMtoonxtStencil stencil, bool outline)
        {
            if (material == null || stencil == null)
            {
                return;
            }

            var prefix = outline
                ? "_M_OutlineStencil"
                : "_M_Stencil";

            TrySetFloat(material, prefix + "Ref", stencil.Ref);
            TrySetFloat(material, prefix + "ReadMask", stencil.ReadMask);
            TrySetFloat(material, prefix + "WriteMask", stencil.WriteMask);
            TrySetFloat(material, prefix + "Comp", stencil.CompUnityInt);
            TrySetFloat(material, prefix + "Pass", stencil.PassUnityInt);
            TrySetFloat(material, prefix + "Fail", stencil.FailUnityInt);
            TrySetFloat(material, prefix + "ZFail", stencil.ZFailUnityInt);
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
