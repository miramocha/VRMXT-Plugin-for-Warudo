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
        private static readonly VrmxtMaterialsMtoonxtStencil StencilOff =
            new VrmxtMaterialsMtoonxtStencil(false, 0, 255, 255, "always", "keep", "keep", "keep");

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

            var extrasByIndex = BuildExtrasByIndex(gltfRoot, store, root);
            VrmxtMaterialsMtoonxtStencilCompiler.Compile(
                extrasByIndex,
                out var compiledBody,
                out var compiledOutline
            );
            var gpuBase = AcquireGpuBase(root, compiledBody, compiledOutline);

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

                var gltfIndex = pair.GltfMaterialIndex;
                VrmxtMaterialsMtoonxtStencil bodyStencil = null;
                VrmxtMaterialsMtoonxtStencil outlineStencil = null;
                if (gltfIndex >= 0 && gltfIndex < compiledBody.Length)
                {
                    bodyStencil = compiledBody[gltfIndex];
                    outlineStencil = compiledOutline[gltfIndex];
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
                    ApplyStencil(material, bodyStencil, outline: false, gpuBase);
                    ApplyStencil(material, outlineStencil, outline: true, gpuBase);
                    ApplyZTest(material, xt.ZTest);
                    ApplyZWrite(material, xt.ZWrite);
                    var bodyOverlay = UsesOverlayDepth(xt);
                    var outlineOverlay = UsesOutlineOverlayDepth(xt);
                    if (bodyOverlay)
                    {
                        ApplyZTest(material, "always");
                        ApplyZWrite(material, false);
                    }

                    ApplyStencilDrawOrder(material, bodyStencil, bodyOverlay);
                    SetKeyword(
                        material,
                        VrmxtMaterialsMtoonxt.OverlayDepthKeyword,
                        bodyOverlay
                    );
                    SetKeyword(
                        material,
                        VrmxtMaterialsMtoonxt.OutlineOverlayDepthKeyword,
                        outlineOverlay
                    );
                    ApplyOverlayColorPasses(material, bodyOverlay, outlineOverlay);
                    swappedAny = true;
                }

                if (swappedAny)
                {
                    applied++;
                }
            }

            return applied;
        }

        private static int AcquireGpuBase(
            GameObject root,
            VrmxtMaterialsMtoonxtStencil[] compiledBody,
            VrmxtMaterialsMtoonxtStencil[] compiledOutline
        )
        {
            if (root == null)
            {
                return 0;
            }

            var span = MaxEnabledRef(compiledBody, compiledOutline);
            if (span < 1)
            {
                VrmxtMaterialsMtoonxtStencilRefs.Release(root.GetInstanceID());
                return 0;
            }

            return VrmxtMaterialsMtoonxtStencilRefs.Acquire(root.GetInstanceID(), span);
        }

        private static int MaxEnabledRef(
            VrmxtMaterialsMtoonxtStencil[] compiledBody,
            VrmxtMaterialsMtoonxtStencil[] compiledOutline
        )
        {
            var max = 0;
            MaxEnabledRef(compiledBody, ref max);
            MaxEnabledRef(compiledOutline, ref max);
            return max;
        }

        private static void MaxEnabledRef(VrmxtMaterialsMtoonxtStencil[] compiled, ref int max)
        {
            if (compiled == null)
            {
                return;
            }

            for (var i = 0; i < compiled.Length; i++)
            {
                var stencil = compiled[i];
                if (stencil == null || !stencil.Enabled || stencil.Ref <= max)
                {
                    continue;
                }

                max = stencil.Ref;
            }
        }

        private static VrmxtMaterialsMtoonxtExtension[] BuildExtrasByIndex(
            JObject gltfRoot,
            VrmxtMaterialsMtoonxtInstance store,
            GameObject root
        )
        {
            var materials = gltfRoot["materials"] as JArray;
            var count = materials != null ? materials.Count : 0;
            var extras = new VrmxtMaterialsMtoonxtExtension[count];
            var pairs = store.Pairs;
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair == null || pair.GltfMaterialIndex < 0 || pair.GltfMaterialIndex >= count)
                {
                    continue;
                }

                var xt = VrmxtMaterialsMtoonxtAuthoring.ToExtension(root, store, pair);
                if (xt != null)
                {
                    extras[pair.GltfMaterialIndex] = xt;
                }
            }

            return extras;
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
            ApplyStencil(material, StencilOff, outline: false, 0);
            ApplyStencil(material, StencilOff, outline: true, 0);
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

        /// <summary>
        /// Body <c>insideOverlay</c> only. Outline overlay must not force body Always /
        /// overlay keyword (URP/BIRP share one material).
        /// </summary>
        public static bool UsesOverlayDepth(VrmxtMaterialsMtoonxtExtension xt)
        {
            return IsInsideOverlay(xt != null ? xt.Stencil : null);
        }

        public static bool UsesOutlineOverlayDepth(VrmxtMaterialsMtoonxtExtension xt)
        {
            if (xt == null)
            {
                return false;
            }

            if (
                xt.OutlineStencil == null
                || string.Equals(
                    xt.OutlineStencil.Op,
                    VrmxtMaterialsMtoonxtStencil.OpSame,
                    StringComparison.Ordinal
                )
            )
            {
                return UsesOverlayDepth(xt);
            }

            return IsInsideOverlay(xt.OutlineStencil);
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

        private static bool IsInsideOverlay(VrmxtMaterialsMtoonxtStencil stencil)
        {
            return stencil != null
                && string.Equals(
                    stencil.Op,
                    VrmxtMaterialsMtoonxtStencil.OpInsideOverlay,
                    StringComparison.Ordinal
                );
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

        /// <summary>
        /// Cutout face meshes often list iris before sclera. Shift body <c>write</c>
        /// two Unity queue slots and <c>inside</c> one slot before the mapped queue so
        /// the stamp lands, then iris clips, then skin/eyelids at the mapped slot can
        /// cover leftover iris-card pixels. <c>insideOverlay</c> one slot after mapped
        /// so it paints after occluders and does not punch their Z.
        /// <c>outside</c> (hair punch) stays mapped.
        /// </summary>
        public static void ApplyStencilDrawOrder(
            Material material,
            VrmxtMaterialsMtoonxtStencil compiledBody,
            bool overlay = false
        )
        {
            if (material == null || compiledBody == null || !compiledBody.Enabled)
            {
                return;
            }

            var delta = 0;
            if (overlay)
            {
                delta = 1;
            }
            else if (string.Equals(compiledBody.Pass, "replace", StringComparison.Ordinal))
            {
                delta = -2;
            }
            else if (
                string.Equals(compiledBody.Comp, "equal", StringComparison.Ordinal)
                && string.Equals(compiledBody.Pass, "keep", StringComparison.Ordinal)
            )
            {
                delta = -1;
            }

            if (delta == 0)
            {
                return;
            }

            var next = (long)material.renderQueue + delta;
            if (next < 0 || next > 5000)
            {
                return;
            }

            material.renderQueue = (int)next;
        }

        public static void ApplyZWrite(Material material, bool? zWrite)
        {
            if (material == null || !zWrite.HasValue)
            {
                return;
            }

            TrySetFloat(material, "_M_ZWrite", zWrite.Value ? 1f : 0f);
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
                ApplyStencil(material, StencilOff, outline: false, 0);
            }
            else if (IsUninitializedComp(material, VrmxtMaterialsMtoonxt.StencilPropComp))
            {
                TrySetFloat(material, VrmxtMaterialsMtoonxt.StencilPropComp, 8f);
            }

            if (!IsEnabled(material, VrmxtMaterialsMtoonxt.OutlineStencilPropEnabled))
            {
                ApplyStencil(material, StencilOff, outline: true, 0);
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

        private static void ApplyStencil(
            Material material,
            VrmxtMaterialsMtoonxtStencil stencil,
            bool outline,
            int gpuBase
        )
        {
            if (material == null || stencil == null)
            {
                return;
            }

            var applied = stencil.Enabled ? stencil : StencilOff;
            var prefix = outline ? "_M_OutlineStencil" : "_M_Stencil";

            TrySetFloat(material, prefix + "Enabled", applied.Enabled ? 1f : 0f);
            TrySetFloat(
                material,
                prefix + "Ref",
                applied.Enabled
                    ? VrmxtMaterialsMtoonxtStencilRefs.GpuRef(applied.Ref, gpuBase)
                    : applied.Ref
            );
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
