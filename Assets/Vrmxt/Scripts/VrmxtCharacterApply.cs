using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UniVRMXT.Mtoonxt;
using UniVRMXT.Vfx;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets;
using Warudo.Plugins.Core.Assets.Character;
using Object = UnityEngine.Object;

/// <summary>
/// Post-load VRMXT applies on a Character GameObject: <c>VRMXT_sprite_particle</c>,
/// <c>VRMXT_materials_override</c>, and <c>VRMC_materials_mtoonxt</c>.
/// </summary>
public static class VrmxtCharacterApply
{
    public sealed class Result : IDisposable
    {
        public VrmxtVfxInstance VfxInstance;
        public VrmxtVfxGlbTextures VfxTextures;
        public VrmxtMaterialsOverrideInstance MaterialsOverride;

        public void Dispose()
        {
            if (VfxInstance != null)
            {
                VfxInstance.ClearParticleSystems();
                Object.Destroy(VfxInstance);
                VfxInstance = null;
            }

            if (VfxTextures != null)
            {
                VfxTextures.Dispose();
                VfxTextures = null;
            }

            if (MaterialsOverride != null)
            {
                ClearExistingMaterialsOverride(MaterialsOverride.gameObject);
                MaterialsOverride = null;
            }
        }
    }

    /// <summary>
    /// Apply VRMXT extensions from re-read GLB bytes onto the Character root.
    /// Returns null when nothing attached (no extension / resolve failure).
    /// Caller owns <see cref="Result"/> until dispose.
    /// When <paramref name="deferMaterialsOverrideApply"/> is true, VFX still applies
    /// immediately and the override store/textures are prepared, but shader/properties
    /// are left for <see cref="ApplyMaterialsOverride"/>.
    /// Feature flags skip attach and clear existing instances; materials off also
    /// restores stock shaders from <see cref="VrmxtMaterialsStockShaders"/>.
    /// </summary>
    public static Result Apply(
        CharacterAsset character,
        byte[] glbBytes,
        bool deferMaterialsOverrideApply = false,
        bool applySpriteParticle = true,
        bool applyMaterialsOverride = true
    )
    {
        if (character == null || !character.IsNonNullAndActive())
        {
            return null;
        }

        // UMod compile: do not read CharacterAsset.GameObject (CoreModule CS0012).
        var root = TryFindCharacterRoot(character);
        if (root == null)
        {
            Debug.Log("VRMXT: could not find GameObject for Character '" + character.Name + "'.");
            return null;
        }

        if (glbBytes == null || glbBytes.Length == 0)
        {
            Debug.Log("VRMXT: empty GLB bytes for Character '" + character.Name + "'.");
            return null;
        }

        ClearExistingVfx(root);
        ClearExistingMaterialsOverride(root);

        var result = new Result();
        var attachedAny = false;

        // VFX first while GlbTextures cache is live; materials then Remember + ReleaseOwnership
        // so Dispose does not Destroy textures still on particle mats / Instance.
        if (applySpriteParticle)
        {
            attachedAny |= TryApplyVfx(character, root, glbBytes, result);
        }

        if (applyMaterialsOverride)
        {
            attachedAny |= TryApplyMaterialsOverride(
                character,
                root,
                glbBytes,
                result,
                runApply: !deferMaterialsOverrideApply
            );
        }
        else
        {
            // Store already cleared; put MToon/stock shaders back without scene reload.
            VrmxtMaterialsStockShaders.Restore(root);
        }

        // MToonXT is a sibling extension. Run now when override Apply will not
        // (no store, or materials override disabled). Override Apply path runs
        // MToonXT after override so rule 14 holds.
        if (result.MaterialsOverride == null)
        {
            attachedAny |= ApplyMtoonxt(root, glbBytes, result) > 0;
        }

        if (!attachedAny)
        {
            result.Dispose();
            return null;
        }

        return result;
    }

    /// <summary>
    /// Run materials-override apply on a prepared <see cref="Result"/> (deferred host path).
    /// Returns the number of glTF materials that received an override.
    /// </summary>
    public static int ApplyMaterialsOverride(
        CharacterAsset character,
        byte[] glbBytes,
        Result result
    )
    {
        if (character == null || !character.IsNonNullAndActive() || result == null)
        {
            return 0;
        }

        var root = TryFindCharacterRoot(character);
        if (root == null)
        {
            return 0;
        }

        return RunMaterialsOverrideApply(character, root, glbBytes, result);
    }

    /// <summary>
    /// Swap stock MToon to the pipeline MToonXT shader when <c>VRMC_materials_mtoonxt</c> is valid
    /// and the shader UMod has warmed. Skips materials where materials-override would apply.
    /// </summary>
    public static int ApplyMtoonxt(GameObject root, string gltfJson)
    {
        if (root == null || string.IsNullOrEmpty(gltfJson))
        {
            return 0;
        }

        var applied = VrmcMaterialsMtoonxtApplier.Apply(
            root,
            gltfJson,
            VrmxtMaterialsOverrideApplier.ShaderResolveProvider
        );
        if (applied > 0)
        {
            Debug.Log(
                "VRMXT: MToonXT applied=" + applied + " root='" + root.name + "'."
            );
        }

        return applied;
    }

    /// <summary>
    /// Extract glTF JSON from <paramref name="result"/> textures or <paramref name="glbBytes"/>,
    /// then <see cref="ApplyMtoonxt(GameObject,string)"/>.
    /// </summary>
    public static int ApplyMtoonxt(GameObject root, byte[] glbBytes, Result result)
    {
        string gltfJson = result != null && result.VfxTextures != null ? result.VfxTextures.Json : null;
        if (string.IsNullOrEmpty(gltfJson))
        {
            if (glbBytes == null || !GlbChunks.TryExtractJson(glbBytes, out gltfJson))
            {
                return 0;
            }
        }

        return ApplyMtoonxt(root, gltfJson);
    }

