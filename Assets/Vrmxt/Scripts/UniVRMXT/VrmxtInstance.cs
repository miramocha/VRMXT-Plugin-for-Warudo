using System;
using System.Collections.Generic;
using UniVRMXT.MaterialsOverride;
using UniVRMXT.Vfx;
using UnityEngine;

namespace UniVRMXT
{
    /// <summary>
    /// Avatar-root facade for UniVRMXT feature components. Holds serialized refs to
    /// <see cref="VrmxtVfxInstance"/> and <see cref="VrmxtMaterialsOverrideInstance"/>;
    /// each feature still owns its own data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmxtInstance : MonoBehaviour
    {
        [SerializeField]
        private VrmxtVfxInstance vfx;

        [SerializeField]
        private VrmxtMaterialsOverrideInstance materialsOverride;

        public VrmxtVfxInstance Vfx => vfx;

        public VrmxtMaterialsOverrideInstance MaterialsOverride => materialsOverride;

        /// <summary>
        /// Ensure a <see cref="VrmxtInstance"/> on <paramref name="root"/> and wire any
        /// feature components already on the same GameObject.
        /// </summary>
        public static VrmxtInstance EnsureOn(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var instance = root.GetComponent<VrmxtInstance>();
            if (instance == null)
            {
                instance = root.AddComponent<VrmxtInstance>();
                if (instance == null)
                {
                    return null;
                }
            }

            instance.WireFromChildren();
            return instance;
        }

        /// <summary>
        /// Fill null props from <see cref="GetComponent{T}"/> on this GameObject.
        /// </summary>
        public void WireFromChildren()
        {
            if (vfx == null)
            {
                vfx = GetComponent<VrmxtVfxInstance>();
            }

            if (materialsOverride == null)
            {
                materialsOverride = GetComponent<VrmxtMaterialsOverrideInstance>();
            }
        }

        /// <summary>
        /// Assign the VFX prop (and ensure facade exists). Does not create VFX.
        /// </summary>
        public static void BindVfx(GameObject root, VrmxtVfxInstance feature)
        {
            var facade = EnsureOn(root);
            if (facade == null || feature == null)
            {
                return;
            }

            facade.vfx = feature;
        }

        /// <summary>
        /// Assign the materials-override prop (and ensure facade exists). Does not create it.
        /// </summary>
        public static void BindMaterialsOverride(GameObject root, VrmxtMaterialsOverrideInstance feature)
        {
            var facade = EnsureOn(root);
            if (facade == null || feature == null)
            {
                return;
            }

            facade.materialsOverride = feature;
        }

        /// <summary>
        /// Resolve VFX via facade prop, then direct child search. Soft-repairs facade when
        /// an orphan feature is found.
        /// </summary>
        public static VrmxtVfxInstance FindVfx(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var facade = root.GetComponentInChildren<VrmxtInstance>(true);
            if (facade != null)
            {
                facade.WireFromChildren();
                if (facade.vfx != null)
                {
                    return facade.vfx;
                }
            }

            var feature = root.GetComponentInChildren<VrmxtVfxInstance>(true);
            if (feature != null)
            {
                BindVfx(feature.gameObject, feature);
            }

            return feature;
        }

        /// <summary>
        /// Resolve materials override via facade prop, then direct child search.
        /// </summary>
        public static VrmxtMaterialsOverrideInstance FindMaterialsOverride(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var facade = root.GetComponentInChildren<VrmxtInstance>(true);
            if (facade != null)
            {
                facade.WireFromChildren();
                if (facade.materialsOverride != null)
                {
                    return facade.materialsOverride;
                }
            }

            var feature = root.GetComponentInChildren<VrmxtMaterialsOverrideInstance>(true);
            if (feature != null)
            {
                BindMaterialsOverride(feature.gameObject, feature);
            }

            return feature;
        }

        private void OnValidate()
        {
            WireFromChildren();
        }
    }
}
