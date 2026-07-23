using System;
using System.Collections.Generic;
using UniVRMXT.MaterialsOverride;
using UnityEngine;

/// <summary>
/// Remembers live material shader names before VRMXT override apply mutates them in place
/// (Warudo path). Used by Clear All to restore MToon / stock look without scene reload.
/// </summary>
public static class VrmxtMaterialsStockShaders
{
    private static readonly Dictionary<int, Dictionary<string, string>> ByRootId =
        new Dictionary<int, Dictionary<string, string>>();

    /// <summary>
    /// Snapshot stripped material name → shader name once per root, before first mutate.
    /// </summary>
    public static void CaptureIfAbsent(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var id = root.GetInstanceID();
        if (ByRootId.ContainsKey(id))
        {
            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            var mats = renderer.sharedMaterials;
            if (mats == null)
            {
                continue;
            }

            for (var m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null || mat.shader == null || string.IsNullOrEmpty(mat.shader.name))
                {
                    continue;
                }

                if ((mat.hideFlags & HideFlags.DontSave) != 0)
                {
                    continue;
                }

                var name = VrmxtMaterialsOverrideRuntime.StripUnityInstanceSuffix(mat.name);
                if (string.IsNullOrEmpty(name) || map.ContainsKey(name))
                {
                    continue;
                }

                map[name] = mat.shader.name;
            }
        }

        ByRootId[id] = map;
        Debug.Log("VRMXT: stock shader snapshot root='" + root.name + "' mats=" + map.Count);
    }

    public static void Forget(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        ByRootId.Remove(root.GetInstanceID());
    }

    /// <summary>
    /// Restore snapped stock shaders onto live materials. Returns slots written.
    /// </summary>
    public static int Restore(GameObject root)
    {
        if (root == null)
        {
            return 0;
        }

        if (!ByRootId.TryGetValue(root.GetInstanceID(), out var map) || map == null || map.Count == 0)
        {
            Debug.LogWarning(
                "VRMXT: no stock shader snapshot for root '" + (root != null ? root.name : "?") +
                "'. Reload Character to capture stock before override apply.");
            return 0;
        }

        var restored = 0;
        foreach (var pair in map)
        {
            var shader = ResolveShader(pair.Value);
            if (shader == null)
            {
                Debug.LogWarning(
                    "VRMXT: stock shader unresolved '" + pair.Value + "' for '" + pair.Key + "'.");
                continue;
            }

            foreach (var live in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                         root, pair.Key))
            {
                if (live == null || (live.hideFlags & HideFlags.DontSave) != 0)
                {
                    continue;
                }

                live.shader = shader;
                restored++;
            }
        }

        return restored;
    }

    private static Shader ResolveShader(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName))
        {
            return null;
        }

        var found = Shader.Find(shaderName);
        if (found != null)
        {
            return found;
        }

        var resolve = VrmxtMaterialsOverrideApplier.ShaderResolveProvider;
        return resolve != null ? resolve(shaderName) : null;
    }
}