    private static bool TryApplyVfx(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result
    )
    {
        var resolveNode = CreateNodeResolver(root, glbBytes);
        if (resolveNode == null)
        {
            Debug.Log(
                "VRMXT: could not resolve glTF nodes for Character '" + character.Name + "'."
            );
            return false;
        }

        if (
            !VrmxtVfxRuntime.TryAttachFromGlb(
                root,
                glbBytes,
                resolveNode,
                out var instance,
                out var textures
            )
        )
        {
            Debug.Log(
                "VRMXT: no VRMXT_sprite_particle attach on Character '"
                    + character.Name
                    + "' (missing extension, parse fail, or all emitters skipped)."
            );
            return false;
        }

        if (instance.Emitters == null || instance.Emitters.Count == 0)
        {
            Debug.Log(
                "VRMXT: VRMXT_sprite_particle present but 0 emitters resolved on '"
                    + character.Name
                    + "' (node name mismatch vs scene hierarchy?). Root='"
                    + root.name
                    + "'."
            );
            Object.Destroy(instance);
            textures?.Dispose();
            return false;
        }

        string gltfJson = textures != null ? textures.Json : null;
        if (string.IsNullOrEmpty(gltfJson))
        {
            GlbChunks.TryExtractJson(glbBytes, out gltfJson);
        }

        // Warudo normalize zeros bone locals; restore glTF rest frame so +Y emit matches UniVRM/Blender.
        if (!string.IsNullOrEmpty(gltfJson))
        {
            VrmxtWarudoBoneAxisCorrection.Apply(instance, gltfJson);
        }

        var particleCount = instance.ParticleSystems != null ? instance.ParticleSystems.Count : 0;
        Debug.Log(
            "VRMXT: attached VFX on Character '"
                + character.Name
                + "' root='"
                + root.name
                + "' emitters="
                + instance.Emitters.Count
                + " particles="
                + particleCount
                + "."
        );

        result.VfxInstance = instance;
        result.VfxTextures = textures;
        return true;
    }

    private static bool TryApplyMaterialsOverride(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result,
        bool runApply
    )
    {
        string gltfJson = result.VfxTextures != null ? result.VfxTextures.Json : null;
        if (string.IsNullOrEmpty(gltfJson) && !GlbChunks.TryExtractJson(glbBytes, out gltfJson))
        {
            return false;
        }

        if (
            !VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store)
            || store == null
        )
        {
            return false;
        }

        // Always clear before pairing with this file's JSON (stale indices → wrong tex).
        store.ClearImportedTextures();

        VrmxtVfxGlbTextures ownedTextures = null;
        var glbTextures = result.VfxTextures;
        if (glbTextures == null && VrmxtVfxGlbTextures.TryCreate(glbBytes, out ownedTextures))
        {
            glbTextures = ownedTextures;
            result.VfxTextures = ownedTextures;
        }

        Func<int, Texture> resolveTexture = null;
        if (glbTextures != null)
        {
            // Decode into Instance first. Apply must resolve from Instance after
            // ReleaseOwnership — never Apply via GlbTextures then Dispose those refs.
            store.RememberTexturesFromPairs(glbTextures.AsResolver(), gltfJson);
            glbTextures.ReleaseOwnership();
            resolveTexture = index =>
                store.TryGetImportedTexture(index, out var texture) ? texture : null;
        }

        var hasOverrideJson = HasOverrideJson(store);
        if (!hasOverrideJson)
        {
            ClearExistingMaterialsOverride(root);
            return false;
        }

        result.MaterialsOverride = store;

        if (!runApply)
        {
            // Snapshot stock (MToon) before deferred apply mutates in place.
            VrmxtMaterialsStockShaders.CaptureIfAbsent(root);
            Debug.Log(
                "VRMXT: materials override prepared (deferred apply) on Character '"
                    + character.Name
                    + "' root='"
                    + root.name
                    + "'."
            );
            return true;
        }

        var applied = RunMaterialsOverrideApply(
            character,
            root,
            glbBytes,
            result,
            gltfJson,
            store,
            resolveTexture
        );
        // Keep store even when applied==0 (missing shader / name miss). Wiping it
        // dropped ExtensionJson so Manager Refresh only showed stock MToon.
        if (applied == 0)
        {
            Debug.LogWarning(
                "VRMXT: materials override store kept on '"
                    + character.Name
                    + "' but 0 slots applied — check shader inventory / material names."
            );
        }

