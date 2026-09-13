using System.Collections.Generic;

namespace UniVRMXT.Mtoonxt
{
    /// <summary>Reader-centric union, without adding transitive or cross-group edges.</summary>
    public static class VrmxtStencilGraph
    {
        // Cyclic connected writer components cannot be topologically layered.
        // Treat their writers as one depth-tested peer stage, including upstream
        // writers in the same component. Reader-only materials remain mask subjects.
        public static HashSet<int> DepthPeers(IReadOnlyList<VrmxtMtoonxtRelationshipPlan> plans)
        {
            var edges = new Dictionary<int, HashSet<int>>();
            var connected = new Dictionary<int, HashSet<int>>();
            foreach (var plan in plans)
            foreach (var w in plan.Source.Writers)
            {
                if (!edges.ContainsKey(w)) edges[w] = new HashSet<int>();
                foreach (var r in plan.Source.Readers)
                {
                    edges[w].Add(r);
                    if (!connected.ContainsKey(w)) connected[w] = new HashSet<int>();
                    if (!connected.ContainsKey(r)) connected[r] = new HashSet<int>();
                    connected[w].Add(r); connected[r].Add(w);
                }
            }
            var result = new HashSet<int>();
            var visited = new HashSet<int>();
            foreach (var seed in connected.Keys)
            {
                if (visited.Contains(seed)) continue;
                var pending = new Stack<int>(); pending.Push(seed);
                var component = new HashSet<int>();
                while (pending.Count > 0)
                {
                    var value = pending.Pop();
                    if (!component.Add(value)) continue;
                    visited.Add(value);
                    foreach (var next in connected[value]) pending.Push(next);
                }
                var remaining = new HashSet<int>();
                foreach (var value in component) if (edges.ContainsKey(value)) remaining.Add(value);
                var writers = new HashSet<int>(remaining);
                while (remaining.Count > 0)
                {
                    var ready = new List<int>();
                    foreach (var value in remaining)
                        if (!edges[value].Overlaps(remaining)) ready.Add(value);
                    if (ready.Count == 0) { result.UnionWith(writers); break; }
                    remaining.ExceptWith(ready);
                }
            }
            return result;
        }

        public static bool NeedsCoverage(IReadOnlyList<VrmxtMtoonxtRelationshipPlan> plans)
        {
            var owners = new HashSet<int>();
            var shared = false;
            foreach (var plan in plans)
            {
                var r = plan.Source;
                // Keep the established compound/transparent/depth matrix paths intact.
                if (r.ShowWritersThroughOccluders || r.WritersOnlyInsideReaders
                    || r.WritersOnlyOutsideReaders || !r.WritersSelfOcclude
                    || !r.IgnoreOccludedReaderAreas || !r.WritersWriteDepth
                    || !r.ReadersWriteDepth || !r.WritersWriteColor
                    || r.Comparison != "outside" || r.WriterDepthTest != "lessEqual"
                    || r.ReaderDepthTest != "lessEqual") return false;
                foreach (var id in r.Writers) if (!owners.Add(id)) shared = true;
                foreach (var id in r.Readers) if (!owners.Add(id)) shared = true;
            }
            return shared;
        }

        public static SortedDictionary<int, HashSet<int>> Readers(
            IReadOnlyList<VrmxtMtoonxtRelationshipPlan> plans)
        {
            var result = new SortedDictionary<int, HashSet<int>>();
            foreach (var plan in plans)
            foreach (var reader in plan.Source.Readers)
            {
                if (!result.TryGetValue(reader, out var writers))
                    result.Add(reader, writers = new HashSet<int>());
                foreach (var writer in plan.Source.Writers)
                    if (writer != reader) writers.Add(writer);
            }
            return result;
        }
    }
}
