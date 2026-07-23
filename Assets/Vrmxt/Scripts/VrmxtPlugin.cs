using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UniVRMXT.MaterialsOverride;
using UniVRMXT.Vfx;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Core.Scenes;
using Warudo.Core.Serializations;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets.Character;

/// <summary>
/// VRMXT host plugin. Auto-attaches <c>VRMXT_sprite_particle</c> and
/// <c>VRMXT_materials_override</c> onto Character GameObjects after load.
/// </summary>
[PluginType(
    Id = "mira.vrmxt",
    Name = "VRMXT",
    Description = "VRMXT extensions for Warudo Characters (VFX + materials override)",
    Version = "0.1.10",
    Author = "Mira",
    SupportUrl = "https://github.com/miramocha/UniVRMXT",
    AssetTypes = new[] { typeof(VrmxtManagerAsset) }
)]
public sealed class VrmxtPlugin : Plugin
{
    /// <summary>
    /// Live plugin instance for Manager assets to request re-apply.
    /// </summary>
    public static VrmxtPlugin ActiveInstance { get; private set; }

    /// <summary>
    /// When off, VRMXT does not attach VFX / materials override on Characters.
    /// Turning off clears attached VFX immediately; reload the scene to restore
    /// stock materials (overrides mutate host materials in place).
    /// </summary>
    [DataInput]
    [Label("Enable VRMXT")]
    public bool EnableVrmxt = true;

    /// <summary>
    /// Wait after character load before writing materials override so Warudo can
    /// finish post-load material setup. Manual apply ignores this delay.
    /// </summary>
    [DataInput]
    [Label("Materials override defer (seconds)")]
    [IntegerSlider(0, 30)]
    [Description("Seconds to wait after load before applying materials override. 0 = immediate.")]
    public int MaterialsOverrideDeferSeconds = 2;

    [Markdown]
    public string EnableVrmxtHint =
        "Reload the scene after toggling to see material override changes. " +
        "VFX clears immediately when disabled.";

    [Markdown]
    [Label("Apply status")]
    public string ApplyStatus = "Idle.";

    [Trigger]
    [Label("Apply VRMXT override")]
    [Description(
        "Re-apply VRMXT VFX and materials override on all active Characters in the open scene. " +
        "Materials override runs immediately (no startup delay).")]
    public void ApplyVrmxtOverrideNow()
    {
        if (!EnableVrmxt)
        {
            SetApplyStatus("Enable VRMXT first.");
            return;
        }

        var scene = Context.OpenedScene;
        if (scene == null)
        {
            SetApplyStatus("No scene is open.");
            return;
        }

        var characters = scene.GetAssets<CharacterAsset>();
        var started = 0;
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            if (character == null || !character.IsNonNullAndActive())
            {
                continue;
            }

            if (!_bound.ContainsKey(character.Id))
            {
                BindCharacter(character);
            }

            if (!_bound.TryGetValue(character.Id, out var bound))
            {
                continue;
            }

            bound.ApplyGeneration++;
            var generation = bound.ApplyGeneration;
            bound.DisposeApply();
            ApplyAsync(character.Id, character, generation, deferMaterialsOverride: false).Forget();
            started++;
        }

        if (started == 0)
        {
            SetApplyStatus("No active Characters found to apply.");
            return;
        }

