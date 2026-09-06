using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRMXT.Format;

namespace UniVRMXT.Mtoonxt
{
    public enum VrmxtMtoonxtStencilComparison
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
        private List<VrmxtMaterialsMtoonxtStencilAuthoring> stencils =
            new List<VrmxtMaterialsMtoonxtStencilAuthoring>();

        public IReadOnlyList<VrmxtMaterialsMtoonxtPair> Pairs => pairs;

        public IReadOnlyList<VrmxtMaterialsMtoonxtStencilAuthoring> Stencils => stencils;

        private void OnEnable()
        {
            // Native material layers and coverage buffers are derived runtime state.
            // Rebuild them from the portable graph after a domain reload, scene
            // reopen, or object re-enable from the serialized authoring graph on this store.
            // The initial import path may invoke this before SetPairs/SetStencils;
            // that empty reapply is harmless and the importer performs the complete Apply next.
            VrmxtMaterialsMtoonxtApplier.ReapplyStencils(gameObject, this);
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

        public void SetStencils(IEnumerable<VrmxtMaterialsMtoonxtStencilAuthoring> values)
        {
            stencils.Clear();
            if (values != null)
            {
                stencils.AddRange(values);
            }
        }
    }

    [Serializable]
    public sealed class VrmxtMaterialsMtoonxtPair
    {
        public string MaterialName;
        public int GltfMaterialIndex = -1;

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
    public sealed class VrmxtMaterialsMtoonxtStencilAuthoring
    {
        public List<Material> Writers = new List<Material>();
        public List<Material> Readers = new List<Material>();
        public VrmxtMtoonxtStencilComparison Comparison = VrmxtMtoonxtStencilComparison.Outside;
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

        public VrmxtMaterialsMtoonxtStencilAuthoring() { }

        public VrmxtMaterialsMtoonxtStencilAuthoring(
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
