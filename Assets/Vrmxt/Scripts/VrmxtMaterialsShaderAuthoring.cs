using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

/// <summary>
/// Host helpers: list Character materials and upsert unity <c>shaderName</c> on store pairs
/// without <c>PrepareTextures</c> / full property re-authoring.
/// </summary>
public static class VrmxtMaterialsShaderAuthoring
{
    public sealed class MaterialRow
    {
        public string MaterialName;
        public string ShaderName;
        public int GltfMaterialIndex = -1;
    }

    /// <summary>
    /// Last names from <see cref="CollectMaterialRows"/> — export UI material dropdown.
    /// </summary>
    public static IReadOnlyList<string> LastMaterialNames { get; private set; } =
        Array.Empty<string>();

    /// <summary>
    /// Build rows from the override store plus live renderer materials on <paramref name="root"/>.
    /// </summary>
    public static List<MaterialRow> CollectMaterialRows(
        GameObject root,
        VrmxtMaterialsOverrideInstance store)
    {
        var rows = new List<MaterialRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (store?.Pairs != null)
        {
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                var name = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(pair.MaterialName);
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                {
                    continue;
                }

                rows.Add(new MaterialRow
                {
                    MaterialName = name,
                    ShaderName = TryGetActiveShaderName(pair) ?? string.Empty,
                    GltfMaterialIndex = pair.GltfMaterialIndex,
                });
            }
        }

