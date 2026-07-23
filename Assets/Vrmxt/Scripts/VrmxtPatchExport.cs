using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;

/// <summary>
/// VRMXT patch export: rewrite <c>VRMXT_materials_override</c> into a copy of
/// the original local Character GLB without UniVRM export or new BIN payloads.
/// </summary>
public static class VrmxtPatchExport
{
    public const string DefaultFileSuffix = ".vrmxt";

    /// <summary>
    /// One store entry staged for injection into glTF <c>materials[]</c>.
    /// </summary>
    public sealed class MaterialEntry
    {
        public string MaterialName;
        public string ExtensionJson;
        public int GltfMaterialIndex = -1;

        public MaterialEntry()
        {
        }

        public MaterialEntry(string materialName, string extensionJson, int gltfMaterialIndex)
        {
            MaterialName = materialName;
            ExtensionJson = extensionJson;
            GltfMaterialIndex = gltfMaterialIndex;
        }
    }

    public sealed class RewriteResult
    {
        public bool Success;
        public string Json;
        public int WrittenCount;
        public readonly List<string> Skipped = new List<string>();
        public string Error;
    }

    public sealed class PathResult
    {
        public bool Success;
        public string SourceRelativePath;
        public string OutputRelativePath;
        public string Error;
    }

    /// <summary>
    /// Build <c>Characters/Foo.vrmxt.vrm</c> from <c>Characters/Foo.vrm</c> and a suffix.
    /// Rejects when output would equal source.
    /// </summary>
    public static PathResult TryBuildOutputPath(string sourceRelativePath, string fileSuffix)
    {
        var result = new PathResult();
        if (string.IsNullOrWhiteSpace(sourceRelativePath))
        {
            result.Error = "Source path is empty.";
            return result;
        }

        var source = sourceRelativePath.Replace('\\', '/').Trim();
        if (!source.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "Source path is not a .vrm file.";
            return result;
        }

        var suffix = NormalizeFileSuffix(fileSuffix);
        if (suffix == null)
        {
            result.Error = "Export file suffix is invalid.";
            return result;
        }

        var stem = source.Substring(0, source.Length - 4);
        var output = stem + suffix + ".vrm";
        if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "Output path must differ from source path.";
            return result;
        }

