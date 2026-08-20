using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRMXT.Format;

namespace UniVRMXT.Mtoonxt
{
    public enum VrmcMtoonxtBodyStencilOp
    {
        Off = 0,
        Write = 1,

        [InspectorName("Clip inside")]
        ClipInside = 2,

        [InspectorName("Clip outside")]
        ClipOutside = 3,
    }

    public enum VrmcMtoonxtOutlineStencilOp
    {
        Off = 0,

        [InspectorName("Same as body")]
        Same = 1,
        Write = 2,

        [InspectorName("Clip inside")]
        ClipInside = 3,

        [InspectorName("Clip outside")]
        ClipOutside = 4,
    }

    /// <summary>
    /// Runtime holder for <c>VRMC_materials_mtoonxt</c> on a loaded avatar root.
    /// Inspector authors Unity fields; export writes glTF JSON.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmcMaterialsMtoonxtInstance : MonoBehaviour
    {
        [SerializeField]
        private List<VrmcMaterialsMtoonxtPair> pairs = new List<VrmcMaterialsMtoonxtPair>();

        public IReadOnlyList<VrmcMaterialsMtoonxtPair> Pairs => pairs;

        private void OnDestroy()
        {
            VrmcMaterialsMtoonxtStencilRefs.Release(gameObject.GetInstanceID());
        }

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
        public int GltfMaterialIndex = -1;
        public VrmcMtoonxtBodyStencilOp BodyOp;
        public VrmcMtoonxtOutlineStencilOp OutlineOp;
        public List<Material> StencilTargets = new List<Material>();
        public List<Material> OutlineStencilTargets = new List<Material>();

        /// <summary>
        /// Import leftover (research <c>zTest</c> / <c>zWrite</c>). Not an authoring field.
        /// </summary>
        public string ExtensionJson;

        public VrmcMaterialsMtoonxtPair() { }

        public VrmcMaterialsMtoonxtPair(
            string materialName,
            string extensionJson,
            int gltfMaterialIndex
        )
        {
            MaterialName = materialName;
            ExtensionJson = extensionJson;
            GltfMaterialIndex = gltfMaterialIndex;
        }
    }
}
