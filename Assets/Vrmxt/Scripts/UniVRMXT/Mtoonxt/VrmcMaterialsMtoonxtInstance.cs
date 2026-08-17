using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Runtime holder for <c>VRMC_materials_mtoonxt</c> on a loaded avatar root.
    /// Stores verbatim extension JSON.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmcMaterialsMtoonxtInstance : MonoBehaviour
    {
        [SerializeField]
        private List<VrmcMaterialsMtoonxtPair> pairs = new List<VrmcMaterialsMtoonxtPair>();

        public IReadOnlyList<VrmcMaterialsMtoonxtPair> Pairs => pairs;

        public void SetPairs(IEnumerable<VrmcMaterialsMtoonxtPair> values)
        {
            pairs.Clear();
            if (values == null)
            {
                return;
            }

            pairs.AddRange(values);
        }
    }

    [Serializable]
    public sealed class VrmcMaterialsMtoonxtPair
    {
        public string MaterialName;
        public string ExtensionJson;
        public int GltfMaterialIndex = -1;

        public VrmcMaterialsMtoonxtPair() { }

        public VrmcMaterialsMtoonxtPair(string materialName, string extensionJson, int gltfMaterialIndex)
        {
            MaterialName = materialName;
            ExtensionJson = extensionJson;
            GltfMaterialIndex = gltfMaterialIndex;
        }
    }
}