        result.Success = true;
        result.SourceRelativePath = source;
        result.OutputRelativePath = output;
        return result;
    }

    /// <summary>
    /// Ensure leading dot; strip path separators and trailing <c>.vrm</c>.
    /// Returns null when empty after normalize.
    /// </summary>
    public static string NormalizeFileSuffix(string fileSuffix)
    {
        if (string.IsNullOrWhiteSpace(fileSuffix))
        {
            fileSuffix = DefaultFileSuffix;
        }

        var suffix = fileSuffix.Trim().Replace('\\', '/');
        while (suffix.StartsWith("/", StringComparison.Ordinal))
        {
            suffix = suffix.Substring(1);
        }

        if (suffix.IndexOf('/') >= 0)
        {
            return null;
        }

        if (suffix.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
        {
            suffix = suffix.Substring(0, suffix.Length - 4);
        }

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        if (!suffix.StartsWith(".", StringComparison.Ordinal))
        {
            suffix = "." + suffix;
        }

        return suffix;
    }

    /// <summary>
    /// Collect exportable pairs from a materials override store (no <c>PrepareTextures</c>).
    /// Optionally syncs from assigned <c>OverrideMaterial</c> assets first.
    /// </summary>
    public static List<MaterialEntry> CollectEntries(
        VrmxtMaterialsOverrideInstance store,
        bool syncFromOverrideMaterials)
    {
        var entries = new List<MaterialEntry>();
        if (store == null)
        {
            return entries;
        }

        if (syncFromOverrideMaterials)
        {
            VrmxtMaterialsOverrideAuthoring.SyncAllFromOverrideMaterials(store);
        }

        var pairs = store.Pairs;
        if (pairs == null)
        {
            return entries;
        }

        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            if (pair == null ||
                string.IsNullOrEmpty(pair.MaterialName) ||
                string.IsNullOrWhiteSpace(pair.ExtensionJson))
            {
                continue;
            }

            if (!VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out _))
            {
                continue;
            }

            // Skip stock MToon — those stay on VRMC_materials_mtoon only.
            var shaderName = VrmxtMaterialsShaderAuthoring.TryGetActiveShaderName(pair);
            if (VrmxtMaterialsOverrideAuthoring.IsStockUnityMtoonShader(shaderName))
            {
                continue;
            }

            entries.Add(new MaterialEntry(
                pair.MaterialName,
                pair.ExtensionJson,
                pair.GltfMaterialIndex));
        }

        return entries;
    }

    /// <summary>
    /// Inject store extension JSON onto matching <c>materials[]</c> entries. Preserves BIN
    /// and unrelated JSON; caller rebuilds the GLB.
    /// </summary>
    public static RewriteResult TryRewriteJson(
        string gltfJson,
        IReadOnlyList<MaterialEntry> entries)
    {
        var result = new RewriteResult();
        if (string.IsNullOrWhiteSpace(gltfJson))
        {
            result.Error = "glTF JSON is empty.";
            return result;
        }

        JObject root;
        try
        {
            root = JToken.Parse(gltfJson) as JObject;
        }
        catch (JsonReaderException e)
        {
            result.Error = "Failed to parse glTF JSON: " + e.Message;
            return result;
        }
        catch (JsonException e)
        {
            result.Error = "Failed to parse glTF JSON: " + e.Message;
            return result;
        }

        if (root == null)
        {
            result.Error = "glTF JSON root is not an object.";
            return result;
        }

        if (!(root["materials"] is JArray materials))
        {
            result.Error = "glTF JSON has no materials array.";
            return result;
        }

        if (entries == null || entries.Count == 0)
        {
            result.Success = true;
            result.Json = root.ToString(Formatting.None);
            return result;
        }

        var written = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.ExtensionJson))
            {
                result.Skipped.Add("empty entry");
                continue;
            }

            JObject extensionObject;
            try
            {
                extensionObject = JToken.Parse(entry.ExtensionJson) as JObject;
            }
            catch (JsonException)
            {
                result.Skipped.Add(DescribeEntry(entry) + ": invalid ExtensionJson");
                continue;
            }

            if (extensionObject == null)
            {
                result.Skipped.Add(DescribeEntry(entry) + ": ExtensionJson is not an object");
                continue;
            }

            if (!TryResolveMaterialIndex(materials, entry, out var materialIndex, out var skipReason))
            {
                result.Skipped.Add(DescribeEntry(entry) + ": " + skipReason);
                continue;
            }

            if (!(materials[materialIndex] is JObject materialObject))
            {
                result.Skipped.Add(DescribeEntry(entry) + ": materials[" + materialIndex + "] is not an object");
                continue;
            }

            var extensions = materialObject["extensions"] as JObject;
            if (extensions == null)
            {
                extensions = new JObject();
                materialObject["extensions"] = extensions;
            }

            extensions[VrmxtMaterialsOverride.ExtensionName] = extensionObject;
            written++;
        }

        if (written > 0)
        {
            EnsureExtensionsUsed(root, VrmxtMaterialsOverride.ExtensionName);
        }

        result.Success = true;
        result.WrittenCount = written;
        result.Json = root.ToString(Formatting.None);
        return result;
    }

    /// <summary>
    /// Extract → rewrite → rebuild. Returns GLB bytes when successful.
    /// </summary>
    public static bool TryRebuildGlb(
        byte[] sourceGlb,
        IReadOnlyList<MaterialEntry> entries,
        out byte[] outputGlb,
        out RewriteResult rewrite)
    {
        outputGlb = null;
        rewrite = null;
        if (sourceGlb == null || sourceGlb.Length == 0)
        {
            rewrite = new RewriteResult { Error = "Source GLB bytes are empty." };
            return false;
        }

        if (!GlbChunks.TryExtract(sourceGlb, out var json, out var binChunk))
        {
            rewrite = new RewriteResult { Error = "Failed to extract JSON/BIN from source GLB." };
            return false;
        }

        rewrite = TryRewriteJson(json, entries);
        if (!rewrite.Success || string.IsNullOrEmpty(rewrite.Json))
        {
            return false;
        }

        if (!GlbChunks.TryRebuild(rewrite.Json, binChunk, out outputGlb))
        {
            rewrite.Success = false;
            rewrite.Error = "Failed to rebuild GLB.";
            outputGlb = null;
            return false;
        }

        return true;
    }

    private static bool TryResolveMaterialIndex(
        JArray materials,
        MaterialEntry entry,
        out int materialIndex,
        out string skipReason)
    {
        materialIndex = -1;
        skipReason = null;

        if (entry.GltfMaterialIndex >= 0 && entry.GltfMaterialIndex < materials.Count)
        {
            if (materials[entry.GltfMaterialIndex] is JObject indexedObject)
            {
                var indexedName = VrmxtMaterialsOverrideRuntime.GetMaterialName(
                    indexedObject, entry.GltfMaterialIndex);
                if (string.IsNullOrEmpty(entry.MaterialName) ||
                    string.Equals(
                        NormalizeMaterialName(indexedName),
                        NormalizeMaterialName(entry.MaterialName),
                        StringComparison.Ordinal))
                {
                    materialIndex = entry.GltfMaterialIndex;
                    return true;
                }

                // Stale index — fall through to name search.
            }
        }

        if (string.IsNullOrEmpty(entry.MaterialName))
        {
            skipReason = "missing GltfMaterialIndex and MaterialName";
            return false;
        }

        var want = NormalizeMaterialName(entry.MaterialName);
        var match = -1;
        for (var i = 0; i < materials.Count; i++)
        {
            if (!(materials[i] is JObject materialObject))
            {
                continue;
            }

            var name = VrmxtMaterialsOverrideRuntime.GetMaterialName(materialObject, i);
            if (!string.Equals(NormalizeMaterialName(name), want, StringComparison.Ordinal))
            {
                continue;
            }

            if (match >= 0)
            {
                skipReason = "ambiguous material name '" + entry.MaterialName + "'";
                return false;
            }

            match = i;
        }

        if (match < 0)
        {
            skipReason = "material not found '" + entry.MaterialName + "'";
            return false;
        }

        materialIndex = match;
        return true;
    }

    private static string NormalizeMaterialName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var stripped = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(name);
        // Store may use Name#N disambiguation; glTF names do not — strip trailing #digits.
        var hash = stripped.LastIndexOf('#');
        if (hash > 0 && hash < stripped.Length - 1)
        {
            var allDigits = true;
            for (var i = hash + 1; i < stripped.Length; i++)
            {
                if (stripped[i] < '0' || stripped[i] > '9')
                {
                    allDigits = false;
                    break;
                }
            }

            if (allDigits)
            {
                stripped = stripped.Substring(0, hash);
            }
        }

        return stripped.Trim();
    }

    private static void EnsureExtensionsUsed(JObject root, string extensionName)
    {
        var used = root["extensionsUsed"] as JArray;
        if (used == null)
        {
            used = new JArray();
            root["extensionsUsed"] = used;
        }

        for (var i = 0; i < used.Count; i++)
        {
            if (string.Equals(used[i]?.ToString(), extensionName, StringComparison.Ordinal))
            {
                return;
            }
        }

        used.Add(extensionName);
    }

    private static string DescribeEntry(MaterialEntry entry)
    {
        if (entry == null)
        {
            return "(null)";
        }

        if (!string.IsNullOrEmpty(entry.MaterialName))
        {
            return entry.MaterialName;
        }

        return "index=" + entry.GltfMaterialIndex;
    }
}
