using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Attach parsed <c>VRMC_materials_mtoonxt</c> objects without UniVRM types.
    /// </summary>
    public static class VrmcMaterialsMtoonxtRuntime
    {
        public static bool TryAttachFromGltfJson(
            GameObject root,
            string gltfJson,
            out VrmcMaterialsMtoonxtInstance store)
        {
            store = null;
            if (root == null)
            {
                return false;
            }

            var found = new List<VrmcMaterialsMtoonxtPair>();
            if (!string.IsNullOrWhiteSpace(gltfJson) &&
                TryGetMaterialsArray(gltfJson, out var materials))
            {
                for (var i = 0; i < materials.Count; i++)
                {
                    var materialObject = materials[i] as JObject;
                    if (materialObject == null)
                    {
                        continue;
                    }

                    if (!TryGetExtensionObject(materialObject, out var extensionObject))
                    {
                        continue;
                    }

                    if (!VrmcMaterialsMtoonxt.TryParse(extensionObject, out _))
                    {
                        continue;
                    }

                    var materialName = VrmxtMaterialsOverrideRuntime.GetMaterialName(materialObject, i);
                    found.Add(new VrmcMaterialsMtoonxtPair(
                        materialName,
                        extensionObject.ToString(Formatting.None),
                        i));
                }

                DisambiguateDuplicateNames(found);
            }

            store = EnsureInstance(root);
            if (store == null)
            {
                return false;
            }

            if (found.Count > 0)
            {
                store.SetPairs(found);
                VrmcMaterialsMtoonxtAuthoring.PopulateFromExtensionJson(root, store);
            }

            return true;
        }

        private static void DisambiguateDuplicateNames(List<VrmcMaterialsMtoonxtPair> found)
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
                found[i].MaterialName = name + "#" + occurrence;
            }
        }

        private static bool TryGetMaterialsArray(string gltfJson, out JArray materials)
        {
            materials = null;
            try
            {
                var root = JToken.Parse(gltfJson) as JObject;
                if (root == null)
                {
                    return false;
                }

                if (root.TryGetValue("materials", StringComparison.Ordinal, out var materialsToken))
                {
                    materials = materialsToken as JArray;
                    return materials != null;
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
            if (!materialObject.TryGetValue("extensions", StringComparison.Ordinal, out var extensionsToken))
            {
                return false;
            }

            var extensions = extensionsToken as JObject;
            if (extensions == null)
            {
                return false;
            }

            if (!extensions.TryGetValue(
                    VrmcMaterialsMtoonxt.ExtensionName,
                    StringComparison.Ordinal,
                    out var extensionToken))
            {
                return false;
            }

            extensionObject = extensionToken as JObject;
            return extensionObject != null;
        }

        private static VrmcMaterialsMtoonxtInstance EnsureInstance(GameObject root)
        {
            var instance = root.GetComponent<VrmcMaterialsMtoonxtInstance>();
            if (instance == null)
            {
                instance = root.AddComponent<VrmcMaterialsMtoonxtInstance>();
            }

            return instance;
        }
    }
}
