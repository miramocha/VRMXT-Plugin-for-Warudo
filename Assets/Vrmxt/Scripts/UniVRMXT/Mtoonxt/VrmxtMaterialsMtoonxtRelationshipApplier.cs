using System.Collections.Generic;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    public static class VrmxtMaterialsMtoonxtRelationshipApplier
    {
        private const string CoverageShaderName = "Hidden/UniVRMXT/StencilCoverageMask";
        private const int RelationshipMaskQueue = 2451;
        private const int RelationshipSubjectQueue = 2452;
        private const int RelationshipOverlayQueue = 2453;

        public static int Apply(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store,
            IReadOnlyList<VrmxtMtoonxtRelationshipPlan> plans,
            int gpuBase
        )
        {
            if (root == null || store == null || plans == null || plans.Count == 0)
            {
                ClearAuxiliary(root);
                return 0;
            }

            root.GetComponent<VrmxtMaterialsMtoonxtAuxiliaryRenderer>()?.RestoreNativeMaterials();
            root.GetComponent<VrmxtStencilGraphRenderer>()?.Configure(null);
            var slots = BuildSlots(root, store);
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null
                && VrmxtStencilGraph.NeedsCoverage(plans))
            {
                ClearAuxiliary(root);
                var rules = new List<VrmxtStencilReaderCoverage>();
                var depthPeers = VrmxtStencilGraph.DepthPeers(plans);
                var neutral = new VrmxtMtoonxtRelationshipPass("always", "keep", "lessEqual", true, false, true);
                var configured = new HashSet<Material>();
                foreach (var plan in plans)
                foreach (var id in new List<int>(plan.Source.Writers))
                foreach (var slot in FindSlots(slots, new[] { id }))
                    if (configured.Add(slot.Material))
                    {
                        VrmxtMaterialsMtoonxtApplier.ApplyRelationshipPass(slot.Material, neutral, 1, gpuBase);
                        slot.Material.renderQueue = RelationshipSubjectQueue;
                    }
                foreach (var entry in VrmxtStencilGraph.Readers(plans))
                foreach (var reader in FindSlots(slots, new[] { entry.Key }))
                {
                    if (rules.Exists(rule => rule.Reader == reader.Material)) continue;
                    var rule = new VrmxtStencilReaderCoverage
                    { Reader = reader.Material, RespectReaderDepth = depthPeers.Contains(entry.Key) };
                    foreach (var writer in FindSlots(slots, new List<int>(entry.Value)))
                        if (!rule.Writers.Contains(writer.Material)) rule.Writers.Add(writer.Material);
                    rules.Add(rule);
                    VrmxtMaterialsMtoonxtApplier.ApplyRelationshipPass(reader.Material, neutral, 1, gpuBase);
                    reader.Material.renderQueue = RelationshipSubjectQueue;
                }
                var graph = root.GetComponent<VrmxtStencilGraphRenderer>() ?? root.AddComponent<VrmxtStencilGraphRenderer>();
                graph.Configure(rules);
                return plans.Count;
            }
            var draws = new List<VrmxtMtoonxtAuxiliaryDraw>();
            var owned = new List<Material>();
            var applied = 0;
            for (var i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                if (plan == null)
                {
                    continue;
                }

                var writerSlots = FindSlots(slots, plan.Source.Writers);
                var readerSlots = FindSlots(slots, plan.Source.Readers);
                if (writerSlots.Count == 0 || readerSlots.Count == 0)
                {
                    continue;
                }

                var writerQueue = plan.WritersStampMask
                    ? RelationshipMaskQueue
                    : RelationshipSubjectQueue;
                var readerQueue = plan.ReadersStampMask
                    ? RelationshipMaskQueue
                    : RelationshipSubjectQueue;
                ApplyPass(writerSlots, plan.WriterPrimary, plan.LocalRef, gpuBase, writerQueue);
                if (plan.Reader != null)
                {
                    ApplyPass(readerSlots, plan.Reader, plan.LocalRef, gpuBase, readerQueue);
                }

                if (plan.WriterSecondary != null)
                {
                    AddSecondaryDraws(
                        writerSlots,
                        plan,
                        gpuBase,
                        RelationshipOverlayQueue,
                        draws,
                        owned
                    );
                }

                if (plan.CoverageMode != VrmxtMtoonxtCoverageMode.None)
                {
                    AddCoverageDraws(
                        plan,
                        gpuBase,
                        writerSlots,
                        readerSlots,
                        draws,
                        owned
                    );
                }

                applied++;
            }

            ConfigureAuxiliary(root, draws, owned);
            return applied;
        }

        private static Dictionary<int, List<MaterialSlot>> BuildSlots(
            GameObject root,
            VrmxtMaterialsMtoonxtInstance store
        )
        {
            var materialIndices = new Dictionary<Material, int>();
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null || pair.GltfMaterialIndex < 0)
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
                        materialIndices[material] = pair.GltfMaterialIndex;
                    }
                }
            }

            var result = new Dictionary<int, List<MaterialSlot>>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var materials = renderer.sharedMaterials;
                for (var j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (
                        material == null
                        || !materialIndices.TryGetValue(material, out var gltfIndex)
                    )
                    {
                        continue;
                    }

                    if (!result.TryGetValue(gltfIndex, out var list))
                    {
                        list = new List<MaterialSlot>();
                        result[gltfIndex] = list;
                    }

                    list.Add(new MaterialSlot(renderer, j, material));
                }
            }

            return result;
        }

        private static List<MaterialSlot> FindSlots(
            Dictionary<int, List<MaterialSlot>> slots,
            IReadOnlyList<int> indices
        )
        {
            var result = new List<MaterialSlot>();
            if (indices == null)
            {
                return result;
            }

            for (var i = 0; i < indices.Count; i++)
            {
                if (!slots.TryGetValue(indices[i], out var list))
                {
                    continue;
                }

                for (var j = 0; j < list.Count; j++)
                {
                    if (!ContainsSlot(result, list[j]))
                    {
                        result.Add(list[j]);
                    }
                }
            }

            return result;
        }

        private static bool ContainsSlot(List<MaterialSlot> values, MaterialSlot value)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i].Renderer == value.Renderer && values[i].Submesh == value.Submesh)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyPass(
            IReadOnlyList<MaterialSlot> slots,
            VrmxtMtoonxtRelationshipPass pass,
            int localRef,
            int gpuBase,
            int queue
        )
        {
            if (pass == null)
            {
                return;
            }

            var seen = new HashSet<Material>();
            for (var i = 0; i < slots.Count; i++)
            {
                var material = slots[i].Material;
                if (material == null || !seen.Add(material))
                {
                    continue;
                }

                VrmxtMaterialsMtoonxtApplier.ApplyRelationshipPass(
                    material,
                    pass,
                    localRef,
                    gpuBase
                );
                material.renderQueue = queue;
            }
        }

        private static void AddSecondaryDraws(
            IReadOnlyList<MaterialSlot> writerSlots,
            VrmxtMtoonxtRelationshipPlan plan,
            int gpuBase,
            int queue,
            ICollection<VrmxtMtoonxtAuxiliaryDraw> draws,
            ICollection<Material> owned
        )
        {
            var cloneBySource = new Dictionary<Material, Material>();
            for (var i = 0; i < writerSlots.Count; i++)
            {
                var slot = writerSlots[i];
                if (!cloneBySource.TryGetValue(slot.Material, out var clone))
                {
                    clone = new Material(slot.Material)
                    {
                        name = slot.Material.name + " (VRMXT relationship overlay)",
                        hideFlags = HideFlags.HideAndDontSave,
                        renderQueue = queue,
                    };
                    VrmxtMaterialsMtoonxtApplier.ApplyRelationshipPass(
                        clone,
                        plan.WriterSecondary,
                        plan.LocalRef,
                        gpuBase
                    );
                    // Approved M02 setup: keep source double-sided rendering unchanged,
                    // but suppress rear faces in the show-through layer when requested.
                    // M07 explicitly opts out. This is not arbitrary concave self-depth.
                    clone.SetFloat("_M_CullMode", plan.Source.WritersSelfOcclude ? 2f : 0f);
                    // Native lighting includes shadow reception; the original surface
                    // remains the sole caster, not the additional color layer.
                    clone.SetShaderPassEnabled("ShadowCaster", false);
                    cloneBySource[slot.Material] = clone;
                    owned.Add(clone);
                }

                draws.Add(
                    new VrmxtMtoonxtAuxiliaryDraw
                    {
                        Renderer = slot.Renderer,
                        Material = clone,
                        Submesh = slot.Submesh,
                        AfterOpaque = true,
                        BodyPass = FindBodyPass(clone),
                        OutlinePass = FindOutlinePass(clone),
                    }
                );
            }
        }

        private static void AddCoverageDraws(
            VrmxtMtoonxtRelationshipPlan plan,
            int gpuBase,
            IReadOnlyList<MaterialSlot> writerSlots,
            IReadOnlyList<MaterialSlot> readerSlots,
            ICollection<VrmxtMtoonxtAuxiliaryDraw> draws,
            ICollection<Material> owned
        )
        {
            var shader = VrmxtMaterialsOverrideApplier.ResolveShader(CoverageShaderName);
            if (shader == null)
            {
                Debug.LogWarning("UniVRMXT: stencil relationship coverage shader is missing.");
                return;
            }

            var material = new Material(shader)
            {
                name = "UniVRMXT relationship coverage " + plan.LocalRef,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var gpuRef = VrmxtMaterialsMtoonxtStencilRefs.GpuRef(plan.LocalRef, gpuBase);
            material.SetFloat("_StencilRef", gpuRef);
            material.SetFloat("_StencilReadMask", 255f);
            material.SetFloat("_StencilWriteMask", 255f);
            owned.Add(material);

            if (plan.CoverageMode == VrmxtMtoonxtCoverageMode.FullReaderSilhouette)
            {
                AddCoverageSlots(readerSlots, material, draws);
                return;
            }

            var writerKeys = new HashSet<string>();
            for (var i = 0; i < writerSlots.Count; i++)
            {
                writerKeys.Add(SlotKey(writerSlots[i].Renderer, writerSlots[i].Submesh));
            }

            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                var count = renderer.sharedMaterials.Length;
                for (var submesh = 0; submesh < count; submesh++)
                {
                    if (writerKeys.Contains(SlotKey(renderer, submesh)))
                    {
                        continue;
                    }

                    draws.Add(
                        new VrmxtMtoonxtAuxiliaryDraw
                        {
                            Renderer = renderer,
                            Material = material,
                            Submesh = submesh,
                            AfterOpaque = false,
                            BodyPass = 0,
                        }
                    );
                }
            }
        }

        private static void AddCoverageSlots(
            IReadOnlyList<MaterialSlot> slots,
            Material material,
            ICollection<VrmxtMtoonxtAuxiliaryDraw> draws
        )
        {
            for (var i = 0; i < slots.Count; i++)
            {
                draws.Add(
                    new VrmxtMtoonxtAuxiliaryDraw
                    {
                        Renderer = slots[i].Renderer,
                        Material = material,
                        Submesh = slots[i].Submesh,
                        AfterOpaque = false,
                        BodyPass = 0,
                    }
                );
            }
        }

        private static string SlotKey(Renderer renderer, int submesh)
        {
            return renderer.GetInstanceID() + ":" + submesh;
        }

        private static int FindBodyPass(Material material)
        {
            var pass = material.FindPass(VrmxtMaterialsMtoonxt.PassForwardBase);
            if (pass < 0)
            {
                pass = material.FindPass(VrmxtMaterialsMtoonxt.PassMtoonForward);
            }

            return pass;
        }

        private static int FindOutlinePass(Material material)
        {
            var pass = material.FindPass(VrmxtMaterialsMtoonxt.PassForwardBaseOutline);
            if (pass < 0)
            {
                pass = material.FindPass(VrmxtMaterialsMtoonxt.PassMtoonOutlineMain);
            }

            return pass;
        }

        private static void ConfigureAuxiliary(
            GameObject root,
            List<VrmxtMtoonxtAuxiliaryDraw> draws,
            List<Material> owned
        )
        {
            var component = root.GetComponent<VrmxtMaterialsMtoonxtAuxiliaryRenderer>();
            if (component == null && draws.Count > 0)
            {
                component = root.AddComponent<VrmxtMaterialsMtoonxtAuxiliaryRenderer>();
            }

            component?.Configure(draws, owned);
        }

        private static void ClearAuxiliary(GameObject root)
        {
            root?.GetComponent<VrmxtMaterialsMtoonxtAuxiliaryRenderer>()?.Configure(null, null);
            root?.GetComponent<VrmxtStencilGraphRenderer>()?.Configure(null);
        }

        private readonly struct MaterialSlot
        {
            public MaterialSlot(Renderer renderer, int submesh, Material material)
            {
                Renderer = renderer;
                Submesh = submesh;
                Material = material;
            }

            public Renderer Renderer { get; }
            public int Submesh { get; }
            public Material Material { get; }
        }
    }
}
