using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Scenes;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets.Character;

/// <summary>
/// Manually added scene asset: bind one Character, toggle VRMXT features,
/// edit per-material unity shader overrides, and patch-export materials
/// into a new local <c>.vrm</c>. At most one Manager may claim a given Character.
/// </summary>
[AssetType(
    Id = "7c4e9a2b-8f1d-4c6e-b3a0-5d9e2f8c1b70",
    Title = "VRMXT Manager",
    Category = "CATEGORY_CHARACTERS",
    Singleton = false)]
public sealed class VrmxtManagerAsset : Asset
{
    [DataInput]
    [Label("Character")]
    [AssetFilter(nameof(FilterLocalCharacter))]
    public CharacterAsset Character;

    [DataInput]
    [Label("Enable Sprite Particle")]
    [Description("Apply VRMXT_sprite_particle on this Character. Off clears live VFX.")]
    public bool EnableSpriteParticle = true;

    [DataInput]
    [Label("Enable Materials Override")]
    [Description(
        "Apply VRMXT_materials_override on this Character. Off restores stock shaders. " +
        "Required for Apply / Clear / Export below.")]
    public bool EnableMaterialsOverride = true;

    [Markdown]
    public string Hint =
        "Add one VRMXT Manager per Character. Feature toggles default on. " +
        "Without a Manager, the plugin still applies both features. " +
        "Export patches `VRMXT_materials_override` into a copy of the original local VRM " +
        "(geometry/BIN unchanged). Does not capture live mesh or VFX edits.";

    [DataInput]
    [Label("Materials")]
    [Description(
        "Filled from the Character. Use Refresh after load. Pick shaders then Apply. " +
        "Add/remove rows are ignored on Apply/Export — Refresh rebuilds the list.")]
    public VrmxtMaterialShaderRow[] Materials = Array.Empty<VrmxtMaterialShaderRow>();

    [DataInput]
    [Label("Export File Suffix")]
    [Description("Inserted before .vrm. Default .vrmxt → Characters/Foo.vrmxt.vrm")]
    public string ExportFileSuffix = VrmxtPatchExport.DefaultFileSuffix;

    [Markdown]
    [Label("Status")]
    public string Status = "Idle.";

    private bool _exportInProgress;
    private bool _applyInProgress;
    private bool _clearInProgress;
    private bool _suppressCharacterWatch;

    protected override void OnCreate()
    {
        base.OnCreate();
        WatchAsset(nameof(Character), OnCharacterChanged);
        Watch<bool>(nameof(EnableSpriteParticle), OnFeatureToggleChanged);
        Watch<bool>(nameof(EnableMaterialsOverride), OnFeatureToggleChanged);
        SetActive(true);
        ReconcileDuplicateClaimsIfNeeded();
    }

    protected bool FilterLocalCharacter(CharacterAsset character)
    {
        if (character == null || !character.IsNonNullAndActive())
        {
            return false;
        }

        if (!VrmxtCharacterSource.TryGetPersistentRelativePath(character.Source, out _))
        {
            return false;
        }

        var claims = CollectSceneClaims();
        var claimedByOthers = VrmxtCharacterOwnership.ClaimedCharacterIdsExcluding(Id, claims);
        return !claimedByOthers.Contains(character.Id);
    }

    private void OnCharacterChanged()
    {
        if (_suppressCharacterWatch)
        {
            return;
        }

        if (Character != null &&
            VrmxtCharacterOwnership.IsClaimedByOther(Character.Id, Id, CollectSceneClaims()))
        {
            var name = Character.Name;
            ClearCharacterAssignment(
                "Character '" + name + "' is already claimed by another VRMXT Manager.");
            return;
        }

        // Sync fill first (same as pre-export-fix path). Re-fill after plugin apply.
        RefreshMaterials();
        RefreshAfterPluginApplyAsync().Forget();
    }

    private async UniTaskVoid RefreshAfterPluginApplyAsync()
    {
        await RequestPluginApplyAsync(deferMaterialsOverride: false);
        RefreshMaterials();
    }

    private void OnFeatureToggleChanged(bool from, bool to)
    {
        if (from == to)
        {
            return;
        }

        RequestPluginApply(deferMaterialsOverride: false);
        if (!EnableMaterialsOverride)
        {
            SetStatus("Materials override disabled for this Character.");
        }
    }

