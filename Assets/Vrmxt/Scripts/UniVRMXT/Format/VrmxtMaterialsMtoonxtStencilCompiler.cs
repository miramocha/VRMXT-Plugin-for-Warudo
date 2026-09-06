using System;
using System.Collections.Generic;
using UniVRMXT.Format;

namespace UniVRMXT.Mtoonxt
{
    public enum VrmxtMtoonxtCoverageMode
    {
        None = 0,
        FullSceneWithoutWriters = 1,
        FullReaderSilhouette = 2,
    }

    public sealed class VrmxtMtoonxtStencilPass
    {
        public VrmxtMtoonxtStencilPass(
            string comp,
            string pass,
            string zTest,
            bool zWrite,
            bool cullBack,
            bool writeColor
        )
        {
            Comp = comp;
            Pass = pass;
            ZTest = zTest;
            ZWrite = zWrite;
            CullBack = cullBack;
            WriteColor = writeColor;
        }

        public string Comp { get; }
        public string Pass { get; }
        public string ZTest { get; }
        public bool ZWrite { get; }
        public bool CullBack { get; }
        public bool WriteColor { get; }
    }

    public sealed class VrmxtMtoonxtStencilPlan
    {
        public VrmxtMtoonxtStencilPlan(
            VrmxtMaterialsMtoonxtStencil source,
            int localRef,
            VrmxtMtoonxtStencilPass writerPrimary,
            VrmxtMtoonxtStencilPass writerSecondary,
            VrmxtMtoonxtStencilPass reader,
            bool writersStampMask,
            bool readersStampMask,
            VrmxtMtoonxtCoverageMode coverageMode
        )
        {
            Source = source;
            LocalRef = localRef;
            WriterPrimary = writerPrimary;
            WriterSecondary = writerSecondary;
            Reader = reader;
            WritersStampMask = writersStampMask;
            ReadersStampMask = readersStampMask;
            CoverageMode = coverageMode;
        }

        public VrmxtMaterialsMtoonxtStencil Source { get; }
        public int LocalRef { get; }
        public VrmxtMtoonxtStencilPass WriterPrimary { get; }
        public VrmxtMtoonxtStencilPass WriterSecondary { get; }
        public VrmxtMtoonxtStencilPass Reader { get; }
        public bool WritersStampMask { get; }
        public bool ReadersStampMask { get; }
        public VrmxtMtoonxtCoverageMode CoverageMode { get; }
    }

