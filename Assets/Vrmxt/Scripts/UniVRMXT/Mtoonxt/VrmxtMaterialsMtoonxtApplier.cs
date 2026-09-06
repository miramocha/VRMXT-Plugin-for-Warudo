using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Swap stock MToon to the pipeline MToonXT shader and write stencil extras.
    /// Skips when <c>VRMXT_materials_override</c> would apply (spec rule 14).
    /// </summary>
    public static class VrmxtMaterialsMtoonxtApplier
    {
        public static int Apply(
            GameObject root,
            string gltfJson,
            Func<string, Shader> resolveShader = null
        )
        {
            VrmxtMaterialsMtoonxtRuntime.TryAttachFromGltfJson(root, gltfJson, out var store);
            return Apply(root, store, gltfJson, resolveShader);
        }

        public static int Apply(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            string gltfJson,
            Func<string, Shader> resolveShader = null
        )
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

            var shader = VrmxtMaterialsOverrideApplier.ResolveShader(shaderName, resolveShader);
            if (shader == null)
            {
                return 0;
            }

            var materials = gltfRoot["materials"] as JArray;
            var materialCount = materials != null ? materials.Count : 0;
            VrmxtMaterialsMtoonxtStencils.TryParseRoot(gltfRoot, materialCount, out var stencils);
            var plans = VrmxtMaterialsMtoonxtStencilCompiler.Compile(stencils, 1);
            var gpuBase = AcquireGpuBase(root, plans.Count);

            var applied = 0;
            var pairs = store.Pairs;
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair == null)
                {
                    continue;
                }

                var xt = VrmxtMaterialsMtoonxtAuthoring.ToExtension(root, store, pair);
                if (xt == null)
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
                foreach (
                    var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                        root,
                        pair.MaterialName
                    )
                )
                {
                    if (material == null || (material.hideFlags & HideFlags.DontSave) != 0)
                    {
                        continue;
                    }

                    material.shader = shader;
                    // Shader switch leaves new floats at 0 (Disabled Comp / ZTest)
                    // and resets Queue/keywords to the ShaderLab tags (Opaque/Geometry).
                    RestoreUnityMtoonPassSettings(material);
                    ApplyStencilOffDefaults(material);
                    ApplyZTest(material, xt.ZTest);
                    ApplyZWrite(material, xt.ZWrite);
                    swappedAny = true;
                }

                if (swappedAny)
                {
                    applied++;
                }
            }

            VrmxtMaterialsMtoonxtStencilApplier.Apply(root, store, plans, gpuBase);
            return applied;
        }

        /// <summary>
        /// Rebuild stencil material state and auxiliary passes from the serialized
        /// authoring component. Unity does not serialize the retained command-buffer draw list,
        /// so imported roots must call this after domain reload and scene/object enable.
        /// </summary>
        public static int ReapplyStencils(GameObject root, VrmxtMaterialsMtoonxtInstance store)
        {
            if (root == null || store == null)
            {
                return 0;
            }

            var stencils = VrmxtMaterialsMtoonxtAuthoring.ToStencils(root, store);
            var plans = VrmxtMaterialsMtoonxtStencilCompiler.Compile(stencils, 1);
            var gpuBase = AcquireGpuBase(root, plans.Count);
            return VrmxtMaterialsMtoonxtStencilApplier.Apply(root, store, plans, gpuBase);
        }

        private static int AcquireGpuBase(GameObject root, int span)
        {
            if (root == null)
            {
                return 0;
            }

            if (span < 1)
            {
                VrmxtMaterialsMtoonxtStencilRefs.Release(root.GetInstanceID());
                return 0;
            }

            return VrmxtMaterialsMtoonxtStencilRefs.Acquire(root.GetInstanceID(), span);
        }

        private static bool TryGetMaterialObject(
            JObject gltfRoot,
            int index,
            out JObject materialObject
        )
        {
            materialObject = null;
            if (
                gltfRoot == null
                || !gltfRoot.TryGetValue(
                    "materials",
                    StringComparison.Ordinal,
                    out var materialsToken
                )
            )
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
            if (
                materialObject == null
                || !materialObject.TryGetValue(
                    "extensions",
                    StringComparison.Ordinal,
                    out var extensionsToken
                )
            )
            {
                return false;
            }

            var extensions = extensionsToken as JObject;
            if (
                extensions == null
                || !extensions.TryGetValue(
                    VrmxtMaterialsMtoonxt.SiblingMtoonExtensionName,
                    StringComparison.Ordinal,
                    out var mtoonToken
                )
            )
            {
                return false;
            }

            return mtoonToken as JObject != null;
        }

        private static bool WouldMaterialsOverrideApply(
            JObject materialObject,
            RenderPipelineVariant pipeline,
            Func<string, Shader> resolveShader
        )
        {
            if (
                materialObject == null
                || !materialObject.TryGetValue(
                    "extensions",
                    StringComparison.Ordinal,
                    out var extensionsToken
                )
            )
            {
                return false;
            }

            var extensions = extensionsToken as JObject;
            if (
                extensions == null
                || !extensions.TryGetValue(
                    VrmxtMaterialsOverride.ExtensionName,
                    StringComparison.Ordinal,
                    out var overrideToken
                )
            )
            {
                return false;
            }

            if (!VrmxtMaterialsOverride.TryParse(overrideToken, out var extension))
            {
                return false;
            }

            if (
                !UnityOverrideSelector.TrySelectUnityEngineOverride(
                    extension,
                    pipeline,
                    out var engineOverride
                )
            )
            {
                return false;
            }

            var unity = engineOverride.Material as UnityMaterialOverride;
            if (unity == null || string.IsNullOrEmpty(unity.ShaderName))
            {
                return false;
            }

            return VrmxtMaterialsOverrideApplier.ResolveShader(unity.ShaderName, resolveShader)
                != null;
        }

        public static string ShaderNameForPipeline(RenderPipelineVariant pipeline)
        {
            switch (pipeline)
            {
                case RenderPipelineVariant.Urp:
                    return VrmxtMaterialsMtoonxt.UrpShaderName;
                case RenderPipelineVariant.Builtin:
                    return VrmxtMaterialsMtoonxt.BuiltinShaderName;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Spec stencil-off: Always / Keep. Comp 0 is Unity Disabled and hides the mesh.
        /// </summary>
        public static void ApplyStencilOffDefaults(Material material)
        {
            ApplyStencilGpu(material, outline: false, enabled: false, 0, 0, "always", "keep");
            ApplyStencilGpu(material, outline: true, enabled: false, 0, 0, "always", "keep");
        }

        /// <summary>
        /// Re-apply stock MToon blend / ZWrite / cull / queue / keywords from
        /// <c>_AlphaMode</c> after a shader swap. Same mapping as UniVRM
        /// <c>MToonValidator</c> without a VRM10 assembly reference.
        /// </summary>
        public static void RestoreUnityMtoonPassSettings(Material material)
        {
            if (material == null || !material.HasProperty("_AlphaMode"))
            {
                return;
            }

            var alphaMode = material.GetInt("_AlphaMode");
            var zWriteOn =
                material.HasProperty("_TransparentWithZWrite")
                && material.GetInt("_TransparentWithZWrite") != 0;
            var renderQueueOffset = material.HasProperty("_RenderQueueOffset")
                ? material.GetInt("_RenderQueueOffset")
                : 0;
            var doubleSided =
                material.HasProperty("_DoubleSided") && material.GetInt("_DoubleSided") != 0;

            switch (alphaMode)
            {
                case 1:
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    TrySetFloat(material, "_M_SrcBlend", (float)BlendMode.One);
                    TrySetFloat(material, "_M_DstBlend", (float)BlendMode.Zero);
                    TrySetFloat(material, "_M_ZWrite", 1f);
                    TrySetFloat(material, "_M_AlphaToMask", 1f);
                    renderQueueOffset = 0;
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    break;
                case 2 when zWriteOn:
                    material.SetOverrideTag("RenderType", "Transparent");
                    TrySetFloat(material, "_M_SrcBlend", (float)BlendMode.SrcAlpha);
                    TrySetFloat(material, "_M_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    TrySetFloat(material, "_M_ZWrite", 1f);
                    TrySetFloat(material, "_M_AlphaToMask", 0f);
                    renderQueueOffset = Mathf.Clamp(renderQueueOffset, 0, 9);
                    material.renderQueue = (int)RenderQueue.GeometryLast + 1 + renderQueueOffset;
                    break;
                case 2:
                    material.SetOverrideTag("RenderType", "Transparent");
                    TrySetFloat(material, "_M_SrcBlend", (float)BlendMode.SrcAlpha);
                    TrySetFloat(material, "_M_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    TrySetFloat(material, "_M_ZWrite", 0f);
                    TrySetFloat(material, "_M_AlphaToMask", 0f);
                    renderQueueOffset = Mathf.Clamp(renderQueueOffset, -9, 0);
                    material.renderQueue = (int)RenderQueue.Transparent + renderQueueOffset;
                    break;
                default:
                    material.SetOverrideTag("RenderType", "Opaque");
                    TrySetFloat(material, "_M_SrcBlend", (float)BlendMode.One);
                    TrySetFloat(material, "_M_DstBlend", (float)BlendMode.Zero);
                    TrySetFloat(material, "_M_ZWrite", 1f);
                    TrySetFloat(material, "_M_AlphaToMask", 0f);
                    renderQueueOffset = 0;
                    material.renderQueue = (int)RenderQueue.Geometry;
                    break;
            }

            TrySetFloat(material, "_M_CullMode", doubleSided ? 0f : 2f);
            TrySetFloat(material, "_M_ColorMask", 15f);
            SetKeyword(material, VrmxtMaterialsMtoonxt.OverlayDepthKeyword, false);
            SetKeyword(material, VrmxtMaterialsMtoonxt.OutlineOverlayDepthKeyword, false);
            ApplyOverlayColorPasses(material, bodyOverlay: false, outlineOverlay: false);
            if (material.HasProperty("_RenderQueueOffset"))
            {
                material.SetInt("_RenderQueueOffset", renderQueueOffset);
            }

            SetKeyword(material, "_ALPHATEST_ON", alphaMode == 1);
            SetKeyword(material, "_ALPHABLEND_ON", alphaMode == 2);
            SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
            SetKeyword(
                material,
                "_NORMALMAP",
                material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null
            );
            SetKeyword(
                material,
                "_MTOON_EMISSIVEMAP",
                material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap") != null
            );
            SetKeyword(
                material,
                "_MTOON_RIMMAP",
                (material.HasProperty("_MatcapTex") && material.GetTexture("_MatcapTex") != null)
                    || (material.HasProperty("_RimTex") && material.GetTexture("_RimTex") != null)
            );
            SetKeyword(
                material,
                "_MTOON_PARAMETERMAP",
                (
                    material.HasProperty("_ShadingShiftTex")
                    && material.GetTexture("_ShadingShiftTex") != null
                )
                    || (
                        material.HasProperty("_OutlineWidthTex")
                        && material.GetTexture("_OutlineWidthTex") != null
                    )
                    || (
                        material.HasProperty("_UvAnimMaskTex")
                        && material.GetTexture("_UvAnimMaskTex") != null
                    )
            );

            var outlineMode = material.HasProperty("_OutlineWidthMode")
                ? material.GetInt("_OutlineWidthMode")
                : 0;
            SetKeyword(material, "_MTOON_OUTLINE_WORLD", outlineMode == 1);
            SetKeyword(material, "_MTOON_OUTLINE_SCREEN", outlineMode == 2);
        }

        public static void ApplyOverlayColorPasses(
            Material material,
            bool bodyOverlay,
            bool outlineOverlay
        )
        {
            if (material == null)
            {
                return;
            }

            SetShaderPassEnabled(material, VrmxtMaterialsMtoonxt.PassMtoonForward, !bodyOverlay);
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassUniversalForwardOverlay,
                bodyOverlay
            );
            SetShaderPassEnabled(material, VrmxtMaterialsMtoonxt.PassForwardBase, !bodyOverlay);
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassForwardBaseOverlay,
                bodyOverlay
            );
            SetShaderPassEnabled(material, VrmxtMaterialsMtoonxt.PassForwardAdd, !bodyOverlay);
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassForwardAddOverlay,
                bodyOverlay
            );
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassMtoonOutlineMain,
                !outlineOverlay
            );
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassMtoonOutlineOverlay,
                outlineOverlay
            );
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassForwardBaseOutline,
                !outlineOverlay
            );
            SetShaderPassEnabled(
                material,
                VrmxtMaterialsMtoonxt.PassForwardBaseOutlineOverlay,
                outlineOverlay
            );
        }

        private static void SetShaderPassEnabled(Material material, string passName, bool enabled)
        {
            material.SetShaderPassEnabled(passName, enabled);
        }

        public static void ApplyZTest(Material material, string zTest)
        {
            if (material == null)
            {
                return;
            }

            if (
                VrmxtMaterialsMtoonxt.TryMapCompareFunction(zTest, out var unityInt)
                && unityInt != 0
            )
            {
                TrySetFloat(material, VrmxtMaterialsMtoonxt.ZTestProp, unityInt);
                return;
            }

            if (IsUninitializedComp(material, VrmxtMaterialsMtoonxt.ZTestProp))
            {
                TrySetFloat(material, VrmxtMaterialsMtoonxt.ZTestProp, DefaultZTestUnityFloat());
            }
        }

        public static void ApplyZWrite(Material material, bool? zWrite)
        {
            if (material == null || !zWrite.HasValue)
            {
                return;
            }

            TrySetFloat(material, "_M_ZWrite", zWrite.Value ? 1f : 0f);
        }

        public static void ApplyStencilPass(
            Material material,
            VrmxtMtoonxtStencilPass pass,
            int localRef,
            int gpuBase
        )
        {
            if (material == null || pass == null)
            {
                return;
            }

            ApplyStencilGpu(material, outline: false, enabled: true, localRef, gpuBase, pass.Comp, pass.Pass);
            ApplyStencilGpu(material, outline: true, enabled: true, localRef, gpuBase, pass.Comp, pass.Pass);
            ApplyZTest(material, pass.ZTest);
            ApplyZWrite(material, pass.ZWrite);
            var doubleSided =
                material.HasProperty("_DoubleSided") && material.GetInt("_DoubleSided") != 0;
            TrySetFloat(material, "_M_CullMode", pass.CullBack && !doubleSided ? 2f : 0f);
            TrySetFloat(material, "_M_ColorMask", pass.WriteColor ? 15f : 0f);
            SetKeyword(material, VrmxtMaterialsMtoonxt.OverlayDepthKeyword, false);
            SetKeyword(material, VrmxtMaterialsMtoonxt.OutlineOverlayDepthKeyword, false);
            ApplyOverlayColorPasses(material, bodyOverlay: false, outlineOverlay: false);
        }

        /// <summary>
        /// Shader switch / Unity enum Disabled leave Comp / ZTest at 0. Restore those
        /// floats only.
        /// </summary>
        public static void EnsureStencilOffIfUninitialized(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (!IsEnabled(material, VrmxtMaterialsMtoonxt.StencilPropEnabled))
            {
                ApplyStencilGpu(material, outline: false, enabled: false, 0, 0, "always", "keep");
            }
            else if (IsUninitializedComp(material, VrmxtMaterialsMtoonxt.StencilPropComp))
            {
                TrySetFloat(material, VrmxtMaterialsMtoonxt.StencilPropComp, 8f);
            }

            if (!IsEnabled(material, VrmxtMaterialsMtoonxt.OutlineStencilPropEnabled))
            {
                ApplyStencilGpu(material, outline: true, enabled: false, 0, 0, "always", "keep");
            }
            else if (IsUninitializedComp(material, VrmxtMaterialsMtoonxt.OutlineStencilPropComp))
            {
                TrySetFloat(material, VrmxtMaterialsMtoonxt.OutlineStencilPropComp, 8f);
            }

            if (IsUninitializedComp(material, VrmxtMaterialsMtoonxt.ZTestProp))
            {
                ApplyZTest(material, VrmxtMaterialsMtoonxt.ZTestDefault);
            }
        }

        private static float DefaultZTestUnityFloat()
        {
            VrmxtMaterialsMtoonxt.TryMapCompareFunction(
                VrmxtMaterialsMtoonxt.ZTestDefault,
                out var unityInt
            );
            return unityInt;
        }

        private static bool IsEnabled(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) && material.GetFloat(propertyName) >= 0.5f;
        }

        private static bool IsUninitializedComp(Material material, string propertyName)
        {
            return material.HasProperty(propertyName)
                && Mathf.Approximately(material.GetFloat(propertyName), 0f);
        }

        private static void ApplyStencilGpu(
            Material material,
            bool outline,
            bool enabled,
            int localRef,
            int gpuBase,
            string comp,
            string pass
        )
        {
            if (material == null)
            {
                return;
            }

            var prefix = outline ? "_M_OutlineStencil" : "_M_Stencil";
            VrmxtMaterialsMtoonxt.TryMapCompareFunction(comp, out var compUnity);
            VrmxtMaterialsMtoonxt.TryMapStencilOp(pass, out var passUnity);
            VrmxtMaterialsMtoonxt.TryMapStencilOp("keep", out var keepUnity);

            TrySetFloat(material, prefix + "Enabled", enabled ? 1f : 0f);
            TrySetFloat(
                material,
                prefix + "Ref",
                enabled ? VrmxtMaterialsMtoonxtStencilRefs.GpuRef(localRef, gpuBase) : 0f
            );
            TrySetFloat(material, prefix + "ReadMask", 255f);
            TrySetFloat(material, prefix + "WriteMask", 255f);
            TrySetFloat(material, prefix + "Comp", enabled ? compUnity : 8f);
            TrySetFloat(material, prefix + "Pass", enabled ? passUnity : keepUnity);
            TrySetFloat(material, prefix + "Fail", keepUnity);
            TrySetFloat(material, prefix + "ZFail", keepUnity);
        }

        private static void TrySetFloat(Material material, string name, float value)
        {
            if (material.HasProperty(name))
            {
                material.SetFloat(name, value);
            }
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }
    }
}
