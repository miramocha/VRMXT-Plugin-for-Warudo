using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Attach parsed <c>VRMXT_materials_override</c> extension objects to a loaded avatar
    /// without referencing UniVRM/UniGLTF types. Call with the same glTF JSON text used by
    /// the host's own VRM load (e.g. <c>Vrm10.LoadGltfDataAsync</c> equivalents).
    /// </summary>
    public static class VrmxtMaterialsOverrideRuntime
    {
        /// <summary>
        /// Fallback name for a glTF material with no <c>name</c> field. Applier and
        /// exporter helpers use the same convention so lookups stay consistent.
        /// </summary>
        public const string FallbackMaterialNameFormat = "material_{0}";

        /// <summary>
        /// Walk <c>materials[]</c> in <paramref name="gltfJson"/> and store every valid
        /// <c>VRMXT_materials_override</c> extension object onto a
        /// <see cref="VrmxtMaterialsOverrideInstance"/> on <paramref name="root"/>.
        /// Always ensures the instance (and <see cref="VrmxtInstance"/> facade) when
        /// <paramref name="root"/> is usable, even if no material carries an override —
        /// so authoring can start on stock VRM imports. Returns false only when the
        /// instance cannot be added (null root / ScriptedImporter reject).
        /// </summary>
        public static bool TryAttachFromGltfJson(
            GameObject root,
            string gltfJson,
            out VrmxtMaterialsOverrideInstance store)
        {
            store = null;
            if (root == null)
            {
                return false;
            }

            var found = new List<VrmxtMaterialsOverridePair>();
            if (!string.IsNullOrWhiteSpace(gltfJson) &&
                TryGetMaterialsArray(gltfJson, out var materials))
            {
                for (var i = 0; i < materials.Count; i++)
                {
                    if (materials[i] is not JObject materialObject)
                    {
                        continue;
                    }

                    if (!TryGetExtensionObject(materialObject, out var extensionObject))
                    {
                        continue;
                    }

                    // Validate against the format layer; invalid entries are skipped, not stored.
                    if (!VrmxtMaterialsOverride.TryParse(extensionObject, out _))
                    {
                        continue;
                    }

                    var materialName = GetMaterialName(materialObject, i);
                    found.Add(new VrmxtMaterialsOverridePair(
                        materialName,
                        extensionObject.ToString(Formatting.None)));
                }

                DisambiguateDuplicateNames(found);
            }

            store = EnsureInstance(root);
            if (store == null)
            {
                // ScriptedImporter main assets reject AddComponent during
                // AssetPostprocessor (see EnsureInstance); nothing to attach to.
                return false;
            }

            if (found.Count > 0)
            {
                store.SetPairs(found);
            }
            else if (store.Pairs.Count == 0)
            {
                // Stock VRM (no overrides): seed empty pairs from live renderer materials
                // so the Inspector list is ready for authoring.
                store.PopulatePairsFromRenderers();
            }

            store.RefreshSourceMaterials();
            return true;
        }

        /// <summary>
        /// glTF material display name: <c>name</c> when present and non-empty, otherwise a
        /// stable index-based fallback (<see cref="FallbackMaterialNameFormat"/>).
        /// Strips Unity's <c> (Instance)</c> suffix when present so store keys match live
        /// sharedMaterials (Warudo / clone exports sometimes bake the suffix into glTF names).
        /// </summary>
        public static string GetMaterialName(JObject materialObject, int index)
        {
            if (materialObject != null &&
                materialObject.TryGetValue("name", StringComparison.Ordinal, out var nameToken) &&
                nameToken.Type == JTokenType.String)
            {
                var name = nameToken.Value<string>();
                if (!string.IsNullOrEmpty(name))
                {
                    return StripUnityInstanceSuffix(name);
                }
            }

            return string.Format(FallbackMaterialNameFormat, index);
        }

        /// <summary>
        /// Remove a trailing Unity <c> (Instance)</c> clone suffix, if any.
        /// </summary>
        public static string StripUnityInstanceSuffix(string materialName)
        {
            const string instanceSuffix = " (Instance)";
            if (materialName != null &&
                materialName.EndsWith(instanceSuffix, StringComparison.Ordinal))
            {
                return materialName.Substring(0, materialName.Length - instanceSuffix.Length);
            }

            return materialName;
        }

        /// <summary>
        /// glTF materials with duplicate <c>name</c> values (or duplicate fallback names)
        /// would otherwise collapse to the same store key: <see cref="VrmxtMaterialsOverrideInstance"/>
        /// lookups return only the first, and the exporter's per-material JSON map would
        /// silently drop the rest. Disambiguate in place as <c>Name#1</c>, <c>Name#2</c>, ...
        /// (1-based, in <c>materials[]</c> order); non-colliding names are left untouched.
        /// <see cref="FindMaterialsForStoreKey"/> is the matching resolver used by the
        /// applier and exporter so both sides agree on which live material a key means.
        /// </summary>
        private static void DisambiguateDuplicateNames(List<VrmxtMaterialsOverridePair> found)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < found.Count; i++)
            {
                var name = found[i].MaterialName;
                counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
            }

            var seenSoFar = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < found.Count; i++)
            {
                var name = found[i].MaterialName;
                if (counts[name] <= 1)
                {
                    continue;
                }

                var occurrence = seenSoFar.TryGetValue(name, out var previous) ? previous + 1 : 1;
                seenSoFar[name] = occurrence;
                found[i].MaterialName = $"{name}#{occurrence}";
            }
        }

        /// <summary>
        /// Resolve the live Unity <see cref="Material"/>(s) for a
        /// <see cref="VrmxtMaterialsOverrideInstance"/> key, honoring the <c>Name#N</c>
        /// disambiguator format from <see cref="DisambiguateDuplicateNames"/>. Falls back to
        /// a plain <see cref="VrmxtMaterialsOverrideApplier.FindMaterialsByName"/> lookup
        /// when the key has no disambiguator suffix.
        /// Note: a material genuinely named e.g. "Hair#1" (no collision, so never
        /// disambiguated at attach time) is indistinguishable from a disambiguated key here
        /// and would resolve as occurrence 1 of "Hair" — an accepted limitation of this
        /// minimal fix.
        /// </summary>
        public static IEnumerable<Material> FindMaterialsForStoreKey(GameObject root, string storeKey)
        {
            if (TryParseDisambiguatedKey(storeKey, out var baseName, out var occurrence))
            {
                var index = 0;
                foreach (var material in VrmxtMaterialsOverrideApplier.FindMaterialsByName(root, baseName))
                {
                    index++;
                    if (index == occurrence)
                    {
                        return new[] { material };
                    }
                }

                return Array.Empty<Material>();
            }

            return VrmxtMaterialsOverrideApplier.FindMaterialsByName(root, storeKey);
        }

        private static bool TryParseDisambiguatedKey(string storeKey, out string baseName, out int occurrence)
        {
            baseName = null;
            occurrence = 0;
            if (string.IsNullOrEmpty(storeKey))
            {
                return false;
            }

            var hashIndex = storeKey.LastIndexOf('#');
            if (hashIndex <= 0 || hashIndex == storeKey.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(storeKey.Substring(hashIndex + 1), out occurrence) || occurrence <= 0)
            {
                return false;
            }

            baseName = storeKey.Substring(0, hashIndex);
            return true;
        }

        private static bool TryGetMaterialsArray(string gltfJson, out JArray materials)
        {
            materials = null;
            try
            {
                if (JToken.Parse(gltfJson) is not JObject root)
                {
                    return false;
                }

                if (root.TryGetValue("materials", StringComparison.Ordinal, out var materialsToken) &&
                    materialsToken is JArray materialsArray)
                {
                    materials = materialsArray;
                    return true;
                }

                return false;
            }
            catch (JsonReaderException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryGetExtensionObject(JObject materialObject, out JObject extensionObject)
        {
            extensionObject = null;
            if (!materialObject.TryGetValue("extensions", StringComparison.Ordinal, out var extensionsToken) ||
                extensionsToken is not JObject extensions)
            {
                return false;
            }

            if (!extensions.TryGetValue(VrmxtMaterialsOverride.ExtensionName, StringComparison.Ordinal, out var extensionToken) ||
                extensionToken is not JObject extensionObj)
            {
                return false;
            }

            extensionObject = extensionObj;
            return true;
        }

        private static VrmxtMaterialsOverrideInstance EnsureInstance(GameObject root)
        {
            var instance = root.GetComponent<VrmxtMaterialsOverrideInstance>();
            if (instance == null)
            {
                // ScriptedImporter main assets reject AddComponent during AssetPostprocessor
                // (returns null); callers must Instantiate / use a companion prefab first.
                instance = root.AddComponent<VrmxtMaterialsOverrideInstance>();
            }

            if (instance != null)
            {
                VrmxtInstance.BindMaterialsOverride(root, instance);
            }

            return instance;
        }
    }
}
