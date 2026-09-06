using System.Collections.Generic;
using UnityEngine;
using UniVRMXT.MaterialsOverride;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// Authoring warnings when MToon <c>_AlphaMode</c> puts a stencil writer after
    /// its reader (Unity mapped queues). Spec: sibling <c>alphaMode</c> rank on
    /// <c>VRMC_materials_mtoon</c>.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtDrawOrder
    {
        public const int RankOpaque = 0;
        public const int RankCutout = 1;
        public const int RankBlend = 2;

        public static bool TryGetAlphaRank(Material material, out int rank)
        {
            rank = RankOpaque;
            if (material == null || !material.HasProperty("_AlphaMode"))
            {
                return false;
            }

            switch (material.GetInt("_AlphaMode"))
            {
                case 1:
                    rank = RankCutout;
                    return true;
                case 2:
                    rank = RankBlend;
                    return true;
                default:
                    rank = RankOpaque;
                    return true;
            }
        }

        public static bool WriterDrawsAfterReader(int writerRank, int readerRank)
        {
            return writerRank > readerRank;
        }

        public static string AlphaLabel(int rank)
        {
            switch (rank)
            {
                case RankCutout:
                    return "Cutout";
                case RankBlend:
                    return "Transparent";
                default:
                    return "Opaque";
            }
        }

        public static List<VrmxtMaterialsMtoonxtDrawWarning> CollectForPair(
            VrmxtMaterialsMtoonxtInstance instance,
            VrmxtMaterialsMtoonxtPair pair
        )
        {
            var warnings = new List<VrmxtMaterialsMtoonxtDrawWarning>();
            if (instance == null || pair == null)
            {
                return warnings;
            }

            var root = instance.gameObject;
            var focus = ResolvePairMaterial(root, pair);
            if (focus == null)
            {
                return warnings;
            }

            var seen = new HashSet<long>();
            for (var i = 0; i < instance.Stencils.Count; i++)
            {
                var stencil = instance.Stencils[i];
                if (stencil == null)
                {
                    continue;
                }

                if (ListContains(stencil.Readers, focus))
                {
                    AddWriterList(stencil.Writers, focus, writerIsSelf: false, seen, warnings);
                }

                if (ListContains(stencil.Writers, focus) && stencil.Readers != null)
                {
                    for (var j = 0; j < stencil.Readers.Count; j++)
                    {
                        TryAddPair(focus, stencil.Readers[j], writerIsSelf: true, seen, warnings);
                    }
                }
            }

            return warnings;
        }

        private static void AddWriterList(
            List<Material> writers,
            Material reader,
            bool writerIsSelf,
            HashSet<long> seen,
            List<VrmxtMaterialsMtoonxtDrawWarning> warnings
        )
        {
            if (writers == null)
            {
                return;
            }

            for (var i = 0; i < writers.Count; i++)
            {
                TryAddPair(writers[i], reader, writerIsSelf, seen, warnings);
            }
        }

        private static void TryAddPair(
            Material writer,
            Material reader,
            bool writerIsSelf,
            HashSet<long> seen,
            List<VrmxtMaterialsMtoonxtDrawWarning> warnings
        )
        {
            if (
                !TryGetAlphaRank(writer, out var writerRank)
                || !TryGetAlphaRank(reader, out var readerRank)
                || !WriterDrawsAfterReader(writerRank, readerRank)
            )
            {
                return;
            }

            var key = ((long)writer.GetInstanceID() << 32) ^ (uint)reader.GetInstanceID();
            if (!seen.Add(key))
            {
                return;
            }

            var writerLabel = AlphaLabel(writerRank);
            var readerLabel = AlphaLabel(readerRank);
            var writerName = string.IsNullOrEmpty(writer.name) ? "Write material" : writer.name;
            var readerName = string.IsNullOrEmpty(reader.name) ? "Clip material" : reader.name;
            if (writerIsSelf)
            {
                warnings.Add(
                    new VrmxtMaterialsMtoonxtDrawWarning(
                        readerName + " is " + readerLabel + " and clips this Write material",
                        "This material is " + writerLabel + ". Write may draw too late for clip"
                    )
                );
                return;
            }

            warnings.Add(
                new VrmxtMaterialsMtoonxtDrawWarning(
                    writerName + " is " + writerLabel + " and set to Write",
                    "This material is " + readerLabel + ". Write may draw too late for clip"
                )
            );
        }

        private static Material ResolvePairMaterial(GameObject root, VrmxtMaterialsMtoonxtPair pair)
        {
            if (root == null || pair == null)
            {
                return null;
            }

            foreach (
                var found in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                    root,
                    pair.MaterialName
                )
            )
            {
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool ListContains(List<Material> list, Material material)
        {
            if (list == null || material == null)
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == material)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