    /// <summary>
    /// Maps the portable stencil graph to the Unity pass choreography confirmed by
    /// the Blender/Unity M01-M10 and A01-A03 parity matrix.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtStencilCompiler
    {
        public static List<VrmxtMtoonxtStencilPlan> Compile(
            IReadOnlyList<VrmxtMaterialsMtoonxtStencil> stencils,
            int firstLocalRef
        )
        {
            var result = new List<VrmxtMtoonxtStencilPlan>();
            if (stencils == null)
            {
                return result;
            }

            var compiledStencils = CoalesceCompatibleStencils(stencils);
            var nextRef = Math.Max(1, firstLocalRef);
            for (var i = 0; i < compiledStencils.Count && nextRef <= 255; i++)
            {
                var stencil = compiledStencils[i];
                if (stencil == null)
                {
                    continue;
                }

                result.Add(CompileOne(stencil, nextRef));
                nextRef++;
            }

            return result;
        }

        private static List<VrmxtMaterialsMtoonxtStencil> CoalesceCompatibleStencils(
            IReadOnlyList<VrmxtMaterialsMtoonxtStencil> stencils
        )
        {
            var result = new List<VrmxtMaterialsMtoonxtStencil>();
            for (var i = 0; i < stencils.Count; i++)
            {
                var stencil = stencils[i];
                if (stencil == null)
                {
                    continue;
                }

                var match = -1;
                var mergeWriters = false;
                for (var j = 0; j < result.Count; j++)
                {
                    if (SameWriterPresentation(result[j], stencil))
                    {
                        match = j;
                        break;
                    }

                    if (SameReaderPresentation(result[j], stencil))
                    {
                        match = j;
                        mergeWriters = true;
                        break;
                    }
                }

                if (match < 0)
                {
                    result.Add(stencil);
                    continue;
                }

                if (mergeWriters)
                {
                    var writers = new List<int>(result[match].Writers);
                    for (var j = 0; j < stencil.Writers.Count; j++)
                    {
                        var writer = stencil.Writers[j];
                        if (!writers.Contains(writer) && !Contains(result[match].Readers, writer))
                        {
                            writers.Add(writer);
                        }
                    }

                    result[match] = result[match].WithMaterialIndices(
                        writers,
                        result[match].Readers
                    );
                    continue;
                }

                var readers = new List<int>(result[match].Readers);
                for (var j = 0; j < stencil.Readers.Count; j++)
                {
                    var reader = stencil.Readers[j];
                    if (!readers.Contains(reader) && !Contains(result[match].Writers, reader))
                    {
                        readers.Add(reader);
                    }
                }

                result[match] = result[match].WithMaterialIndices(
                    result[match].Writers,
                    readers
                );
            }

            return result;
        }

        private static bool SameWriterPresentation(
            VrmxtMaterialsMtoonxtStencil left,
            VrmxtMaterialsMtoonxtStencil right
        )
        {
            return SameSet(left.Writers, right.Writers)
                && SamePresentation(left, right);
        }

        private static bool SameReaderPresentation(
            VrmxtMaterialsMtoonxtStencil left,
            VrmxtMaterialsMtoonxtStencil right
        )
        {
            return SameSet(left.Readers, right.Readers)
                && SamePresentation(left, right);
        }

        private static bool SamePresentation(
            VrmxtMaterialsMtoonxtStencil left,
            VrmxtMaterialsMtoonxtStencil right
        )
        {
            return string.Equals(left.Comparison, right.Comparison, StringComparison.Ordinal)
                && left.ShowWritersThroughOccluders == right.ShowWritersThroughOccluders
                && left.WritersOnlyInsideReaders == right.WritersOnlyInsideReaders
                && left.WritersOnlyOutsideReaders == right.WritersOnlyOutsideReaders
                && left.WritersSelfOcclude == right.WritersSelfOcclude
                && left.IgnoreOccludedReaderAreas == right.IgnoreOccludedReaderAreas
                && left.WritersWriteColor == right.WritersWriteColor
                && left.WritersWriteDepth == right.WritersWriteDepth
                && left.ReadersWriteDepth == right.ReadersWriteDepth
                && string.Equals(
                    left.WriterDepthTest,
                    right.WriterDepthTest,
                    StringComparison.Ordinal
                )
                && string.Equals(
                    left.ReaderDepthTest,
                    right.ReaderDepthTest,
                    StringComparison.Ordinal
                );
        }

        private static bool SameSet(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!Contains(right, left[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(IReadOnlyList<int> values, int value)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static VrmxtMtoonxtStencilPlan CompileOne(
            VrmxtMaterialsMtoonxtStencil stencil,
            int localRef
        )
        {
            var writerDepth = stencil.WriterDepthTest;
            var readerDepth = stencil.ReaderDepthTest;
            var cullBack = stencil.WritersSelfOcclude;

            if (stencil.WritersOnlyInsideReaders)
            {
                return new VrmxtMtoonxtStencilPlan(
                    stencil,
                    localRef,
                    Subject(
                        "equal",
                        stencil.ShowWritersThroughOccluders ? "always" : writerDepth,
                        stencil.WritersWriteDepth,
                        cullBack,
                        stencil.WritersWriteColor
                    ),
                    null,
                    Mask(readerDepth, stencil.ReadersWriteDepth),
                    writersStampMask: false,
                    readersStampMask: true,
                    coverageMode: stencil.IgnoreOccludedReaderAreas
                        ? VrmxtMtoonxtCoverageMode.None
                        : VrmxtMtoonxtCoverageMode.FullReaderSilhouette
                );
            }

            if (stencil.WritersOnlyOutsideReaders)
            {
                var backgroundOnly = stencil.ShowWritersThroughOccluders;
                return new VrmxtMtoonxtStencilPlan(
                    stencil,
                    localRef,
                    Subject(
                        "notEqual",
                        writerDepth,
                        stencil.WritersWriteDepth,
                        cullBack,
                        stencil.WritersWriteColor
                    ),
                    null,
                    backgroundOnly
                        ? null
                        : Mask(readerDepth, stencil.ReadersWriteDepth),
                    writersStampMask: false,
                    readersStampMask: !backgroundOnly,
                    coverageMode: backgroundOnly
                        ? VrmxtMtoonxtCoverageMode.FullSceneWithoutWriters
                        : (
                            stencil.IgnoreOccludedReaderAreas
                                ? VrmxtMtoonxtCoverageMode.None
                                : VrmxtMtoonxtCoverageMode.FullReaderSilhouette
                        )
                );
            }

            if (stencil.ShowWritersThroughOccluders)
            {
                return new VrmxtMtoonxtStencilPlan(
                    stencil,
                    localRef,
                    Subject(
                        "notEqual",
                        writerDepth,
                        stencil.WritersWriteDepth,
                        cullBack,
                        stencil.WritersWriteColor
                    ),
                    Subject(
                        "equal",
                        "always",
                        stencil.WritersWriteDepth,
                        cullBack,
                        stencil.WritersWriteColor
                    ),
                    Mask(readerDepth, stencil.ReadersWriteDepth),
                    writersStampMask: false,
                    readersStampMask: true,
                    coverageMode: stencil.IgnoreOccludedReaderAreas
                        ? VrmxtMtoonxtCoverageMode.None
                        : VrmxtMtoonxtCoverageMode.FullReaderSilhouette
                );
            }

            var readerComp = string.Equals(
                stencil.Comparison,
                VrmxtMaterialsMtoonxtStencils.ComparisonInside,
                StringComparison.Ordinal
            )
                ? "equal"
                : "notEqual";
            return new VrmxtMtoonxtStencilPlan(
                stencil,
                localRef,
                Mask(
                    writerDepth,
                    stencil.WritersWriteDepth,
                    stencil.WritersWriteColor
                ),
                null,
                Subject(
                    readerComp,
                    readerDepth,
                    stencil.ReadersWriteDepth,
                    cullBack: false
                ),
                writersStampMask: true,
                readersStampMask: false,
                coverageMode: VrmxtMtoonxtCoverageMode.None
            );
        }

        private static VrmxtMtoonxtStencilPass Mask(
            string zTest,
            bool zWrite,
            bool writeColor = true
        )
        {
            return new VrmxtMtoonxtStencilPass(
                "always",
                "replace",
                zTest,
                zWrite,
                cullBack: false,
                writeColor: writeColor
            );
        }

        private static VrmxtMtoonxtStencilPass Subject(
            string comp,
            string zTest,
            bool zWrite,
            bool cullBack,
            bool writeColor = true
        )
        {
            return new VrmxtMtoonxtStencilPass(
                comp,
                "keep",
                zTest,
                zWrite,
                cullBack,
                writeColor
            );
        }
    }
}