        if (root == null)
        {
            LastMaterialNames = ExtractNames(rows);
            return rows;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            var mats = renderer.sharedMaterials;
            if (mats == null)
            {
                continue;
            }

            for (var m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null || string.IsNullOrEmpty(mat.name))
                {
                    continue;
                }

                var name = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(mat.name);
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                {
                    continue;
                }

                var shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                rows.Add(new MaterialRow
                {
                    MaterialName = name,
                    ShaderName = shaderName ?? string.Empty,
                    GltfMaterialIndex = -1,
                });
            }
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.MaterialName, b.MaterialName));
        LastMaterialNames = ExtractNames(rows);
        return rows;
    }

    private static IReadOnlyList<string> ExtractNames(List<MaterialRow> rows)
    {
        var names = new List<string>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            if (!string.IsNullOrEmpty(rows[i].MaterialName))
            {
                names.Add(rows[i].MaterialName);
            }
        }

        return names;
    }

    /// <summary>
    /// Set active unity slot <c>shaderName</c> on the named pair (create pair/extension if needed).
    /// Keeps sibling variants, properties, and bindings when present.
    /// </summary>
    public static bool TrySetShaderName(
        VrmxtMaterialsOverrideInstance store,
        string materialName,
        string shaderName,
        out string error)
    {
        error = null;
        if (store == null)
        {
            error = "Materials override store is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            error = "Material name is empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(shaderName))
        {
            error = "Shader name is empty.";
            return false;
        }

        shaderName = shaderName.Trim();
        materialName = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(materialName.Trim());
        var pair = FindOrCreatePair(store, materialName);
        if (pair == null)
        {
            error = "Failed to create store pair for '" + materialName + "'.";
            return false;
        }

        // Stock MToon 1.0 / VRM MToon — clear override; do not author VRMXT JSON.
        if (VrmxtMaterialsOverrideAuthoring.IsStockUnityMtoonShader(shaderName))
        {
            pair.ExtensionJson = null;
            return true;
        }

        var activePipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
        var activeVariant = UnityOverrideSelector.RenderPipelineVariantToVariantString(activePipeline);

        MaterialProvider existingProvider = null;
        IReadOnlyList<VrmxtMaterialBinding> existingBindings = Array.Empty<VrmxtMaterialBinding>();
        IReadOnlyList<VrmxtMaterialProperty> existingProperties = Array.Empty<VrmxtMaterialProperty>();
        string slotVariant = null;
        var siblings = new List<VrmxtMaterialEngineOverride>();
        VrmxtMaterialEngineOverride emptyVariantUnity = null;

        if (!string.IsNullOrWhiteSpace(pair.ExtensionJson) &&
            VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var existing))
        {
            foreach (var entry in existing.Overrides)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!string.Equals(entry.Engine, VrmxtMaterialsOverride.EngineUnity, StringComparison.Ordinal))
                {
                    siblings.Add(entry);
                    continue;
                }

                var unity = entry.Material as UnityMaterialOverride;
                if (unity == null)
                {
                    siblings.Add(entry);
                    continue;
                }

                if (string.Equals(unity.Variant, activeVariant, StringComparison.Ordinal))
                {
                    existingProvider = unity.Provider;
                    existingBindings = entry.Bindings;
                    existingProperties = VrmxtMaterialsOverrideAuthoring.WithoutTextureProperties(
                        entry.Properties);
                    slotVariant = unity.Variant;
                    continue;
                }

                if (string.IsNullOrEmpty(unity.Variant))
                {
                    emptyVariantUnity = entry;
                    continue;
                }

                siblings.Add(entry);
            }
        }

        if (slotVariant == null && emptyVariantUnity != null)
        {
            var emptyUnity = emptyVariantUnity.Material as UnityMaterialOverride;
            existingProvider = emptyUnity?.Provider;
            existingBindings = emptyVariantUnity.Bindings;
            existingProperties = VrmxtMaterialsOverrideAuthoring.WithoutTextureProperties(
                emptyVariantUnity.Properties);
            slotVariant = activeVariant;
            emptyVariantUnity = null;
        }
        else if (emptyVariantUnity != null)
        {
            siblings.Add(emptyVariantUnity);
        }

        if (slotVariant == null)
        {
            slotVariant = activeVariant;
        }

        var provider = existingProvider ?? new MaterialProvider(
            VrmxtMaterialsOverrideAuthoring.DefaultProviderId,
            "0.0.0");

        var unityMaterial = new UnityMaterialOverride(
            VrmxtMaterialsOverride.UnityMaterialIdTypeShaderName,
            shaderName,
            slotVariant,
            provider);

        var unityOverride = new VrmxtMaterialEngineOverride(
            VrmxtMaterialsOverride.EngineUnity,
            unityMaterial,
            existingBindings,
            existingProperties);

        var overrides = new List<VrmxtMaterialEngineOverride> { unityOverride };
        overrides.AddRange(siblings);

        pair.ExtensionJson = VrmxtMaterialsOverride.ToJson(
            new VrmxtMaterialsOverrideExtension(overrides));
        return true;
    }

    public static string TryGetActiveShaderName(VrmxtMaterialsOverridePair pair)
    {
        if (pair == null || string.IsNullOrWhiteSpace(pair.ExtensionJson))
        {
            return null;
        }

        if (!VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension))
        {
            return null;
        }

        var pipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
        if (!UnityOverrideSelector.TrySelectUnityOverride(extension, pipeline, out var unity))
        {
            return null;
        }

        return unity.ShaderName;
    }

    private static VrmxtMaterialsOverridePair FindOrCreatePair(
        VrmxtMaterialsOverrideInstance store,
        string materialName)
    {
        var pairs = store.Pairs;
        if (pairs != null)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                var existing = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(pair.MaterialName);
                if (string.Equals(existing, materialName, StringComparison.Ordinal))
                {
                    // Normalize stored key so UI / export stay without (Instance).
                    pair.MaterialName = materialName;
                    return pair;
                }
            }
        }

        var created = new VrmxtMaterialsOverridePair(materialName, null, -1);
        var next = new List<VrmxtMaterialsOverridePair>();
        if (pairs != null)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i] != null)
                {
                    next.Add(pairs[i]);
                }
            }
        }

        next.Add(created);
        store.SetPairs(next);
        return created;
    }
}
