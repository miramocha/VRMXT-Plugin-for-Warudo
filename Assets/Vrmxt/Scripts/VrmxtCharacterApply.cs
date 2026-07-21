using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UniVRMXT.Vfx;
using UnityEngine;
using UnityEngine.Rendering;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets;
using Warudo.Plugins.Core.Assets.Character;
using Object = UnityEngine.Object;

/// <summary>
/// Post-load VRMXT applies on a Character GameObject: <c>VRMXT_vfx</c> and
/// <c>VRMXT_materials_override</c>.
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
    /// </summary>
    public static Result Apply(
        CharacterAsset character,
        byte[] glbBytes,
        bool deferMaterialsOverrideApply = false)
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
        attachedAny |= TryApplyVfx(character, root, glbBytes, result);
        attachedAny |= TryApplyMaterialsOverride(
            character,
            root,
            glbBytes,
            result,
            runApply: !deferMaterialsOverrideApply);

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
        Result result)
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

    private static bool TryApplyVfx(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result)
    {
        var resolveNode = CreateNodeResolver(root, glbBytes);
        if (resolveNode == null)
        {
            Debug.Log("VRMXT: could not resolve glTF nodes for Character '" + character.Name + "'.");
            return false;
        }

        if (!VrmxtVfxRuntime.TryAttachFromGlb(
                root,
                glbBytes,
                resolveNode,
                out var instance,
                out var textures))
        {
            Debug.Log(
                "VRMXT: no VRMXT_vfx attach on Character '" + character.Name +
                "' (missing extension, parse fail, or all emitters skipped).");
            return false;
        }

        if (instance.Emitters == null || instance.Emitters.Count == 0)
        {
            Debug.Log(
                "VRMXT: VRMXT_vfx present but 0 emitters resolved on '" + character.Name +
                "' (node name mismatch vs scene hierarchy?). Root='" + root.name + "'.");
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
            "VRMXT: attached VFX on Character '" + character.Name + "' root='" + root.name +
            "' emitters=" + instance.Emitters.Count + " particles=" + particleCount + ".");

        result.VfxInstance = instance;
        result.VfxTextures = textures;
        return true;
    }

    private static bool TryApplyMaterialsOverride(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result,
        bool runApply)
    {
        string gltfJson = result.VfxTextures != null ? result.VfxTextures.Json : null;
        if (string.IsNullOrEmpty(gltfJson) && !GlbChunks.TryExtractJson(glbBytes, out gltfJson))
        {
            return false;
        }

        if (!VrmxtMaterialsOverrideRuntime.TryAttachFromGltfJson(root, gltfJson, out var store) ||
            store == null)
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
            Debug.Log(
                "VRMXT: materials override prepared (deferred apply) on Character '" +
                character.Name + "' root='" + root.name + "'.");
            return true;
        }

        var applied = RunMaterialsOverrideApply(
            character,
            root,
            glbBytes,
            result,
            gltfJson,
            store,
            resolveTexture);
        if (applied == 0)
        {
            ClearExistingMaterialsOverride(root);
            result.MaterialsOverride = null;
            return false;
        }

        return true;
    }

    private static int RunMaterialsOverrideApply(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result)
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
            resolveTexture);
    }

    private static int RunMaterialsOverrideApply(
        CharacterAsset character,
        GameObject root,
        byte[] glbBytes,
        Result result,
        string gltfJson,
        VrmxtMaterialsOverrideInstance store,
        Func<int, Texture> resolveTexture)
    {
        if (store == null || string.IsNullOrEmpty(gltfJson))
        {
            return 0;
        }

        var pipeline = DetectActivePipelineForWarudo();
        var applied = VrmxtMaterialsOverrideApplier.Apply(
            root,
            store,
            gltfJson,
            pipeline,
            resolveTexture);

        if (applied > 0)
        {
            var catalogRefreshed = RefreshMaterialPropertiesCatalog(character, root, store);
            Debug.Log(
                "VRMXT: materials override on Character '" + character.Name +
                "' root='" + root.name + "' applied=" + applied +
                " catalogRefreshed=" + catalogRefreshed +
                " pipeline=" + pipeline + ".");
        }
        else
        {
            var wanted = CollectWantedUnityShaderNames(store);
            Debug.LogWarning(
                "VRMXT: materials override attached on '" + character.Name +
                "' but 0 unity slots applied (missing variant/shader or stock-only)." +
                " pipeline=" + pipeline +
                " wantedShaders=[" + string.Join(", ", wanted) + "]." +
                " Check console for 'VRMXT: shader inventory' and lilToon warm logs.");
        }

        return applied;
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
        VrmxtMaterialsOverrideInstance store)
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
            if (pair == null ||
                string.IsNullOrEmpty(pair.MaterialName) ||
                string.IsNullOrEmpty(pair.ExtensionJson))
            {
                continue;
            }

            Material live = null;
            foreach (var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                         root,
                         pair.MaterialName))
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
                live.name);
            for (var k = 0; k < catalogKeys.Count; k++)
            {
                var catalogKey = catalogKeys[k];
                if (string.IsNullOrEmpty(catalogKey))
                {
                    continue;
                }

                // Each key needs its own list instance — Warudo may mutate entries.
                catalog[catalogKey] = k == 0
                    ? props
                    : new List<ShaderProperty>(props);
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
            if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0)
            {
                continue;
            }

            var name = shader.GetPropertyName(i);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!TryMapMaterialPropertyType(
                    shader.GetPropertyType(i),
                    out var propertyType))
            {
                continue;
            }

            properties.Add(new ShaderProperty
            {
                Shader = shader.name,
                Name = name,
                Description = shader.GetPropertyDescription(i),
                Type = propertyType,
                Attributes = new List<string>(shader.GetPropertyAttributes(i)),
            });
        }

        return properties;
    }

    private static bool TryMapMaterialPropertyType(
        ShaderPropertyType shaderType,
        out MaterialPropertyType propertyType)
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
        string liveName)
    {
        var keys = new List<string>();
        var storeStripped =
            VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(storeKey);
        var liveStripped =
            VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(liveName);

        foreach (var key in catalog.Keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (string.Equals(key, liveName, StringComparison.Ordinal) ||
                string.Equals(key, storeKey, StringComparison.Ordinal))
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

            var keyStripped =
                VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(key);
            if (string.Equals(keyStripped, liveStripped, StringComparison.Ordinal) ||
                string.Equals(keyStripped, storeStripped, StringComparison.Ordinal))
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
        VrmxtMaterialsOverrideInstance store)
    {
        if (character == null || root == null)
        {
            return;
        }

        var sb = new System.Text.StringBuilder(2048);
        sb.Append("VRMXT materials debug [").Append(character.Name).Append("]\n");

        DumpLiveOverrideMaterials(sb, root, store);
        DumpWarudoMaterialPropertiesCatalog(sb, character, store);
        DumpWarudoLastMaterialProperties(sb, character);

        Debug.Log(sb.ToString());
    }

    private static void DumpLiveOverrideMaterials(
        System.Text.StringBuilder sb,
        GameObject root,
        VrmxtMaterialsOverrideInstance store)
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
            if (pair == null ||
                string.IsNullOrEmpty(pair.MaterialName) ||
                string.IsNullOrEmpty(pair.ExtensionJson))
            {
                continue;
            }

            any = true;
            var found = false;
            foreach (var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                         root,
                         pair.MaterialName))
            {
                if (material == null)
                {
                    continue;
                }

                found = true;
                var shaderName = material.shader != null ? material.shader.name : "(null shader)";
                sb.Append("  store='").Append(pair.MaterialName)
                    .Append("' live='").Append(material.name)
                    .Append("' id=").Append(material.GetInstanceID())
                    .Append(" shader='").Append(shaderName)
                    .Append("' family=").Append(ClassifyShaderFamily(shaderName))
                    .Append('\n');
            }

            if (!found)
            {
                sb.Append("  store='").Append(pair.MaterialName)
                    .Append("' live=(none)\n");
            }
        }

        if (!any)
        {
            sb.AppendLine("(no override JSON pairs)");
        }
    }

    private static void DumpWarudoMaterialPropertiesCatalog(
        System.Text.StringBuilder sb,
        CharacterAsset character,
        VrmxtMaterialsOverrideInstance store)
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
                if (pair != null &&
                    !string.IsNullOrEmpty(pair.MaterialName) &&
                    !string.IsNullOrEmpty(pair.ExtensionJson))
                {
                    overrideKeys.Add(pair.MaterialName);
                    var stripped =
                        VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(pair.MaterialName);
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
                if (string.Equals(key, ok, StringComparison.Ordinal) ||
                    string.Equals(
                        VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(key),
                        VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(ok),
                        StringComparison.Ordinal))
                {
                    isOverrideTarget = true;
                    break;
                }
            }

            if (!isOverrideTarget && overrideKeys.Count > 0)
            {
                continue;
            }

            sb.Append("  key='").Append(key)
                .Append("' props=").Append(propCount)
                .Append(" catalogShader='").Append(catalogShader)
                .Append("' family=").Append(ClassifyShaderFamily(catalogShader))
                .Append(" sample=[").Append(sample)
                .Append("]\n");
        }
    }

    private static void DumpWarudoLastMaterialProperties(
        System.Text.StringBuilder sb,
        CharacterAsset character)
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

            sb.Append("  mat='").Append(kv.Key)
                .Append("' values=").Append(propCount)
                .Append(" sample=[").Append(sample)
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

        if (shaderName.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("lil/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "lilToon";
        }

        if (shaderName.IndexOf("MToon", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("VRM10/MToon", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("VRM/", StringComparison.OrdinalIgnoreCase) >= 0)
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
        if (sampleCsv.IndexOf("_ShadeColor", StringComparison.Ordinal) >= 0 ||
            sampleCsv.IndexOf("_RimFresnelPower", StringComparison.Ordinal) >= 0 ||
            sampleCsv.IndexOf("_MToonVersion", StringComparison.Ordinal) >= 0)
        {
            return "mtoon-ish";
        }

        if (sampleCsv.IndexOf("_UseShadow", StringComparison.Ordinal) >= 0 ||
            sampleCsv.IndexOf("_lilShadowCasterBias", StringComparison.Ordinal) >= 0 ||
            sampleCsv.IndexOf("_AsUnlit", StringComparison.Ordinal) >= 0)
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

            if (!VrmxtMaterialsOverride.TryParse(pair.ExtensionJson, out var extension) ||
                extension?.Overrides == null)
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
        if (!GlbChunks.TryExtractJson(glbBytes, out var json) ||
            !VrmxtVfxNodeResolver.TryReadNodeNames(json, out var names))
        {
            return null;
        }

        return VrmxtVfxNodeResolver.CreateResolver(root.transform, names);
    }
}
