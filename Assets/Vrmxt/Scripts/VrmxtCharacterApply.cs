using System;
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
    /// </summary>
    public static Result Apply(CharacterAsset character, byte[] glbBytes)
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
        attachedAny |= TryApplyMaterialsOverride(character, root, glbBytes, result);

        if (!attachedAny)
        {
            result.Dispose();
            return null;
        }

        return result;
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
        Result result)
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
            store.RememberTexturesFromPairs(glbTextures.AsResolver());
            glbTextures.ReleaseOwnership();
            resolveTexture = index =>
                store.TryGetImportedTexture(index, out var texture) ? texture : null;
        }

        var pipeline = DetectActivePipelineForWarudo();
        LogAvailableShadersBeforeOverride(character.Name, pipeline);
        var applied = VrmxtMaterialsOverrideApplier.Apply(
            root,
            store,
            gltfJson,
            pipeline,
            resolveTexture);

        var hasOverrideJson = false;
        if (store.Pairs != null)
        {
            for (var i = 0; i < store.Pairs.Count; i++)
            {
                if (!string.IsNullOrEmpty(store.Pairs[i]?.ExtensionJson))
                {
                    hasOverrideJson = true;
                    break;
                }
            }
        }

        if (!hasOverrideJson && applied == 0)
        {
            // Stock VRM: drop empty authoring shell so Character stays clean.
            ClearExistingMaterialsOverride(root);
            return false;
        }

        result.MaterialsOverride = store;

        if (applied > 0)
        {
            Debug.Log(
                "VRMXT: materials override on Character '" + character.Name +
                "' root='" + root.name + "' applied=" + applied +
                " pipeline=" + pipeline + ".");
        }
        else
        {
            Debug.Log(
                "VRMXT: materials override attached on '" + character.Name +
                "' but 0 unity slots applied (missing variant/shader or stock-only).");
        }

        return true;
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

    /// <summary>
    /// Debug: dump loaded shaders + probe sample <c>Shader.Find</c> names before apply.
    /// Uses <see cref="Resources.FindObjectsOfTypeAll{T}"/> (no System.Reflection).
    /// </summary>
    private static void LogAvailableShadersBeforeOverride(
        string characterName,
        RenderPipelineVariant pipeline)
    {
        var probes = new[]
        {
            "VRMXT/Samples/TestOverrideBuiltin",
            "VRMXT/Samples/TestOverrideURP",
            "VRMXT/Particles Unlit",
        };

        for (var i = 0; i < probes.Length; i++)
        {
            var name = probes[i];
            var found = Shader.Find(name);
            Debug.Log(
                "VRMXT: Shader.Find('" + name + "') => " +
                (found != null ? "OK id=" + found.GetInstanceID() : "null") +
                " (Character '" + characterName + "' pipeline=" + pipeline + ")");
        }

        var shaders = Resources.FindObjectsOfTypeAll<Shader>();
        var total = shaders != null ? shaders.Length : 0;
        var vrmxtCount = 0;
        var sb = new System.Text.StringBuilder(4096);
        sb.Append("VRMXT: loaded shaders before materials override on '")
            .Append(characterName)
            .Append("' count=")
            .Append(total)
            .AppendLine();

        if (shaders != null)
        {
            // Sort by name for stable logs (no LINQ OrderBy — keep deps light).
            var names = new string[total];
            for (var i = 0; i < total; i++)
            {
                names[i] = shaders[i] != null ? shaders[i].name : "<null>";
            }

            System.Array.Sort(names, System.StringComparer.Ordinal);

            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                if (name.IndexOf("VRMXT", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    vrmxtCount++;
                    sb.Append("  [VRMXT] ").AppendLine(name);
                }
                else
                {
                    sb.Append("  ").AppendLine(name);
                }
            }
        }

        sb.Append("VRMXT: VRMXT-named shaders among loaded=").Append(vrmxtCount);
        Debug.Log(sb.ToString());
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
            var viaParent = AsGameObjectFromTransform(d.ParentTransform, "ParentTransform");
            var viaAnimator = AsGameObjectFromAnimator(d.Animator, "Animator");

            Debug.Log(
                "VRMXT: Character '" + character.Name + "' transforms — " +
                "GameObject=" + Describe(viaGameObject) +
                ", RootTransform=" + Describe(viaRoot) +
                ", MainTransform=" + Describe(viaMain) +
                ", ParentTransform=" + Describe(viaParent) +
                ", Animator=" + Describe(viaAnimator));

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

    private static string Describe(GameObject go)
    {
        if (go == null)
        {
            return "null";
        }

        return "'" + go.name + "' active=" + go.activeInHierarchy;
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
