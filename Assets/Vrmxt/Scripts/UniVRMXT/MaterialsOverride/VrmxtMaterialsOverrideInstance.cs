using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Runtime holder for <c>VRMXT_materials_override</c> on a loaded avatar root.
    /// Each pair keys a glTF material name to optional authoring <see cref="OverrideMaterial"/>
    /// and keeps full extension JSON (all engines) for round-trip export.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmxtMaterialsOverrideInstance : MonoBehaviour
    {
        [SerializeField]
        private List<VrmxtMaterialsOverridePair> pairs = new();

        /// <summary>
        /// glTF <c>textures[]</c> decoded on import for override property/binding indices.
        /// Survives as sub-assets so re-export can re-register foreign-RP slot textures.
        /// </summary>
        [SerializeField]
        private List<VrmxtImportedGltfTexture> importedTextures = new();

        public IReadOnlyList<VrmxtMaterialsOverridePair> Pairs => pairs;

        /// <summary>Alias for callers migrating from the former Entries API.</summary>
        public IReadOnlyList<VrmxtMaterialsOverridePair> Entries => pairs;

        public IReadOnlyList<VrmxtImportedGltfTexture> ImportedTextures => importedTextures;

        public void SetPairs(IEnumerable<VrmxtMaterialsOverridePair> values)
        {
            pairs.Clear();
            if (values == null)
            {
                return;
            }

            pairs.AddRange(values);
        }

        /// <summary>Alias for <see cref="SetPairs"/>.</summary>
        public void SetEntries(IEnumerable<VrmxtMaterialsOverridePair> values) => SetPairs(values);

        public void ClearImportedTextures()
        {
            importedTextures.Clear();
        }

        /// <summary>
        /// Remember a decoded glTF texture by its import-time index. Later indices for the
        /// same slot overwrite. Null textures are ignored.
        /// </summary>
        public void RememberImportedTexture(int gltfIndex, Texture texture)
        {
            if (gltfIndex < 0 || texture == null)
            {
                return;
            }

            for (var i = 0; i < importedTextures.Count; i++)
            {
                if (importedTextures[i] != null && importedTextures[i].GltfIndex == gltfIndex)
                {
                    importedTextures[i] = new VrmxtImportedGltfTexture(gltfIndex, texture);
                    return;
                }
            }

            importedTextures.Add(new VrmxtImportedGltfTexture(gltfIndex, texture));
        }

        public bool TryGetImportedTexture(int gltfIndex, out Texture texture)
        {
            for (var i = 0; i < importedTextures.Count; i++)
            {
                var entry = importedTextures[i];
                if (entry != null && entry.GltfIndex == gltfIndex && entry.Texture != null)
                {
                    texture = entry.Texture;
                    return true;
                }
            }

            texture = null;
            return false;
        }

        /// <summary>
        /// Decode every texture index referenced by stored ExtensionJson via
        /// <paramref name="resolveTexture"/> and keep the results for export remapping.
        /// </summary>
        public void RememberTexturesFromPairs(Func<int, Texture> resolveTexture)
        {
            if (resolveTexture == null)
            {
                return;
            }

            var indices = new HashSet<int>();
            for (var i = 0; i < pairs.Count; i++)
            {
                CollectTextureIndicesFromExtensionJson(pairs[i]?.ExtensionJson, indices);
            }

            foreach (var index in indices)
            {
                RememberImportedTexture(index, resolveTexture(index));
            }
        }

        private static void CollectTextureIndicesFromExtensionJson(
            string extensionJson,
            HashSet<int> indices)
        {
            if (string.IsNullOrEmpty(extensionJson) ||
                !VrmxtMaterialsOverride.TryParse(extensionJson, out var extension))
            {
                return;
            }

            for (var i = 0; i < extension.Overrides.Count; i++)
            {
                var engineOverride = extension.Overrides[i];
                if (engineOverride?.Properties == null)
                {
                    continue;
                }

                for (var p = 0; p < engineOverride.Properties.Count; p++)
                {
                    var property = engineOverride.Properties[p];
                    if (property != null && property.TextureIndex.HasValue)
                    {
                        indices.Add(property.TextureIndex.Value);
                    }
                }
            }
        }

        public void Clear()
        {
            pairs.Clear();
            importedTextures.Clear();
        }

        /// <summary>
        /// Clear authored overrides (Override Material + Extension JSON) but keep glTF
        /// material name / source rows for re-authoring. Restores
        /// <see cref="VrmxtMaterialsOverridePair.SourceMaterial"/> onto matching renderer
        /// slots and destroys scene preview clones.
        /// </summary>
        [ContextMenu("Clear Material Overrides")]
        public void ClearOverrides()
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                ClearOverrideAt(i);
            }
        }

        /// <summary>
        /// Clear one pair by list index: restore Source onto matching slots, then clear
        /// Override Material + Extension JSON. Keeps the pair row for re-authoring.
        /// </summary>
        public bool ClearOverrideAt(int index)
        {
            if (index < 0 || index >= pairs.Count)
            {
                return false;
            }

            var pair = pairs[index];
            if (pair == null)
            {
                return false;
            }

            if (IsPreviewMaterial(pair.SourceMaterial))
            {
                pair.SourceMaterial = null;
            }

            if (!string.IsNullOrEmpty(pair.MaterialName) && pair.SourceMaterial != null)
            {
                VrmxtMaterialsOverrideAuthoring.RestoreSourceMaterial(
                    gameObject,
                    pair.MaterialName,
                    pair.SourceMaterial);
            }

            pair.OverrideMaterial = null;
            pair.ExtensionJson = null;
            return true;
        }

        /// <summary>
        /// Clear the first pair whose <see cref="VrmxtMaterialsOverridePair.MaterialName"/>
        /// matches <paramref name="materialName"/>.
        /// </summary>
        public bool ClearOverride(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return false;
            }

            for (var i = 0; i < pairs.Count; i++)
            {
                if (string.Equals(pairs[i]?.MaterialName, materialName, StringComparison.Ordinal))
                {
                    return ClearOverrideAt(i);
                }
            }

            return false;
        }

        public bool TryGetPair(string materialName, out VrmxtMaterialsOverridePair pair)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (string.Equals(pairs[i]?.MaterialName, materialName, StringComparison.Ordinal))
                {
                    pair = pairs[i];
                    return true;
                }
            }

            pair = null;
            return false;
        }

        /// <summary>Alias for <see cref="TryGetPair"/>.</summary>
        public bool TryGetEntry(string materialName, out VrmxtMaterialsOverridePair entry) =>
            TryGetPair(materialName, out entry);

        public bool RemovePair(string materialName)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (string.Equals(pairs[i]?.MaterialName, materialName, StringComparison.Ordinal))
                {
                    pairs.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Alias for <see cref="RemovePair"/>.</summary>
        public bool RemoveEntry(string materialName) => RemovePair(materialName);

        /// <summary>
        /// Re-resolve <see cref="VrmxtMaterialsOverridePair.SourceMaterial"/> from renderers
        /// under this GameObject's root (this transform).
        /// </summary>
        public void RefreshSourceMaterials()
        {
            var root = gameObject;
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                Material resolved = null;
                foreach (var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                             root, pair.MaterialName))
                {
                    if (IsPreviewMaterial(material))
                    {
                        continue;
                    }

                    resolved = material;
                    break;
                }

                // Keep the first wired stock reference. After authoring apply, slots hold
                // scene clones with the same name — do not replace Source with those.
                if (IsPreviewMaterial(pair.SourceMaterial))
                {
                    pair.SourceMaterial = null;
                }

                if (pair.SourceMaterial == null && resolved != null)
                {
                    pair.SourceMaterial = resolved;
                }
            }
        }

        /// <summary>
        /// Add pairs for unique renderer material names under this root that lack an entry.
        /// Skips materials already covered by an import/store key (including <c>Name#N</c>
        /// disambiguated keys whose live mats still use the plain glTF name).
        /// </summary>
        [ContextMenu("Populate Pairs From Renderers")]
        public void PopulatePairsFromRenderers()
        {
            var root = gameObject;
            var coveredNames = new HashSet<string>(StringComparer.Ordinal);
            var coveredMaterials = new HashSet<Material>();

            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(pair.MaterialName))
                {
                    coveredNames.Add(pair.MaterialName);
                    if (TryGetDisambiguatedBaseName(pair.MaterialName, out var baseName))
                    {
                        coveredNames.Add(baseName);
                    }

                    foreach (var live in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                                 root, pair.MaterialName))
                    {
                        if (live != null)
                        {
                            coveredMaterials.Add(live);
                        }
                    }
                }

                if (pair.SourceMaterial != null)
                {
                    coveredMaterials.Add(pair.SourceMaterial);
                }
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                // VFX preview ParticleSystems are not glTF materials — skip them.
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var shared = renderer.sharedMaterials;
                for (var j = 0; j < shared.Length; j++)
                {
                    var material = shared[j];
                    if (material == null || IsPreviewMaterial(material))
                    {
                        continue;
                    }

                    if (coveredMaterials.Contains(material))
                    {
                        continue;
                    }

                    var name = StripInstanceSuffix(material.name);
                    if (string.IsNullOrEmpty(name) || coveredNames.Contains(name))
                    {
                        continue;
                    }

                    coveredNames.Add(name);
                    coveredMaterials.Add(material);
                    pairs.Add(new VrmxtMaterialsOverridePair(name, null)
                    {
                        SourceMaterial = material,
                    });
                }
            }
        }

        /// <summary>
        /// <c>Hair#2</c> → base <c>Hair</c>. Used so Populate does not add a plain-name
        /// duplicate beside import disambiguated keys.
        /// </summary>
        private static bool TryGetDisambiguatedBaseName(string storeKey, out string baseName)
        {
            baseName = null;
            if (string.IsNullOrEmpty(storeKey))
            {
                return false;
            }

            var hashIndex = storeKey.LastIndexOf('#');
            if (hashIndex <= 0 || hashIndex == storeKey.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(storeKey.Substring(hashIndex + 1), out var occurrence) ||
                occurrence <= 0)
            {
                return false;
            }

            baseName = storeKey.Substring(0, hashIndex);
            return !string.IsNullOrEmpty(baseName);
        }

        /// <summary>
        /// Sync <see cref="ExtensionJson"/> from assigned override materials and push
        /// override shader/props onto matching live materials.
        /// </summary>
        public void SyncFromOverrideMaterials()
        {
            VrmxtMaterialsOverrideAuthoring.SyncAllFromOverrideMaterials(this);
            VrmxtMaterialsOverrideAuthoring.ApplyOverrideMaterialsToRenderers(gameObject, this);
        }

        private void OnValidate()
        {
            if (this == null)
            {
                return;
            }

#if UNITY_EDITOR
            // Domain reload / Test Runner / asset refresh: skip Apply while compiling or
            // updating so DontSave preview mats are not created against half-ready shaders
            // (pink / "VRMXT shader" errors after scene restore). Re-run once settled.
            if (UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorApplication.isUpdating)
            {
                ScheduleValidateFlush();
                return;
            }
#endif

            FlushValidate();
        }

