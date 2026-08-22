using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRMXT.Format;

namespace UniVRMXT.Mtoonxt
{
    public enum VrmxtMtoonxtBodyStencilOp
    {
        Off = 0,
        Write = 1,

        [InspectorName("Clip inside")]
        ClipInside = 2,

        [InspectorName("Clip outside")]
        ClipOutside = 3,

        [InspectorName("Clip inside overlay")]
        ClipInsideOverlay = 4,
    }

    public enum VrmxtMtoonxtOutlineStencilOp
    {
        Off = 0,

        [InspectorName("Same as body")]
        Same = 1,
        Write = 2,

        [InspectorName("Clip inside")]
        ClipInside = 3,

        [InspectorName("Clip outside")]
        ClipOutside = 4,

        [InspectorName("Clip inside overlay")]
        ClipInsideOverlay = 5,
    }

    /// <summary>
    /// Runtime holder for <c>VRMXT_materials_mtoonxt</c> on a loaded avatar root.
    /// Inspector authors Unity fields; export writes glTF JSON.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmxtMaterialsMtoonxtInstance : MonoBehaviour
    {
        [SerializeField]
        private List<VrmxtMaterialsMtoonxtPair> pairs = new List<VrmxtMaterialsMtoonxtPair>();

        public IReadOnlyList<VrmxtMaterialsMtoonxtPair> Pairs => pairs;

        private void OnDestroy()
        {
            VrmxtMaterialsMtoonxtStencilRefs.Release(gameObject.GetInstanceID());
        }

        public void SetPairs(IEnumerable<VrmxtMaterialsMtoonxtPair> values)
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
    public sealed class VrmxtMaterialsMtoonxtPair
    {
        public string MaterialName;
        public int GltfMaterialIndex = -1;
        public VrmxtMtoonxtBodyStencilOp BodyOp;
        public VrmxtMtoonxtOutlineStencilOp OutlineOp;
        public List<Material> StencilTargets = new List<Material>();
        public List<Material> OutlineStencilTargets = new List<Material>();

        /// <summary>
        /// Import leftover (research <c>zTest</c> / <c>zWrite</c>). Not an authoring field.
        /// </summary>
        public string ExtensionJson;

        public VrmxtMaterialsMtoonxtPair() { }

        public VrmxtMaterialsMtoonxtPair(
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
