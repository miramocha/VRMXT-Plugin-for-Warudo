using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Portable root-level stencil graph for
    /// <c>VRMXT_materials_mtoonxt</c>.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtStencils
    {
        public const string PropertyName = "stencil";
        public const string ComparisonInside = "inside";
        public const string ComparisonOutside = "outside";

        public static bool TryParseRoot(
            JToken gltfRoot,
            int materialCount,
            out List<VrmxtMaterialsMtoonxtStencil> stencils
        )
        {
            stencils = new List<VrmxtMaterialsMtoonxtStencil>();
            if (!(gltfRoot is JObject root))
            {
                return false;
            }

            if (!TryGetRootExtension(root, out var extension))
            {
                return false;
            }

            if (
                !extension.TryGetValue("specVersion", StringComparison.Ordinal, out var version)
                || version.Type != JTokenType.String
                || !string.Equals(
                    version.Value<string>(),
                    VrmxtMaterialsMtoonxt.SpecVersionValue,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }

            if (
                !extension.TryGetValue(PropertyName, StringComparison.Ordinal, out var token)
                || !(token is JArray array)
            )
            {
                return true;
            }

            for (var i = 0; i < array.Count; i++)
            {
                if (TryParse(array[i], materialCount, out var stencil))
                {
                    stencils.Add(stencil);
                }
            }

            return true;
        }

        public static bool TryParse(
            JToken token,
            int materialCount,
            out VrmxtMaterialsMtoonxtStencil stencil
        )
        {
            stencil = null;
            if (!(token is JObject obj))
            {
                return false;
            }

            if (
                !TryReadMaterialIndices(obj, "writers", materialCount, out var writers)
                || !TryReadMaterialIndices(obj, "readers", materialCount, out var readers)
                || Intersects(writers, readers)
            )
            {
                return false;
            }

            var comparison = ReadString(obj, "comparison", ComparisonOutside);
            var writerDepthTest = ReadString(
                obj,
                "writerDepthTest",
                VrmxtMaterialsMtoonxt.ZTestDefault
            );
            var readerDepthTest = ReadString(
                obj,
                "readerDepthTest",
                VrmxtMaterialsMtoonxt.ZTestDefault
            );
            if (
                !IsComparison(comparison)
                || !VrmxtMaterialsMtoonxt.TryMapCompareFunction(writerDepthTest, out _)
                || !VrmxtMaterialsMtoonxt.TryMapCompareFunction(readerDepthTest, out _)
            )
            {
                return false;
            }

            if (
                !TryReadBool(obj, "showWritersThroughOccluders", false, out var showThrough)
                || !TryReadBool(obj, "writersOnlyInsideReaders", false, out var insideOnly)
                || !TryReadBool(obj, "writersOnlyOutsideReaders", false, out var outsideOnly)
                || !TryReadBool(obj, "writersSelfOcclude", true, out var selfOcclude)
                || !TryReadBool(obj, "ignoreOccludedReaderAreas", true, out var ignoreOccluded)
                || !TryReadBool(obj, "writersWriteColor", true, out var writersWriteColor)
                || !TryReadBool(obj, "writersWriteDepth", true, out var writersWriteDepth)
                || !TryReadBool(obj, "readersWriteDepth", true, out var readersWriteDepth)
                || (insideOnly && outsideOnly)
            )
            {
                return false;
            }

            stencil = new VrmxtMaterialsMtoonxtStencil(
                writers,
                readers,
                comparison,
                showThrough,
                insideOnly,
                outsideOnly,
                selfOcclude,
                ignoreOccluded,
                writersWriteDepth,
                readersWriteDepth,
                writerDepthTest,
                readerDepthTest,
                writersWriteColor: writersWriteColor
            );
            return true;
        }

        public static JObject BuildRootExtension(
            IReadOnlyList<VrmxtMaterialsMtoonxtStencil> stencils
        )
        {
            var extension = new JObject
            {
                ["specVersion"] = VrmxtMaterialsMtoonxt.SpecVersionValue,
            };
            var array = new JArray();
            if (stencils != null)
            {
                for (var i = 0; i < stencils.Count; i++)
                {
                    var stencil = stencils[i];
                    if (stencil != null)
                    {
                        array.Add(Build(stencil));
                    }
                }
            }

            extension[PropertyName] = array;
            return extension;
        }

        public static string ToJson(
            IReadOnlyList<VrmxtMaterialsMtoonxtStencil> stencils
        )
        {
            return BuildRootExtension(stencils).ToString(Formatting.None);
        }

        public static bool TryRemap(
            VrmxtMaterialsMtoonxtStencil stencil,
            Func<int, int?> resolve,
            out VrmxtMaterialsMtoonxtStencil remapped
        )
        {
            remapped = null;
            if (
                stencil == null
                || resolve == null
                || !TryRemapList(stencil.Writers, resolve, out var writers)
                || !TryRemapList(stencil.Readers, resolve, out var readers)
                || Intersects(writers, readers)
            )
            {
                return false;
            }

            remapped = stencil.WithMaterialIndices(writers, readers);
            return true;
        }

        private static JObject Build(VrmxtMaterialsMtoonxtStencil stencil)
        {
            var obj = new JObject
            {
                ["writers"] = new JArray(stencil.Writers),
                ["readers"] = new JArray(stencil.Readers),
            };
            AddIfDifferent(obj, "comparison", stencil.Comparison, ComparisonOutside);
            AddIfDifferent(
                obj,
                "showWritersThroughOccluders",
                stencil.ShowWritersThroughOccluders,
                false
            );
            AddIfDifferent(
                obj,
                "writersOnlyInsideReaders",
                stencil.WritersOnlyInsideReaders,
                false
            );
            AddIfDifferent(
                obj,
                "writersOnlyOutsideReaders",
                stencil.WritersOnlyOutsideReaders,
                false
            );
            AddIfDifferent(obj, "writersSelfOcclude", stencil.WritersSelfOcclude, true);
            AddIfDifferent(
                obj,
                "ignoreOccludedReaderAreas",
                stencil.IgnoreOccludedReaderAreas,
                true
            );
            AddIfDifferent(obj, "writersWriteColor", stencil.WritersWriteColor, true);
            AddIfDifferent(obj, "writersWriteDepth", stencil.WritersWriteDepth, true);
            AddIfDifferent(obj, "readersWriteDepth", stencil.ReadersWriteDepth, true);
            AddIfDifferent(
                obj,
                "writerDepthTest",
                stencil.WriterDepthTest,
                VrmxtMaterialsMtoonxt.ZTestDefault
            );
            AddIfDifferent(
                obj,
                "readerDepthTest",
                stencil.ReaderDepthTest,
                VrmxtMaterialsMtoonxt.ZTestDefault
            );
            return obj;
        }

        private static bool TryGetRootExtension(JObject root, out JObject extension)
        {
            extension = null;
            if (
                !root.TryGetValue("extensions", StringComparison.Ordinal, out var extensionsToken)
                || !(extensionsToken is JObject extensions)
                || !extensions.TryGetValue(
                    VrmxtMaterialsMtoonxt.ExtensionName,
                    StringComparison.Ordinal,
                    out var extensionToken
                )
            )
            {
                return false;
            }

            extension = extensionToken as JObject;
            return extension != null;
        }

        private static bool TryReadMaterialIndices(
            JObject obj,
            string name,
            int materialCount,
            out int[] result
        )
        {
            result = null;
            if (
                !obj.TryGetValue(name, StringComparison.Ordinal, out var token)
                || !(token is JArray array)
                || array.Count == 0
            )
            {
                return false;
            }

            var values = new List<int>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.Integer)
                {
                    return false;
                }

                var value = array[i].Value<int>();
                if (value < 0 || (materialCount >= 0 && value >= materialCount))
                {
                    return false;
                }

                if (!values.Contains(value))
                {
                    values.Add(value);
                }
            }

            result = values.ToArray();
            return result.Length > 0;
        }

        private static bool TryReadBool(
            JObject obj,
            string name,
            bool defaultValue,
            out bool value
        )
        {
            value = defaultValue;
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out var token))
            {
                return true;
            }

            if (token.Type != JTokenType.Boolean)
            {
                return false;
            }

            value = token.Value<bool>();
            return true;
        }

        private static string ReadString(JObject obj, string name, string defaultValue)
        {
            if (
                obj.TryGetValue(name, StringComparison.Ordinal, out var token)
                && token.Type == JTokenType.String
            )
            {
                return token.Value<string>();
            }

            return defaultValue;
        }

        private static bool IsComparison(string value)
        {
            return string.Equals(value, ComparisonInside, StringComparison.Ordinal)
                || string.Equals(value, ComparisonOutside, StringComparison.Ordinal);
        }

        private static bool Intersects(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            for (var i = 0; i < left.Count; i++)
            {
                for (var j = 0; j < right.Count; j++)
                {
                    if (left[i] == right[j])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryRemapList(
            IReadOnlyList<int> source,
            Func<int, int?> resolve,
            out int[] result
        )
        {
            result = null;
            if (source == null || source.Count == 0)
            {
                return false;
            }

            var values = new List<int>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                var mapped = resolve(source[i]);
                if (!mapped.HasValue || mapped.Value < 0)
                {
                    return false;
                }

                if (!values.Contains(mapped.Value))
                {
                    values.Add(mapped.Value);
                }
            }

            result = values.ToArray();
            return result.Length > 0;
        }

        private static void AddIfDifferent(JObject obj, string name, string value, string defaultValue)
        {
            if (!string.Equals(value, defaultValue, StringComparison.Ordinal))
            {
                obj[name] = value;
            }
        }

        private static void AddIfDifferent(JObject obj, string name, bool value, bool defaultValue)
        {
            if (value != defaultValue)
            {
                obj[name] = value;
            }
        }
    }

    public sealed class VrmxtMaterialsMtoonxtStencil
    {
        public VrmxtMaterialsMtoonxtStencil(
            IReadOnlyList<int> writers,
            IReadOnlyList<int> readers,
            string comparison = VrmxtMaterialsMtoonxtStencils.ComparisonOutside,
            bool showWritersThroughOccluders = false,
            bool writersOnlyInsideReaders = false,
            bool writersOnlyOutsideReaders = false,
            bool writersSelfOcclude = true,
            bool ignoreOccludedReaderAreas = true,
            bool writersWriteDepth = true,
            bool readersWriteDepth = true,
            string writerDepthTest = VrmxtMaterialsMtoonxt.ZTestDefault,
            string readerDepthTest = VrmxtMaterialsMtoonxt.ZTestDefault,
            bool writersWriteColor = true
        )
        {
            Writers = writers;
            Readers = readers;
            Comparison = comparison;
            ShowWritersThroughOccluders = showWritersThroughOccluders;
            WritersOnlyInsideReaders = writersOnlyInsideReaders;
            WritersOnlyOutsideReaders = writersOnlyOutsideReaders;
            WritersSelfOcclude = writersSelfOcclude;
            IgnoreOccludedReaderAreas = ignoreOccludedReaderAreas;
            WritersWriteColor = writersWriteColor;
            WritersWriteDepth = writersWriteDepth;
            ReadersWriteDepth = readersWriteDepth;
            WriterDepthTest = writerDepthTest;
            ReaderDepthTest = readerDepthTest;
        }

        public IReadOnlyList<int> Writers { get; }
        public IReadOnlyList<int> Readers { get; }
        public string Comparison { get; }
        public bool ShowWritersThroughOccluders { get; }
        public bool WritersOnlyInsideReaders { get; }
        public bool WritersOnlyOutsideReaders { get; }
        public bool WritersSelfOcclude { get; }
        public bool IgnoreOccludedReaderAreas { get; }
        public bool WritersWriteColor { get; }
        public bool WritersWriteDepth { get; }
        public bool ReadersWriteDepth { get; }
        public string WriterDepthTest { get; }
        public string ReaderDepthTest { get; }

        public VrmxtMaterialsMtoonxtStencil WithMaterialIndices(
            IReadOnlyList<int> writers,
            IReadOnlyList<int> readers
        )
        {
            return new VrmxtMaterialsMtoonxtStencil(
                writers,
                readers,
                Comparison,
                ShowWritersThroughOccluders,
                WritersOnlyInsideReaders,
                WritersOnlyOutsideReaders,
                WritersSelfOcclude,
                IgnoreOccludedReaderAreas,
                WritersWriteDepth,
                ReadersWriteDepth,
                WriterDepthTest,
                ReaderDepthTest,
                writersWriteColor: WritersWriteColor
            );
        }
    }
}
