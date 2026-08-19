using System;
using System.Collections.Generic;
using System.Text;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Compile <c>op</c> + material indices to GPU Ref / compare / pass.
    /// </summary>
    public static class VrmcMaterialsMtoonxtStencilCompiler
    {
        public static void Compile(
            IReadOnlyList<VrmcMaterialsMtoonxtExtension> extrasByIndex,
            out VrmcMaterialsMtoonxtStencil[] body,
            out VrmcMaterialsMtoonxtStencil[] outline)
        {
            var count = extrasByIndex != null ? extrasByIndex.Count : 0;
            body = new VrmcMaterialsMtoonxtStencil[count];
            outline = new VrmcMaterialsMtoonxtStencil[count];
            if (count == 0)
            {
                return;
            }

            CompilePass(extrasByIndex, count, body: true, body, outline);
            CompilePass(extrasByIndex, count, body: false, body, outline);
        }

        private static void CompilePass(
            IReadOnlyList<VrmcMaterialsMtoonxtExtension> extrasByIndex,
            int count,
            bool body,
            VrmcMaterialsMtoonxtStencil[] bodyOut,
            VrmcMaterialsMtoonxtStencil[] outlineOut)
        {
            var sets = new Dictionary<string, int[]>(StringComparer.Ordinal);
            var writerToKeys = new Dictionary<int, HashSet<string>>();

            for (var i = 0; i < count; i++)
            {
                var stencil = GetSource(extrasByIndex[i], body);
                if (stencil == null || !stencil.HasOp)
                {
                    continue;
                }

                if (string.Equals(stencil.Op, VrmcMaterialsMtoonxtStencil.OpSame, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(stencil.Op, VrmcMaterialsMtoonxtStencil.OpWrite, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryNormalizeReaderSet(
                        stencil,
                        i,
                        count,
                        extrasByIndex,
                        body,
                        out var sorted,
                        out var key))
                {
                    continue;
                }

                sets[key] = sorted;
                RegisterWriters(writerToKeys, sorted, key);
            }

            for (var i = 0; i < count; i++)
            {
                var stencil = GetSource(extrasByIndex[i], body);
                if (stencil == null ||
                    !string.Equals(stencil.Op, VrmcMaterialsMtoonxtStencil.OpWrite, StringComparison.Ordinal))
                {
                    continue;
                }

                if (writerToKeys.ContainsKey(i))
                {
                    continue;
                }

                var singleton = new[] { i };
                var key = MakeKey(singleton);
                sets[key] = singleton;
                RegisterWriters(writerToKeys, singleton, key);
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
                var source = GetSource(extrasByIndex[i], body);
                var compiled = CompileOne(
                    i,
                    source,
                    body,
                    extrasByIndex,
                    count,
                    writerToKeys,
                    invalidKeys,
                    refByKey,
                    bodyOut);
                if (body)
                {
                    bodyOut[i] = compiled;
                }
                else
                {
                    outlineOut[i] = compiled;
                }
            }
        }

        private static VrmcMaterialsMtoonxtStencil CompileOne(
            int index,
            VrmcMaterialsMtoonxtStencil source,
            bool body,
            IReadOnlyList<VrmcMaterialsMtoonxtExtension> extrasByIndex,
            int count,
            Dictionary<int, HashSet<string>> writerToKeys,
            HashSet<string> invalidKeys,
            Dictionary<string, int> refByKey,
            VrmcMaterialsMtoonxtStencil[] bodyOut)
        {
            if (source == null || !source.HasOp)
            {
                return null;
            }

            if (string.Equals(source.Op, VrmcMaterialsMtoonxtStencil.OpSame, StringComparison.Ordinal))
            {
                if (body)
                {
                    return null;
                }

                return bodyOut[index];
            }

            if (string.Equals(source.Op, VrmcMaterialsMtoonxtStencil.OpWrite, StringComparison.Ordinal))
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

                if (onlyKey == null || invalidKeys.Contains(onlyKey) || !refByKey.TryGetValue(onlyKey, out var writeRef))
                {
                    return null;
                }

                return VrmcMaterialsMtoonxtStencil.Compiled(writeRef, "always", "replace");
            }

            if (!TryNormalizeReaderSet(
                    source,
                    index,
                    count,
                    extrasByIndex,
                    body,
                    out _,
                    out var readerKey) ||
                invalidKeys.Contains(readerKey) ||
                !refByKey.TryGetValue(readerKey, out var clipRef))
            {
                return null;
            }

            if (string.Equals(source.Op, VrmcMaterialsMtoonxtStencil.OpInside, StringComparison.Ordinal))
            {
                return VrmcMaterialsMtoonxtStencil.Compiled(clipRef, "equal", "keep");
            }

            if (string.Equals(source.Op, VrmcMaterialsMtoonxtStencil.OpOutside, StringComparison.Ordinal))
            {
                return VrmcMaterialsMtoonxtStencil.Compiled(clipRef, "notEqual", "keep");
            }

            return null;
        }

        private static bool TryNormalizeReaderSet(
            VrmcMaterialsMtoonxtStencil stencil,
            int readerIndex,
            int materialCount,
            IReadOnlyList<VrmcMaterialsMtoonxtExtension> extrasByIndex,
            bool body,
            out int[] sorted,
            out string key)
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

                var writer = GetSource(extrasByIndex[writerIndex], body);
                if (writer == null ||
                    !string.Equals(writer.Op, VrmcMaterialsMtoonxtStencil.OpWrite, StringComparison.Ordinal))
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

        private static void RegisterWriters(
            Dictionary<int, HashSet<string>> writerToKeys,
            int[] sorted,
            string key)
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

        private static VrmcMaterialsMtoonxtStencil GetSource(
            VrmcMaterialsMtoonxtExtension extra,
            bool body)
        {
            if (extra == null)
            {
                return null;
            }

            return body ? extra.Stencil : extra.OutlineStencil;
        }
    }
}
