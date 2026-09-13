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

    public enum VrmxtMtoonxtRelationshipComparison
    {
        Outside = 0,
        Inside = 1,
    }

    public enum VrmxtMtoonxtDepthTest
    {
        Never = 1,
        Less = 2,
        Equal = 3,
        LessEqual = 4,
        Greater = 5,
        NotEqual = 6,
        GreaterEqual = 7,
        Always = 8,
    }

    /// <summary>
    /// Runtime holder for <c>VRMXT_materials_mtoonxt</c> on a loaded avatar root.
    /// Inspector authors Unity fields; export writes glTF JSON.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VrmxtMaterialsMtoonxtInstance : MonoBehaviour
    {
        [SerializeField]
        private List<VrmxtMaterialsMtoonxtPair> pairs = new List<VrmxtMaterialsMtoonxtPair>();

        [SerializeField]
        private List<VrmxtMaterialsMtoonxtRelationshipAuthoring> stencilRelationships =
            new List<VrmxtMaterialsMtoonxtRelationshipAuthoring>();

        public IReadOnlyList<VrmxtMaterialsMtoonxtPair> Pairs => pairs;

        public IReadOnlyList<VrmxtMaterialsMtoonxtRelationshipAuthoring> StencilRelationships =>
            stencilRelationships;

        private void OnEnable()
        {
            // Native material layers and coverage buffers are derived runtime state.
            // Rebuild them from the portable graph after a domain reload, scene
            // reopen, or object re-enable from the serialized authoring graph on this store.
            // The initial import path may invoke this before SetPairs/SetStencilRelationships;
            // that empty reapply is harmless and the importer performs the complete Apply next.
            VrmxtMaterialsMtoonxtApplier.ReapplyRelationships(gameObject, this);
        }

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

        public void SetStencilRelationships(
            IEnumerable<VrmxtMaterialsMtoonxtRelationshipAuthoring> values
        )
        {
            stencilRelationships.Clear();
            if (values != null)
            {
                stencilRelationships.AddRange(values);
            }
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

    [Serializable]
    public sealed class VrmxtMaterialsMtoonxtRelationshipAuthoring
    {
        public List<Material> Writers = new List<Material>();
        public List<Material> Readers = new List<Material>();
        public VrmxtMtoonxtRelationshipComparison Comparison =
            VrmxtMtoonxtRelationshipComparison.Outside;
        public bool ShowWritersThroughOccluders;
        public bool WritersOnlyInsideReaders;
        public bool WritersOnlyOutsideReaders;
        public bool WritersSelfOcclude = true;
        public bool IgnoreOccludedReaderAreas = true;
        public bool WritersWriteColor = true;
        public bool WritersWriteDepth = true;
        public bool ReadersWriteDepth = true;
        public VrmxtMtoonxtDepthTest WriterDepthTest = VrmxtMtoonxtDepthTest.LessEqual;
        public VrmxtMtoonxtDepthTest ReaderDepthTest = VrmxtMtoonxtDepthTest.LessEqual;

        public VrmxtMaterialsMtoonxtRelationshipAuthoring() { }

        public VrmxtMaterialsMtoonxtRelationshipAuthoring(
            IEnumerable<Material> writers,
            IEnumerable<Material> readers
        )
        {
            if (writers != null)
            {
                Writers.AddRange(writers);
            }

            if (readers != null)
            {
                Readers.AddRange(readers);
            }
        }
    }
}