        return true;
    }

    private static int RunMaterialsOverrideApply(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result
    )
    {
        var store = result.MaterialsOverride;
        if (store == null)
        {
            store = root.GetComponent<VrmxtMaterialsOverrideInstance>();
            if (store == null)
            {
                return 0;
            }

            result.MaterialsOverride = store;
        }

        string gltfJson = result.VfxTextures != null ? result.VfxTextures.Json : null;
        if (string.IsNullOrEmpty(gltfJson) && !GlbChunks.TryExtractJson(glbBytes, out gltfJson))
        {
            return 0;
        }

        Func<int, Texture> resolveTexture = index =>
            store.TryGetImportedTexture(index, out var texture) ? texture : null;

        return RunMaterialsOverrideApply(
            character,
            root,
            glbBytes,
            result,
            gltfJson,
            store,
            resolveTexture
        );
    }

    private static int RunMaterialsOverrideApply(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result,
        string gltfJson,
        VrmxtMaterialsOverrideInstance store,
        Func<int, Texture> resolveTexture
    )
    {
        if (store == null || string.IsNullOrEmpty(gltfJson))
        {
            return 0;
        }

        // Capture MToon / stock shaders before in-place override mutate.
        VrmxtMaterialsStockShaders.CaptureIfAbsent(root);

        var pipeline = DetectActivePipelineForWarudo();
        var applied = VrmxtMaterialsOverrideApplier.Apply(
            root,
            store,
            gltfJson,
            pipeline,
            resolveTexture,
            null,
            VrmxtMaterialsOverrideApplier.ShaderResolveProvider
        );

        if (applied > 0)
        {
            var catalogRefreshed = RefreshMaterialPropertiesCatalog(character, root, store);
            Debug.Log(
                "VRMXT: materials override on Character '"
                    + character.Name
                    + "' root='"
                    + root.name
                    + "' applied="
                    + applied
                    + " catalogRefreshed="
                    + catalogRefreshed
                    + " pipeline="
                    + pipeline
                    + "."
            );
        }
        else
        {
            var wanted = CollectWantedUnityShaderNames(store);
            Debug.LogWarning(
                "VRMXT: materials override attached on '"
                    + character.Name
                    + "' but 0 unity slots applied (missing variant/shader or stock-only)."
                    + " pipeline="
                    + pipeline
                    + " wantedShaders=["
                    + string.Join(", ", wanted)
                    + "]."
                    + " Check console for 'VRMXT: shader inventory' and lilToon warm logs."
            );
        }

        // Always dump visibility health after apply (Poiyomi missing _MainTex / black
        // _Color / cleared stock maps are the usual "invisible override" causes).
        LogMaterialsOverrideApplyHealth(character, root, store, applied, pipeline);

        ApplyMtoonxt(root, gltfJson);

        return applied;
    }

    /// <summary>
    /// Compact post-apply visibility check. Logs one line per override pair plus a
    /// summary warning when albedo/main map is missing or tint is near-black.
    /// </summary>
    private static void LogMaterialsOverrideApplyHealth(
        CharacterAsset character,
        GameObject root,
        VrmxtMaterialsOverrideInstance store,
        int applied,
        RenderPipelineVariant pipeline
    )
    {
        if (store?.Pairs == null || root == null)
        {
            return;
        }

        var riskCount = 0;
        var remembered =
            store.ImportedTextures != null ? store.ImportedTextures.Count : 0;
        Debug.Log(
            "VRMXT: materials override health '"
                + (character != null ? character.Name : "?")
                + "' applied="
                + applied
                + " pipeline="
                + pipeline
                + " rememberedTextures="
                + remembered
                + "."
        );

        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (
                pair == null
                || string.IsNullOrEmpty(pair.MaterialName)
                || string.IsNullOrEmpty(pair.ExtensionJson)
            )
            {
                continue;
            }

            SummarizeOverrideJson(
                pair.ExtensionJson,
                out var shaderId,
                out var variant,
                out var propCount,
                out var texPropCount,
                out var bindingCount,
                out var jsonHasMainTex,
                out var jsonColor,
                out var texNames
            );

            var foundLive = false;
            foreach (
                var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                    root,
                    pair.MaterialName
                )
            )
            {
                if (material == null)
                {
                    continue;
                }

                foundLive = true;
                DescribeLiveMaterialHealth(
                    material,
                    out var liveShader,
                    out var mainTexName,
                    out var mainTex,
                    out var color,
                    out var risks
                );

                var unresolved = CountUnresolvedOverrideTextures(pair, store);
                Debug.Log(
                    "VRMXT: override health store='"
                        + pair.MaterialName
                        + "' jsonShader='"
                        + shaderId
                        + "' variant="
                        + variant
                        + " props="
                        + propCount
                        + " texProps="
                        + texPropCount
                        + " bindings="
                        + bindingCount
                        + " jsonHasMainTex="
                        + jsonHasMainTex
                        + " jsonColor="
                        + FormatColor(jsonColor)
                        + " texNames=["
                        + string.Join(",", texNames)
                        + "] liveShader='"
                        + liveShader
                        + "' liveMain="
                        + (mainTex != null ? mainTexName + "='" + mainTex.name + "'" : mainTexName + "=null")
                        + " liveColor="
                        + FormatColor(color)
                        + " unresolvedTex="
                        + unresolved
                        + (risks.Count > 0 ? " RISKS=[" + string.Join(", ", risks) + "]" : "")
                        + "."
                );

                if (risks.Count > 0)
                {
                    riskCount++;
                }
            }

            if (!foundLive)
            {
                riskCount++;
                Debug.LogWarning(
                    "VRMXT: override health store='"
                        + pair.MaterialName
                        + "' live=(none) — name mismatch vs renderers?"
                );
            }
        }

        if (riskCount > 0)
        {
            Debug.LogWarning(
                "VRMXT: materials override visibility risks on '"
                    + (character != null ? character.Name : "?")
                    + "' ("
                    + riskCount
                    + " pair(s)). Common: TestPoi/_override mat has null _MainTex but "
                    + "ships default LUT textures → ClearUnlisted wipes stock MToon "
                    + "_MainTex; black _Color; shader unresolved. Use Dump materials debug."
            );
        }
    }

    /// <summary>
    /// Rebuild <see cref="CharacterAsset.MaterialProperties"/> for overridden mats from
    /// the live shader (Warudo UI catalog). Match by material name with
    /// <c> (Instance)</c> strip — same join as apply. UMod-safe: mutates the existing
    /// <c>Dictionary&lt;string, List&lt;ShaderProperty&gt;&gt;</c> only; does not touch
    /// <see cref="CharacterAsset.Materials"/>.
    /// </summary>
    /// <returns>Number of catalog keys rewritten.</returns>
    public static int RefreshMaterialPropertiesCatalog(
        CharacterAsset character,
        GameObject root,
        VrmxtMaterialsOverrideInstance store
    )
    {
        if (character == null || root == null || store?.Pairs == null)
        {
            return 0;
        }

        var catalog = character.MaterialProperties;
        if (catalog == null)
        {
            return 0;
        }

        var refreshed = 0;
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (
                pair == null
                || string.IsNullOrEmpty(pair.MaterialName)
                || string.IsNullOrEmpty(pair.ExtensionJson)
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

            var props = BuildMaterialPropertiesCatalog(live);
            if (props.Count == 0)
            {
                continue;
            }

            var catalogKeys = ResolveMaterialPropertiesCatalogKeys(
                catalog,
                pair.MaterialName,
                live.name
            );
            for (var k = 0; k < catalogKeys.Count; k++)
            {
                var catalogKey = catalogKeys[k];
                if (string.IsNullOrEmpty(catalogKey))
                {
                    continue;
                }

                // Each key needs its own list instance — Warudo may mutate entries.
                catalog[catalogKey] = k == 0 ? props : new List<ShaderProperty>(props);
                refreshed++;
            }
        }

        return refreshed;
    }

    /// <summary>
    /// Local equivalent of Warudo's <c>ShaderPropertyExtensions.GetShaderProperties</c>.
    /// Calling that extension crosses a Warudo API boundary typed with CoreModule
    /// <c>Shader</c>, which UMod rejects with CS0012.
    /// </summary>
    private static List<ShaderProperty> BuildMaterialPropertiesCatalog(Material material)
    {
        var properties = new List<ShaderProperty>();
        if (material == null || material.shader == null)
        {
            return properties;
        }

        var shader = material.shader;
        var count = shader.GetPropertyCount();
        for (var i = 0; i < count; i++)
        {
            // Include HideInInspector — Poiyomi parks UV pans / feature toggles there.
            // Warudo Material Properties needs those names to edit scroll/glitter after apply.
            var name = shader.GetPropertyName(i);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!TryMapMaterialPropertyType(shader.GetPropertyType(i), out var propertyType))
            {
                continue;
            }

            properties.Add(
                new ShaderProperty
                {
                    Shader = shader.name,
                    Name = name,
                    Description = shader.GetPropertyDescription(i),
                    Type = propertyType,
                    Attributes = new List<string>(shader.GetPropertyAttributes(i)),
                }
            );
        }

        return properties;
    }

    private static bool TryMapMaterialPropertyType(
        ShaderPropertyType shaderType,
        out MaterialPropertyType propertyType
    )
    {
        switch (shaderType)
        {
            case ShaderPropertyType.Color:
                propertyType = MaterialPropertyType.Color;
                return true;
            case ShaderPropertyType.Vector:
                propertyType = MaterialPropertyType.Vector;
                return true;
            case ShaderPropertyType.Float:
            case ShaderPropertyType.Range:
                propertyType = MaterialPropertyType.Float;
                return true;
            case ShaderPropertyType.Int:
                propertyType = MaterialPropertyType.Int;
                return true;
            case ShaderPropertyType.Texture:
                propertyType = MaterialPropertyType.Texture;
                return true;
            default:
                propertyType = default;
                return false;
        }
    }

    /// <summary>
    /// All existing <see cref="CharacterAsset.MaterialProperties"/> keys that match
    /// the store/live name (exact first, then <c> (Instance)</c> strip). Returns every
    /// match so both <c>Mat</c> and <c>Mat (Instance)</c> get rewritten. If none match,
    /// inserts under the stripped live name (Warudo's usual key shape).
    /// </summary>
    private static List<string> ResolveMaterialPropertiesCatalogKeys(
        Dictionary<string, List<ShaderProperty>> catalog,
        string storeKey,
        string liveName
    )
    {
        var keys = new List<string>();
        var storeStripped = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(storeKey);
        var liveStripped = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(liveName);

        foreach (var key in catalog.Keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (
                string.Equals(key, liveName, StringComparison.Ordinal)
                || string.Equals(key, storeKey, StringComparison.Ordinal)
            )
            {
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        foreach (var key in catalog.Keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var keyStripped = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(key);
            if (
                string.Equals(keyStripped, liveStripped, StringComparison.Ordinal)
                || string.Equals(keyStripped, storeStripped, StringComparison.Ordinal)
            )
            {
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        if (keys.Count > 0)
        {
            return keys;
        }

        if (!string.IsNullOrEmpty(liveStripped))
        {
            keys.Add(liveStripped);
            return keys;
        }

        if (!string.IsNullOrEmpty(storeStripped))
        {
            keys.Add(storeStripped);
        }

        return keys;
    }

    /// <summary>
    /// UMod-safe mismatch dump after override: live renderer shaders vs Warudo
    /// <see cref="CharacterAsset.MaterialProperties"/> catalog (string/ShaderProperty only).
    /// Does not read <see cref="CharacterAsset.Materials"/> (CoreModule CS0012).
    /// </summary>
    public static void DumpMaterialsOverrideDebug(
        CharacterAsset character,
        GameObject root,
        VrmxtMaterialsOverrideInstance store
    )
    {
        if (character == null || root == null)
        {
            return;
        }

        var sb = new System.Text.StringBuilder(4096);
        sb.Append("VRMXT materials debug [").Append(character.Name).Append("]\n");

        DumpOverrideJsonSummary(sb, store);
        DumpImportedTextureResolve(sb, store);
        DumpLiveOverrideMaterials(sb, root, store);
        DumpWarudoMaterialPropertiesCatalog(sb, character, store);
        DumpWarudoLastMaterialProperties(sb, character);

        Debug.Log(sb.ToString());
    }

    private static void DumpOverrideJsonSummary(
        System.Text.StringBuilder sb,
        VrmxtMaterialsOverrideInstance store
    )
    {
        sb.AppendLine("--- override JSON (store) ---");
        if (store?.Pairs == null)
        {
            sb.AppendLine("(no store pairs)");
            return;
        }

        var any = false;
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (
                pair == null
                || string.IsNullOrEmpty(pair.MaterialName)
                || string.IsNullOrEmpty(pair.ExtensionJson)
            )
            {
                continue;
            }

            any = true;
            SummarizeOverrideJson(
                pair.ExtensionJson,
                out var shaderId,
                out var variant,
                out var propCount,
                out var texPropCount,
                out var bindingCount,
                out var jsonHasMainTex,
                out var jsonColor,
                out var texNames
            );
            sb.Append("  store='")
                .Append(pair.MaterialName)
                .Append("' shader='")
                .Append(shaderId)
                .Append("' variant=")
                .Append(variant)
                .Append(" props=")
                .Append(propCount)
                .Append(" texProps=")
                .Append(texPropCount)
                .Append(" bindings=")
                .Append(bindingCount)
                .Append(" hasMainTex=")
                .Append(jsonHasMainTex)
                .Append(" _Color=")
                .Append(FormatColor(jsonColor))
                .Append(" tex=[")
                .Append(string.Join(",", texNames))
                .Append("]\n");

            if (texPropCount > 0 && !jsonHasMainTex)
            {
                sb.Append(
                    "    RISK: texture ownership claimed without _MainTex/_BaseMap — "
                        + "apply clears stock MToon albedo.\n"
                );
            }

            if (jsonColor.HasValue && IsNearBlack(jsonColor.Value))
            {
                sb.Append("    RISK: JSON _Color near-black → mesh may look invisible.\n");
            }
        }

        if (!any)
        {
            sb.AppendLine("(no override JSON pairs)");
        }
    }

    private static void DumpImportedTextureResolve(
        System.Text.StringBuilder sb,
        VrmxtMaterialsOverrideInstance store
    )
    {
        sb.AppendLine("--- remembered glTF textures (override indices) ---");
        if (store?.Pairs == null)
        {
            sb.AppendLine("(no store)");
            return;
        }

        var indices = new SortedSet<int>();
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (pair == null || string.IsNullOrEmpty(pair.ExtensionJson))
            {
                continue;
            }

            CollectOverrideTextureIndices(pair.ExtensionJson, indices);
        }

        if (indices.Count == 0)
        {
            sb.AppendLine("(no texture indices in override JSON)");
            return;
        }

        foreach (var index in indices)
        {
            var ok = store.TryGetImportedTexture(index, out var texture) && texture != null;
            sb.Append("  textures[")
                .Append(index)
                .Append("]=")
                .Append(ok ? "'" + texture.name + "'" : "UNRESOLVED")
                .Append('\n');
        }
    }

    private static void DumpLiveOverrideMaterials(
        System.Text.StringBuilder sb,
        GameObject root,
        VrmxtMaterialsOverrideInstance store
    )
    {
        sb.AppendLine("--- live renderers (post-apply) ---");
        if (store?.Pairs == null)
        {
            sb.AppendLine("(no store pairs)");
            return;
        }

        var any = false;
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (
                pair == null
                || string.IsNullOrEmpty(pair.MaterialName)
                || string.IsNullOrEmpty(pair.ExtensionJson)
            )
            {
                continue;
            }

            any = true;
            var found = false;
            foreach (
                var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                    root,
                    pair.MaterialName
                )
            )
            {
                if (material == null)
                {
                    continue;
                }

                found = true;
                DescribeLiveMaterialHealth(
                    material,
                    out var shaderName,
                    out var mainTexName,
                    out var mainTex,
                    out var color,
                    out var risks
                );
                sb.Append("  store='")
                    .Append(pair.MaterialName)
                    .Append("' live='")
                    .Append(material.name)
                    .Append("' id=")
                    .Append(material.GetInstanceID())
                    .Append(" shader='")
                    .Append(shaderName)
                    .Append("' family=")
                    .Append(ClassifyShaderFamily(shaderName))
                    .Append(" ")
                    .Append(mainTexName)
                    .Append("=")
                    .Append(mainTex != null ? "'" + mainTex.name + "'" : "null")
                    .Append(" _Color=")
                    .Append(FormatColor(color))
                    .Append('\n');
                if (risks.Count > 0)
                {
                    sb.Append("    RISKS=[").Append(string.Join(", ", risks)).Append("]\n");
                }
            }

            if (!found)
            {
                sb.Append("  store='").Append(pair.MaterialName).Append("' live=(none)\n");
            }
        }

        if (!any)
        {
            sb.AppendLine("(no override JSON pairs)");
        }
    }

    private static void SummarizeOverrideJson(
        string extensionJson,
        out string shaderId,
        out string variant,
        out int propCount,
        out int texPropCount,
        out int bindingCount,
        out bool jsonHasMainTex,
        out Color? jsonColor,
        out List<string> texNames
    )
    {
        shaderId = "(parse fail)";
        variant = "?";
        propCount = 0;
        texPropCount = 0;
        bindingCount = 0;
        jsonHasMainTex = false;
        jsonColor = null;
        texNames = new List<string>();

        if (!VrmxtMaterialsOverride.TryParse(extensionJson, out var extension))
        {
            return;
        }

        if (
            !UnityOverrideSelector.TrySelectUnityEngineOverride(
                extension,
                DetectActivePipelineForWarudo(),
                out var engineOverride
            )
        )
        {
            shaderId = "(no unity slot for pipeline)";
            return;
        }

        var unity = engineOverride.Material as UnityMaterialOverride;
        shaderId = unity != null ? unity.ShaderName ?? "(null)" : "(not unity)";
        variant = unity != null ? (unity.Variant ?? "") : "?";

        var props = engineOverride.Properties;
        if (props != null)
        {
            propCount = props.Count;
            for (var i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null)
                {
                    continue;
                }

                if (
                    string.Equals(
                        p.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                )
                {
                    texPropCount++;
                    texNames.Add(
                        (p.Name ?? "?")
                            + "@"
                            + (p.TextureIndex.HasValue ? p.TextureIndex.Value.ToString() : "?")
                    );
                    if (IsMainAlbedoTextureName(p.Name))
                    {
                        jsonHasMainTex = true;
                    }
                }
                else if (
                    string.Equals(p.Name, "_Color", StringComparison.Ordinal)
                    && p.VectorValue != null
                    && p.VectorValue.Count >= 3
                )
                {
                    jsonColor = new Color(
                        p.VectorValue[0],
                        p.VectorValue[1],
                        p.VectorValue[2],
                        p.VectorValue.Count >= 4 ? p.VectorValue[3] : 1f
                    );
                }
            }
        }

        bindingCount = engineOverride.Bindings != null ? engineOverride.Bindings.Count : 0;
    }

    private static void CollectOverrideTextureIndices(string extensionJson, SortedSet<int> indices)
    {
        if (
            !VrmxtMaterialsOverride.TryParse(extensionJson, out var extension)
            || !VrmxtMaterialsOverride.TryGetUnityOverrides(extension, out var slots)
        )
        {
            return;
        }

        for (var s = 0; s < slots.Count; s++)
        {
            var props = slots[s]?.Properties;
            if (props == null)
            {
                continue;
            }

            for (var i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (
                    p != null
                    && string.Equals(
                        p.Type,
                        VrmxtMaterialsOverride.TargetTypeTexture,
                        StringComparison.Ordinal
                    )
                    && p.TextureIndex.HasValue
                    && p.TextureIndex.Value >= 0
                )
                {
                    indices.Add(p.TextureIndex.Value);
                }
            }
        }
    }

    private static int CountUnresolvedOverrideTextures(
        VrmxtMaterialsOverridePair pair,
        VrmxtMaterialsOverrideInstance store
    )
    {
        if (pair == null || store == null || string.IsNullOrEmpty(pair.ExtensionJson))
        {
            return 0;
        }

        var indices = new SortedSet<int>();
        CollectOverrideTextureIndices(pair.ExtensionJson, indices);
        var unresolved = 0;
        foreach (var index in indices)
        {
            if (!store.TryGetImportedTexture(index, out var texture) || texture == null)
            {
                unresolved++;
            }
        }

        return unresolved;
    }

    private static void DescribeLiveMaterialHealth(
        Material material,
        out string shaderName,
        out string mainTexName,
        out Texture mainTex,
        out Color? color,
        out List<string> risks
    )
    {
        shaderName = material.shader != null ? material.shader.name : "(null shader)";
        mainTexName = "(no main slot)";
        mainTex = null;
        color = null;
        risks = new List<string>();

        if (material.HasProperty("_MainTex"))
        {
            mainTexName = "_MainTex";
            mainTex = material.GetTexture("_MainTex");
        }
        else if (material.HasProperty("_BaseMap"))
        {
            mainTexName = "_BaseMap";
            mainTex = material.GetTexture("_BaseMap");
        }
        else if (material.HasProperty("_BaseColorMap"))
        {
            mainTexName = "_BaseColorMap";
            mainTex = material.GetTexture("_BaseColorMap");
        }

        if (material.HasProperty("_Color"))
        {
            color = material.GetColor("_Color");
        }
        else if (material.HasProperty("_BaseColor"))
        {
            color = material.GetColor("_BaseColor");
        }

        if (ClassifyShaderFamily(shaderName) == "mtoon")
        {
            risks.Add("still-mtoon");
        }

        if (mainTexName != "(no main slot)" && mainTex == null)
        {
            risks.Add("mainTex-null");
        }

        if (color.HasValue && IsNearBlack(color.Value))
        {
            risks.Add("color-near-black");
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

    private static string FormatColor(Color? c)
    {
        if (!c.HasValue)
        {
            return "(none)";
        }

        var v = c.Value;
        return "("
            + v.r.ToString("0.###")
            + ","
            + v.g.ToString("0.###")
            + ","
            + v.b.ToString("0.###")
            + ","
            + v.a.ToString("0.###")
            + ")";
    }

    private static void DumpWarudoMaterialPropertiesCatalog(
        System.Text.StringBuilder sb,
        CharacterAsset character,
        VrmxtMaterialsOverrideInstance store
    )
    {
        sb.AppendLine("--- Character.MaterialProperties (Warudo UI catalog) ---");
        // MaterialProperties is Dictionary<string, List<ShaderProperty>> — no Unity
        // Material type in the signature, so UMod can read it. Materials cannot.
        var catalog = character.MaterialProperties;
        if (catalog == null)
        {
            sb.AppendLine("(null)");
            return;
        }

        sb.Append("  keys=").Append(catalog.Count).Append('\n');

        var overrideKeys = new HashSet<string>(StringComparer.Ordinal);
        if (store?.Pairs != null)
        {
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (
                    pair != null
                    && !string.IsNullOrEmpty(pair.MaterialName)
                    && !string.IsNullOrEmpty(pair.ExtensionJson)
                )
                {
                    overrideKeys.Add(pair.MaterialName);
                    var stripped = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(
                        pair.MaterialName
                    );
                    if (!string.IsNullOrEmpty(stripped))
                    {
                        overrideKeys.Add(stripped);
                    }
                }
            }
        }

        foreach (var kv in catalog)
        {
            var key = kv.Key ?? "(null)";
            var props = kv.Value;
            var propCount = props != null ? props.Count : 0;
            var catalogShader = "(empty)";
            var sample = "";
            if (props != null && props.Count > 0 && props[0] != null)
            {
                catalogShader = string.IsNullOrEmpty(props[0].Shader)
                    ? "(blank Shader field)"
                    : props[0].Shader;
                var n = Math.Min(8, props.Count);
                for (var i = 0; i < n; i++)
                {
                    if (i > 0)
                    {
                        sample += ",";
                    }

                    sample += props[i] != null ? props[i].Name : "?";
                }
            }

            var isOverrideTarget = false;
            foreach (var ok in overrideKeys)
            {
                if (
                    string.Equals(key, ok, StringComparison.Ordinal)
                    || string.Equals(
                        VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(key),
                        VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(ok),
                        StringComparison.Ordinal
                    )
                )
                {
                    isOverrideTarget = true;
                    break;
                }
            }

            if (!isOverrideTarget && overrideKeys.Count > 0)
            {
                continue;
            }

            sb.Append("  key='")
                .Append(key)
                .Append("' props=")
                .Append(propCount)
                .Append(" catalogShader='")
                .Append(catalogShader)
                .Append("' family=")
                .Append(ClassifyShaderFamily(catalogShader))
                .Append(" sample=[")
                .Append(sample)
                .Append("]\n");
        }
    }

    private static void DumpWarudoLastMaterialProperties(
        System.Text.StringBuilder sb,
        CharacterAsset character
    )
    {
        sb.AppendLine("--- Character.LastMaterialProperties (runtime values) ---");
        var last = character.LastMaterialProperties;
        if (last == null)
        {
            sb.AppendLine("(null)");
            return;
        }

        sb.Append("  mats=").Append(last.Count).Append('\n');
        var shown = 0;
        foreach (var kv in last)
        {
            if (shown >= 12)
            {
                sb.Append("  ... (").Append(last.Count - shown).Append(" more)\n");
                break;
            }

            var props = kv.Value;
            var propCount = props != null ? props.Count : 0;
            var sample = "";
            if (props != null)
            {
                var n = 0;
                foreach (var pk in props.Keys)
                {
                    if (n >= 6)
                    {
                        break;
                    }

                    if (n > 0)
                    {
                        sample += ",";
                    }

                    sample += pk;
                    n++;
                }
            }

            sb.Append("  mat='")
                .Append(kv.Key)
                .Append("' values=")
                .Append(propCount)
                .Append(" sample=[")
                .Append(sample)
                .Append("] family=")
                .Append(ClassifyPropNameSample(sample))
                .Append('\n');
            shown++;
        }
    }

    private static string ClassifyShaderFamily(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName))
        {
            return "unknown";
        }

        if (
            shaderName.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("lil/", StringComparison.OrdinalIgnoreCase) >= 0
        )
        {
            return "lilToon";
        }

        if (shaderName.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "poiyomi";
        }

        if (
            shaderName.IndexOf("MToon", StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("VRM10/MToon", StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("VRM/", StringComparison.OrdinalIgnoreCase) >= 0
        )
        {
            return "mtoon";
        }

        return "other";
    }

    private static string ClassifyPropNameSample(string sampleCsv)
    {
        if (string.IsNullOrEmpty(sampleCsv))
        {
            return "unknown";
        }

        // MToon-ish vs lilToon-ish from common property names in the sample.
        if (
            sampleCsv.IndexOf("_ShadeColor", StringComparison.Ordinal) >= 0
            || sampleCsv.IndexOf("_RimFresnelPower", StringComparison.Ordinal) >= 0
            || sampleCsv.IndexOf("_MToonVersion", StringComparison.Ordinal) >= 0
        )
        {
            return "mtoon-ish";
        }

        if (
            sampleCsv.IndexOf("_UseShadow", StringComparison.Ordinal) >= 0
            || sampleCsv.IndexOf("_lilShadowCasterBias", StringComparison.Ordinal) >= 0
            || sampleCsv.IndexOf("_AsUnlit", StringComparison.Ordinal) >= 0
        )
        {
            return "liltoon-ish";
        }

        return "other";
    }

    private static bool HasOverrideJson(VrmxtMaterialsOverrideInstance store)
    {
        if (store?.Pairs == null)
        {
            return false;
        }

        for (var i = 0; i < store.Pairs.Count; i++)
        {
            if (!string.IsNullOrEmpty(store.Pairs[i]?.ExtensionJson))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectWantedUnityShaderNames(VrmxtMaterialsOverrideInstance store)
    {
        var names = new List<string>();
        if (store?.Pairs == null)
        {
            return names;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < store.Pairs.Count; i++)
        {
            var pair = store.Pairs[i];
            if (pair == null || string.IsNullOrEmpty(pair.ExtensionJson))
            {
                continue;
            }

            if (
                !VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension)
                || extension?.Overrides == null
            )
            {
                continue;
            }

            for (var j = 0; j < extension.Overrides.Count; j++)
            {
                var engineOverride = extension.Overrides[j];
                var unity = engineOverride?.Material as UnityMaterialOverride;
                if (unity == null || string.IsNullOrEmpty(unity.ShaderName))
                {
                    continue;
                }

                if (seen.Add(unity.ShaderName))
                {
                    names.Add(unity.ShaderName);
                }
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Warudo-safe RP detect: no Reflection. Null pipeline asset → Builtin; else Urp
    /// (Warudo Pro). HDRP not used by Warudo hosts.
    /// </summary>
    public static RenderPipelineVariant DetectActivePipelineForWarudo()
    {
        if (GraphicsSettings.currentRenderPipeline == null)
        {
            return RenderPipelineVariant.Builtin;
        }

        return RenderPipelineVariant.Urp;
    }

    public static void ClearExistingVfx(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var existing = root.GetComponent<VrmxtVfxInstance>();
        if (existing == null)
        {
            return;
        }

        existing.ClearParticleSystems();
        Object.Destroy(existing);
    }

    public static void ClearExistingMaterialsOverride(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var existing = root.GetComponent<VrmxtMaterialsOverrideInstance>();
        if (existing == null)
        {
            return;
        }

        // Destroy decoded override textures we remembered onto the Instance.
        var imported = existing.ImportedTextures;
        if (imported != null)
        {
            for (var i = 0; i < imported.Count; i++)
            {
                var texture = imported[i] != null ? imported[i].Texture : null;
                if (texture == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(texture);
                }
                else
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        existing.ClearImportedTextures();
        Object.Destroy(existing);
        // Keep stock shader snapshot so Clear / re-author can still restore MToon.
    }

    /// <summary>
    /// Resolve Character root without static <c>CharacterAsset.GameObject</c> (UMod CS0012).
    /// Uses <c>dynamic</c> to read Warudo Unity refs at runtime, then name-match fallback.
    /// </summary>
    public static GameObject TryFindCharacterRoot(CharacterAsset character)
    {
        if (character == null)
        {
            return null;
        }

        // Warudo/UMod: cannot touch GameObject/Transform members statically (CoreModule CS0012).
        // dynamic → runtime binder; log what Warudo actually exposes.
        try
        {
            dynamic d = character;
            var viaGameObject = AsGameObject(d.GameObject, "GameObject");
            var viaRoot = AsGameObjectFromTransform(d.RootTransform, "RootTransform");
            var viaMain = AsGameObjectFromTransform(d.MainTransform, "MainTransform");
            var viaAnimator = AsGameObjectFromAnimator(d.Animator, "Animator");

            if (viaGameObject != null)
            {
                return viaGameObject;
            }

            if (viaRoot != null)
            {
                return viaRoot;
            }

            if (viaMain != null)
            {
                return viaMain;
            }

            if (viaAnimator != null)
            {
                return viaAnimator;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: dynamic Warudo transform probe failed: " + e.Message);
        }

        return TryFindCharacterRootByName(character.Name);
    }

    private static GameObject AsGameObject(object value, string label)
    {
        try
        {
            return value as GameObject;
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: " + label + " cast failed: " + e.Message);
            return null;
        }
    }

    private static GameObject AsGameObjectFromTransform(object value, string label)
    {
        try
        {
            var t = value as Transform;
            return t != null ? t.gameObject : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: " + label + " cast failed: " + e.Message);
            return null;
        }
    }

    private static GameObject AsGameObjectFromAnimator(object value, string label)
    {
        try
        {
            var a = value as Animator;
            return a != null ? a.gameObject : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: " + label + " cast failed: " + e.Message);
            return null;
        }
    }

    private static GameObject TryFindCharacterRootByName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var transforms = Object.FindObjectsOfType<Transform>(true);
        GameObject best = null;
        var bestScore = -1;
        for (var i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || !string.Equals(t.name, name, StringComparison.Ordinal))
            {
                continue;
            }

            var score = ScoreCandidate(t.gameObject);
            if (score > bestScore)
            {
                bestScore = score;
                best = t.gameObject;
            }
        }

        if (best == null)
        {
            Debug.Log("VRMXT: name-match fallback found no Transform named '" + name + "'.");
        }

        return best;
    }

    private static int ScoreCandidate(GameObject go)
    {
        var score = 0;
        if (go.GetComponentInChildren<Animator>(true) != null)
        {
            score += 10;
        }

        if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
        {
            score += 5;
        }

        if (go.GetComponent<VrmxtVfxInstance>() != null)
        {
            score += 3;
        }

        if (go.GetComponent<VrmxtMaterialsOverrideInstance>() != null)
        {
            score += 2;
        }

        if (go.transform.parent == null)
        {
            score += 1;
        }

        return score;
    }

    private static Func<int, Transform> CreateNodeResolver(GameObject root, byte[] glbBytes)
    {
        // Prefer GLB name resolve over RuntimeGltfInstance.Nodes (UniGLTF not a UMod ref).
        if (
            !GlbChunks.TryExtractJson(glbBytes, out var json)
            || !VrmxtVfxNodeResolver.TryReadNodeNames(json, out var names)
        )
        {
            return null;
        }

        return VrmxtVfxNodeResolver.CreateResolver(root.transform, names);
    }
}
