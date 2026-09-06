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

        public static void PopulateStencils(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            IReadOnlyList<VrmxtMaterialsMtoonxtStencil> stencils
        )
        {
            var authored = new List<VrmxtMaterialsMtoonxtStencilAuthoring>();
            if (store == null || stencils == null)
            {
                store?.SetStencils(authored);
                return;
            }

            for (var i = 0; i < stencils.Count; i++)
            {
                var stencil = stencils[i];
                if (stencil == null)
                {
                    continue;
                }

                var next = new VrmxtMaterialsMtoonxtStencilAuthoring(
                    ToMaterialList(root, store, stencil.Writers),
                    ToMaterialList(root, store, stencil.Readers)
                )
                {
                    Comparison = string.Equals(
                        stencil.Comparison,
                        VrmxtMaterialsMtoonxtStencils.ComparisonInside,
                        StringComparison.Ordinal
                    )
                        ? VrmxtMtoonxtStencilComparison.Inside
                        : VrmxtMtoonxtStencilComparison.Outside,
                    ShowWritersThroughOccluders = stencil.ShowWritersThroughOccluders,
                    WritersOnlyInsideReaders = stencil.WritersOnlyInsideReaders,
                    WritersOnlyOutsideReaders = stencil.WritersOnlyOutsideReaders,
                    WritersSelfOcclude = stencil.WritersSelfOcclude,
                    IgnoreOccludedReaderAreas = stencil.IgnoreOccludedReaderAreas,
                    WritersWriteColor = stencil.WritersWriteColor,
                    WritersWriteDepth = stencil.WritersWriteDepth,
                    ReadersWriteDepth = stencil.ReadersWriteDepth,
                    WriterDepthTest = DepthTestFromPortable(stencil.WriterDepthTest),
                    ReaderDepthTest = DepthTestFromPortable(stencil.ReaderDepthTest),
                };
                if (next.Writers.Count > 0 && next.Readers.Count > 0)
                {
                    authored.Add(next);
                }
            }

            store.SetStencils(authored);
        }

        public static List<VrmxtMaterialsMtoonxtStencil> ToStencils(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store
        )
        {
            var result = new List<VrmxtMaterialsMtoonxtStencil>();
            if (root == null || store == null)
            {
                return result;
            }

            for (var i = 0; i < store.Stencils.Count; i++)
            {
                var authored = store.Stencils[i];
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
                    new VrmxtMaterialsMtoonxtStencil(
                        writers,
                        readers,
                        authored.Comparison == VrmxtMtoonxtStencilComparison.Inside
                            ? VrmxtMaterialsMtoonxtStencils.ComparisonInside
                            : VrmxtMaterialsMtoonxtStencils.ComparisonOutside,
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

        public static List<VrmxtMaterialsMtoonxtStencil> ToExportStencils(
            VrmxtMaterialsMtoonxtInstance store,
            Func<Material, int?> resolveMaterialIndex
        )
        {
            var result = new List<VrmxtMaterialsMtoonxtStencil>();
            if (store == null || resolveMaterialIndex == null)
            {
                return result;
            }

            for (var i = 0; i < store.Stencils.Count; i++)
            {
                var authored = store.Stencils[i];
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
                    new VrmxtMaterialsMtoonxtStencil(
                        writers,
                        readers,
                        authored.Comparison == VrmxtMtoonxtStencilComparison.Inside
                            ? VrmxtMaterialsMtoonxtStencils.ComparisonInside
                            : VrmxtMaterialsMtoonxtStencils.ComparisonOutside,
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

            VrmxtMaterialsMtoonxt.TryParse(pair.ExtensionJson, out _);
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

            return new VrmxtMaterialsMtoonxtExtension(zTest, zWrite);
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