        SetApplyStatus("Manual apply started for " + started + " Character(s).");
    }

    [Trigger]
    [Label("Dump materials debug")]
    [Description(
        "Log live post-override renderer shaders vs Character.MaterialProperties catalog " +
        "(detects Warudo UI stuck on VRM1/MToon props). Does not touch Character.Materials.")]
    public void DumpMaterialsDebugNow()
    {
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            SetApplyStatus("No scene is open.");
            return;
        }

        var characters = scene.GetAssets<CharacterAsset>();
        var dumped = 0;
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            if (character == null || !character.IsNonNullAndActive())
            {
                continue;
            }

            var root = VrmxtCharacterApply.TryFindCharacterRoot(character);
            if (root == null)
            {
                continue;
            }

            var store = root.GetComponent<VrmxtMaterialsOverrideInstance>();
            VrmxtCharacterApply.DumpMaterialsOverrideDebug(character, root, store);
            dumped++;
        }

        if (dumped == 0)
        {
            SetApplyStatus("No active Characters to dump.");
            return;
        }

        SetApplyStatus("Materials debug dumped for " + dumped + " Character(s). See console.");
    }

    private void SetApplyStatus(string status)
    {
        SetDataInput(nameof(ApplyStatus), status, broadcast: true);
        Debug.Log("VRMXT: " + status);
    }

    /// <summary>
    /// Mod-folder paths (Warudo handbook: load via <see cref="Plugin.ModHost"/>, not
    /// <c>Resources.Load</c> — Unity Resources cannot see uMod assets).
    /// </summary>
    public const string ParticleMaterialAssetPath =
        "Assets/Vrmxt/Resources/UniVRMXT/ParticlesUnlit.mat";

    public const string ParticleShaderAssetPath =
        "Assets/Vrmxt/Shaders/VrmxtParticlesUnlit.shader";

    public const string MaterialsOverrideBuiltinShaderAssetPath =
        "Assets/Vrmxt/Shaders/VrmxtTestOverrideBuiltin.shader";

    public const string MaterialsOverrideUrpShaderAssetPath =
        "Assets/Vrmxt/Shaders/VrmxtTestOverrideURP.shader";

    public const string MaterialsOverrideBuiltinMaterialAssetPath =
        "Assets/Vrmxt/Resources/UniVRMXT/VrmxtTestOverrideBuiltin.mat";

    public const string MaterialsOverrideUrpMaterialAssetPath =
        "Assets/Vrmxt/Resources/UniVRMXT/VrmxtTestOverrideURP.mat";

    private readonly Dictionary<Guid, BoundCharacter> _bound = new();
    private readonly Dictionary<string, Shader> _modShaders =
        new Dictionary<string, Shader>(StringComparer.Ordinal);
    private Material _particleMaterialTemplate;

    protected override void OnCreate()
    {
        base.OnCreate();
        ActiveInstance = this;
        BindPackagedParticleMaterial();
        WarmPackagedMaterialsOverrideShaders();
        BindMaterialsOverrideShaderResolve();
        VrmxtShaderInventory.ExtraNamesProvider = () => _modShaders.Keys;
        LogAvailableShaders("OnCreate");
        Watch<bool>(nameof(EnableVrmxt), OnEnableVrmxtChanged);
        if (EnableVrmxt && Context.OpenedScene != null)
        {
            VrmxtManagerAsset.ReconcileAllDuplicateClaims(Context.OpenedScene);
            BindAllCharacters(Context.OpenedScene);
        }
    }

    private void OnEnableVrmxtChanged(bool from, bool to)
    {
        if (to)
        {
            if (Context.OpenedScene != null)
            {
                VrmxtManagerAsset.ReconcileAllDuplicateClaims(Context.OpenedScene);
                BindAllCharacters(Context.OpenedScene);
            }

            return;
        }

        UnbindAll();
    }

    protected override void OnDestroy()
    {
        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }

        UnbindAll();
        ClearPackagedParticleMaterial();
        ClearMaterialsOverrideShaderResolve();
        VrmxtShaderInventory.ExtraNamesProvider = null;
        base.OnDestroy();
    }

    /// <summary>
    /// Re-apply VRMXT for one Character (Manager toggles / Character assign).
    /// </summary>
    public void RequestCharacterApply(CharacterAsset character, bool deferMaterialsOverride = false)
    {
        RequestCharacterApplyAsync(character, deferMaterialsOverride).Forget();
    }

    /// <summary>
    /// Awaitable re-apply so Manager can refresh the Materials list after the store exists.
    /// </summary>
    public UniTask RequestCharacterApplyAsync(
        CharacterAsset character,
        bool deferMaterialsOverride = false)
    {
        if (!EnableVrmxt || character == null)
        {
            return UniTask.CompletedTask;
        }

        if (!_bound.ContainsKey(character.Id))
        {
            BindCharacter(character);
        }

        if (!_bound.TryGetValue(character.Id, out var bound))
        {
            return UniTask.CompletedTask;
        }

        bound.ApplyGeneration++;
        var generation = bound.ApplyGeneration;
        bound.DisposeApply();
        return ApplyAsync(character.Id, character, generation, deferMaterialsOverride);
    }

    /// <summary>
    /// Load packaged particle mat/shader from the mod (handbook: Including Unity Assets +
    /// <c>ModHost.Assets.Load</c>). Prefer that transparent ShaderLab mat over host BIRP
    /// <c>Shader.Find</c> names that may lack alpha in Warudo.
    /// </summary>
    private void BindPackagedParticleMaterial()
    {
        ClearPackagedParticleMaterial();

        try
        {
            // Warm shader asset so the material's shader resolves inside the mod.
            var particleShader = ModHost.Assets.Load<Shader>(ParticleShaderAssetPath);
            RememberModShader(particleShader);
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: ModHost.Assets.Load shader failed: " + e.Message);
        }

        try
        {
            _particleMaterialTemplate = ModHost.Assets.Load<Material>(ParticleMaterialAssetPath);
            if (_particleMaterialTemplate != null && _particleMaterialTemplate.shader != null)
            {
                RememberModShader(_particleMaterialTemplate.shader);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: ModHost.Assets.Load material failed: " + e.Message);
            _particleMaterialTemplate = null;
        }

        if (_particleMaterialTemplate == null)
        {
            Debug.LogWarning(
                "VRMXT: packaged particle material missing at '" + ParticleMaterialAssetPath +
                "'. Rebuild mod with Assets/Vrmxt/Shaders + Resources. Falling back to Shader.Find.");
            return;
        }

        var template = _particleMaterialTemplate;
        VrmxtVfxParticleSystemMapper.PackagedMaterialProvider = () => template;
        VrmxtVfxParticleSystemMapper.PreferPackagedParticleMaterial = true;
    }

    /// <summary>
    /// Warm sample override shaders/mats via ModHost. uMod shaders load into memory but
    /// <c>Shader.Find</c> still returns null — Applier uses <see cref="BindMaterialsOverrideShaderResolve"/>.
    /// </summary>
    private void WarmPackagedMaterialsOverrideShaders()
    {
        RememberModShader(
            WarmModAsset<Shader>(MaterialsOverrideBuiltinShaderAssetPath, "materials override builtin shader"));
        RememberModShader(
            WarmModAsset<Shader>(MaterialsOverrideUrpShaderAssetPath, "materials override URP shader"));

        var builtinMat = WarmModAsset<Material>(
            MaterialsOverrideBuiltinMaterialAssetPath, "materials override builtin mat");
        if (builtinMat != null)
        {
            RememberModShader(builtinMat.shader);
        }

        var urpMat = WarmModAsset<Material>(
            MaterialsOverrideUrpMaterialAssetPath, "materials override URP mat");
        if (urpMat != null)
        {
            RememberModShader(urpMat.shader);
        }
    }

    private void BindMaterialsOverrideShaderResolve()
    {
        var cache = _modShaders;
        VrmxtMaterialsOverrideApplier.ShaderResolveProvider = name =>
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (cache.TryGetValue(name, out var shader) && shader != null)
            {
                return shader;
            }

            // Other mods (e.g. lilToon) warm via ModHost but never hit this cache.
            // uMod Shader.Find stays null for those — scan already-loaded Shader assets.
            shader = TryFindLoadedShader(name);
            if (shader != null)
            {
                cache[name] = shader;
                Debug.Log(
                    "VRMXT: ShaderResolveProvider cached loaded shader '" + name +
                    "' (Shader.Find was null; typical for cross-mod ModHost shaders).");
            }

            return shader;
        };
    }

    private void ClearMaterialsOverrideShaderResolve()
    {
        VrmxtMaterialsOverrideApplier.ShaderResolveProvider = null;
        _modShaders.Clear();
    }

    private void RememberModShader(Shader shader)
    {
        if (shader == null || string.IsNullOrEmpty(shader.name))
        {
            return;
        }

        _modShaders[shader.name] = shader;
    }

    /// <summary>
    /// Debug dump: ModHost cache + <see cref="Shader.Find"/> probes + loaded Shader assets.
    /// Use when materials override fails (esp. cross-mod lilToon / Poiyomi / SampleShader).
    /// </summary>
    private void LogAvailableShaders(string reason)
    {
        var findLilToon = Shader.Find("lilToon");
        var findLilCutout = Shader.Find("Hidden/lilToonCutout");
        var findPoiyomi = Shader.Find(".poiyomi/Poiyomi Toon");
        var findSample = Shader.Find("VRMXT/Samples/ExternalShaderPlugin");
        var findTestBuiltin = Shader.Find("VRMXT/Samples/TestOverrideBuiltin");
        var findTestUrp = Shader.Find("VRMXT/Samples/TestOverrideURP");

        Debug.Log(
            "VRMXT: shader inventory (" + reason + "): modCache=" + _modShaders.Count +
            " Shader.Find lilToon=" + (findLilToon != null ? "ok" : "null") +
            " Hidden/lilToonCutout=" + (findLilCutout != null ? "ok" : "null") +
            " .poiyomi/Poiyomi Toon=" + (findPoiyomi != null ? "ok" : "null") +
            " Samples/External=" + (findSample != null ? "ok" : "null") +
            " Samples/TestBuiltin=" + (findTestBuiltin != null ? "ok" : "null") +
            " Samples/TestURP=" + (findTestUrp != null ? "ok" : "null") + ".");

        if (_modShaders.Count > 0)
        {
            var cached = new List<string>(_modShaders.Keys);
            cached.Sort(StringComparer.Ordinal);
            Debug.Log("VRMXT: mod shader cache: " + string.Join(", ", cached));
        }

        Shader[] loaded;
        try
        {
            loaded = Resources.FindObjectsOfTypeAll<Shader>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: FindObjectsOfTypeAll<Shader> failed: " + e.Message);
            return;
        }

        if (loaded == null || loaded.Length == 0)
        {
            Debug.LogWarning("VRMXT: no Shader assets loaded in memory.");
            return;
        }

        var relevant = VrmxtShaderInventory.CollectRelevantShaderNames();
        // Collect already includes mod-cache extras; dump wants loaded-only count too.
        var loadedRelevant = 0;
        for (var i = 0; i < loaded.Length; i++)
        {
            var shader = loaded[i];
            if (shader != null && VrmxtShaderInventory.IsRelevantShaderName(shader.name))
            {
                loadedRelevant++;
            }
        }

        Debug.Log(
            "VRMXT: loaded shaders total=" + loaded.Length +
            " relevant(lil|VRMXT|MToon|Sample)=" + relevant.Count +
            " loadedRelevant=" + loadedRelevant +
            (relevant.Count > 0 ? ": " + string.Join(", ", relevant) : "."));
    }

    /// <summary>
    /// Match by shader name among assets already in memory (ModHost warm from any mod).
    /// </summary>
    private static Shader TryFindLoadedShader(string shaderName)
    {
        Shader[] loaded;
        try
        {
            loaded = Resources.FindObjectsOfTypeAll<Shader>();
        }
        catch
        {
            return null;
        }

        if (loaded == null)
        {
            return null;
        }

        for (var i = 0; i < loaded.Length; i++)
        {
            var shader = loaded[i];
            if (shader != null &&
                string.Equals(shader.name, shaderName, StringComparison.Ordinal))
            {
                return shader;
            }
        }

        return null;
    }

    private T WarmModAsset<T>(string assetPath, string label) where T : UnityEngine.Object
    {
        try
        {
            var asset = ModHost.Assets.Load<T>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning("VRMXT: ModHost.Assets.Load " + label + " null at '" + assetPath + "'.");
                return null;
            }

            return asset;
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: ModHost.Assets.Load " + label + " failed: " + e.Message);
            return null;
        }
    }

    private static void ClearPackagedParticleMaterial()
    {
        VrmxtVfxParticleSystemMapper.PackagedMaterialProvider = null;
        VrmxtVfxParticleSystemMapper.PreferPackagedParticleMaterial = false;
    }

    public override void OnSceneLoaded(Scene scene, SerializedScene serializedScene)
    {
        base.OnSceneLoaded(scene, serializedScene);
        // Re-dump after all mods OnCreate — lilToon may warm after VRMXT.
        LogAvailableShaders("OnSceneLoaded");
        UnbindAll();
        if (!EnableVrmxt)
        {
            return;
        }

        BindAllCharacters(scene);
    }

    public override void OnSceneUnloaded(Scene scene)
    {
        UnbindAll();
        base.OnSceneUnloaded(scene);
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        if (!EnableVrmxt)
        {
            if (_bound.Count > 0)
            {
                UnbindAll();
            }

            return;
        }

        ReconcileCharacters();
        PollActiveStateChanges();
    }

    /// <summary>
    /// Poll active; avoid <c>OnActiveStateChange</c> (UMod CS0012 on UnityEvent/CoreModule).
    /// </summary>
    private void PollActiveStateChanges()
    {
        foreach (var pair in _bound)
        {
            var bound = pair.Value;
            var character = bound.Character;
            if (character == null)
            {
                continue;
            }

            var active = character.IsNonNullAndActive();
            if (active == bound.WasActive)
            {
                continue;
            }

            bound.WasActive = active;
            OnCharacterChanged(pair.Key);
        }
    }

    private void BindAllCharacters(Scene scene)
    {
        if (scene == null)
        {
            return;
        }

        VrmxtManagerAsset.ReconcileAllDuplicateClaims(scene);

        foreach (var character in scene.GetAssets<CharacterAsset>())
        {
            BindCharacter(character);
        }
    }

    private void ReconcileCharacters()
    {
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            if (_bound.Count > 0)
            {
                UnbindAll();
            }

            return;
        }

        var live = scene.GetAssets<CharacterAsset>();
        var liveIds = new HashSet<Guid>();
        for (var i = 0; i < live.Count; i++)
        {
            var character = live[i];
            if (character == null)
            {
                continue;
            }

            liveIds.Add(character.Id);
            if (!_bound.ContainsKey(character.Id))
            {
                BindCharacter(character);
            }
        }

        if (_bound.Count == liveIds.Count)
        {
            return;
        }

        var stale = new List<Guid>();
        foreach (var id in _bound.Keys)
        {
            if (!liveIds.Contains(id))
            {
                stale.Add(id);
            }
        }

        for (var i = 0; i < stale.Count; i++)
        {
            UnbindCharacter(stale[i]);
        }
    }

    private void BindCharacter(CharacterAsset character)
    {
        if (!EnableVrmxt || character == null || _bound.ContainsKey(character.Id))
        {
            return;
        }

        var bound = new BoundCharacter(character);
        bound.SourceWatchHandle = Watch(character, "Source", () => OnCharacterChanged(character.Id));
        bound.WasActive = character.IsNonNullAndActive();
        _bound[character.Id] = bound;

        OnCharacterChanged(character.Id);
    }

    private void UnbindCharacter(Guid characterId)
    {
        if (!_bound.TryGetValue(characterId, out var bound))
        {
            return;
        }

        _bound.Remove(characterId);

        if (bound.Character != null)
        {
            var root = VrmxtCharacterApply.TryFindCharacterRoot(bound.Character);
            VrmxtMaterialsStockShaders.Forget(root);

            if (bound.SourceWatchHandle != Guid.Empty)
            {
                Unwatch(bound.Character, bound.SourceWatchHandle);
            }
        }

        bound.DisposeApply();
    }

    private void UnbindAll()
    {
        var ids = new List<Guid>(_bound.Keys);
        for (var i = 0; i < ids.Count; i++)
        {
            UnbindCharacter(ids[i]);
        }
    }

    private void OnCharacterChanged(Guid characterId)
    {
        if (!_bound.TryGetValue(characterId, out var bound))
        {
            return;
        }

        bound.ApplyGeneration++;
        var generation = bound.ApplyGeneration;
        bound.DisposeApply();

        var character = bound.Character;
        if (character == null || !character.IsNonNullAndActive())
        {
            return;
        }

        // Source changed — drop prior stock snapshot so CaptureIfAbsent re-clones
        // fresh MToon (and so recycled instance IDs cannot restore the wrong avatar).
        var root = VrmxtCharacterApply.TryFindCharacterRoot(character);
        VrmxtMaterialsStockShaders.Forget(root);

        ApplyAsync(characterId, character, generation, deferMaterialsOverride: true).Forget();
    }

    private async UniTask ApplyAsync(
        Guid characterId,
        CharacterAsset character,
        int generation,
        bool deferMaterialsOverride)
    {
        try
        {
            if (!VrmxtCharacterSource.TryGetPersistentRelativePath(character.Source, out var relativePath))
            {
                Debug.Log(
                    "VRMXT: skip Character '" + character.Name +
                    "' — Source not a local character:// .vrm (Source='" + character.Source + "').");
                return;
            }

            if (!Context.PersistentDataManager.HasFile(relativePath))
            {
                Debug.Log($"VRMXT: Character file not found at '{relativePath}'.");
                return;
            }

            Debug.Log("VRMXT: loading '" + relativePath + "' for Character '" + character.Name + "'.");

            var bytes = await Context.PersistentDataManager.ReadFileBytesAsync(relativePath);
            if (!_bound.TryGetValue(characterId, out var bound) ||
                bound.Character != character ||
                bound.ApplyGeneration != generation)
            {
                return;
            }

            if (!character.IsNonNullAndActive())
            {
                return;
            }

            // Character mesh may appear a few frames after active; retry root find briefly.
            VrmxtCharacterApply.Result applyResult = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (!_bound.TryGetValue(characterId, out bound) ||
                    bound.Character != character ||
                    bound.ApplyGeneration != generation)
                {
                    return;
                }

                if (!character.IsNonNullAndActive())
                {
                    return;
                }

                ResolveFeatureFlags(characterId, out var applySprite, out var applyMats);
                applyResult = VrmxtCharacterApply.Apply(
                    character,
                    bytes,
                    deferMaterialsOverrideApply: deferMaterialsOverride && applyMats,
                    applySpriteParticle: applySprite,
                    applyMaterialsOverride: applyMats);
                if (applyResult != null)
                {
                    break;
                }

                if (VrmxtCharacterApply.TryFindCharacterRoot(character) != null)
                {
                    // Root exists but attach failed or features cleared — do not spin.
                    break;
                }

                await UniTask.Delay(50);
            }

            if (!_bound.TryGetValue(characterId, out bound) ||
                bound.Character != character ||
                bound.ApplyGeneration != generation)
            {
                applyResult?.Dispose();
                return;
            }

            if (deferMaterialsOverride && applyResult?.MaterialsOverride != null)
            {
                var delayMs = Mathf.Max(0, MaterialsOverrideDeferSeconds) * 1000;
                if (delayMs > 0)
                {
                    var delaySeconds = delayMs / 1000f;
                    SetApplyStatus(
                        "Delayed materials override starting (" + delaySeconds +
                        "s)... [" + character.Name + "]");

                    await UniTask.Delay(delayMs);

                    if (!_bound.TryGetValue(characterId, out bound) ||
                        bound.Character != character ||
                        bound.ApplyGeneration != generation)
                    {
                        applyResult.Dispose();
                        return;
                    }

                    if (!character.IsNonNullAndActive())
                    {
                        applyResult.Dispose();
                        return;
                    }
                }

                SetApplyStatus("Applying materials override... [" + character.Name + "]");

                var applied = VrmxtCharacterApply.ApplyMaterialsOverride(
                    character,
                    bytes,
                    applyResult);
                SetApplyStatus(
                    "Deferred materials override done on '" + character.Name +
                    "' (applied=" + applied + ", defer=" + MaterialsOverrideDeferSeconds + "s).");
            }
            else if (!deferMaterialsOverride && applyResult?.MaterialsOverride != null)
            {
                SetApplyStatus("Applied materials override. [" + character.Name + "]");
            }

            if (!_bound.TryGetValue(characterId, out bound) ||
                bound.Character != character ||
                bound.ApplyGeneration != generation)
            {
                applyResult?.Dispose();
                return;
            }

            bound.DisposeApply();
            bound.ApplyResult = applyResult;
        }
        catch (Exception e)
        {
            SetApplyStatus("Failed to apply on '" + character.Name + "': " + e.Message);
            Log.UserError("VRMXT: failed to apply extensions on Character " + character.Name, e);
        }
    }

    private static void ResolveFeatureFlags(
        Guid characterId,
        out bool applySpriteParticle,
        out bool applyMaterialsOverride)
    {
        applySpriteParticle = true;
        applyMaterialsOverride = true;

        var scene = Context.OpenedScene;
        if (scene == null)
        {
            return;
        }

        if (!VrmxtManagerAsset.TryGetForCharacter(scene, characterId, out var settings) ||
            settings == null)
        {
            return;
        }

        applySpriteParticle = settings.EnableSpriteParticle;
        applyMaterialsOverride = settings.EnableMaterialsOverride;
    }

    private sealed class BoundCharacter
    {
        public readonly CharacterAsset Character;
        public Guid SourceWatchHandle;
        public bool WasActive;
        public VrmxtCharacterApply.Result ApplyResult;
        public int ApplyGeneration;

        public BoundCharacter(CharacterAsset character)
        {
            Character = character;
        }

        public void DisposeApply()
        {
            if (ApplyResult == null)
            {
                return;
            }

            ApplyResult.Dispose();
            ApplyResult = null;
        }
    }
}
