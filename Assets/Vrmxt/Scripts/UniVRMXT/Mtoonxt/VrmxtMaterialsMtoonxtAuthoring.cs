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

        public static void PopulateRelationships(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            IReadOnlyList<VrmxtMaterialsMtoonxtRelationship> relationships
        )
        {
            var authored = new List<VrmxtMaterialsMtoonxtRelationshipAuthoring>();
            if (store == null || relationships == null)
            {
                store?.SetStencilRelationships(authored);
                return;
            }

            for (var i = 0; i < relationships.Count; i++)
            {
                var relationship = relationships[i];
                if (relationship == null)
                {
                    continue;
                }

                var next = new VrmxtMaterialsMtoonxtRelationshipAuthoring(
                    ToMaterialList(root, store, relationship.Writers),
                    ToMaterialList(root, store, relationship.Readers)
                )
                {
                    Comparison = string.Equals(
                        relationship.Comparison,
                        VrmxtMaterialsMtoonxtRelationships.ComparisonInside,
                        StringComparison.Ordinal
                    )
                        ? VrmxtMtoonxtRelationshipComparison.Inside
                        : VrmxtMtoonxtRelationshipComparison.Outside,
                    ShowWritersThroughOccluders = relationship.ShowWritersThroughOccluders,
                    WritersOnlyInsideReaders = relationship.WritersOnlyInsideReaders,
                    WritersOnlyOutsideReaders = relationship.WritersOnlyOutsideReaders,
                    WritersSelfOcclude = relationship.WritersSelfOcclude,
                    IgnoreOccludedReaderAreas = relationship.IgnoreOccludedReaderAreas,
                    WritersWriteColor = relationship.WritersWriteColor,
                    WritersWriteDepth = relationship.WritersWriteDepth,
                    ReadersWriteDepth = relationship.ReadersWriteDepth,
                    WriterDepthTest = DepthTestFromPortable(relationship.WriterDepthTest),
                    ReaderDepthTest = DepthTestFromPortable(relationship.ReaderDepthTest),
                };
                if (next.Writers.Count > 0 && next.Readers.Count > 0)
                {
                    authored.Add(next);
                }
            }

            store.SetStencilRelationships(authored);
        }

        public static List<VrmxtMaterialsMtoonxtRelationship> ToRelationships(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store
        )
        {
            var result = new List<VrmxtMaterialsMtoonxtRelationship>();
            if (root == null || store == null)
            {
                return result;
            }

            for (var i = 0; i < store.StencilRelationships.Count; i++)
            {
                var authored = store.StencilRelationships[i];
                if (authored == null || (authored.WritersOnlyInsideReaders && authored.WritersOnlyOutsideReaders))
                {
                    continue;
                }

                var writers = MaterialsToIndices(root, store, authored.Writers);
                var readers = MaterialsToIndices(root, store, authored.Readers);
                if (writers.Count == 0 || readers.Count == 0 || Intersects(writers, readers))
                {
                    continue;
                }

                result.Add(
                    new VrmxtMaterialsMtoonxtRelationship(
                        writers,
                        readers,
                        authored.Comparison == VrmxtMtoonxtRelationshipComparison.Inside
                            ? VrmxtMaterialsMtoonxtRelationships.ComparisonInside
                            : VrmxtMaterialsMtoonxtRelationships.ComparisonOutside,
                        authored.ShowWritersThroughOccluders,
                        authored.WritersOnlyInsideReaders,
                        authored.WritersOnlyOutsideReaders,
                        authored.WritersSelfOcclude,
                        authored.IgnoreOccludedReaderAreas,
                        authored.WritersWriteDepth,
                        authored.ReadersWriteDepth,
                        DepthTestToPortable(authored.WriterDepthTest),
                        DepthTestToPortable(authored.ReaderDepthTest),
                        writersWriteColor: authored.WritersWriteColor
                    )
                );
            }

            return result;
        }

        public static List<VrmxtMaterialsMtoonxtRelationship> ToExportRelationships(
            VrmxtMaterialsMtoonxtInstance store,
            Func<Material, int?> resolveMaterialIndex
        )
        {
            var result = new List<VrmxtMaterialsMtoonxtRelationship>();
            if (store == null || resolveMaterialIndex == null)
            {
                return result;
            }

            for (var i = 0; i < store.StencilRelationships.Count; i++)
            {
                var authored = store.StencilRelationships[i];
                if (
                    authored == null
                    || (authored.WritersOnlyInsideReaders && authored.WritersOnlyOutsideReaders)
                    || !ResolveMaterialIndices(
                        authored.Writers,
                        resolveMaterialIndex,
                        out var writers
                    )
                    || !ResolveMaterialIndices(
                        authored.Readers,
                        resolveMaterialIndex,
                        out var readers
                    )
                    || Intersects(writers, readers)
                )
                {
                    continue;
                }

                result.Add(
                    new VrmxtMaterialsMtoonxtRelationship(
                        writers,
                        readers,
                        authored.Comparison == VrmxtMtoonxtRelationshipComparison.Inside
                            ? VrmxtMaterialsMtoonxtRelationships.ComparisonInside
                            : VrmxtMaterialsMtoonxtRelationships.ComparisonOutside,
                        authored.ShowWritersThroughOccluders,
                        authored.WritersOnlyInsideReaders,
                        authored.WritersOnlyOutsideReaders,
                        authored.WritersSelfOcclude,
                        authored.IgnoreOccludedReaderAreas,
                        authored.WritersWriteDepth,
                        authored.ReadersWriteDepth,
                        DepthTestToPortable(authored.WriterDepthTest),
                        DepthTestToPortable(authored.ReaderDepthTest),
                        writersWriteColor: authored.WritersWriteColor
                    )
                );
            }

            return result;
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
                null,
                null,
                zTest,
                zWrite
            );
        }

        private static VrmxtMaterialsMtoonxtStencil BodyToStencil(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            VrmxtMtoonxtBodyStencilOp op,
            List<Material> targets
        )
        {
            switch (op)
            {
                case VrmxtMtoonxtBodyStencilOp.Write:
                    return VrmxtMaterialsMtoonxtStencil.FromOp(
                        VrmxtMaterialsMtoonxtStencil.OpWrite,
                        null
                    );
                case VrmxtMtoonxtBodyStencilOp.ClipInside:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInside,
                        root,
                        store,
                        targets
                    );
                case VrmxtMtoonxtBodyStencilOp.ClipInsideOverlay:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInsideOverlay,
                        root,
                        store,
                        targets
                    );
                case VrmxtMtoonxtBodyStencilOp.ClipOutside:
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
            VrmxtMtoonxtOutlineStencilOp op,
            List<Material> targets
        )
        {
            switch (op)
            {
                case VrmxtMtoonxtOutlineStencilOp.Same:
                    return VrmxtMaterialsMtoonxtStencil.FromOp(
                        VrmxtMaterialsMtoonxtStencil.OpSame,
                        null
                    );
                case VrmxtMtoonxtOutlineStencilOp.Write:
                    return VrmxtMaterialsMtoonxtStencil.FromOp(
                        VrmxtMaterialsMtoonxtStencil.OpWrite,
                        null
                    );
                case VrmxtMtoonxtOutlineStencilOp.ClipInside:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInside,
                        root,
                        store,
                        targets
                    );
                case VrmxtMtoonxtOutlineStencilOp.ClipInsideOverlay:
                    return ClipToStencil(
                        VrmxtMaterialsMtoonxtStencil.OpInsideOverlay,
                        root,
                        store,
                        targets
                    );
                case VrmxtMtoonxtOutlineStencilOp.ClipOutside:
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

        private static VrmxtMtoonxtBodyStencilOp BodyOpFromStencil(
            VrmxtMaterialsMtoonxtStencil stencil
        )
        {
            if (stencil == null || !stencil.HasOp)
            {
                return VrmxtMtoonxtBodyStencilOp.Off;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpWrite)
            {
                return VrmxtMtoonxtBodyStencilOp.Write;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInside)
            {
                return VrmxtMtoonxtBodyStencilOp.ClipInside;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInsideOverlay)
            {
                return VrmxtMtoonxtBodyStencilOp.ClipInsideOverlay;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpOutside)
            {
                return VrmxtMtoonxtBodyStencilOp.ClipOutside;
            }

            return VrmxtMtoonxtBodyStencilOp.Off;
        }

        private static VrmxtMtoonxtOutlineStencilOp OutlineOpFromStencil(
            VrmxtMaterialsMtoonxtStencil stencil
        )
        {
            if (stencil == null || !stencil.HasOp)
            {
                return VrmxtMtoonxtOutlineStencilOp.Off;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpSame)
            {
                return VrmxtMtoonxtOutlineStencilOp.Same;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpWrite)
            {
                return VrmxtMtoonxtOutlineStencilOp.Write;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInside)
            {
                return VrmxtMtoonxtOutlineStencilOp.ClipInside;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpInsideOverlay)
            {
                return VrmxtMtoonxtOutlineStencilOp.ClipInsideOverlay;
            }

            if (stencil.Op == VrmxtMaterialsMtoonxtStencil.OpOutside)
            {
                return VrmxtMtoonxtOutlineStencilOp.ClipOutside;
            }

            return VrmxtMtoonxtOutlineStencilOp.Off;
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

        private static List<Material> ToMaterialList(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            IReadOnlyList<int> indices
        )
        {
            var list = new List<Material>();
            if (indices == null)
            {
                return list;
            }

            for (var i = 0; i < indices.Count; i++)
            {
                var material = FindMaterial(root, store, indices[i]);
                if (material != null && !list.Contains(material))
                {
                    list.Add(material);
                }
            }

            return list;
        }

        private static bool Intersects(IReadOnlyList<int> writers, IReadOnlyList<int> readers)
        {
            for (var i = 0; i < writers.Count; i++)
            {
                for (var j = 0; j < readers.Count; j++)
                {
                    if (writers[i] == readers[j])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ResolveMaterialIndices(
            IReadOnlyList<Material> materials,
            Func<Material, int?> resolve,
            out int[] indices
        )
        {
            indices = null;
            if (materials == null || materials.Count == 0)
            {
                return false;
            }

            var result = new List<int>(materials.Count);
            for (var i = 0; i < materials.Count; i++)
            {
                var material = ExportLiveMaterial(materials[i]);
                var index = resolve(material);
                if (!index.HasValue || index.Value < 0)
                {
                    return false;
                }

                if (!result.Contains(index.Value))
                {
                    result.Add(index.Value);
                }
            }

            indices = result.ToArray();
            return indices.Length > 0;
        }

        public static string DepthTestToPortable(VrmxtMtoonxtDepthTest value)
        {
            switch (value)
            {
                case VrmxtMtoonxtDepthTest.Never:
                    return "never";
                case VrmxtMtoonxtDepthTest.Less:
                    return "less";
                case VrmxtMtoonxtDepthTest.Equal:
                    return "equal";
                case VrmxtMtoonxtDepthTest.Greater:
                    return "greater";
                case VrmxtMtoonxtDepthTest.NotEqual:
                    return "notEqual";
                case VrmxtMtoonxtDepthTest.GreaterEqual:
                    return "greaterEqual";
                case VrmxtMtoonxtDepthTest.Always:
                    return "always";
                default:
                    return VrmxtMaterialsMtoonxt.ZTestDefault;
            }
        }

        public static VrmxtMtoonxtDepthTest DepthTestFromPortable(string value)
        {
            switch (value)
            {
                case "never":
                    return VrmxtMtoonxtDepthTest.Never;
                case "less":
                    return VrmxtMtoonxtDepthTest.Less;
                case "equal":
                    return VrmxtMtoonxtDepthTest.Equal;
                case "greater":
                    return VrmxtMtoonxtDepthTest.Greater;
                case "notEqual":
                    return VrmxtMtoonxtDepthTest.NotEqual;
                case "greaterEqual":
                    return VrmxtMtoonxtDepthTest.GreaterEqual;
                case "always":
                    return VrmxtMtoonxtDepthTest.Always;
                default:
                    return VrmxtMtoonxtDepthTest.LessEqual;
            }
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
