using System.Collections.Generic;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Per-loaded-root GPU stencil Ref band. Compile stays file-local 1, 2, …;
    /// Apply adds <c>gpuRef = local + base - 1</c>. Not serialized in glTF.
    /// </summary>
    public static class VrmcMaterialsMtoonxtStencilRefs
    {
        public const int BandStart = 32;
        public const int MaxRef = 255;

        /// <summary>
        /// Shipped Unity/toon defaults: 0 clear/UI/lilToon/Poiyomi/XS idle;
        /// 1 UTS / Mochie particles / visor tutorials; 51 Poiyomi Fake Shadow;
        /// 255 UTS special-case warning.
        /// </summary>
        private static readonly int[] Skip = { 0, 1, 51, 255 };

        private static readonly Dictionary<int, Lease> Leases = new Dictionary<int, Lease>();

        public static int Acquire(int instanceId, int span)
        {
            if (span < 1)
            {
                Release(instanceId);
                return 0;
            }

            Release(instanceId);

            for (var start = BandStart; start <= MaxRef - span + 1; start++)
            {
                if (!RangeOk(start, span))
                {
                    continue;
                }

                Leases[instanceId] = new Lease(start, span);
                return start;
            }

            return 0;
        }

        public static void Release(int instanceId)
        {
            Leases.Remove(instanceId);
        }

        public static void Reset()
        {
            Leases.Clear();
        }

        public static int GpuRef(int localRef, int gpuBase)
        {
            if (gpuBase < BandStart || localRef < 1)
            {
                return localRef;
            }

            var next = localRef + gpuBase - 1;
            if (next > MaxRef)
            {
                return localRef;
            }

            return next;
        }

        private static bool RangeOk(int start, int span)
        {
            var end = start + span - 1;
            if (end > MaxRef)
            {
                return false;
            }

            for (var r = start; r <= end; r++)
            {
                for (var i = 0; i < Skip.Length; i++)
                {
                    if (r == Skip[i])
                    {
                        return false;
                    }
                }
            }

            foreach (var pair in Leases)
            {
                var lease = pair.Value;
                var leaseEnd = lease.Start + lease.Span - 1;
                if (start <= leaseEnd && lease.Start <= end)
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct Lease
        {
            public Lease(int start, int span)
            {
                Start = start;
                Span = span;
            }

            public int Start { get; }

            public int Span { get; }
        }
    }
}
