using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Authoring helpers: capture Unity override JSON from a Material asset and apply
    /// override materials onto matching avatar renderer slots.
    /// </summary>
    public static class VrmxtMaterialsOverrideAuthoring
    {
        public const string DefaultProviderId = "com.miramocha.univrmxt";

        public static void SyncAllFromOverrideMaterials(VrmxtMaterialsOverrideInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            foreach (var pair in instance.Pairs)
            {
                if (pair?.OverrideMaterial == null)
                {
                    continue;
                }

                SyncUnityOverrideFromMaterial(pair);
            }
        }

        /// <summary>
        /// Upsert the active <c>(unity, variant)</c> slot from
        /// <see cref="VrmxtMaterialsOverridePair.OverrideMaterial"/>. Sibling unity variants
        /// and other engines stay intact. Fills <c>variant</c> from the active RP when creating
        /// a new slot (see <see cref="VrmxtMaterialsOverrideExporter.ResolveUnityVariant"/>).
        /// </summary>
        public static void SyncUnityOverrideFromMaterial(VrmxtMaterialsOverridePair pair)
        {
            if (pair?.OverrideMaterial == null || pair.OverrideMaterial.shader == null)
            {
                return;
            }

            var material = pair.OverrideMaterial;
            var shaderName = material.shader.name;
            var activePipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
            var activeVariant = UnityOverrideSelector.RenderPipelineVariantToVariantString(activePipeline);

            MaterialProvider existingProvider = null;
            IReadOnlyList<VrmxtMaterialBinding> existingBindings = Array.Empty<VrmxtMaterialBinding>();
            string slotVariant = null;
            var siblings = new List<VrmxtMaterialEngineOverride>();
            VrmxtMaterialEngineOverride emptyVariantUnity = null;
            var typedUnityCount = 0;

            if (VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var existing))
            {
                foreach (var entry in existing.Overrides)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    if (!string.Equals(entry.Engine, VrmxtMaterialsOverride.EngineUnity, StringComparison.Ordinal))
                    {
                        siblings.Add(entry);
                        continue;
                    }

                    var unity = entry.Material as UnityMaterialOverride;
                    if (unity == null)
                    {
                        siblings.Add(entry);
                        continue;
                    }

                    if (string.Equals(unity.Variant, activeVariant, StringComparison.Ordinal))
                    {
                        // Active slot — replace below; keep bindings / provider / variant.
                        existingProvider = unity.Provider;
                        existingBindings = entry.Bindings;
                        slotVariant = unity.Variant;
                        continue;
                    }

                    if (string.IsNullOrEmpty(unity.Variant))
                    {
                        emptyVariantUnity = entry;
                        continue;
                    }

                    typedUnityCount++;
                    siblings.Add(entry);
                }
            }

            if (slotVariant == null && emptyVariantUnity != null)
            {
                var emptyUnity = emptyVariantUnity.Material as UnityMaterialOverride;
                var sameShader = emptyUnity != null &&
                                 string.Equals(emptyUnity.Id, shaderName, StringComparison.Ordinal);

                // Only fold an empty-variant slot into the active RP when it is the sole
                // unity entry and the shader matches (in-place single-slot edit). A different
                // shader means a new pipeline slot — keep the empty entry so BIRP/URP
                // siblings survive (stamp builtin when adding urp/hdrp for a conforming key).
                if (sameShader && typedUnityCount == 0)
                {
                    existingProvider = emptyUnity.Provider;
                    existingBindings = emptyVariantUnity.Bindings;
                    slotVariant = VrmxtMaterialsOverrideExporter.ResolveUnityVariant(
                        emptyUnity.Variant,
                        activePipeline);
                }
                else
                {
                    siblings.Add(StampEmptyUnityVariantForSibling(
                        emptyVariantUnity,
                        activeVariant,
                        CollectOccupiedUnityVariants(siblings, activeVariant)));
                }
            }
            else if (emptyVariantUnity != null)
            {
                // Active typed slot already matched — still keep the empty sibling.
                siblings.Add(StampEmptyUnityVariantForSibling(
                    emptyVariantUnity,
                    activeVariant,
                    CollectOccupiedUnityVariants(siblings, activeVariant)));
            }

            if (slotVariant == null)
            {
                slotVariant = activeVariant;
            }

            var provider = existingProvider ?? new MaterialProvider(
                DefaultProviderId,
                ResolvePackageVersion());

            var properties = CaptureProperties(material);

            var unityMaterial = new UnityMaterialOverride(
                VrmxtMaterialsOverride.UnityMaterialIdTypeShaderName,
                shaderName,
                slotVariant,
                provider);

            var unityOverride = new VrmxtMaterialEngineOverride(
                VrmxtMaterialsOverride.EngineUnity,
                unityMaterial,
                existingBindings,
                properties);

            var overrides = new List<VrmxtMaterialEngineOverride> { unityOverride };
            overrides.AddRange(siblings);

            pair.ExtensionJson = VrmxtMaterialsOverride.ToJson(
                new VrmxtMaterialsOverrideExtension(overrides));
        }

        /// <summary>
        /// Variants already taken by sibling unity slots plus the active slot about to be
        /// written — used so empty→builtin stamping cannot collide with an existing builtin.
        /// </summary>
        private static HashSet<string> CollectOccupiedUnityVariants(
            IReadOnlyList<VrmxtMaterialEngineOverride> siblings,
            string activeVariant)
        {
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(activeVariant))
            {
                occupied.Add(activeVariant);
            }

            if (siblings == null)
            {
                return occupied;
            }

            for (var i = 0; i < siblings.Count; i++)
            {
                if (siblings[i]?.Material is UnityMaterialOverride unity &&
                    !string.IsNullOrEmpty(unity.Variant))
                {
                    occupied.Add(unity.Variant);
                }
            }

            return occupied;
        }

        /// <summary>
        /// When keeping an empty-variant unity entry beside a new typed slot, give it a
        /// concrete variant so selection-key uniqueness stays valid (2+ unity entries).
        /// Skips stamping when the preferred variant is already occupied (leave empty).
        /// </summary>
        private static VrmxtMaterialEngineOverride StampEmptyUnityVariantForSibling(
            VrmxtMaterialEngineOverride emptyEntry,
            string activeVariant,
            HashSet<string> occupiedVariants)
        {
            var emptyUnity = emptyEntry?.Material as UnityMaterialOverride;
            if (emptyUnity == null || !string.IsNullOrEmpty(emptyUnity.Variant))
            {
                return emptyEntry;
            }

            // Most common sibling when authoring urp/hdrp on top of an unlabeled slot.
            var stampedVariant = string.Equals(activeVariant, "builtin", StringComparison.Ordinal)
                ? null
                : "builtin";
            if (string.IsNullOrEmpty(stampedVariant) ||
                (occupiedVariants != null && occupiedVariants.Contains(stampedVariant)))
            {
                // Prefer leaving empty over duplicating (unity, builtin) — TryParse rejects
                // duplicate selection keys.
                return emptyEntry;
            }

            var stampedMaterial = new UnityMaterialOverride(
                emptyUnity.IdType,
                emptyUnity.Id,
                stampedVariant,
                emptyUnity.Provider);
            return new VrmxtMaterialEngineOverride(
                emptyEntry.Engine,
                stampedMaterial,
                emptyEntry.Bindings,
                emptyEntry.Properties);
        }

        public static void ApplyOverrideMaterialsToRenderers(
            GameObject root,
            VrmxtMaterialsOverrideInstance instance)
        {
            if (root == null || instance == null)
            {
                return;
            }

            foreach (var pair in instance.Pairs)
            {
                if (pair?.OverrideMaterial == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                var source = pair.OverrideMaterial;
                if (source.shader == null)
                {
                    continue;
                }

                ApplyOverrideToNamedSlots(
                    root,
                    pair.MaterialName,
                    source);
            }
        }

        /// <summary>
        /// Put <see cref="VrmxtMaterialsOverridePair.SourceMaterial"/> back onto matching
        /// renderer slots and optionally destroy non-persistent preview instances.
        /// </summary>
        /// <param name="destroyPreviewMaterials">
        /// When false (export throwaway copy), do not <c>DestroyImmediate</c> DontSave
        /// previews — <see cref="UnityEngine.Object.Instantiate"/> may still share them
        /// with the scene original.
        /// </param>
        public static void RestoreSourceMaterialsToRenderers(
            GameObject root,
            VrmxtMaterialsOverrideInstance instance,
            bool destroyPreviewMaterials = true)
        {
            if (root == null || instance == null)
            {
                return;
            }

            foreach (var pair in instance.Pairs)
            {
                if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                RestoreSourceMaterial(
                    root,
                    pair.MaterialName,
                    pair.SourceMaterial,
                    destroyPreviewMaterials);
            }
        }

        /// <summary>
        /// Restore one material name's renderer slots to <paramref name="sourceMaterial"/>.
        /// </summary>
        public static void RestoreSourceMaterial(
            GameObject root,
            string materialName,
            Material sourceMaterial,
            bool destroyPreviewMaterials = true)
        {
            if (root == null || string.IsNullOrEmpty(materialName) || sourceMaterial == null)
            {
                return;
            }

            RestoreSourceToNamedSlots(root, materialName, sourceMaterial, destroyPreviewMaterials);
        }

        /// <summary>
        /// Swap matching renderer slots to a scene-owned clone of
        /// <paramref name="overrideMaterial"/> (never mutate imported asset materials).
        /// Clone keeps <paramref name="materialName"/> so export/applier name lookup still works.
        /// </summary>
        private static void ApplyOverrideToNamedSlots(
            GameObject root,
            string materialName,
            Material overrideMaterial)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var j = 0; j < shared.Length; j++)
                {
                    var current = shared[j];
                    if (current == null || !MaterialNameMatches(current.name, materialName))
                    {
                        continue;
                    }

                    var previousIsPreview = (current.hideFlags & HideFlags.DontSave) != 0;

                    if (previousIsPreview)
                    {
                        // Prior scene preview instance — update in place.
                        CopyMaterialState(overrideMaterial, current);
                        current.name = materialName;
                        continue;
                    }

                    // Stock / override assets: never mutate — swap slot to a DontSave clone.
                    var preview = new Material(overrideMaterial)
                    {
                        name = materialName,
                        hideFlags = HideFlags.DontSave,
                    };
                    shared[j] = preview;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        private static void RestoreSourceToNamedSlots(
            GameObject root,
            string materialName,
            Material sourceMaterial,
            bool destroyPreviewMaterials)
        {
            if (sourceMaterial == null)
            {
                return;
            }

            // Resolve live mats for this store key (honors Name#N). Those are the slots
            // currently showing stock or a DontSave preview that we need to replace.
            var liveTargets = new HashSet<Material>();
            foreach (var live in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                         root, materialName))
            {
                if (live != null)
                {
                    liveTargets.Add(live);
                }
            }

            if (liveTargets.Count == 0)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var shared = renderer.sharedMaterials;
                var changed = false;
                for (var j = 0; j < shared.Length; j++)
                {
                    var current = shared[j];
                    if (current == null || !liveTargets.Contains(current))
                    {
                        continue;
                    }

                    if (ReferenceEquals(current, sourceMaterial))
                    {
                        continue;
                    }

                    shared[j] = sourceMaterial;
                    changed = true;

                    if (destroyPreviewMaterials &&
                        (current.hideFlags & HideFlags.DontSave) != 0)
                    {
                        DestroyOwnedMaterial(current);
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        private static bool MaterialNameMatches(string unityMaterialName, string gltfMaterialName)
        {
            var unity = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(unityMaterialName);
            var gltf = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(gltfMaterialName);
            return string.Equals(unity, gltf, StringComparison.Ordinal);
        }

        private static void DestroyOwnedMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        public static List<VrmxtMaterialProperty> CaptureProperties(Material material)
        {
            var list = new List<VrmxtMaterialProperty>();
            if (material == null || material.shader == null)
            {
                return list;
            }

            var shader = material.shader;
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                var flags = shader.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.HideInInspector) != 0)
                {
                    continue;
                }

                var name = shader.GetPropertyName(i);
                if (string.IsNullOrEmpty(name) || !material.HasProperty(name))
                {
                    continue;
                }

                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    {
                        var c = material.GetColor(name);
                        list.Add(new VrmxtMaterialProperty(
                            name,
                            VrmxtMaterialsOverride.TargetTypeVector,
                            null,
                            new[] { c.r, c.g, c.b, c.a },
                            null,
                            null));
                        break;
                    }
                    case ShaderPropertyType.Vector:
                    {
                        var v = material.GetVector(name);
                        list.Add(new VrmxtMaterialProperty(
                            name,
                            VrmxtMaterialsOverride.TargetTypeVector,
                            null,
                            new[] { v.x, v.y, v.z, v.w },
                            null,
                            null));
                        break;
                    }
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    {
                        list.Add(new VrmxtMaterialProperty(
                            name,
                            VrmxtMaterialsOverride.TargetTypeScalar,
                            material.GetFloat(name),
                            null,
                            null,
                            null));
                        break;
                    }
                    case ShaderPropertyType.Texture:
                    {
                        if (material.GetTexture(name) == null)
                        {
                            break;
                        }

                        // Placeholder index; export PrepareTextures remaps from live material.
                        list.Add(new VrmxtMaterialProperty(
                            name,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            null,
                            null,
                            null,
                            0));
                        break;
                    }
                }
            }

            CaptureShaderFeatures(material, list);
            return list;
        }

        private static void CaptureShaderFeatures(Material material, List<VrmxtMaterialProperty> list)
        {
            var shader = material.shader;
            if (shader == null)
            {
                return;
            }

            try
            {
                foreach (var keyword in material.enabledKeywords)
                {
                    var name = keyword.name;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    list.Add(new VrmxtMaterialProperty(
                        name,
                        VrmxtMaterialsOverride.TargetTypeShaderFeature,
                        null,
                        null,
                        true,
                        null));
                }
            }
            catch (Exception)
            {
                // LocalKeyword API may be unavailable on older pipelines; skip features.
            }
        }

        private static void CopyMaterialState(Material source, Material target)
        {
            if (source == null || target == null || source.shader == null)
            {
                return;
            }

            target.shader = source.shader;
            target.CopyPropertiesFromMaterial(source);
        }

        private static string ResolvePackageVersion()
        {
            return "0.1.0";
        }
    }
}