#if UNITY_EDITOR
        [System.NonSerialized]
        private bool validateFlushScheduled;

        private void ScheduleValidateFlush()
        {
            if (validateFlushScheduled)
            {
                return;
            }

            validateFlushScheduled = true;
            UnityEditor.EditorApplication.delayCall += FlushValidateFromDelayCall;
        }

        private void FlushValidateFromDelayCall()
        {
            validateFlushScheduled = false;
            if (this == null)
            {
                return;
            }

            if (UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorApplication.isUpdating)
            {
                ScheduleValidateFlush();
                return;
            }

            FlushValidate();
        }
#endif

        private void FlushValidate()
        {
            RefreshSourceMaterials();
            SyncFromOverrideMaterials();
        }

        private static bool IsPreviewMaterial(Material material)
        {
            return material != null && (material.hideFlags & HideFlags.DontSave) != 0;
        }

        private static string StripInstanceSuffix(string unityMaterialName) =>
            VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(unityMaterialName);
    }

    /// <summary>
    /// One glTF material ↔ optional Unity override Material, plus verbatim extension JSON.
    /// </summary>
    [Serializable]
    public sealed class VrmxtMaterialsOverridePair
    {
        public string MaterialName;
        public Material SourceMaterial;
        public Material OverrideMaterial;
        public string ExtensionJson;

        public VrmxtMaterialsOverridePair()
        {
        }

        public VrmxtMaterialsOverridePair(string materialName, string extensionJson)
        {
            MaterialName = materialName;
            ExtensionJson = extensionJson;
        }
    }

    /// <summary>
    /// Import-time glTF texture kept on the Instance so foreign-RP override slots can
    /// re-register images on export (write-through indices alone omit images from a new GLB).
    /// </summary>
    [Serializable]
    public sealed class VrmxtImportedGltfTexture
    {
        [SerializeField]
        private int gltfIndex;

        [SerializeField]
        private Texture texture;

        public VrmxtImportedGltfTexture()
        {
        }

        public VrmxtImportedGltfTexture(int gltfIndex, Texture texture)
        {
            this.gltfIndex = gltfIndex;
            this.texture = texture;
        }

        public int GltfIndex => gltfIndex;

        public Texture Texture => texture;
    }

}
