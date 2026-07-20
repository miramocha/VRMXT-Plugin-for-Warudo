using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UniVRMXT.MaterialsOverride;
using UniVRMXT.Vfx;
using UnityEngine;
using UnityEngine.Rendering;
using Warudo.Core.Utils;
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
            Debug.Log(
                "VRMXT: materials override on Character '" + character.Name +
                "' root='" + root.name + "' applied=" + applied +
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
