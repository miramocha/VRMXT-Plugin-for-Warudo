using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Persistence;

/// <summary>
/// Warudo host Transfer: read Unity <c>.mat</c> YAML from StreamingAssets
/// (<c>VRMXT/MaterialTemplates/</c>), parse floats/colors/keywords as text, merge into
/// the active unity override. Does not change shader and does not transfer textures.
/// </summary>
public static class VrmxtMaterialsTemplateTransfer
{
    /// <summary>Warudo / Editor StreamingAssets folder (PersistentDataManager).</summary>
    public const string WarudoDataMaterialTemplatesRelative = "VRMXT/MaterialTemplates";

    public const string TextureHandlingKeepPacked = "Keep packed";
    public const string TextureHandlingClearIfSet = "Clear if set";
    public const string TextureHandlingClearAll = "Clear all";

    public static readonly string[] TextureHandlingOptions =
    {
        TextureHandlingKeepPacked,
        TextureHandlingClearIfSet,
        TextureHandlingClearAll,
    };

    public static string NormalizeTextureHandling(string textureHandling)
    {
        if (string.IsNullOrWhiteSpace(textureHandling))
        {
            return TextureHandlingKeepPacked;
        }

        var value = textureHandling.Trim();
        for (var i = 0; i < TextureHandlingOptions.Length; i++)
        {
            if (string.Equals(TextureHandlingOptions[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return TextureHandlingOptions[i];
            }
        }

        return TextureHandlingKeepPacked;
    }

    /// <summary>
    /// Open an absolute filesystem path in the OS file browser without <c>System.IO</c>.
    /// </summary>
    public static void OpenAbsolutePathInOs(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        var normalized = absolutePath.Trim().Replace('\\', '/');
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            Application.OpenURL("file:///" + normalized);
            return;
        }

        Application.OpenURL("file://" + normalized);
    }

    public static string TryGetWarudoDataMaterialTemplatesAbsolutePath()
    {
        try
        {
            if (Context.PersistentDataManager != null)
            {
                return Context.PersistentDataManager.GetFullPath(
                    WarudoDataMaterialTemplatesRelative
                );
            }
        }
        catch
        {
            // Fall through.
        }

        var root = Application.streamingAssetsPath;
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        return root.TrimEnd('/', '\\')
            + "/"
            + WarudoDataMaterialTemplatesRelative.Replace('\\', '/');
    }

    public static void EnsureTemplatesFolder(PersistentDataManager data = null)
    {
        data = data ?? Context.PersistentDataManager;
        if (data == null)
        {
            return;
        }

        var readme = WarudoDataMaterialTemplatesRelative + "/README.txt";
        if (data.HasFile(readme))
        {
            return;
        }

        data.WriteFile(
            readme,
            "VRMXT material Transfer templates.\r\n"
                + "\r\n"
                + "Drop Unity .mat files here (YAML text). Transfer parses floats, ints,\r\n"
                + "colors, and keywords only — not shader GUID, not textures.\r\n"
                + "Set Shader on the Manager row before Transfer.\r\n"
                + "\r\n"
                + "Path example: "
                + WarudoDataMaterialTemplatesRelative
                + "/MyLook.mat\r\n"
        );
    }

    /// <summary>StreamingAssets <c>*.mat</c> paths for Manager autocomplete.</summary>
    public static List<string> ListTemplatePaths()
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            EnsureTemplatesFolder();
            var data = Context.PersistentDataManager;
            if (data == null)
            {
                return paths;
            }

            foreach (
                var entry in data.GetFileEntries(WarudoDataMaterialTemplatesRelative, "*.mat")
            )
            {
                if (entry == null || string.IsNullOrEmpty(entry.path))
                {
                    continue;
                }

                if (seen.Add(entry.path))
                {
                    paths.Add(entry.path);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: list material templates failed: " + e.Message);
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    public static string FormatTemplateTextureSlotsLabel(string templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return "Template textures: (none)";
        }

        if (!TryReadTemplateYaml(templatePath, out var yaml, out _))
        {
            return "Template textures: (could not load)";
        }

        if (
            !VrmxtUnityMaterialYaml.TryParseProperties(
                yaml,
                out _,
                out var textureSlots,
                out _
            )
        )
        {
            return "Template textures: (could not parse)";
        }

        if (textureSlots == null || textureSlots.Count == 0)
        {
            return "Template textures: (none)";
        }

        var parts = new List<string>(textureSlots.Count);
        for (var i = 0; i < textureSlots.Count; i++)
        {
            parts.Add("`" + textureSlots[i] + "`");
        }

        return "Template textures (not transferred): " + string.Join(", ", parts);
    }

    public static bool TryReadTemplateYaml(string templatePath, out string yaml, out string error)
    {
        yaml = null;
        error = null;

        if (string.IsNullOrWhiteSpace(templatePath))
        {
            error = "Template path is empty.";
            return false;
        }

        var path = templatePath.Trim().Replace('\\', '/');
        var data = Context.PersistentDataManager;
        if (data == null)
        {
            error = "PersistentDataManager unavailable.";
            return false;
        }

        if (!data.HasFile(path))
        {
            error = "Template file not found at '" + path + "'.";
            return false;
        }

        try
        {
            yaml = data.ReadFile(path);
        }
        catch (Exception e)
        {
            error = "Failed to read template: " + e.Message;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parse <paramref name="templatePath"/> YAML and merge values into the active unity
    /// override. Does not change shader. <paramref name="textureHandling"/> controls packed
    /// texture rows. Pass <paramref name="root"/> to clear live texture slots when clearing.
    /// </summary>
    public static bool TryTransferValuesFromTemplatePath(
        VrmxtMaterialsOverrideInstance store,
        string materialName,
        string templatePath,
        string textureHandling,
        GameObject root,
        out string error
    )
    {
        error = null;
        textureHandling = NormalizeTextureHandling(textureHandling);

        if (store == null)
        {
            error = "Materials store is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            error = "Material name is empty.";
            return false;
        }

        if (!TryReadTemplateYaml(templatePath, out var yaml, out error))
        {
            return false;
        }

        if (
            !VrmxtUnityMaterialYaml.TryParseProperties(
                yaml,
                out var fromYaml,
                out _,
                out error
            )
        )
        {
            return false;
        }

        var pair = FindOrCreatePair(store, materialName);
        if (pair == null)
        {
            error = "Failed to resolve store pair for '" + materialName + "'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(pair.ExtensionJson))
        {
            error =
                "No unity override on '"
                + materialName
                + "'. Set Shader and Apply shader overrides first, then Transfer values.";
            return false;
        }

        if (
            !VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension)
            || !UnityOverrideSelector.TrySelectUnityEngineOverride(
                extension,
                VrmxtMaterialsOverrideApplier.DetectActivePipeline(),
                out var selected
            )
            || selected == null
        )
        {
            error =
                "No active unity override slot on '"
                + materialName
                + "'. Set Shader and Apply shader overrides first.";
            return false;
        }

        var unity = selected.Material as UnityMaterialOverride;
        if (
            unity == null
            || string.IsNullOrWhiteSpace(unity.Id)
            || VrmxtMaterialsOverrideAuthoring.IsStockUnityMtoonShader(unity.Id)
        )
        {
            error =
                "Active override is missing a non-MToon Shader. Set Shader on the row first.";
            return false;
        }

        var live = FindLiveMaterial(root, materialName);
        var merged = MergeProperties(selected.Properties, fromYaml, textureHandling, live);
        if (!TryReplaceActiveUnityProperties(pair, extension, selected, merged))
        {
            error = "Failed to write transferred properties for '" + materialName + "'.";
            return false;
        }

        if (
            !string.Equals(textureHandling, TextureHandlingKeepPacked, StringComparison.Ordinal)
            && live != null
        )
        {
            ClearLiveTextures(live, textureHandling);
        }

        return true;
    }

    /// <summary>
    /// Merge YAML values with existing override props under <paramref name="textureHandling"/>.
    /// </summary>
    public static List<VrmxtMaterialProperty> MergeProperties(
        IReadOnlyList<VrmxtMaterialProperty> existing,
        IReadOnlyList<VrmxtMaterialProperty> fromYaml,
        string textureHandling,
        Material live
    )
    {
        textureHandling = NormalizeTextureHandling(textureHandling);
        var list = new List<VrmxtMaterialProperty>();

        if (
            string.Equals(textureHandling, TextureHandlingKeepPacked, StringComparison.Ordinal)
            && existing != null
        )
        {
            for (var i = 0; i < existing.Count; i++)
            {
                var property = existing[i];
                if (
                    property != null
                    && string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    list.Add(property);
                }
            }
        }
        else if (
            string.Equals(textureHandling, TextureHandlingClearIfSet, StringComparison.Ordinal)
            && existing != null
        )
        {
            // Keep packed texture rows only when the live slot is empty.
            for (var i = 0; i < existing.Count; i++)
            {
                var property = existing[i];
                if (
                    property == null
                    || !string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                if (LiveSlotHasTexture(live, property.Name))
                {
                    continue;
                }

                list.Add(property);
            }
        }
        // Clear all: keep no texture rows.

        if (fromYaml != null)
        {
            for (var i = 0; i < fromYaml.Count; i++)
            {
                if (fromYaml[i] != null)
                {
                    list.Add(fromYaml[i]);
                }
            }
        }

        return list;
    }

    /// <summary>Null live texture slots for clear modes.</summary>
    public static void ClearLiveTextures(Material live, string textureHandling)
    {
        if (live == null || live.shader == null)
        {
            return;
        }

        textureHandling = NormalizeTextureHandling(textureHandling);
        if (string.Equals(textureHandling, TextureHandlingKeepPacked, StringComparison.Ordinal))
        {
            return;
        }

        var clearAll = string.Equals(
            textureHandling,
            TextureHandlingClearAll,
            StringComparison.Ordinal
        );
        var shader = live.shader;
        var count = shader.GetPropertyCount();
        for (var i = 0; i < count; i++)
        {
            if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                continue;
            }

            var name = shader.GetPropertyName(i);
            if (string.IsNullOrEmpty(name) || !live.HasProperty(name))
            {
                continue;
            }

            if (!clearAll && live.GetTexture(name) == null)
            {
                continue;
            }

            live.SetTexture(name, null);
        }
    }

    private static bool LiveSlotHasTexture(Material live, string propertyName)
    {
        return live != null
            && !string.IsNullOrEmpty(propertyName)
            && live.HasProperty(propertyName)
            && live.GetTexture(propertyName) != null;
    }

    private static Material FindLiveMaterial(GameObject root, string materialName)
    {
        if (root == null || string.IsNullOrWhiteSpace(materialName))
        {
            return null;
        }

        foreach (
            var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                root,
                materialName
            )
        )
        {
            if (material != null && material.shader != null)
            {
                return material;
            }
        }

        return null;
    }

    public static bool StripTexturePropertiesFromActiveUnity(VrmxtMaterialsOverridePair pair)
    {
        if (
            pair == null
            || string.IsNullOrWhiteSpace(pair.ExtensionJson)
            || !VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension)
            || !UnityOverrideSelector.TrySelectUnityEngineOverride(
                extension,
                VrmxtMaterialsOverrideApplier.DetectActivePipeline(),
                out var selected
            )
            || selected == null
        )
        {
            return false;
        }

        var kept = VrmxtMaterialsOverrideAuthoring.WithoutTextureProperties(selected.Properties);
        return TryReplaceActiveUnityProperties(pair, extension, selected, kept);
    }

    private static bool TryReplaceActiveUnityProperties(
        VrmxtMaterialsOverridePair pair,
        VrmxtMaterialsOverrideExtension extension,
        VrmxtMaterialEngineOverride selected,
        IReadOnlyList<VrmxtMaterialProperty> properties
    )
    {
        var rebuilt = new List<VrmxtMaterialEngineOverride>();
        var replaced = false;
        for (var i = 0; i < extension.Overrides.Count; i++)
        {
            var entry = extension.Overrides[i];
            if (entry == null)
            {
                continue;
            }

            if (!ReferenceEquals(entry, selected))
            {
                rebuilt.Add(entry);
                continue;
            }

            rebuilt.Add(
                new VrmxtMaterialEngineOverride(
                    entry.Engine,
                    entry.Material,
                    entry.Bindings,
                    properties
                )
            );
            replaced = true;
        }

        if (!replaced)
        {
            return false;
        }

        pair.ExtensionJson = VrmxtMaterialsOverride.ToJson(
            new VrmxtMaterialsOverrideExtension(rebuilt)
        );
        return true;
    }

    private static VrmxtMaterialsOverridePair FindOrCreatePair(
        VrmxtMaterialsOverrideInstance store,
        string materialName
    )
    {
        var key = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(materialName);
        if (store.Pairs != null)
        {
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                var existing = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(
                    pair.MaterialName
                );
                if (string.Equals(existing, key, StringComparison.Ordinal))
                {
                    return pair;
                }
            }
        }

        var created = new VrmxtMaterialsOverridePair(key, null);
        var list = new List<VrmxtMaterialsOverridePair>();
        if (store.Pairs != null)
        {
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                if (store.Pairs[i] != null)
                {
                    list.Add(store.Pairs[i]);
                }
            }
        }

        list.Add(created);
        store.SetPairs(list);
        return created;
    }
}
