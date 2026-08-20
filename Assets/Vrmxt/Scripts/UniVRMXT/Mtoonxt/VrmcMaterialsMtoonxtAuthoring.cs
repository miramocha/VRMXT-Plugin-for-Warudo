using System.Collections.Generic;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Unity serialized stencil fields ↔ <c>VRMC_materials_mtoonxt</c> objects.
    /// Inspector edits the fields. Export (and Apply) build JSON / GPU from them.
    /// </summary>
    public static class VrmcMaterialsMtoonxtAuthoring
    {
        public static void PopulateFromExtensionJson(
            GameObject root,
            VrmcMaterialsMtoonxtInstance store)
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
            VrmcMaterialsMtoonxtInstance store,
            VrmcMaterialsMtoonxtPair pair)
        {
            if (pair == null || string.IsNullOrEmpty(pair.ExtensionJson))
            {
                return;
            }

            if (!VrmcMaterialsMtoonxt.TryParse(pair.ExtensionJson, out var xt) || xt == null)
            {
                return;
            }

            pair.BodyOp = BodyOpFromStencil(xt.Stencil);
            pair.OutlineOp = OutlineOpFromStencil(xt.OutlineStencil);
            ReplaceMaterials(pair.StencilTargets, ToMaterialList(root, store, xt.Stencil));
            ReplaceMaterials(pair.OutlineStencilTargets, ToMaterialList(root, store, xt.OutlineStencil));
        }

        public static VrmcMaterialsMtoonxtExtension ToExtension(
            GameObject root,
            VrmcMaterialsMtoonxtInstance store,
            VrmcMaterialsMtoonxtPair pair)
        {
            if (pair == null)
            {
                return null;
            }

            string zTest = null;
            bool? zWrite = null;
            if (!string.IsNullOrEmpty(pair.ExtensionJson) &&
                VrmcMaterialsMtoonxt.TryParse(pair.ExtensionJson, out var imported) &&
                imported != null)
            {
                zTest = imported.ZTest;
                zWrite = imported.ZWrite;
            }

            return new VrmcMaterialsMtoonxtExtension(
                BodyToStencil(root, store, pair.BodyOp, pair.StencilTargets),
                OutlineToStencil(root, store, pair.OutlineOp, pair.OutlineStencilTargets),
                zTest,
                zWrite);
        }

        private static VrmcMaterialsMtoonxtStencil BodyToStencil(
            GameObject root,
            VrmcMaterialsMtoonxtInstance store,
            VrmcMtoonxtBodyStencilOp op,
            List<Material> targets)
        {
            switch (op)
            {
                case VrmcMtoonxtBodyStencilOp.Write:
                    return VrmcMaterialsMtoonxtStencil.FromOp(VrmcMaterialsMtoonxtStencil.OpWrite, null);
                case VrmcMtoonxtBodyStencilOp.ClipInside:
                    return ClipToStencil(VrmcMaterialsMtoonxtStencil.OpInside, root, store, targets);
                case VrmcMtoonxtBodyStencilOp.ClipOutside:
                    return ClipToStencil(VrmcMaterialsMtoonxtStencil.OpOutside, root, store, targets);
                default:
                    return null;
            }
        }

        private static VrmcMaterialsMtoonxtStencil OutlineToStencil(
            GameObject root,
            VrmcMaterialsMtoonxtInstance store,
            VrmcMtoonxtOutlineStencilOp op,
            List<Material> targets)
        {
            switch (op)
            {
                case VrmcMtoonxtOutlineStencilOp.Same:
                    return VrmcMaterialsMtoonxtStencil.FromOp(VrmcMaterialsMtoonxtStencil.OpSame, null);
                case VrmcMtoonxtOutlineStencilOp.Write:
                    return VrmcMaterialsMtoonxtStencil.FromOp(VrmcMaterialsMtoonxtStencil.OpWrite, null);
                case VrmcMtoonxtOutlineStencilOp.ClipInside:
                    return ClipToStencil(VrmcMaterialsMtoonxtStencil.OpInside, root, store, targets);
                case VrmcMtoonxtOutlineStencilOp.ClipOutside:
                    return ClipToStencil(VrmcMaterialsMtoonxtStencil.OpOutside, root, store, targets);
                default:
                    return null;
            }
        }

        private static VrmcMaterialsMtoonxtStencil ClipToStencil(
            string op,
            GameObject root,
            VrmcMaterialsMtoonxtInstance store,
            List<Material> targets)
        {
            var indices = MaterialsToIndices(root, store, targets);
            if (indices.Count == 0)
            {
                return null;
            }

            return VrmcMaterialsMtoonxtStencil.FromOp(op, indices);
        }

        private static VrmcMtoonxtBodyStencilOp BodyOpFromStencil(VrmcMaterialsMtoonxtStencil stencil)
        {
            if (stencil == null || !stencil.HasOp)
            {
                return VrmcMtoonxtBodyStencilOp.Off;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpWrite)
            {
                return VrmcMtoonxtBodyStencilOp.Write;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpInside)
            {
                return VrmcMtoonxtBodyStencilOp.ClipInside;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpOutside)
            {
                return VrmcMtoonxtBodyStencilOp.ClipOutside;
            }

            return VrmcMtoonxtBodyStencilOp.Off;
        }

        private static VrmcMtoonxtOutlineStencilOp OutlineOpFromStencil(VrmcMaterialsMtoonxtStencil stencil)
        {
            if (stencil == null || !stencil.HasOp)
            {
                return VrmcMtoonxtOutlineStencilOp.Off;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpSame)
            {
                return VrmcMtoonxtOutlineStencilOp.Same;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpWrite)
            {
                return VrmcMtoonxtOutlineStencilOp.Write;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpInside)
            {
                return VrmcMtoonxtOutlineStencilOp.ClipInside;
            }

            if (stencil.Op == VrmcMaterialsMtoonxtStencil.OpOutside)
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
            VrmcMaterialsMtoonxtInstance store,
            VrmcMaterialsMtoonxtStencil stencil)
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
            VrmcMaterialsMtoonxtInstance store,
            List<Material> materials)
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
            VrmcMaterialsMtoonxtInstance store,
            int gltfIndex)
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

                foreach (var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                             root,
                             pair.MaterialName))
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
            VrmcMaterialsMtoonxtInstance store,
            Material material)
        {
            if (store == null || material == null)
            {
                return -1;
            }

            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null)
                {
                    continue;
                }

                foreach (var candidate in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                             root,
                             pair.MaterialName))
                {
                    if (candidate == material)
                    {
                        return pair.GltfMaterialIndex;
                    }
                }
            }

            return -1;
        }
    }
}
