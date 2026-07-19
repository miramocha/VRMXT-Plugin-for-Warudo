using System;
using UniVRMXT.Format;

namespace UniVRMXT.MaterialsOverride
{
    public enum RenderPipelineVariant
    {
        Builtin,
        Urp,
        Hdrp,
    }

    public static class UnityOverrideSelector
    {
        /// <summary>
        /// Pick at most one <c>unity</c> slot for the active render pipeline:
        /// (1) exact <c>material.variant</c> match, else (2) exactly one empty/omitted
        /// variant entry, else none (stock import).
        /// </summary>
        public static bool TrySelectUnityOverride(
            VrmxtMaterialsOverrideExtension extension,
            RenderPipelineVariant activePipeline,
            out UnityMaterialOverride unityOverride)
        {
            unityOverride = null;
            if (!TrySelectUnityEngineOverride(extension, activePipeline, out var selected))
            {
                return false;
            }

            unityOverride = selected.Material as UnityMaterialOverride;
            return unityOverride != null;
        }

        /// <summary>
        /// Same selection as <see cref="TrySelectUnityOverride"/> but returns the full
        /// engine override (bindings / properties).
        /// </summary>
        public static bool TrySelectUnityEngineOverride(
            VrmxtMaterialsOverrideExtension extension,
            RenderPipelineVariant activePipeline,
            out VrmxtMaterialEngineOverride selected)
        {
            selected = null;
            if (extension == null ||
                !VrmxtMaterialsOverride.TryGetUnityOverrides(extension, out var candidates))
            {
                return false;
            }

            var activeVariant = RenderPipelineVariantToVariantString(activePipeline);
            VrmxtMaterialEngineOverride exact = null;
            VrmxtMaterialEngineOverride emptyVariant = null;
            var emptyCount = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                var entry = candidates[i];
                var unity = entry.Material as UnityMaterialOverride;
                if (unity == null ||
                    !string.Equals(
                        unity.IdType,
                        VrmxtMaterialsOverride.UnityMaterialIdTypeShaderName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(unity.Variant))
                {
                    emptyCount++;
                    emptyVariant = entry;
                    continue;
                }

                if (string.Equals(unity.Variant, activeVariant, StringComparison.Ordinal))
                {
                    exact = entry;
                }
            }

            if (exact != null)
            {
                selected = exact;
                return true;
            }

            // Single empty/omitted variant matches any active pipeline.
            if (emptyCount == 1)
            {
                selected = emptyVariant;
                return true;
            }

            return false;
        }

        public static string RenderPipelineVariantToVariantString(RenderPipelineVariant pipeline)
        {
            switch (pipeline)
            {
                case RenderPipelineVariant.Urp:
                    return "urp";
                case RenderPipelineVariant.Hdrp:
                    return "hdrp";
                default:
                    return "builtin";
            }
        }

        public static bool IsVariantCompatible(string variant, RenderPipelineVariant activePipeline)
        {
            if (string.IsNullOrEmpty(variant))
            {
                return true;
            }

            if (string.Equals(variant, "builtin", StringComparison.Ordinal))
            {
                return activePipeline == RenderPipelineVariant.Builtin;
            }

            if (string.Equals(variant, "urp", StringComparison.Ordinal))
            {
                return activePipeline == RenderPipelineVariant.Urp;
            }

            if (string.Equals(variant, "hdrp", StringComparison.Ordinal))
            {
                return activePipeline == RenderPipelineVariant.Hdrp;
            }

            return false;
        }
    }
}
