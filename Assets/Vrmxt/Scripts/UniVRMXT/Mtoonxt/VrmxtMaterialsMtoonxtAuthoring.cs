using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Unity serialized stencil fields ↔ <c>VRMXT_materials_mtoonxt</c> objects.
    /// Inspector edits the fields. Export (and Apply) build JSON / GPU from them.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtAuthoring
    {
        private static Dictionary<Material, Material> s_exportStockCopies;

        /// <summary>
        /// Export remap: stock MToon copy of <paramref name="source"/>. Clip lists still
        /// point at authored assets; <see cref="ToExtension"/> matches via this map.
        /// </summary>
        public static void RegisterExportStockCopy(Material source, Material copy)
        {
            if (source == null || copy == null)
            {
                return;
            }

            if (s_exportStockCopies == null)
            {
                s_exportStockCopies = new Dictionary<Material, Material>();
            }

            s_exportStockCopies[source] = copy;
        }

        public static void ClearExportStockCopies()
        {
            s_exportStockCopies?.Clear();
        }

        public static void PopulateFromExtensionJson(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store
        )
        {
            if (store == null)
            {
                return;
            }

            for (var i = 0; i < store.Pairs.Count; i++)
            {
                PopulateFromExtensionJson(root, store, store.Pairs[i]);
            }
        }

        public static void PopulateFromExtensionJson(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            VrmxtMaterialsMtoonxtPair pair
        )
        {
            if (pair == null || string.IsNullOrEmpty(pair.ExtensionJson))
            {
                return;
            }

            if (!VrmxtMaterialsMtoonxt.TryParse(pair.ExtensionJson, out var xt) || xt == null)
            {
                return;
            }

            pair.BodyOp = BodyOpFromStencil(xt.Stencil);
            pair.OutlineOp = OutlineOpFromStencil(xt.OutlineStencil);
            ReplaceMaterials(pair.StencilTargets, ToMaterialList(root, store, xt.Stencil));
            ReplaceMaterials(
                pair.OutlineStencilTargets,
                ToMaterialList(root, store, xt.OutlineStencil)
            );
        }

        public static VrmxtMaterialsMtoonxtExtension ToExtension(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            VrmxtMaterialsMtoonxtPair pair
        )
        {
            if (pair == null)
            {
                return null;
            }

            string zTest = null;
            bool? zWrite = null;
            if (
                !string.IsNullOrEmpty(pair.ExtensionJson)
                && VrmxtMaterialsMtoonxt.TryParse(pair.ExtensionJson, out var imported)
                && imported != null
            )
            {
                zTest = imported.ZTest;
                zWrite = imported.ZWrite;
            }

            return new VrmxtMaterialsMtoonxtExtension(
                BodyToStencil(root, store, pair.BodyOp, pair.StencilTargets),
                OutlineToStencil(root, store, pair.OutlineOp, pair.OutlineStencilTargets),
                zTest,
                zWrite
            );
        }

        private static VrmxtMaterialsMtoonxtStencil BodyToStencil(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            VrmcMtoonxtBodyStencilOp op,
            List<Material> targets
        )
        {
            switch (op)
            {
                case VrmcMtoonxtBodyStencilOp.Write:
                    return VrmxtMaterialsMtoonxtStencil.FromOp(
                        VrmxtMaterialsMtoonxtStencil.OpWrite,
                        null
                    );
                case VrmcMtoonxtBodyStencilOp.ClipInside:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInside,
                        root,
                        store,
                        targets
                    );
                case VrmcMtoonxtBodyStencilOp.ClipInsideOverlay:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInsideOverlay,
                        root,
                        store,
                        targets
                    );
                case VrmcMtoonxtBodyStencilOp.ClipOutside:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpOutside,
                        root,
                        store,
                        targets
                    );
                default:
                    return null;
            }
        }

        private static VrmxtMaterialsMtoonxtStencil OutlineToStencil(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            VrmcMtoonxtOutlineStencilOp op,
            List<Material> targets
        )
        {
            switch (op)
            {
                case VrmcMtoonxtOutlineStencilOp.Same:
                    return VrmxtMaterialsMtoonxtStencil.FromOp(
                        VrmxtMaterialsMtoonxtStencil.OpSame,
                        null
                    );
                case VrmcMtoonxtOutlineStencilOp.Write:
                    return VrmxtMaterialsMtoonxtStencil.FromOp(
                        VrmxtMaterialsMtoonxtStencil.OpWrite,
                        null
                    );
                case VrmcMtoonxtOutlineStencilOp.ClipInside:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInside,
                        root,
                        store,
                        targets
                    );
                case VrmcMtoonxtOutlineStencilOp.ClipInsideOverlay:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInsideOverlay,
                        root,
                        store,
                        targets
                    );
                case VrmcMtoonxtOutlineStencilOp.ClipOutside:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpOutside,
                        root,
                        store,
                        targets
                    );
                default:
                    return null;
            }
        }

        private static VrmxtMaterialsMtoonxtStencil ClipToStencil(
            string op,
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            List<Material> targets
        )
        {
            var indices = MaterialsToIndices(root, store, targets);
            if (indices.Count == 0)
            {
                return null;
            }

            return VrmxtMaterialsMtoonxtStencil.FromOp(op, indices);
        }

        private static VrmcMtoonxtBodyStencilOp BodyOpFromStencil(
            VrmxtMaterialsMtoonxtStencil stencil
        )
        {
            if (stencil == null || !stencil.HasOp)
            {
                return VrmcMtoonxtBodyStencilOp.Off;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpWrite)
            {
                return VrmcMtoonxtBodyStencilOp.Write;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInside)
            {
                return VrmcMtoonxtBodyStencilOp.ClipInside;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInsideOverlay)
            {
                return VrmcMtoonxtBodyStencilOp.ClipInsideOverlay;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpOutside)
            {
                return VrmcMtoonxtBodyStencilOp.ClipOutside;
            }

            return VrmcMtoonxtBodyStencilOp.Off;
        }

        private static VrmcMtoonxtOutlineStencilOp OutlineOpFromStencil(
            VrmxtMaterialsMtoonxtStencil stencil
        )
        {
            if (stencil == null || !stencil.HasOp)
            {
                return VrmcMtoonxtOutlineStencilOp.Off;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpSame)
            {
                return VrmcMtoonxtOutlineStencilOp.Same;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpWrite)
            {
                return VrmcMtoonxtOutlineStencilOp.Write;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInside)
            {
                return VrmcMtoonxtOutlineStencilOp.ClipInside;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInsideOverlay)
            {
                return VrmcMtoonxtOutlineStencilOp.ClipInsideOverlay;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpOutside)
            {
                return VrmcMtoonxtOutlineStencilOp.ClipOutside;
            }

            return VrmcMtoonxtOutlineStencilOp.Off;
        }

        private static void ReplaceMaterials(List<Material> list, List<Material> values)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            if (values == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                list.Add(values[i]);
            }
        }

        private static List<Material> ToMaterialList(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            VrmxtMaterialsMtoonxtStencil stencil
        )
        {
            var list = new List<Material>();
            if (stencil == null || stencil.Materials == null)
            {
                return list;
            }

            for (var i = 0; i < stencil.Materials.Count; i++)
            {
                list.Add(FindMaterial(root, store, stencil.Materials[i]));
            }

            return list;
        }

        private static List<int> MaterialsToIndices(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            List<Material> materials
        )
        {
            var indices = new List<int>();
            if (materials == null)
            {
                return indices;
            }

            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                var index = FindGltfIndex(root, store, material);
                if (index >= 0 && !indices.Contains(index))
                {
                    indices.Add(index);
                }
            }

            return indices;
        }

        private static Material FindMaterial(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            int gltfIndex
        )
        {
            if (store == null || root == null)
            {
                return null;
            }

            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null || pair.GltfMaterialIndex != gltfIndex)
                {
                    continue;
                }

                foreach (
                    var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                        root,
                        pair.MaterialName
                    )
                )
                {
                    if (material != null)
                    {
                        return material;
                    }
                }
            }

            return null;
        }

        private static int FindGltfIndex(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            Material material
        )
        {
            if (store == null || material == null)
            {
                return -1;
            }

            var live = ExportLiveMaterial(material);
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null)
                {
                    continue;
                }

                foreach (
                    var candidate in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                        root,
                        pair.MaterialName
                    )
                )
                {
                    if (candidate == live || candidate == material)
                    {
                        return pair.GltfMaterialIndex;
                    }
                }
            }

            return FindGltfIndexByUniqueStrippedName(store, material);
        }

        private static Material ExportLiveMaterial(Material material)
        {
            if (
                material == null
                || s_exportStockCopies == null
                || !s_exportStockCopies.TryGetValue(material, out var copy)
                || copy == null
            )
            {
                return material;
            }

            return copy;
        }

        /// <summary>
        /// Export remaps MToonXT to throwaway stock MToon copies; clip lists keep authored
        /// assets. Name fallback only when a single store pair owns that stripped name so
        /// <c>Hair#1</c> / <c>Hair#2</c> cannot steal each other's glTF index when no copy map
        /// is registered.
        /// </summary>
        private static int FindGltfIndexByUniqueStrippedName(
            VrmxtMaterialsMtoonxtInstance store,
            Material material
        )
        {
            var clipped = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(material.name);
            if (string.IsNullOrEmpty(clipped))
            {
                return -1;
            }

            var match = -1;
            var hits = 0;
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null)
                {
                    continue;
                }

                if (!StoreKeyBaseEquals(pair.MaterialName, clipped))
                {
                    continue;
                }

                hits++;
                match = pair.GltfMaterialIndex;
                if (hits > 1)
                {
                    return -1;
                }
            }

            return hits == 1 ? match : -1;
        }

        private static bool StoreKeyBaseEquals(string storeKey, string strippedMaterialName)
        {
            if (string.IsNullOrEmpty(storeKey))
            {
                return false;
            }

            if (
                VrmxtMaterialsOverrideRuntime.TryGetDisambiguatedStoreKey(
                    storeKey,
                    out var baseName,
                    out _
                )
            )
            {
                storeKey = baseName;
            }

            var strippedKey = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(storeKey);
            return !string.IsNullOrEmpty(strippedKey)
                && string.Equals(strippedKey, strippedMaterialName, StringComparison.Ordinal);
        }
    }
}