    [Trigger]
    [Label("Refresh Materials")]
    [Description("Rebuild the material list from the Character store and renderers.")]
    public void RefreshMaterials()
    {
        if (Character == null || !Character.IsNonNullAndActive())
        {
            SetStatus("Select an active local Character.");
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            return;
        }

        var root = VrmxtCharacterApply.TryFindCharacterRoot(Character);
        if (root == null)
        {
            SetStatus("Character root not found yet. Wait for load, then Refresh.");
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            return;
        }

        var store = root.GetComponent<VrmxtMaterialsOverrideInstance>();
        if (store != null && (store.Pairs == null || store.Pairs.Count == 0))
        {
            store.PopulatePairsFromRenderers();
        }

        var rows = VrmxtMaterialsShaderAuthoring.CollectMaterialRows(root, store);
        var structured = new VrmxtMaterialShaderRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            structured[i] = StructuredData.Create<VrmxtMaterialShaderRow>(sd =>
            {
                sd.MaterialName = row.MaterialName;
                sd.ShaderName = row.ShaderName ?? string.Empty;
                sd.GltfMaterialIndex = row.GltfMaterialIndex;
            });
        }

        SetDataInput(nameof(Materials), structured, broadcast: true);
        SetStatus("Loaded " + structured.Length + " material(s) from '" + Character.Name + "'.");
    }

    private static bool StoreHasOverrideJson(VrmxtMaterialsOverrideInstance store)
    {
        if (store?.Pairs == null)
        {
            return false;
        }

        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (pair != null && !string.IsNullOrWhiteSpace(pair.ExtensionJson))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Build export store from live Apply state and/or Manager Materials rows.
    /// Does not replace an authored live store with stock source-file JSON.
    /// </summary>
    private bool TryPrepareStoreForExport(
        GameObject root,
        string gltfJson,
        out VrmxtMaterialsOverrideInstance store,
        out string error)
    {
        store = root != null ? root.GetComponent<VrmxtMaterialsOverrideInstance>() : null;
        error = null;

        if (!StoreHasOverrideJson(store))
        {
            // Seed store from source file (or empty renderer pairs). Do not call this when
            // live Apply already wrote ExtensionJson — SetPairs would wipe shaders.
            if (!VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out store) ||
                store == null)
            {
                error = "Failed to attach VRMXT materials store for export.";
                return false;
            }
        }

        var materials = Materials ?? Array.Empty<VrmxtMaterialShaderRow>();
        var shaderRows = 0;
        for (var i = 0; i < materials.Length; i++)
        {
            var row = materials[i];
            if (row == null ||
                string.IsNullOrWhiteSpace(row.MaterialName) ||
                string.IsNullOrWhiteSpace(row.ShaderName))
            {
                continue;
            }

            if (!VrmxtMaterialsShaderAuthoring.TrySetShaderName(
                    store,
                    row.MaterialName,
                    row.ShaderName,
                    out var setError))
            {
                Debug.LogWarning("VRMXT: export shader upsert skipped: " + setError);
                continue;
            }

            if (row.GltfMaterialIndex >= 0)
            {
                var pair = FindStorePair(store, row.MaterialName);
                if (pair != null)
                {
                    pair.GltfMaterialIndex = row.GltfMaterialIndex;
                }
            }

            shaderRows++;
        }

        if (!StoreHasOverrideJson(store))
        {
            error = shaderRows == 0
                ? "No materials override entries to export. Set shaders and Apply first."
                : "Failed to write shader overrides into the store for export.";
            return false;
        }

        // Non-texture props from live Character mats; textures stay Warudo-owned.
        VrmxtMaterialsOverrideAuthoring.SyncPropertiesFromLiveMaterials(store, root);
        return true;
    }

    private static VrmxtMaterialsOverridePair FindStorePair(
        VrmxtMaterialsOverrideInstance store,
        string materialName)
    {
        if (store?.Pairs == null || string.IsNullOrEmpty(materialName))
        {
            return null;
        }

        var key = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(materialName);
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
            {
                continue;
            }

            var existing = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(pair.MaterialName);
            if (string.Equals(existing, key, StringComparison.Ordinal))
            {
                return pair;
            }
        }

        return null;
    }

    [Trigger]
    [Label("Apply Shader Overrides")]
    [Description("Write shader selections into the VRMXT store and re-apply on the Character.")]
    public void ApplyShaderOverrides()
    {
        ApplyShaderOverridesAsync().Forget();
    }

    [Trigger]
    [Label("Clear All Material Overrides")]
    [Description(
        "Empty VRMXT materials-override JSON and restore stock shaders (MToon snapshot from " +
        "before override apply). Does not rewrite the source .vrm file.")]
    public void ClearAllMaterialOverrides()
    {
        ClearAllMaterialOverridesAsync().Forget();
    }

    [Trigger]
    [Label("Export VRMXT Patch")]
    [Description("Patch current materials override JSON into a new copy of the Character's local VRM.")]
    public void ExportVrmxtPatch()
    {
        ExportAsync().Forget();
    }

    /// <summary>
    /// Soft reconcile: if this asset is a duplicate Character claim, clear Character.
    /// </summary>
    public void ReconcileDuplicateClaimsIfNeeded()
    {
        if (Character == null)
        {
            return;
        }

        var toClear = VrmxtCharacterOwnership.AssetsThatShouldClearDuplicateClaims(CollectSceneClaims());
        if (!toClear.Contains(Id))
        {
            return;
        }

        var name = Character.Name;
        ClearCharacterAssignment(
            "Cleared duplicate claim on '" + name + "' (another VRMXT Manager owns it).");
    }

    /// <summary>
    /// Scene lookup used by the plugin apply path. First matching asset wins.
    /// </summary>
    public static bool TryGetForCharacter(
        Scene scene,
        Guid characterId,
        out VrmxtManagerAsset asset)
    {
        asset = null;
        if (scene == null || characterId == Guid.Empty)
        {
            return false;
        }

        var assets = scene.GetAssets<VrmxtManagerAsset>();
        if (assets == null)
        {
            return false;
        }

        for (var i = 0; i < assets.Count; i++)
        {
            var candidate = assets[i];
            if (candidate == null || candidate.Character == null)
            {
                continue;
            }

            if (candidate.Character.Id == characterId)
            {
                asset = candidate;
                return true;
            }
        }

        return false;
    }

    public static void ReconcileAllDuplicateClaims(Scene scene)
    {
        if (scene == null)
        {
            return;
        }

        var assets = scene.GetAssets<VrmxtManagerAsset>();
        if (assets == null)
        {
            return;
        }

        for (var i = 0; i < assets.Count; i++)
        {
            assets[i]?.ReconcileDuplicateClaimsIfNeeded();
        }
    }

    private List<(Guid AssetId, Guid CharacterId)> CollectSceneClaims()
    {
        var result = new List<(Guid, Guid)>();
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            return result;
        }

        var assets = scene.GetAssets<VrmxtManagerAsset>();
        if (assets == null)
        {
            return result;
        }

        for (var i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            if (asset == null || asset.Character == null)
            {
                continue;
            }

            result.Add((asset.Id, asset.Character.Id));
        }

        return result;
    }

    private void ClearCharacterAssignment(string status)
    {
        _suppressCharacterWatch = true;
        try
        {
            SetDataInput(nameof(Character), null, broadcast: true);
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            SetStatus(status);
        }
        finally
        {
            _suppressCharacterWatch = false;
        }
    }

    private void RequestPluginApply(bool deferMaterialsOverride)
    {
        RequestPluginApplyAsync(deferMaterialsOverride).Forget();
    }

    private UniTask RequestPluginApplyAsync(bool deferMaterialsOverride)
    {
        if (Character == null || !Character.IsNonNullAndActive())
        {
            return UniTask.CompletedTask;
        }

        var plugin = VrmxtPlugin.ActiveInstance;
        if (plugin == null)
        {
            return UniTask.CompletedTask;
        }

        return plugin.RequestCharacterApplyAsync(Character, deferMaterialsOverride);
    }

    private async UniTaskVoid ApplyShaderOverridesAsync()
    {
        if (_applyInProgress)
        {
            SetStatus("Apply already in progress.");
            return;
        }

        _applyInProgress = true;
        try
        {
            if (!EnableMaterialsOverride)
            {
                SetStatus("Enable Materials Override first.");
                return;
            }

            if (Character == null || !Character.IsNonNullAndActive())
            {
                SetStatus("Select an active local Character.");
                return;
            }

            if (!VrmxtCharacterSource.TryGetPersistentRelativePath(Character.Source, out var relativePath))
            {
                SetStatus("Character Source is not a local character:// .vrm.");
                return;
            }

            if (!Context.PersistentDataManager.HasFile(relativePath))
            {
                SetStatus("Character file not found at '" + relativePath + "'.");
                return;
            }

            var root = VrmxtCharacterApply.TryFindCharacterRoot(Character);
            if (root == null)
            {
                SetStatus("Character root not found.");
                return;
            }

            var bytes = await Context.PersistentDataManager.ReadFileBytesAsync(relativePath);
            if (!GlbChunks.TryExtractJson(bytes, out var gltfJson) || string.IsNullOrEmpty(gltfJson))
            {
                SetStatus("Failed to extract glTF JSON for re-apply.");
                return;
            }

            // Stock VRMs have no store after plugin apply (cleared when no override JSON).
            // Authoring must re-attach an empty store before writing shader overrides.
            if (!VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store) ||
                store == null)
            {
                SetStatus("Failed to create VRMXT materials store on Character root.");
                return;
            }

            var materials = Materials ?? Array.Empty<VrmxtMaterialShaderRow>();
            var changed = 0;
            var errors = 0;
            for (var i = 0; i < materials.Length; i++)
            {
                var row = materials[i];
                if (row == null || string.IsNullOrWhiteSpace(row.MaterialName))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.ShaderName))
                {
                    continue;
                }

                if (!VrmxtMaterialsShaderAuthoring.TrySetShaderName(
                        store,
                        row.MaterialName,
                        row.ShaderName,
                        out var error))
                {
                    errors++;
                    Debug.LogWarning("VRMXT: shader apply skipped: " + error);
                    continue;
                }

                changed++;
            }

            if (changed == 0)
            {
                SetStatus(
                    "No shader rows applied (pick shaders first). storePairs=" +
                    store.Pairs.Count + " errors=" + errors + ".");
                return;
            }

            VrmxtMaterialsStockShaders.CaptureIfAbsent(root);

            Func<int, Texture> resolveTexture = index =>
                store.TryGetImportedTexture(index, out var texture) ? texture : null;
            var pipeline = VrmxtCharacterApply.DetectActivePipelineForWarudo();
            var applied = VrmxtMaterialsOverrideApplier.Apply(
                root,
                store,
                gltfJson,
                pipeline,
                resolveTexture);
            // Snapshot non-texture props from live Character mats into the store (textures
            // stay owned by Warudo Character Material Properties).
            var snapped = VrmxtMaterialsOverrideAuthoring.SyncPropertiesFromLiveMaterials(store, root);
            var catalog = VrmxtCharacterApply.RefreshMaterialPropertiesCatalog(Character, root, store);

            SetStatus(
                "Applied shaders: rows=" + changed + " live=" + applied +
                " snapped=" + snapped + " catalog=" + catalog +
                " errors=" + errors + " [" + Character.Name + "]");
        }
        catch (Exception e)
        {
            SetStatus("Apply failed: " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            _applyInProgress = false;
        }
    }

    private async UniTaskVoid ClearAllMaterialOverridesAsync()
    {
        if (_clearInProgress || _applyInProgress)
        {
            SetStatus("Busy — wait for Apply/Clear to finish.");
            return;
        }

        _clearInProgress = true;
        try
        {
            if (!EnableMaterialsOverride)
            {
                SetStatus("Enable Materials Override first.");
                return;
            }

            if (Character == null || !Character.IsNonNullAndActive())
            {
                SetStatus("Select an active local Character.");
                return;
            }

            if (!VrmxtCharacterSource.TryGetPersistentRelativePath(Character.Source, out var relativePath))
            {
                SetStatus("Character Source is not a local character:// .vrm.");
                return;
            }

            if (!Context.PersistentDataManager.HasFile(relativePath))
            {
                SetStatus("Character file not found at '" + relativePath + "'.");
                return;
            }

            var root = VrmxtCharacterApply.TryFindCharacterRoot(Character);
            if (root == null)
            {
                SetStatus("Character root not found.");
                return;
            }

            var store = root.GetComponent<VrmxtMaterialsOverrideInstance>();
            if (store == null)
            {
                var bytes = await Context.PersistentDataManager.ReadFileBytesAsync(relativePath);
                if (!GlbChunks.TryExtractJson(bytes, out var gltfJson) || string.IsNullOrEmpty(gltfJson))
                {
                    SetStatus("Failed to extract glTF JSON for clear.");
                    return;
                }

                if (!VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out store) ||
                    store == null)
                {
                    SetStatus("Failed to attach VRMXT materials store.");
                    return;
                }
            }

            var pairCount = store.Pairs != null ? store.Pairs.Count : 0;
            store.ClearOverrides();

            // Restore MToon/stock shaders snapped before first override apply (in-place mutate).
            var restored = VrmxtMaterialsStockShaders.Restore(root);
            var catalog = VrmxtCharacterApply.RefreshMaterialPropertiesCatalog(Character, root, store);
            RefreshMaterials();

            SetStatus(
                "Cleared override JSON + restored stock shaders: pairs=" + pairCount +
                " shadersRestored=" + restored + " catalog=" + catalog +
                " [" + Character.Name + "].");
        }
        catch (Exception e)
        {
            SetStatus("Clear failed: " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            _clearInProgress = false;
        }
    }

    private async UniTaskVoid ExportAsync()
    {
        if (_exportInProgress)
        {
            SetStatus("Export already in progress.");
            return;
        }

        _exportInProgress = true;
        try
        {
            if (!EnableMaterialsOverride)
            {
                SetStatus("Enable Materials Override first.");
                return;
            }

            if (Character == null || !Character.IsNonNullAndActive())
            {
                SetStatus("Select an active local Character.");
                return;
            }

            if (!VrmxtCharacterSource.TryGetPersistentRelativePath(Character.Source, out var sourcePath))
            {
                SetStatus("Character Source is not a local character:// .vrm.");
                return;
            }

            var paths = VrmxtPatchExport.TryBuildOutputPath(sourcePath, ExportFileSuffix);
            if (!paths.Success)
            {
                SetStatus("Export path error: " + paths.Error);
                return;
            }

            if (!Context.PersistentDataManager.HasFile(paths.SourceRelativePath))
            {
                SetStatus("Source file missing: '" + paths.SourceRelativePath + "'.");
                return;
            }

            var root = VrmxtCharacterApply.TryFindCharacterRoot(Character);
            if (root == null)
            {
                SetStatus("Character root not found.");
                return;
            }

            SetStatus("Exporting to '" + paths.OutputRelativePath + "'...");
            var sourceBytes = await Context.PersistentDataManager.ReadFileBytesAsync(paths.SourceRelativePath);
            if (!GlbChunks.TryExtractJson(sourceBytes, out var gltfJson) || string.IsNullOrEmpty(gltfJson))
            {
                SetStatus("Failed to extract glTF JSON from source.");
                return;
            }

            if (!TryPrepareStoreForExport(root, gltfJson, out var store, out var prepareError))
            {
                SetStatus(prepareError);
                return;
            }

            var entries = VrmxtPatchExport.CollectEntries(store, syncFromOverrideMaterials: true);
            if (entries.Count == 0)
            {
                SetStatus("No materials override entries to export. Apply shaders first.");
                return;
            }

            if (!VrmxtPatchExport.TryRebuildGlb(
                    sourceBytes,
                    entries,
                    out var outputBytes,
                    out var rewrite))
            {
                SetStatus("Export failed: " + (rewrite?.Error ?? "unknown error"));
                return;
            }

            await Context.PersistentDataManager.WriteFileBytesAsync(paths.OutputRelativePath, outputBytes);

            var skipPart = rewrite.Skipped.Count > 0
                ? " skipped=" + rewrite.Skipped.Count + " (" + string.Join("; ", rewrite.Skipped) + ")"
                : string.Empty;
            SetStatus(
                "Exported '" + paths.OutputRelativePath + "' written=" + rewrite.WrittenCount +
                skipPart + ".");
        }
        catch (Exception e)
        {
            SetStatus("Export failed: " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            _exportInProgress = false;
        }
    }

    private void SetStatus(string status)
    {
        SetDataInput(nameof(Status), status, broadcast: true);
        Debug.Log("VRMXT: " + status);
    }
}
