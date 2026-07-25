using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UniVRMXT.Format;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Authoring helpers: capture Unity override JSON from a Material asset and apply
    /// override materials onto matching avatar renderer slots.
    /// </summary>
    public static class VrmxtMaterialsOverrideAuthoring
    {
        public const string DefaultProviderId = "com.miramocha.univrmxt";

        /// <summary>
        /// UniVRM / VRM 1.0 stock MToon — not a VRMXT override target. Leave on
        /// <c>VRMC_materials_mtoon</c> only.
        /// </summary>
        public static bool IsStockUnityMtoonShader(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return false;
            }

            shaderName = shaderName.Trim();
            if (
                string.Equals(shaderName, "VRM10/MToon10", StringComparison.Ordinal)
                || string.Equals(shaderName, "VRM10/MToon10Outline", StringComparison.Ordinal)
                || string.Equals(shaderName, "VRM/MToon", StringComparison.Ordinal)
                || string.Equals(shaderName, "VRM/MToonOutline", StringComparison.Ordinal)
            )
            {
                return true;
            }

            // Defensive: any VRM10/MToon* stock path.
            return shaderName.StartsWith("VRM10/MToon", StringComparison.Ordinal);
        }

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
            var shaderName = VrmxtMaterialsOverrideApplier.GetPortableShaderName(material);
            if (IsStockUnityMtoonShader(shaderName))
            {
                // Stock MToon is not a VRMXT unity target — drop only the active RP
                // unity slot (and empty-variant that applies on this RP). Keep other
                // engines and typed unity pipeline siblings.
                ClearActiveUnityOverrideSlot(pair);
                return;
            }

            var activePipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
            var activeVariant = UnityOverrideSelector.RenderPipelineVariantToVariantString(
                activePipeline
            );

            MaterialProvider existingProvider = null;
            IReadOnlyList<VrmxtMaterialBinding> existingBindings =
                Array.Empty<VrmxtMaterialBinding>();
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

                    if (
                        !string.Equals(
                            entry.Engine,
                            VrmxtMaterialsOverride.EngineUnity,
                            StringComparison.Ordinal
                        )
                    )
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
                var sameShader =
                    emptyUnity != null
                    && string.Equals(emptyUnity.Id, shaderName, StringComparison.Ordinal);

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
                        activePipeline
                    );
                }
                else
                {
                    siblings.Add(
                        StampEmptyUnityVariantForSibling(
                            emptyVariantUnity,
                            activeVariant,
                            CollectOccupiedUnityVariants(siblings, activeVariant)
                        )
                    );
                }
            }
            else if (emptyVariantUnity != null)
            {
                // Active typed slot already matched — still keep the empty sibling.
                siblings.Add(
                    StampEmptyUnityVariantForSibling(
                        emptyVariantUnity,
                        activeVariant,
                        CollectOccupiedUnityVariants(siblings, activeVariant)
                    )
                );
            }

            if (slotVariant == null)
            {
                slotVariant = activeVariant;
            }

            var provider =
                existingProvider
                ?? new MaterialProvider(DefaultProviderId, ResolvePackageVersion());

            var properties = CaptureProperties(material, pair.SourceMaterial);

            var unityMaterial = new UnityMaterialOverride(
                VrmxtMaterialsOverride.UnityMaterialIdTypeShaderName,
                shaderName,
                slotVariant,
                provider
            );

            var unityOverride = new VrmxtMaterialEngineOverride(
                VrmxtMaterialsOverride.EngineUnity,
                unityMaterial,
                existingBindings,
                properties
            );

            var overrides = new List<VrmxtMaterialEngineOverride> { unityOverride };
            overrides.AddRange(siblings);

            pair.ExtensionJson = VrmxtMaterialsOverride.ToJson(
                new VrmxtMaterialsOverrideExtension(overrides)
            );
        }

        /// <summary>
        /// Remove the active-(RP) unity override slot from <paramref name="pair"/> while
        /// keeping non-unity engines and typed unity siblings for other variants.
        /// Empty-variant unity is dropped too (it selects on the active RP).
        /// </summary>
        private static void ClearActiveUnityOverrideSlot(VrmxtMaterialsOverridePair pair)
        {
            if (pair == null)
            {
                return;
            }

            if (
                string.IsNullOrWhiteSpace(pair.ExtensionJson)
                || !VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var existing)
            )
            {
                pair.ExtensionJson = null;
                return;
            }

            var activePipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
            var activeVariant = UnityOverrideSelector.RenderPipelineVariantToVariantString(
                activePipeline
            );

            var siblings = new List<VrmxtMaterialEngineOverride>();
            for (var i = 0; i < existing.Overrides.Count; i++)
            {
                var entry = existing.Overrides[i];
                if (entry == null)
                {
                    continue;
                }

                if (
                    !string.Equals(
                        entry.Engine,
                        VrmxtMaterialsOverride.EngineUnity,
                        StringComparison.Ordinal
                    )
                )
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

                // Drop empty-variant and the active RP slot; keep other typed variants.
                if (string.IsNullOrEmpty(unity.Variant))
                {
                    continue;
                }

                if (string.Equals(unity.Variant, activeVariant, StringComparison.Ordinal))
                {
                    continue;
                }

                siblings.Add(entry);
            }

            if (siblings.Count == 0)
            {
                pair.ExtensionJson = null;
                return;
            }

            pair.ExtensionJson = VrmxtMaterialsOverride.ToJson(
                new VrmxtMaterialsOverrideExtension(siblings)
            );
        }

        /// <summary>
        /// Variants already taken by sibling unity slots plus the active slot about to be
        /// written — used so empty→builtin stamping cannot collide with an existing builtin.
        /// </summary>
        private static HashSet<string> CollectOccupiedUnityVariants(
            IReadOnlyList<VrmxtMaterialEngineOverride> siblings,
            string activeVariant
        )
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
                if (
                    siblings[i]?.Material is UnityMaterialOverride unity
                    && !string.IsNullOrEmpty(unity.Variant)
                )
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
            HashSet<string> occupiedVariants
        )
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
            if (
                string.IsNullOrEmpty(stampedVariant)
                || (occupiedVariants != null && occupiedVariants.Contains(stampedVariant))
            )
            {
                // Prefer leaving empty over duplicating (unity, builtin) — TryParse rejects
                // duplicate selection keys.
                return emptyEntry;
            }

            var stampedMaterial = new UnityMaterialOverride(
                emptyUnity.IdType,
                emptyUnity.Id,
                stampedVariant,
                emptyUnity.Provider
            );
            return new VrmxtMaterialEngineOverride(
                emptyEntry.Engine,
                stampedMaterial,
                emptyEntry.Bindings,
                emptyEntry.Properties
            );
        }

        public static void ApplyOverrideMaterialsToRenderers(
            GameObject root,
            VrmxtMaterialsOverrideInstance instance
        )
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

                ApplyOverrideToNamedSlots(root, pair.MaterialName, source);
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
            bool destroyPreviewMaterials = true
        )
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
                    destroyPreviewMaterials
                );
            }
        }

        /// <summary>
        /// Restore one material name's renderer slots to <paramref name="sourceMaterial"/>.
        /// </summary>
        public static void RestoreSourceMaterial(
            GameObject root,
            string materialName,
            Material sourceMaterial,
            bool destroyPreviewMaterials = true
        )
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
            Material overrideMaterial
        )
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
            bool destroyPreviewMaterials
        )
        {
            if (sourceMaterial == null)
            {
                return;
            }

            // Resolve live mats for this store key (honors Name#N). Those are the slots
            // currently showing stock or a DontSave preview that we need to replace.
            var liveTargets = new HashSet<Material>();
            foreach (
                var live in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                    root,
                    materialName
                )
            )
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

                    if (destroyPreviewMaterials && (current.hideFlags & HideFlags.DontSave) != 0)
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

        /// <summary>
        /// Snapshot shader properties from a live material into VRMXT property rows.
        /// Assigned textures become <c>type: texture</c> with a placeholder index;
        /// full VRM export <c>PrepareTextures</c> remaps and packs them into the GLB.
        /// <para>
        /// When the override leaves albedo unset (<c>_MainTex</c>/<c>_BaseMap</c> null) but
        /// <paramref name="textureFallback"/> (usually <c>SourceMaterial</c> / stock MToon)
        /// has one, that map is included so Apply does not ClearUnlisted-wipe stock albedo
        /// while keeping only Poiyomi default LUTs. Near-black <c>_Color</c> is replaced from
        /// the fallback (or white) in that case. If no albedo exists on either side, texture
        /// rows are dropped so Apply leaves stock import maps alone.
        /// </para>
        /// </summary>
        public static List<VrmxtMaterialProperty> CaptureProperties(
            Material material,
            Material textureFallback = null
        )
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
                // Keep HideInInspector scalars/vectors. Poiyomi/Thry parks feature toggles
                // and UV pans there (_GlitterEnable, _ScrollingEmission, _MainTexPan, …);
                // skipping them drops glitter / emission scroll / UV scroll on re-import.
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
                        list.Add(
                            new VrmxtMaterialProperty(
                                name,
                                VrmxtMaterialsOverride.TargetTypeVector,
                                null,
                                new[] { c.r, c.g, c.b, c.a },
                                null,
                                null
                            )
                        );
                        break;
                    }
                    case ShaderPropertyType.Vector:
                    {
                        var v = material.GetVector(name);
                        list.Add(
                            new VrmxtMaterialProperty(
                                name,
                                VrmxtMaterialsOverride.TargetTypeVector,
                                null,
                                new[] { v.x, v.y, v.z, v.w },
                                null,
                                null
                            )
                        );
                        break;
                    }
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    {
                        list.Add(
                            new VrmxtMaterialProperty(
                                name,
                                VrmxtMaterialsOverride.TargetTypeScalar,
                                material.GetFloat(name),
                                null,
                                null,
                                null
                            )
                        );
                        break;
                    }
                    case ShaderPropertyType.Texture:
                    {
                        if (material.GetTexture(name) == null)
                        {
                            break;
                        }

                        // Placeholder index; export PrepareTextures remaps from live material.
                        // VectorValue carries Unity texture ST [sx, sy, ox, oy] when non-identity.
                        var scale = material.GetTextureScale(name);
                        var offset = material.GetTextureOffset(name);
                        float[] transform = null;
                        if (Math.Abs(scale.x - 1f) > 1e-5f ||
                            Math.Abs(scale.y - 1f) > 1e-5f ||
                            Math.Abs(offset.x) > 1e-5f ||
                            Math.Abs(offset.y) > 1e-5f)
                        {
                            transform = new[] { scale.x, scale.y, offset.x, offset.y };
                        }

                        list.Add(
                            new VrmxtMaterialProperty(
                                name,
                                VrmxtMaterialsOverride.TargetTypeTexture,
                                null,
                                transform,
                                null,
                                0
                            )
                        );
                        break;
                    }
                }
            }

            CaptureShaderFeatures(material, list);
            ApplyAlbedoExportPolicy(list, material, textureFallback);
            return list;
        }

        /// <summary>
        /// Ensure override JSON either owns a real albedo map or does not claim texture
        /// ownership with defaults-only LUTs (which clears stock <c>_MainTex</c> on apply).
        /// </summary>
        private static void ApplyAlbedoExportPolicy(
            List<VrmxtMaterialProperty> list,
            Material material,
            Material textureFallback
        )
        {
            if (list == null || material == null)
            {
                return;
            }

            if (ListHasMainAlbedoTexture(list))
            {
                return;
            }

            // Override albedo unset — prefer stock / SourceMaterial map.
            if (
                TryGetFallbackAlbedo(material, textureFallback, out var fallbackSlot, out _)
            )
            {
                float[] transform = null;
                if (textureFallback != null && textureFallback.HasProperty(fallbackSlot))
                {
                    var scale = textureFallback.GetTextureScale(fallbackSlot);
                    var offset = textureFallback.GetTextureOffset(fallbackSlot);
                    if (Math.Abs(scale.x - 1f) > 1e-5f ||
                        Math.Abs(scale.y - 1f) > 1e-5f ||
                        Math.Abs(offset.x) > 1e-5f ||
                        Math.Abs(offset.y) > 1e-5f)
                    {
                        transform = new[] { scale.x, scale.y, offset.x, offset.y };
                    }
                }

                list.Add(
                    new VrmxtMaterialProperty(
                        fallbackSlot,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        null,
                        transform,
                        null,
                        0
                    )
                );
                ReplaceNearBlackColor(list, textureFallback);
                return;
            }

            // Defaults-only LUTs without albedo → drop all texture rows so Apply keeps
            // stock import maps (ClearUnlisted only runs when ownership is claimed).
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var property = list[i];
                if (
                    property != null
                    && string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    list.RemoveAt(i);
                }
            }

            ReplaceNearBlackColor(list, textureFallback);
        }

        private static bool ListHasMainAlbedoTexture(IReadOnlyList<VrmxtMaterialProperty> list)
        {
            if (list == null)
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var property = list[i];
                if (
                    property != null
                    && IsMainAlbedoTextureName(property.Name)
                    && string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveMainAlbedoSlot(Material material, out string slot)
        {
            slot = null;
            if (material == null)
            {
                return false;
            }

            if (material.HasProperty("_MainTex"))
            {
                slot = "_MainTex";
                return true;
            }

            if (material.HasProperty("_BaseMap"))
            {
                slot = "_BaseMap";
                return true;
            }

            if (material.HasProperty("_BaseColorMap"))
            {
                slot = "_BaseColorMap";
                return true;
            }

            return false;
        }

        private static bool TryGetFallbackAlbedo(
            Material overrideMaterial,
            Material textureFallback,
            out string slot,
            out Texture texture
        )
        {
            slot = null;
            texture = null;
            if (textureFallback == null)
            {
                return false;
            }

            // Prefer the override shader's albedo slot name when present on fallback too.
            if (
                TryResolveMainAlbedoSlot(overrideMaterial, out var overrideSlot)
                && textureFallback.HasProperty(overrideSlot)
            )
            {
                texture = textureFallback.GetTexture(overrideSlot);
                if (texture != null)
                {
                    slot = overrideSlot;
                    return true;
                }
            }

            if (TryResolveMainAlbedoSlot(textureFallback, out var fallbackSlot))
            {
                texture = textureFallback.GetTexture(fallbackSlot);
                if (texture != null)
                {
                    // Map onto override slot when names differ (_MainTex vs _BaseMap).
                    slot =
                        TryResolveMainAlbedoSlot(overrideMaterial, out var dest) && dest != null
                            ? dest
                            : fallbackSlot;
                    return true;
                }
            }

            return false;
        }

        private static void ReplaceNearBlackColor(
            List<VrmxtMaterialProperty> list,
            Material textureFallback
        )
        {
            if (list == null)
            {
                return;
            }

            float[] replacement = null;
            if (textureFallback != null)
            {
                if (textureFallback.HasProperty("_Color"))
                {
                    var c = textureFallback.GetColor("_Color");
                    if (!IsNearBlack(c))
                    {
                        replacement = new[] { c.r, c.g, c.b, c.a };
                    }
                }
                else if (textureFallback.HasProperty("_BaseColor"))
                {
                    var c = textureFallback.GetColor("_BaseColor");
                    if (!IsNearBlack(c))
                    {
                        replacement = new[] { c.r, c.g, c.b, c.a };
                    }
                }
            }

            replacement ??= new[] { 1f, 1f, 1f, 1f };

            for (var i = 0; i < list.Count; i++)
            {
                var property = list[i];
                if (
                    property == null
                    || (
                        !string.Equals(property.Name, "_Color", StringComparison.Ordinal)
                        && !string.Equals(property.Name, "_BaseColor", StringComparison.Ordinal)
                    )
                    || !string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeVector,
                        StringComparison.Ordinal
                    )
                    || property.VectorValue == null
                    || property.VectorValue.Count < 3
                )
                {
                    continue;
                }

                var color = new Color(
                    property.VectorValue[0],
                    property.VectorValue[1],
                    property.VectorValue[2],
                    property.VectorValue.Count >= 4 ? property.VectorValue[3] : 1f
                );
                if (!IsNearBlack(color))
                {
                    continue;
                }

                list[i] = new VrmxtMaterialProperty(
                    property.Name,
                    property.Type,
                    property.ScalarValue,
                    replacement,
                    property.BoolValue,
                    property.TextureIndex
                );
                return;
            }
        }

        private static bool IsMainAlbedoTextureName(string name)
        {
            return string.Equals(name, "_MainTex", StringComparison.Ordinal)
                || string.Equals(name, "_BaseMap", StringComparison.Ordinal)
                || string.Equals(name, "_BaseColorMap", StringComparison.Ordinal);
        }

        private static bool IsNearBlack(Color c)
        {
            return c.r <= 0.02f && c.g <= 0.02f && c.b <= 0.02f;
        }

        /// <summary>
        /// Replace active-unity <c>properties</c> on each store pair from the live Character
        /// renderer material. Keeps shader, bindings, and sibling overrides.
        /// <para>
        /// Texture policy (Warudo patch): keep a texture row only when the live slot map is
        /// already packed in the GLB (matches <see cref="VrmxtMaterialsOverrideInstance.ImportedTextures"/>
        /// or an existing override texture index). New Warudo Images / editor maps that are
        /// not in the file are omitted — patch export cannot add GLB images.
        /// </para>
        /// </summary>
        public static int SyncPropertiesFromLiveMaterials(
            VrmxtMaterialsOverrideInstance store,
            GameObject root
        )
        {
            if (store?.Pairs == null || root == null)
            {
                return 0;
            }

            var updated = 0;
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (
                    pair == null
                    || string.IsNullOrEmpty(pair.MaterialName)
                    || string.IsNullOrWhiteSpace(pair.ExtensionJson)
                )
                {
                    continue;
                }

                Material live = null;
                foreach (
                    var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                        root,
                        pair.MaterialName
                    )
                )
                {
                    if (material != null && material.shader != null)
                    {
                        live = material;
                        break;
                    }
                }

                if (live == null)
                {
                    continue;
                }

                // Live still on stock MToon: do not snapshot props. Never clear a
                // non-stock ExtensionJson here — apply may have failed / not run yet
                // (live stays MToon while JSON still wants lilToon etc.).
                if (IsStockUnityMtoonShader(live.shader.name))
                {
                    continue;
                }

                var properties = FilterTexturesToPackedOnly(
                    CaptureProperties(live, pair.SourceMaterial),
                    live,
                    pair,
                    store
                );

                if (!TryReplaceActiveUnityProperties(pair, properties))
                {
                    continue;
                }

                updated++;
            }

            return updated;
        }

        /// <summary>
        /// Keep non-texture props; keep texture props only when the live map is already
        /// packed (imported GLB texture or surviving override index).
        /// </summary>
        public static List<VrmxtMaterialProperty> FilterTexturesToPackedOnly(
            IReadOnlyList<VrmxtMaterialProperty> captured,
            Material live,
            VrmxtMaterialsOverridePair pair,
            VrmxtMaterialsOverrideInstance store
        )
        {
            var list = new List<VrmxtMaterialProperty>();
            if (captured == null)
            {
                return list;
            }

            var existingSlots = CollectActiveUnityTextureSlots(pair);
            for (var i = 0; i < captured.Count; i++)
            {
                var property = captured[i];
                if (property == null)
                {
                    continue;
                }

                if (
                    !string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    list.Add(property);
                    continue;
                }

                if (
                    live == null
                    || string.IsNullOrEmpty(property.Name)
                    || !live.HasProperty(property.Name)
                )
                {
                    continue;
                }

                var liveTexture = live.GetTexture(property.Name);
                if (liveTexture == null)
                {
                    continue;
                }

                if (
                    store != null
                    && store.TryGetGltfIndexForTexture(liveTexture, out var packedIndex)
                )
                {
                    list.Add(
                        new VrmxtMaterialProperty(
                            property.Name,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            null,
                            property.VectorValue,
                            null,
                            packedIndex
                        )
                    );
                    continue;
                }

                if (existingSlots.TryGetValue(property.Name, out var existingIndex))
                {
                    // Packed in file already. Keep when live still is that import, or when
                    // import bookkeeping is missing (survive round-trip).
                    if (
                        store != null
                        && store.TryGetImportedTexture(existingIndex, out var imported)
                        && !ReferenceEquals(imported, liveTexture)
                    )
                    {
                        // Live slot replaced with a different, unpackaged map — omit.
                        continue;
                    }

                    list.Add(
                        new VrmxtMaterialProperty(
                            property.Name,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            null,
                            property.VectorValue,
                            null,
                            existingIndex
                        )
                    );
                }

                // else: live map never packed → omit for Warudo patch
            }

            return list;
        }

        private static Dictionary<string, int> CollectActiveUnityTextureSlots(
            VrmxtMaterialsOverridePair pair
        )
        {
            var slots = new Dictionary<string, int>(StringComparer.Ordinal);
            if (
                pair == null
                || string.IsNullOrWhiteSpace(pair.ExtensionJson)
                || !VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension)
                || !UnityOverrideSelector.TrySelectUnityEngineOverride(
                    extension,
                    VrmxtMaterialsOverrideApplier.DetectActivePipeline(),
                    out var engineOverride
                )
                || engineOverride?.Properties == null
            )
            {
                return slots;
            }

            for (var i = 0; i < engineOverride.Properties.Count; i++)
            {
                var property = engineOverride.Properties[i];
                if (
                    property == null
                    || string.IsNullOrEmpty(property.Name)
                    || !string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                    || !property.TextureIndex.HasValue
                    || property.TextureIndex.Value < 0
                )
                {
                    continue;
                }

                slots[property.Name] = property.TextureIndex.Value;
            }

            return slots;
        }

        /// <summary>
        /// Drop <c>texture</c>-typed entries from a properties list (kept for shader-only
        /// upserts that would otherwise preserve stale texture indices).
        /// </summary>
        public static List<VrmxtMaterialProperty> WithoutTextureProperties(
            IReadOnlyList<VrmxtMaterialProperty> properties
        )
        {
            var list = new List<VrmxtMaterialProperty>();
            if (properties == null)
            {
                return list;
            }

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                if (property == null)
                {
                    continue;
                }

                if (
                    string.Equals(
                        property.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                list.Add(property);
            }

            return list;
        }

        private static bool TryReplaceActiveUnityProperties(
            VrmxtMaterialsOverridePair pair,
            IReadOnlyList<VrmxtMaterialProperty> properties
        )
        {
            if (
                pair == null
                || string.IsNullOrWhiteSpace(pair.ExtensionJson)
                || !VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var existing)
            )
            {
                return false;
            }

            var activePipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
            var activeVariant = UnityOverrideSelector.RenderPipelineVariantToVariantString(
                activePipeline
            );

            MaterialProvider provider = null;
            IReadOnlyList<VrmxtMaterialBinding> bindings = Array.Empty<VrmxtMaterialBinding>();
            string shaderName = null;
            string slotVariant = null;
            var siblings = new List<VrmxtMaterialEngineOverride>();
            VrmxtMaterialEngineOverride emptyVariantUnity = null;

            for (var i = 0; i < existing.Overrides.Count; i++)
            {
                var entry = existing.Overrides[i];
                if (entry == null)
                {
                    continue;
                }

                if (
                    !string.Equals(
                        entry.Engine,
                        VrmxtMaterialsOverride.EngineUnity,
                        StringComparison.Ordinal
                    )
                )
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
                    provider = unity.Provider;
                    bindings = entry.Bindings;
                    shaderName = unity.Id;
                    slotVariant = unity.Variant;
                    continue;
                }

                if (string.IsNullOrEmpty(unity.Variant))
                {
                    emptyVariantUnity = entry;
                    continue;
                }

                siblings.Add(entry);
            }

            if (string.IsNullOrEmpty(shaderName) && emptyVariantUnity != null)
            {
                var emptyUnity = emptyVariantUnity.Material as UnityMaterialOverride;
                provider = emptyUnity?.Provider;
                bindings = emptyVariantUnity.Bindings;
                shaderName = emptyUnity?.Id;
                slotVariant = activeVariant;
                emptyVariantUnity = null;
            }
            else if (emptyVariantUnity != null)
            {
                siblings.Add(emptyVariantUnity);
            }

            if (string.IsNullOrEmpty(shaderName))
            {
                return false;
            }

            if (slotVariant == null)
            {
                slotVariant = activeVariant;
            }

            if (provider == null)
            {
                provider = new MaterialProvider(DefaultProviderId, ResolvePackageVersion());
            }

            var unityMaterial = new UnityMaterialOverride(
                VrmxtMaterialsOverride.UnityMaterialIdTypeShaderName,
                shaderName,
                slotVariant,
                provider
            );

            var unityOverride = new VrmxtMaterialEngineOverride(
                VrmxtMaterialsOverride.EngineUnity,
                unityMaterial,
                bindings,
                properties ?? Array.Empty<VrmxtMaterialProperty>()
            );

            var overrides = new List<VrmxtMaterialEngineOverride> { unityOverride };
            overrides.AddRange(siblings);
            pair.ExtensionJson = VrmxtMaterialsOverride.ToJson(
                new VrmxtMaterialsOverrideExtension(overrides)
            );
            return true;
        }

        private static void CaptureShaderFeatures(
            Material material,
            List<VrmxtMaterialProperty> list
        )
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

                    list.Add(
                        new VrmxtMaterialProperty(
                            name,
                            VrmxtMaterialsOverride.TargetTypeShaderFeature,
                            null,
                            null,
                            true,
                            null
                        )
                    );
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
