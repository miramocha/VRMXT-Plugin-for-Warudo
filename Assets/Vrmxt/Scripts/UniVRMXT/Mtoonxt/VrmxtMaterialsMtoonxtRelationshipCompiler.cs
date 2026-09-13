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

    public sealed class VrmxtMtoonxtRelationshipPass
    {
        public VrmxtMtoonxtRelationshipPass(
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

    public sealed class VrmxtMtoonxtRelationshipPlan
    {
        public VrmxtMtoonxtRelationshipPlan(
            VrmxtMaterialsMtoonxtRelationship source,
            int localRef,
            VrmxtMtoonxtRelationshipPass writerPrimary,
            VrmxtMtoonxtRelationshipPass writerSecondary,
            VrmxtMtoonxtRelationshipPass reader,
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

        public VrmxtMaterialsMtoonxtRelationship Source { get; }
        public int LocalRef { get; }
        public VrmxtMtoonxtRelationshipPass WriterPrimary { get; }
        public VrmxtMtoonxtRelationshipPass WriterSecondary { get; }
        public VrmxtMtoonxtRelationshipPass Reader { get; }
        public bool WritersStampMask { get; }
        public bool ReadersStampMask { get; }
        public VrmxtMtoonxtCoverageMode CoverageMode { get; }
    }

    /// <summary>
    /// Maps the portable relationship graph to the Unity pass choreography confirmed by
    /// the Blender/Unity M01-M10 and A01-A03 parity matrix.
    /// </summary>
    public static class VrmxtMaterialsMtoonxtRelationshipCompiler
    {
        public static List<VrmxtMtoonxtRelationshipPlan> Compile(
            IReadOnlyList<VrmxtMaterialsMtoonxtRelationship> relationships,
            int firstLocalRef
        )
        {
            var result = new List<VrmxtMtoonxtRelationshipPlan>();
            if (relationships == null)
            {
                return result;
            }

            var compiledRelationships = CoalesceCompatibleRelationships(relationships);
            var nextRef = Math.Max(1, firstLocalRef);
            for (var i = 0; i < compiledRelationships.Count && nextRef <= 255; i++)
            {
                var relationship = compiledRelationships[i];
                if (relationship == null)
                {
                    continue;
                }

                result.Add(CompileOne(relationship, nextRef));
                nextRef++;
            }

            return result;
        }

        private static List<VrmxtMaterialsMtoonxtRelationship> CoalesceCompatibleRelationships(
            IReadOnlyList<VrmxtMaterialsMtoonxtRelationship> relationships
        )
        {
            var result = new List<VrmxtMaterialsMtoonxtRelationship>();
            for (var i = 0; i < relationships.Count; i++)
            {
                var relationship = relationships[i];
                if (relationship == null)
                {
                    continue;
                }

                var match = -1;
                var mergeWriters = false;
                for (var j = 0; j < result.Count; j++)
                {
                    if (SameWriterPresentation(result[j], relationship))
                    {
                        match = j;
                        break;
                    }

                    if (SameReaderPresentation(result[j], relationship))
                    {
                        match = j;
                        mergeWriters = true;
                        break;
                    }
                }

                if (match < 0)
                {
                    result.Add(relationship);
                    continue;
                }

                if (mergeWriters)
                {
                    var writers = new List<int>(result[match].Writers);
                    for (var j = 0; j < relationship.Writers.Count; j++)
                    {
                        var writer = relationship.Writers[j];
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
                for (var j = 0; j < relationship.Readers.Count; j++)
                {
                    var reader = relationship.Readers[j];
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
            VrmxtMaterialsMtoonxtRelationship left,
            VrmxtMaterialsMtoonxtRelationship right
        )
        {
            return SameSet(left.Writers, right.Writers)
                && SamePresentation(left, right);
        }

        private static bool SameReaderPresentation(
            VrmxtMaterialsMtoonxtRelationship left,
            VrmxtMaterialsMtoonxtRelationship right
        )
        {
            return SameSet(left.Readers, right.Readers)
                && SamePresentation(left, right);
        }

        private static bool SamePresentation(
            VrmxtMaterialsMtoonxtRelationship left,
            VrmxtMaterialsMtoonxtRelationship right
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

        private static VrmxtMtoonxtRelationshipPlan CompileOne(
            VrmxtMaterialsMtoonxtRelationship relationship,
            int localRef
        )
        {
            var writerDepth = relationship.WriterDepthTest;
            var readerDepth = relationship.ReaderDepthTest;
            var cullBack = relationship.WritersSelfOcclude;

            if (relationship.WritersOnlyInsideReaders)
            {
                return new VrmxtMtoonxtRelationshipPlan(
                    relationship,
                    localRef,
                    Subject(
                        "equal",
                        relationship.ShowWritersThroughOccluders ? "always" : writerDepth,
                        relationship.WritersWriteDepth,
                        cullBack,
                        relationship.WritersWriteColor
                    ),
                    null,
                    Mask(readerDepth, relationship.ReadersWriteDepth),
                    writersStampMask: false,
                    readersStampMask: true,
                    coverageMode: relationship.IgnoreOccludedReaderAreas
                        ? VrmxtMtoonxtCoverageMode.None
                        : VrmxtMtoonxtCoverageMode.FullReaderSilhouette
                );
            }

            if (relationship.WritersOnlyOutsideReaders)
            {
                var backgroundOnly = relationship.ShowWritersThroughOccluders;
                return new VrmxtMtoonxtRelationshipPlan(
                    relationship,
                    localRef,
                    Subject(
                        "notEqual",
                        writerDepth,
                        relationship.WritersWriteDepth,
                        cullBack,
                        relationship.WritersWriteColor
                    ),
                    null,
                    backgroundOnly
                        ? null
                        : Mask(readerDepth, relationship.ReadersWriteDepth),
                    writersStampMask: false,
                    readersStampMask: !backgroundOnly,
                    coverageMode: backgroundOnly
                        ? VrmxtMtoonxtCoverageMode.FullSceneWithoutWriters
                        : (
                            relationship.IgnoreOccludedReaderAreas
                                ? VrmxtMtoonxtCoverageMode.None
                                : VrmxtMtoonxtCoverageMode.FullReaderSilhouette
                        )
                );
            }

            if (relationship.ShowWritersThroughOccluders)
            {
                return new VrmxtMtoonxtRelationshipPlan(
                    relationship,
                    localRef,
                    Subject(
                        "notEqual",
                        writerDepth,
                        relationship.WritersWriteDepth,
                        cullBack,
                        relationship.WritersWriteColor
                    ),
                    Subject(
                        "equal",
                        "always",
                        relationship.WritersWriteDepth,
                        cullBack,
                        relationship.WritersWriteColor
                    ),
                    Mask(readerDepth, relationship.ReadersWriteDepth),
                    writersStampMask: false,
                    readersStampMask: true,
                    coverageMode: relationship.IgnoreOccludedReaderAreas
                        ? VrmxtMtoonxtCoverageMode.None
                        : VrmxtMtoonxtCoverageMode.FullReaderSilhouette
                );
            }

            var readerComp = string.Equals(
                relationship.Comparison,
                VrmxtMaterialsMtoonxtRelationships.ComparisonInside,
                StringComparison.Ordinal
            )
                ? "equal"
                : "notEqual";
            return new VrmxtMtoonxtRelationshipPlan(
                relationship,
                localRef,
                Mask(
                    writerDepth,
                    relationship.WritersWriteDepth,
                    relationship.WritersWriteColor
                ),
                null,
                Subject(
                    readerComp,
                    readerDepth,
                    relationship.ReadersWriteDepth,
                    cullBack: false
                ),
                writersStampMask: true,
                readersStampMask: false,
                coverageMode: VrmxtMtoonxtCoverageMode.None
            );
        }

        private static VrmxtMtoonxtRelationshipPass Mask(
            string zTest,
            bool zWrite,
            bool writeColor = true
        )
        {
            return new VrmxtMtoonxtRelationshipPass(
                "always",
                "replace",
                zTest,
                zWrite,
                cullBack: false,
                writeColor: writeColor
            );
        }

        private static VrmxtMtoonxtRelationshipPass Subject(
            string comp,
            string zTest,
            bool zWrite,
            bool cullBack,
            bool writeColor = true
        )
        {
            return new VrmxtMtoonxtRelationshipPass(
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
