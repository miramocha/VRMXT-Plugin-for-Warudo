using System;
using System.Collections.Generic;
using System.Text;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Compile <c>op</c> + material indices to GPU Ref / compare / pass.
    /// One Ref table for body and outline. Clip lists resolve writers via body
    /// <c>stencil.op</c> <c>write</c>.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtStencilCompiler
    {
        public static void Compile(
            IReadOnlyList<VrmxtMaterialsMtoonxtExtension> extrasByIndex,
            out VrmxtMaterialsMtoonxtStencil[] body,
            out VrmxtMaterialsMtoonxtStencil[] outline
        )
        {
            var count = extrasByIndex != null ? extrasByIndex.Count : 0;
            body = new VrmxtMaterialsMtoonxtStencil[count];
            outline = new VrmxtMaterialsMtoonxtStencil[count];
            if (count == 0)
            {
                return;
            }

            var sets = new Dictionary<string, int[]>(StringComparer.Ordinal);
            var writerToKeys = new Dictionary<int, HashSet<string>>();

            for (var i = 0; i < count; i++)
            {
                CollectReaderSet(
                    extrasByIndex,
                    count,
                    i,
                    GetSource(extrasByIndex[i], body: true),
                    sets,
                    writerToKeys
                );
                CollectReaderSet(
                    extrasByIndex,
                    count,
                    i,
                    GetSource(extrasByIndex[i], body: false),
                    sets,
                    writerToKeys
                );
            }

            for (var i = 0; i < count; i++)
            {
                CollectUnlistedWrite(
                    GetSource(extrasByIndex[i], body: true),
                    i,
                    sets,
                    writerToKeys
                );
                CollectUnlistedWrite(
                    GetSource(extrasByIndex[i], body: false),
                    i,
                    sets,
                    writerToKeys
                );
            }

            var invalidKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in writerToKeys)
            {
                if (pair.Value.Count > 1)
                {
                    foreach (var key in pair.Value)
                    {
                        invalidKeys.Add(key);
                    }
                }
            }

            var refByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var nextRef = 1;
            var orderedKeys = new List<string>(sets.Keys);
            orderedKeys.Sort(StringComparer.Ordinal);
            for (var k = 0; k < orderedKeys.Count; k++)
            {
                var key = orderedKeys[k];
                if (invalidKeys.Contains(key) || nextRef > 255)
                {
                    continue;
                }

                refByKey[key] = nextRef;
                nextRef++;
            }

            for (var i = 0; i < count; i++)
            {
                body[i] = CompileOne(
                    i,
                    GetSource(extrasByIndex[i], body: true),
                    isBody: true,
                    extrasByIndex,
                    count,
                    writerToKeys,
                    invalidKeys,
                    refByKey,
                    body
                );
            }

            for (var i = 0; i < count; i++)
            {
                outline[i] = CompileOne(
                    i,
                    GetSource(extrasByIndex[i], body: false),
                    isBody: false,
                    extrasByIndex,
                    count,
                    writerToKeys,
                    invalidKeys,
                    refByKey,
                    body
                );
            }
        }

        private static void CollectReaderSet(
            IReadOnlyList<VrmxtMaterialsMtoonxtExtension> extrasByIndex,
            int count,
            int readerIndex,
            VrmxtMaterialsMtoonxtStencil stencil,
            Dictionary<string, int[]> sets,
            Dictionary<int, HashSet<string>> writerToKeys
        )
        {
            if (stencil == null || !stencil.HasOp)
            {
                return;
            }

            if (
                string.Equals(
                    stencil.Op,
                    VrmxtMaterialsMtoonxtStencil.OpSame,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    stencil.Op,
                    VrmxtMaterialsMtoonxtStencil.OpWrite,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            if (
                !TryNormalizeReaderSet(
                    stencil,
                    readerIndex,
                    count,
                    extrasByIndex,
                    out var sorted,
                    out var key
                )
            )
            {
                return;
            }

            sets[key] = sorted;
            RegisterWriters(writerToKeys, sorted, key);
        }

        private static void CollectUnlistedWrite(
            VrmxtMaterialsMtoonxtStencil stencil,
            int index,
            Dictionary<string, int[]> sets,
            Dictionary<int, HashSet<string>> writerToKeys
        )
        {
            if (
                stencil == null
                || !string.Equals(
                    stencil.Op,
                    VrmxtMaterialsMtoonxtStencil.OpWrite,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            if (writerToKeys.ContainsKey(index))
            {
                return;
            }

            var singleton = new[] { index };
            var key = MakeKey(singleton);
            sets[key] = singleton;
            RegisterWriters(writerToKeys, singleton, key);
        }

        private static VrmxtMaterialsMtoonxtStencil CompileOne(
            int index,
            VrmxtMaterialsMtoonxtStencil source,
            bool isBody,
            IReadOnlyList<VrmxtMaterialsMtoonxtExtension> extrasByIndex,
            int count,
            Dictionary<int, HashSet<string>> writerToKeys,
            HashSet<string> invalidKeys,
            Dictionary<string, int> refByKey,
            VrmxtMaterialsMtoonxtStencil[] bodyOut
        )
        {
            if (source == null || !source.HasOp)
            {
                return null;
            }

            if (
                string.Equals(
                    source.Op,
                    VrmxtMaterialsMtoonxtStencil.OpSame,
                    StringComparison.Ordinal
                )
            )
            {
                if (isBody)
                {
                    return null;
                }

                return bodyOut[index];
            }

            if (
                string.Equals(
                    source.Op,
                    VrmxtMaterialsMtoonxtStencil.OpWrite,
                    StringComparison.Ordinal
                )
            )
            {
                if (!writerToKeys.TryGetValue(index, out var keys) || keys.Count != 1)
                {
                    return null;
                }

                string onlyKey = null;
                foreach (var key in keys)
                {
                    onlyKey = key;
                }

                if (
                    onlyKey == null
                    || invalidKeys.Contains(onlyKey)
                    || !refByKey.TryGetValue(onlyKey, out var writeRef)
                )
                {
                    return null;
                }

                return VrmxtMaterialsMtoonxtStencil.Compiled(writeRef, "always", "replace");
            }

            if (
                !TryNormalizeReaderSet(
                    source,
                    index,
                    count,
                    extrasByIndex,
                    out _,
                    out var readerKey
                )
                || invalidKeys.Contains(readerKey)
                || !refByKey.TryGetValue(readerKey, out var clipRef)
            )
            {
                return null;
            }

            if (
                string.Equals(
                    source.Op,
                    VrmxtMaterialsMtoonxtStencil.OpInside,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    source.Op,
                    VrmxtMaterialsMtoonxtStencil.OpInsideOverlay,
                    StringComparison.Ordinal
                )
            )
            {
                return VrmxtMaterialsMtoonxtStencil.Compiled(clipRef, "equal", "keep");
            }

            if (
                string.Equals(
                    source.Op,
                    VrmxtMaterialsMtoonxtStencil.OpOutside,
                    StringComparison.Ordinal
                )
            )
            {
                return VrmxtMaterialsMtoonxtStencil.Compiled(clipRef, "notEqual", "keep");
            }

            return null;
        }

        private static bool TryNormalizeReaderSet(
            VrmxtMaterialsMtoonxtStencil stencil,
            int readerIndex,
            int materialCount,
            IReadOnlyList<VrmxtMaterialsMtoonxtExtension> extrasByIndex,
            out int[] sorted,
            out string key
        )
        {
            sorted = null;
            key = null;
            if (stencil.Materials == null || stencil.Materials.Count == 0)
            {
                return false;
            }

            var unique = new SortedSet<int>();
            for (var i = 0; i < stencil.Materials.Count; i++)
            {
                var writerIndex = stencil.Materials[i];
                if (writerIndex < 0 || writerIndex >= materialCount || writerIndex == readerIndex)
                {
                    return false;
                }

                var writer = GetBodyWrite(extrasByIndex[writerIndex]);
                if (
                    writer == null
                    || !string.Equals(
                        writer.Op,
                        VrmxtMaterialsMtoonxtStencil.OpWrite,
                        StringComparison.Ordinal
                    )
                )
                {
                    return false;
                }

                unique.Add(writerIndex);
            }

            if (unique.Count == 0)
            {
                return false;
            }

            sorted = new int[unique.Count];
            unique.CopyTo(sorted);
            key = MakeKey(sorted);
            return true;
        }

        private static VrmxtMaterialsMtoonxtStencil GetBodyWrite(VrmxtMaterialsMtoonxtExtension extra)
        {
            return extra != null ? extra.Stencil : null;
        }

        private static void RegisterWriters(
            Dictionary<int, HashSet<string>> writerToKeys,
            int[] sorted,
            string key
        )
        {
            for (var i = 0; i < sorted.Length; i++)
            {
                var writer = sorted[i];
                if (!writerToKeys.TryGetValue(writer, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.Ordinal);
                    writerToKeys[writer] = keys;
                }

                keys.Add(key);
            }
        }

        private static string MakeKey(int[] sorted)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < sorted.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(sorted[i]);
            }

            return sb.ToString();
        }

        private static VrmxtMaterialsMtoonxtStencil GetSource(
            VrmxtMaterialsMtoonxtExtension extra,
            bool body
        )
        {
            if (extra == null)
            {
                return null;
            }

            return body ? extra.Stencil : extra.OutlineStencil;
        }
    }
}
