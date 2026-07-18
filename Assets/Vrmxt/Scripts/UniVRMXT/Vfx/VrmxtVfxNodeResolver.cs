using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Resolve glTF <c>nodes[]</c> indices to Transforms when <c>RuntimeGltfInstance.Nodes</c>
    /// is unavailable (AssetDatabase ScriptedImporter prefabs).
    /// </summary>
    public static class VrmxtVfxNodeResolver
    {
        /// <summary>
        /// Read <c>nodes[].name</c> (UniVRM empty-name fallback: <c>nodeIndex_{i}</c>).
        /// </summary>
        public static bool TryReadNodeNames(string gltfJson, out IReadOnlyList<string> names)
        {
            names = null;
            if (string.IsNullOrWhiteSpace(gltfJson))
            {
                return false;
            }

            try
            {
                var root = JToken.Parse(gltfJson);
                if (root is not JObject rootObject ||
                    !rootObject.TryGetValue("nodes", StringComparison.Ordinal, out var nodesToken) ||
                    nodesToken is not JArray nodesArray)
                {
                    return false;
                }

                var list = new List<string>(nodesArray.Count);
                for (var i = 0; i < nodesArray.Count; i++)
                {
                    list.Add(ReadNodeName(nodesArray[i], i));
                }

                names = list;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static Transform ResolveByName(
            Transform root,
            IReadOnlyList<string> nodeNames,
            int nodeIndex)
        {
            if (root == null || nodeNames == null ||
                nodeIndex < 0 || nodeIndex >= nodeNames.Count)
            {
                return null;
            }

            var targetName = nodeNames[nodeIndex];
            if (string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            if (string.Equals(root.name, targetName, StringComparison.Ordinal))
            {
                return root;
            }

            return FindDescendantByName(root, targetName);
        }

        public static Func<int, Transform> CreateResolver(
            Transform root,
            IReadOnlyList<string> nodeNames)
        {
            return index => ResolveByName(root, nodeNames, index);
        }

        private static string ReadNodeName(JToken nodeToken, int index)
        {
            if (nodeToken is JObject nodeObject &&
                nodeObject.TryGetValue("name", StringComparison.Ordinal, out var nameToken) &&
                nameToken.Type == JTokenType.String)
            {
                var name = nameToken.Value<string>();
                if (!string.IsNullOrEmpty(name))
                {
                    return name.Contains("/") ? name.Replace("/", "_") : name;
                }
            }

            return "nodeIndex_" + index;
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, targetName, StringComparison.Ordinal))
                {
                    return transforms[i];
                }
            }

            return null;
        }
    }
}
