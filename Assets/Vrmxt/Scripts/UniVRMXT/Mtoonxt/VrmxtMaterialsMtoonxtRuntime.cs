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
    /// Attach parsed <c>VRMXT_materials_mtoonxt</c> objects without UniVRM types.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtRuntime
    {
        public static bool TryAttachFromGltfJson(
            GameObject root,
            string gltfJson,
            out VrmxtMaterialsMtoonxtInstance store)
        {
            store = null;
            if (root == null)
            {
                return false;
            }

            var found = new List<VrmxtMaterialsMtoonxtPair>();
            var parsedStencils = new List<VrmxtMaterialsMtoonxtStencil>();
            if (
                !string.IsNullOrWhiteSpace(gltfJson)
                && TryGetRoot(gltfJson, out var gltfRoot)
                && TryGetMaterialsArray(gltfRoot, out var materials)
            )
            {
                VrmxtMaterialsMtoonxtStencils.TryParseRoot(
                    gltfRoot,
                    materials.Count,
                    out parsedStencils
                );
                var referenced = ReferencedMaterialIndices(parsedStencils);
                for (var i = 0; i < materials.Count; i++)
                {
                    var materialObject = materials[i] as JObject;
                    if (materialObject == null)
                    {
                        continue;
                    }

                    JObject extensionObject = null;
                    var hasMaterialExtension = TryGetExtensionObject(
                        materialObject,
                        out extensionObject
                    ) && VrmxtMaterialsMtoonxt.TryParse(extensionObject, out _);
                    if (!hasMaterialExtension && !referenced.Contains(i))
                    {
                        continue;
                    }

                    var materialName = VrmxtMaterialsOverrideRuntime.GetMaterialName(materialObject, i);
                    found.Add(new VrmxtMaterialsMtoonxtPair(
                        materialName,
                        extensionObject != null
                            ? extensionObject.ToString(Formatting.None)
                            : null,
                        i));
                }

                DisambiguateDuplicateNames(found);
            }

            store = EnsureInstance(root);
            if (store == null)
            {
                return false;
            }

            store.SetPairs(found);
            if (found.Count > 0)
            {
                VrmxtMaterialsMtoonxtAuthoring.PopulateFromExtensionJson(root, store);
            }

            store.SetStencils(null);
            if (parsedStencils.Count > 0)
            {
                VrmxtMaterialsMtoonxtAuthoring.PopulateStencils(root, store, parsedStencils);
            }

            return true;
        }

        private static void DisambiguateDuplicateNames(List<VrmxtMaterialsMtoonxtPair> found)
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

        private static HashSet<int> ReferencedMaterialIndices(
            IReadOnlyList<VrmxtMaterialsMtoonxtStencil> stencils
        )
        {
            var result = new HashSet<int>();
            if (stencils == null)
            {
                return result;
            }

            for (var i = 0; i < stencils.Count; i++)
            {
                var stencil = stencils[i];
                if (stencil == null)
                {
                    continue;
                }

                AddIndices(result, stencil.Writers);
                AddIndices(result, stencil.Readers);
            }

            return result;
        }

        private static void AddIndices(HashSet<int> result, IReadOnlyList<int> values)
        {
            if (values == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                result.Add(values[i]);
            }
        }

        private static bool TryGetRoot(string gltfJson, out JObject root)
        {
            root = null;
            try
            {
                root = JToken.Parse(gltfJson) as JObject;
                return root != null;
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

        private static bool TryGetMaterialsArray(JObject root, out JArray materials)
        {
            materials = null;
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
                    VrmxtMaterialsMtoonxt.ExtensionName,
                    StringComparison.Ordinal,
                    out var extensionToken))
            {
                return false;
            }

            extensionObject = extensionToken as JObject;
            return extensionObject != null;
        }

        private static VrmxtMaterialsMtoonxtInstance EnsureInstance(GameObject root)
        {
            var instance = root.GetComponent<VrmxtMaterialsMtoonxtInstance>();
            if (instance == null)
            {
                instance = root.AddComponent<VrmxtMaterialsMtoonxtInstance>();
            }

            return instance;
        }
    }
}
