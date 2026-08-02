using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
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
    Singleton = false
)]
public sealed class VrmxtManagerAsset : Asset
{
    [DataInput]
    [Label("Character")]
    [AssetFilter(nameof(FilterLocalCharacter))]
    public CharacterAsset Character;

    [DataInput]
    [Label("Enable sprite particle")]
    [Description("Apply VRMXT_sprite_particle on this Character. Off clears live VFX.")]
    [HiddenIf(nameof(HideVrmxtControls))]
    public bool EnableSpriteParticle = true;

    [DataInput]
    [Label("Enable materials override")]
    [Description(
        "Apply VRMXT_materials_override on this Character. Off restores stock shaders. "
            + "Required for Apply / Transfer / Clear / Export below."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public bool EnableMaterialsOverride = true;

    [Markdown]
    [HiddenIf(nameof(HideVrmxtControls))]
    public string Hint =
        "Add one VRMXT Manager per Character. Feature toggles default on. "
        + "Without a Manager, the plugin still applies both features. "
        + "Material templates: copy Unity `.mat` YAML into StreamingAssets/"
        + "VRMXT/MaterialTemplates/, set Shader + Apply shader overrides, set Material "
        + "template, then Transfer (floats/colors/keywords only — not shader or textures). "
        + "Export patches `VRMXT_materials_override` into a copy of the original local VRM "
        + "(geometry/BIN unchanged). Does not capture live mesh or VFX edits.";

    [DataInput]
    [Label("Materials")]
    [Description(
        "Filled from the Character. Use Refresh after load. Pick shaders or Transfer from "
            + "templates, then Apply. Add/remove rows are ignored on Apply/Export — Refresh "
            + "rebuilds the list (template paths are preserved by material name)."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public VrmxtMaterialShaderRow[] Materials = Array.Empty<VrmxtMaterialShaderRow>();

    [DataInput]
    [Label("Export file suffix")]
    [Description("Inserted before .vrm. Default .vrmxt → Characters/Foo.vrmxt.vrm")]
    [HiddenIf(nameof(HideVrmxtControls))]
    public string ExportFileSuffix = VrmxtPatchExport.DefaultFileSuffix;

    [Markdown]
    [Label("Status")]
    public string Status = "Idle.";

    private bool _exportInProgress;
    private bool _applyInProgress;
    private bool _transferInProgress;
    private bool _clearInProgress;
    private bool _suppressCharacterWatch;
    private Guid _characterSourceWatch;

    protected override void OnCreate()
    {
        base.OnCreate();
        WatchAsset(nameof(Character), OnCharacterChanged);
        Watch<bool>(nameof(EnableSpriteParticle), OnFeatureToggleChanged);
        Watch<bool>(nameof(EnableMaterialsOverride), OnFeatureToggleChanged);
        SetActive(true);
        ReconcileDuplicateClaimsIfNeeded();
        RefreshCharacterSourceWatch();
    }

    /// <summary>True → hide feature / materials / export controls (not VRM1 or no Character).</summary>
    protected bool HideVrmxtControls() => !IsAssignedVrm1Character();

    /// <summary>
    /// VRM 1.0 Characters expose <see cref="CharacterAsset.Vrm10Instance"/>;
    /// VRM 0.x uses <see cref="CharacterAsset.VRMBlendShapeProxy"/> instead.
    /// </summary>
    private bool IsAssignedVrm1Character()
    {
        if (Character == null || !Character.IsNonNullAndActive())
        {
            return false;
        }

        if (Character.Vrm10Instance != null)
        {
            return true;
        }

        // Explicit VRM0 — keep controls hidden (also covers mid-load before either is set).
        return false;
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

        RefreshCharacterSourceWatch();

        if (
            Character != null
            && VrmxtCharacterOwnership.IsClaimedByOther(Character.Id, Id, CollectSceneClaims())
        )
        {
            var name = Character.Name;
            ClearCharacterAssignment(
                "Character '" + name + "' is already claimed by another VRMXT Manager."
            );
            return;
        }

        // Plugin apply first, then fill Materials list — avoid racing Refresh against
        // Apply which destroys the materials-override component at start.
        SyncCharacterAsync().Forget();
    }

    private void RefreshCharacterSourceWatch()
    {
        if (_characterSourceWatch != Guid.Empty)
        {
            Unwatch(_characterSourceWatch);
            _characterSourceWatch = Guid.Empty;
        }

        if (Character == null)
        {
            return;
        }

        _characterSourceWatch = Watch(Character, "Source", OnCharacterChanged);
    }

    private async UniTaskVoid SyncCharacterAsync()
    {
        if (Character == null || !Character.IsNonNullAndActive())
        {
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            SetStatus("Select a VRM 1.0 Character.");
            Broadcast();
            return;
        }

        // Wait for Character load / plugin apply so Vrm10Instance is available when VRM1.
        await RequestPluginApplyAsync(deferMaterialsOverride: false);

        if (!IsAssignedVrm1Character())
        {
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            SetStatus("'" + Character.Name + "' is not VRM 1.0.");
            Broadcast();
            return;
        }

        await RefreshMaterialsAsync(reApplyOverrides: false);
        Broadcast();
    }

    private void OnFeatureToggleChanged(bool from, bool to)
    {
        if (from == to || !IsAssignedVrm1Character())
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
    [Label("Refresh materials")]
    [Description(
        "Rebuild the material list from the Character VRM / store. Re-attaches file "
            + "overrides when the live store is empty, then re-applies if Materials Override is on."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public void RefreshMaterials()
    {
        RefreshMaterialsAsync(reApplyOverrides: true, reattachFromFile: true).Forget();
    }

    [Trigger]
    [Label("Dump materials debug")]
    [Description(
        "Log JSON vs live renderer vs remembered textures (same format as Unity Editor dump). "
            + "Compare to VRC project Dump Materials Debug output."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public void DumpMaterialsDebug()
    {
        if (!IsAssignedVrm1Character())
        {
            SetStatus(
                Character == null || !Character.IsNonNullAndActive()
                    ? "Select a VRM 1.0 Character."
                    : "'" + Character.Name + "' is not VRM 1.0."
            );
            return;
        }

        var root = VrmxtCharacterApply.TryFindCharacterRoot(Character);
        if (root == null)
        {
            SetStatus("Character GameObject not found.");
            return;
        }

        var store = root.GetComponent<VrmxtMaterialsOverrideInstance>();
        if (store == null)
        {
            SetStatus(
                "No VrmxtMaterialsOverrideInstance on '"
                    + Character.Name
                    + "'. Enable Materials Override / Apply first."
            );
            return;
        }

        // Same console format as UniVRMXT Editor dump — easy side-by-side compare.
        VrmxtMaterialsOverrideDebug.Dump(root, store);
        // Extra Warudo UI catalog mismatch lines.
        VrmxtCharacterApply.DumpMaterialsOverrideDebug(Character, root, store);
        SetStatus("Materials debug dumped for '" + Character.Name + "'. See console.");
    }

    private async UniTask RefreshMaterialsAsync(bool reApplyOverrides, bool reattachFromFile = true)
    {
        if (!IsAssignedVrm1Character())
        {
            SetStatus(
                Character == null || !Character.IsNonNullAndActive()
                    ? "Select a VRM 1.0 Character."
                    : "'" + Character.Name + "' is not VRM 1.0."
            );
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            Broadcast();
            return;
        }

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
        var attachedFromFile = false;
        var applied = 0;
        string gltfJsonForApply = null;

        // Recover ExtensionJson when load apply wiped/missed the store.
        // Skip after intentional Clear — empty store must not reload from source .vrm.
        if (reattachFromFile && !StoreHasOverrideJson(store))
        {
            if (
                !VrmxtCharacterSource.TryGetPersistentRelativePath(
                    Character.Source,
                    out var relativePath
                ) || !Context.PersistentDataManager.HasFile(relativePath)
            )
            {
                SetStatus("Character Source is not a readable local character:// .vrm.");
            }
            else
            {
                try
                {
                    var bytes = await Context.PersistentDataManager.ReadFileBytesAsync(
                        relativePath
                    );
                    if (
                        GlbChunks.TryExtractJson(bytes, out var gltfJson)
                        && !string.IsNullOrEmpty(gltfJson)
                        && VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(
                            root,
                            gltfJson,
                            out store
                        )
                        && store != null
                    )
                    {
                        gltfJsonForApply = gltfJson;
                        attachedFromFile = StoreHasOverrideJson(store);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("VRMXT: Refresh Materials re-attach failed: " + e.Message);
                }
            }
        }

        // Re-apply when Materials Override is on (covers late lilToon warm / prior applied=0).
        if (reApplyOverrides && EnableMaterialsOverride && StoreHasOverrideJson(store))
        {
            try
            {
                if (
                    string.IsNullOrEmpty(gltfJsonForApply)
                    && VrmxtCharacterSource.TryGetPersistentRelativePath(
                        Character.Source,
                        out var path
                    )
                    && Context.PersistentDataManager.HasFile(path)
                )
                {
                    var bytes = await Context.PersistentDataManager.ReadFileBytesAsync(path);
                    GlbChunks.TryExtractJson(bytes, out gltfJsonForApply);
                }

                if (!string.IsNullOrEmpty(gltfJsonForApply))
                {
                    VrmxtMaterialsStockShaders.CaptureIfAbsent(root);
                    Func<int, Texture> resolveTexture = index =>
                        store.TryGetImportedTexture(index, out var texture) ? texture : null;
                    // Explicit provider — uMod shaders stay null under Shader.Find.
                    var resolveShader = VrmxtMaterialsOverrideApplier.ShaderResolveProvider;
                    if (resolveShader == null)
                    {
                        Debug.LogWarning(
                            "VRMXT: Refresh re-apply — ShaderResolveProvider is null; " +
                            "uMod shaders may fail Shader.Find."
                        );
                    }

                    applied = VrmxtMaterialsOverrideApplier.Apply(
                        root,
                        store,
                        gltfJsonForApply,
                        VrmxtCharacterApply.DetectActivePipelineForWarudo(),
                        resolveTexture,
                        null,
                        resolveShader
                    );
                    if (applied > 0)
                    {
                        VrmxtCharacterApply.RefreshMaterialPropertiesCatalog(
                            Character,
                            root,
                            store
                        );
                    }
                    else
                    {
                        Debug.LogWarning(
                            "VRMXT: Refresh re-apply returned 0 (overrides=" +
                            CountOverrideJson(store) +
                            ", rememberedTextures=" +
                            (store.ImportedTextures?.Count ?? 0) +
                            ", resolveShader=" +
                            (resolveShader != null ? "set" : "null") +
                            "). Live mats may already be overridden; check shader resolve warnings."
                        );
                    }
                }
                else
                {
                    Debug.LogWarning(
                        "VRMXT: Refresh skipped re-apply — could not read glTF JSON from Character Source."
                    );
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("VRMXT: Refresh Materials re-apply failed: " + e.Message);
            }
        }

        if (store != null && (store.Pairs == null || store.Pairs.Count == 0))
        {
            store.PopulatePairsFromRenderers();
        }

        FillMaterialsFromStore(root, store);

        var overrideCount = CountOverrideJson(store);
        SetStatus(
            "Refreshed "
                + (Materials?.Length ?? 0)
                + " material(s); overrides="
                + overrideCount
                + " reAttached="
                + attachedFromFile
                + " applied="
                + applied
                + " ["
                + Character.Name
                + "]."
        );
    }

    private void FillMaterialsFromStore(GameObject root, VrmxtMaterialsOverrideInstance store)
    {
        var priorUi = CollectRowUiByMaterialName();
        var rows = VrmxtMaterialsShaderAuthoring.CollectMaterialRows(root, store);
        var structured = new VrmxtMaterialShaderRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var templatePath = string.Empty;
            var textureHandling = VrmxtMaterialsTemplateTransfer.TextureHandlingKeepPacked;
            if (!string.IsNullOrEmpty(row.MaterialName))
            {
                var key = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(row.MaterialName);
                if (priorUi.TryGetValue(key, out var prior))
                {
                    templatePath = prior.TemplateAssetPath;
                    textureHandling = prior.TextureHandling;
                }
            }

            structured[i] = StructuredData.Create<VrmxtMaterialShaderRow>(sd =>
            {
                sd.MaterialName = row.MaterialName;
                sd.ShaderName = row.ShaderName ?? string.Empty;
                sd.TemplateAssetPath = templatePath;
                sd.TextureHandling = textureHandling;
                sd.GltfMaterialIndex = row.GltfMaterialIndex;
            });
            // UMod often leaves StructuredData<T>.Parent null for top-level row types.
            structured[i].BindManager(this);
            // OnCreate/OnAssignedParent refresh TemplateTextureSlots; nudge if path set.
            if (!string.IsNullOrEmpty(templatePath))
            {
                structured[i].RefreshTemplateTextureSlots();
            }
        }

        SetDataInput(nameof(Materials), structured, broadcast: true);
    }

    private struct RowUiState
    {
        public string TemplateAssetPath;
        public string TextureHandling;
    }

    private Dictionary<string, RowUiState> CollectRowUiByMaterialName()
    {
        var map = new Dictionary<string, RowUiState>(StringComparer.Ordinal);
        var materials = Materials ?? Array.Empty<VrmxtMaterialShaderRow>();
        for (var i = 0; i < materials.Length; i++)
        {
            var row = materials[i];
            if (row == null || string.IsNullOrWhiteSpace(row.MaterialName))
            {
                continue;
            }

            var key = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(row.MaterialName);
            if (map.ContainsKey(key))
            {
                continue;
            }

            map[key] = new RowUiState
            {
                TemplateAssetPath = string.IsNullOrWhiteSpace(row.TemplateAssetPath)
                    ? string.Empty
                    : row.TemplateAssetPath.Trim(),
                TextureHandling = VrmxtMaterialsTemplateTransfer.NormalizeTextureHandling(
                    row.TextureHandling
                ),
            };
        }

        return map;
    }

    private static int CountOverrideJson(VrmxtMaterialsOverrideInstance store)
    {
        if (store?.Pairs == null)
        {
            return 0;
        }

        var n = 0;
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(store.Pairs[i]?.ExtensionJson))
            {
                n++;
            }
        }

        return n;
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
        out string error
    )
    {
        store = root != null ? root.GetComponent<VrmxtMaterialsOverrideInstance>() : null;
        error = null;

        if (!StoreHasOverrideJson(store))
        {
            // Seed store from source file (or empty renderer pairs). Do not call this when
            // live Apply already wrote ExtensionJson — SetPairs would wipe shaders.
            if (
                !VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out store)
                || store == null
            )
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
            if (row == null || string.IsNullOrWhiteSpace(row.MaterialName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.ShaderName))
            {
                if (
                    !VrmxtMaterialsShaderAuthoring.TrySetShaderName(
                        store,
                        row.MaterialName,
                        row.ShaderName,
                        out var setError
                    )
                )
                {
                    Debug.LogWarning("VRMXT: export shader upsert skipped: " + setError);
                }
                else
                {
                    shaderRows++;
                }
            }

            if (row.GltfMaterialIndex >= 0)
            {
                var pair = FindStorePair(store, row.MaterialName);
                if (pair != null)
                {
                    pair.GltfMaterialIndex = row.GltfMaterialIndex;
                }
            }
        }

        // Packed GLB textures from live, then YAML template values (keeps those textures).
        VrmxtMaterialsOverrideAuthoring.SyncPropertiesFromLiveMaterials(store, root);

        for (var i = 0; i < materials.Length; i++)
        {
            var row = materials[i];
            if (
                row == null
                || string.IsNullOrWhiteSpace(row.MaterialName)
                || string.IsNullOrWhiteSpace(row.TemplateAssetPath)
            )
            {
                continue;
            }

            if (
                VrmxtMaterialsTemplateTransfer.TryTransferValuesFromTemplatePath(
                    store,
                    row.MaterialName,
                    row.TemplateAssetPath,
                    row.TextureHandling,
                    root,
                    out var transferError
                )
            )
            {
                shaderRows++;
            }
            else
            {
                Debug.LogWarning("VRMXT: export template Transfer skipped: " + transferError);
            }
        }

        if (!StoreHasOverrideJson(store))
        {
            error =
                shaderRows == 0
                    ? "No materials override entries to export. Set shaders or Transfer templates first."
                    : "Failed to write shader overrides into the store for export.";
            return false;
        }

        return true;
    }

    private static VrmxtMaterialsOverridePair FindStorePair(
        VrmxtMaterialsOverrideInstance store,
        string materialName
    )
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

            var existing = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(
                pair.MaterialName
            );
            if (string.Equals(existing, key, StringComparison.Ordinal))
            {
                return pair;
            }
        }

        return null;
    }

    [Trigger]
    [Label("Apply shader overrides")]
    [Description(
        "Write shader selections into the VRMXT store (keeps packed texture properties) "
            + "and re-apply on the Character."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public void ApplyShaderOverrides()
    {
        ApplyShaderOverridesAsync().Forget();
    }

    [Trigger]
    [Label("Transfer from templates")]
    [Description(
        "Parse StreamingAssets .mat YAML templates (floats, colors, keywords), merge into "
            + "VRMXT_materials_override, then Apply. Does not change Shader. Per-row Texture "
            + "handling can keep packed maps, clear slots that are set, or clear all. "
            + "Set Shader and Apply shader overrides first."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public void TransferFromTemplates()
    {
        TransferFromTemplatesAsync(singleRow: null).Forget();
    }

    [Trigger]
    [Label("Open material templates folder")]
    [Description(
        "Open StreamingAssets/VRMXT/MaterialTemplates in the OS file browser "
            + "(drop Unity .mat YAML files here)."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public void OpenMaterialTemplatesFolder()
    {
#if UNITY_EDITOR
        var abs = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "VRMXT",
                "MaterialTemplates"
            )
        );
        if (!System.IO.Directory.Exists(abs))
        {
            System.IO.Directory.CreateDirectory(abs);
            UnityEditor.AssetDatabase.Refresh();
        }

        UnityEditor.EditorUtility.RevealInFinder(abs);
        SetStatus("Opened " + abs);
#else
        OpenWarudoMaterialTemplatesFolderAsync().Forget();
#endif
    }

    private async UniTaskVoid OpenWarudoMaterialTemplatesFolderAsync()
    {
        await UniTask.Yield();
        try
        {
            VrmxtMaterialsTemplateTransfer.EnsureTemplatesFolder();
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "VRMXT: could not ensure StreamingAssets/"
                    + VrmxtMaterialsTemplateTransfer.WarudoDataMaterialTemplatesRelative
                    + ": "
                    + e.Message
            );
        }

        var abs = VrmxtMaterialsTemplateTransfer.TryGetWarudoDataMaterialTemplatesAbsolutePath();
        if (string.IsNullOrEmpty(abs))
        {
            SetStatus("StreamingAssets path unavailable.");
            return;
        }

        VrmxtMaterialsTemplateTransfer.OpenAbsolutePathInOs(abs);
        SetStatus("Opened " + abs);
    }

    /// <summary>Per-row Transfer trigger from <see cref="VrmxtMaterialShaderRow"/>.</summary>
    public void TransferSingleTemplate(VrmxtMaterialShaderRow row)
    {
        if (row == null)
        {
            return;
        }

        TransferFromTemplatesAsync(row).Forget();
    }

    [Trigger]
    [Label("Clear all material overrides")]
    [Description(
        "Empty VRMXT materials-override JSON and restore stock shaders (MToon snapshot from "
            + "before override apply). Does not rewrite the source .vrm file."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
    public void ClearAllMaterialOverrides()
    {
        ClearAllMaterialOverridesAsync().Forget();
    }

    [Trigger]
    [Label("Export VRMXT patch")]
    [Description(
        "Patch current materials override JSON into a new copy of the Character's local VRM."
    )]
    [HiddenIf(nameof(HideVrmxtControls))]
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

        var toClear = VrmxtCharacterOwnership.AssetsThatShouldClearDuplicateClaims(
            CollectSceneClaims()
        );
        if (!toClear.Contains(Id))
        {
            return;
        }

        var name = Character.Name;
        ClearCharacterAssignment(
            "Cleared duplicate claim on '" + name + "' (another VRMXT Manager owns it)."
        );
    }

    /// <summary>
    /// Scene lookup used by the plugin apply path. First matching asset wins.
    /// </summary>
    public static bool TryGetForCharacter(
        Scene scene,
        Guid characterId,
        out VrmxtManagerAsset asset
    )
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
            if (_characterSourceWatch != Guid.Empty)
            {
                Unwatch(_characterSourceWatch);
                _characterSourceWatch = Guid.Empty;
            }

            SetDataInput(nameof(Character), null, broadcast: true);
            SetDataInput(nameof(Materials), Array.Empty<VrmxtMaterialShaderRow>(), broadcast: true);
            SetStatus(status);
            Broadcast();
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
        if (_applyInProgress || _transferInProgress || _clearInProgress)
        {
            SetStatus("Busy — wait for Apply/Transfer/Clear to finish.");
            return;
        }

        _applyInProgress = true;
        try
        {
            if (!IsAssignedVrm1Character())
            {
                SetStatus(
                    Character != null
                        ? "'" + Character.Name + "' is not VRM 1.0."
                        : "Select a VRM 1.0 Character."
                );
                Broadcast();
                return;
            }

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

            if (
                !VrmxtCharacterSource.TryGetPersistentRelativePath(
                    Character.Source,
                    out var relativePath
                )
            )
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
            if (
                !GlbChunks.TryExtractJson(bytes, out var gltfJson) || string.IsNullOrEmpty(gltfJson)
            )
            {
                SetStatus("Failed to extract glTF JSON for re-apply.");
                return;
            }

            // Stock VRMs have no store after plugin apply (cleared when no override JSON).
            // Authoring must re-attach an empty store before writing shader overrides.
            if (
                !VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store)
                || store == null
            )
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

                if (
                    !VrmxtMaterialsShaderAuthoring.TrySetShaderName(
                        store,
                        row.MaterialName,
                        row.ShaderName,
                        out var error
                    )
                )
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
                    "No shader rows applied (pick shaders first). storePairs="
                        + store.Pairs.Count
                        + " errors="
                        + errors
                        + "."
                );
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
                resolveTexture,
                null,
                VrmxtMaterialsOverrideApplier.ShaderResolveProvider
            );
            // Snapshot live props; keep packed GLB textures, omit new unpackaged maps.
            var snapped = VrmxtMaterialsOverrideAuthoring.SyncPropertiesFromLiveMaterials(
                store,
                root
            );
            var catalog = VrmxtCharacterApply.RefreshMaterialPropertiesCatalog(
                Character,
                root,
                store
            );

            SetStatus(
                "Applied shaders: rows="
                    + changed
                    + " live="
                    + applied
                    + " snapped="
                    + snapped
                    + " catalog="
                    + catalog
                    + " errors="
                    + errors
                    + " ["
                    + Character.Name
                    + "]"
            );
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

    private async UniTaskVoid TransferFromTemplatesAsync(VrmxtMaterialShaderRow singleRow)
    {
        if (_transferInProgress || _applyInProgress || _clearInProgress)
        {
            SetStatus("Busy — wait for Apply/Transfer/Clear to finish.");
            return;
        }

        _transferInProgress = true;
        try
        {
            if (!IsAssignedVrm1Character())
            {
                SetStatus(
                    Character != null
                        ? "'" + Character.Name + "' is not VRM 1.0."
                        : "Select a VRM 1.0 Character."
                );
                Broadcast();
                return;
            }

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

            var plugin = VrmxtPlugin.ActiveInstance;
            if (plugin == null)
            {
                SetStatus("VRMXT plugin is not active.");
                return;
            }

            if (
                !VrmxtCharacterSource.TryGetPersistentRelativePath(
                    Character.Source,
                    out var relativePath
                )
            )
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
            if (
                !GlbChunks.TryExtractJson(bytes, out var gltfJson) || string.IsNullOrEmpty(gltfJson)
            )
            {
                SetStatus("Failed to extract glTF JSON for Transfer.");
                return;
            }

            if (
                !VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store)
                || store == null
            )
            {
                SetStatus("Failed to create VRMXT materials store on Character root.");
                return;
            }

            // File attach may omit SourceMaterial; Apply needs it for restore + name match.
            store.RefreshSourceMaterials();

            var materials =
                singleRow != null
                    ? new[] { singleRow }
                    : Materials ?? Array.Empty<VrmxtMaterialShaderRow>();
            var transferred = 0;
            var skipped = 0;
            var errors = 0;
            for (var i = 0; i < materials.Length; i++)
            {
                var row = materials[i];
                if (row == null || string.IsNullOrWhiteSpace(row.MaterialName))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.TemplateAssetPath))
                {
                    skipped++;
                    continue;
                }

                // Values-only Transfer needs an active non-MToon unity slot.
                if (!string.IsNullOrWhiteSpace(row.ShaderName))
                {
                    if (
                        !VrmxtMaterialsShaderAuthoring.TrySetShaderName(
                            store,
                            row.MaterialName,
                            row.ShaderName,
                            out var setError
                        )
                    )
                    {
                        errors++;
                        Debug.LogWarning("VRMXT: template Transfer shader upsert: " + setError);
                        continue;
                    }
                }

                if (
                    !VrmxtMaterialsTemplateTransfer.TryTransferValuesFromTemplatePath(
                        store,
                        row.MaterialName,
                        row.TemplateAssetPath,
                        row.TextureHandling,
                        root,
                        out var transferError
                    )
                )
                {
                    errors++;
                    Debug.LogWarning("VRMXT: template Transfer skipped: " + transferError);
                    continue;
                }

                transferred++;
            }

            if (transferred == 0)
            {
                SetStatus(
                    singleRow != null
                        ? "Transfer skipped. Set Material template on this row first. errors="
                            + errors
                            + "."
                        : "No templates transferred. Set Material template paths first. skipped="
                            + skipped
                            + " errors="
                            + errors
                            + "."
                );
                return;
            }

            VrmxtMaterialsStockShaders.CaptureIfAbsent(root);

            Func<int, Texture> resolveTexture = index =>
                store.TryGetImportedTexture(index, out var texture) ? texture : null;
            var pipeline = VrmxtCharacterApply.DetectActivePipelineForWarudo();
            var resolveShader = VrmxtMaterialsOverrideApplier.ShaderResolveProvider;
            if (resolveShader == null)
            {
                Debug.LogWarning(
                    "VRMXT: Transfer Apply — ShaderResolveProvider is null; "
                        + "mod shaders may not resolve (Shader.Find is null under UMod)."
                );
            }

            var applied = VrmxtMaterialsOverrideApplier.Apply(
                root,
                store,
                gltfJson,
                pipeline,
                resolveTexture,
                null,
                resolveShader
            );

            // Re-assert clear modes after Apply (Apply won't null slots without texture ownership).
            for (var i = 0; i < materials.Length; i++)
            {
                var row = materials[i];
                if (
                    row == null
                    || string.IsNullOrWhiteSpace(row.MaterialName)
                    || string.IsNullOrWhiteSpace(row.TemplateAssetPath)
                )
                {
                    continue;
                }

                var mode = VrmxtMaterialsTemplateTransfer.NormalizeTextureHandling(
                    row.TextureHandling
                );
                if (
                    string.Equals(
                        mode,
                        VrmxtMaterialsTemplateTransfer.TextureHandlingKeepPacked,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                foreach (
                    var live in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                        root,
                        row.MaterialName
                    )
                )
                {
                    VrmxtMaterialsTemplateTransfer.ClearLiveTextures(live, mode);
                }
            }

            // Snap live→JSON. Packed maps survive for Keep packed; cleared slots stay empty.
            var snapped = 0;
            if (applied > 0)
            {
                snapped = VrmxtMaterialsOverrideAuthoring.SyncPropertiesFromLiveMaterials(
                    store,
                    root,
                    includePackedTextures: true
                );
            }

            var catalog = VrmxtCharacterApply.RefreshMaterialPropertiesCatalog(
                Character,
                root,
                store
            );

            FillMaterialsFromStore(root, store);

            var scope =
                singleRow != null
                    ? "row '" + singleRow.MaterialName + "'"
                    : "all rows";
            SetStatus(
                "Transferred templates ("
                    + scope
                    + "): ok="
                    + transferred
                    + " live="
                    + applied
                    + " snapped="
                    + snapped
                    + " catalog="
                    + catalog
                    + " skipped="
                    + skipped
                    + " errors="
                    + errors
                    + " (YAML values; texture handling per row) ["
                    + Character.Name
                    + "]"
            );
            if (applied == 0)
            {
                Debug.LogWarning(
                    "VRMXT: Transfer wrote override JSON but Apply updated 0 materials. "
                        + "Check Status live=0 — shader unresolved or material name mismatch."
                );
            }
        }
        catch (Exception e)
        {
            SetStatus("Transfer failed: " + e.Message);
            Debug.LogException(e);
        }
        finally
        {
            _transferInProgress = false;
        }
    }

    private async UniTaskVoid ClearAllMaterialOverridesAsync()
    {
        if (_clearInProgress || _applyInProgress || _transferInProgress)
        {
            SetStatus("Busy — wait for Apply/Transfer/Clear to finish.");
            return;
        }

        _clearInProgress = true;
        try
        {
            if (!IsAssignedVrm1Character())
            {
                SetStatus(
                    Character != null
                        ? "'" + Character.Name + "' is not VRM 1.0."
                        : "Select a VRM 1.0 Character."
                );
                Broadcast();
                return;
            }

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

            if (
                !VrmxtCharacterSource.TryGetPersistentRelativePath(
                    Character.Source,
                    out var relativePath
                )
            )
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
                if (
                    !GlbChunks.TryExtractJson(bytes, out var gltfJson)
                    || string.IsNullOrEmpty(gltfJson)
                )
                {
                    SetStatus("Failed to extract glTF JSON for clear.");
                    return;
                }

                if (
                    !VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out store)
                    || store == null
                )
                {
                    SetStatus("Failed to attach VRMXT materials store.");
                    return;
                }
            }

            var pairCount = store.Pairs != null ? store.Pairs.Count : 0;
            store.ClearOverrides();

            // Restore MToon/stock shaders snapped before first override apply (in-place mutate).
            var restored = VrmxtMaterialsStockShaders.Restore(root);
            var catalog = VrmxtCharacterApply.RefreshMaterialPropertiesCatalog(
                Character,
                root,
                store
            );
            // Do not re-attach/re-apply from source .vrm — that undoes Clear on patched files.
            await RefreshMaterialsAsync(reApplyOverrides: false, reattachFromFile: false);

            SetStatus(
                "Cleared override JSON + restored stock shaders: pairs="
                    + pairCount
                    + " shadersRestored="
                    + restored
                    + " catalog="
                    + catalog
                    + " ["
                    + Character.Name
                    + "]."
            );
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
            if (!IsAssignedVrm1Character())
            {
                SetStatus(
                    Character != null
                        ? "'" + Character.Name + "' is not VRM 1.0."
                        : "Select a VRM 1.0 Character."
                );
                Broadcast();
                return;
            }

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

            if (
                !VrmxtCharacterSource.TryGetPersistentRelativePath(
                    Character.Source,
                    out var sourcePath
                )
            )
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
            var sourceBytes = await Context.PersistentDataManager.ReadFileBytesAsync(
                paths.SourceRelativePath
            );
            if (
                !GlbChunks.TryExtractJson(sourceBytes, out var gltfJson)
                || string.IsNullOrEmpty(gltfJson)
            )
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

            if (
                !VrmxtPatchExport.TryRebuildGlb(
                    sourceBytes,
                    entries,
                    out var outputBytes,
                    out var rewrite
                )
            )
            {
                SetStatus("Export failed: " + (rewrite?.Error ?? "unknown error"));
                return;
            }

            await Context.PersistentDataManager.WriteFileBytesAsync(
                paths.OutputRelativePath,
                outputBytes
            );

            var skipPart =
                rewrite.Skipped.Count > 0
                    ? " skipped="
                        + rewrite.Skipped.Count
                        + " ("
                        + string.Join("; ", rewrite.Skipped)
                        + ")"
                    : string.Empty;
            SetStatus(
                "Exported '"
                    + paths.OutputRelativePath
                    + "' written="
                    + rewrite.WrittenCount
                    + skipPart
                    + "."
            );
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
