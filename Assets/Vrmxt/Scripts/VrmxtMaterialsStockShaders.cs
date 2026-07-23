using System;
using System.Collections.Generic;
using UniVRMXT.MaterialsOverride;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Remembers live materials before VRMXT override apply mutates them in place
/// (Warudo path). Used by Clear All to restore MToon / stock look without scene reload.
/// Snapshots are Material clones (shader + properties + keywords), keyed by root
/// instance id — callers must <see cref="Forget"/> on Character unbind / source change.
/// </summary>
public static class VrmxtMaterialsStockShaders
{
    private static readonly Dictionary<int, Dictionary<string, Material>> ByRootId =
        new Dictionary<int, Dictionary<string, Material>>();

    /// <summary>
    /// Snapshot stripped material name → Material clone once per root, before first mutate.
    /// Empty captures are not stored so a later retry can succeed when renderers appear.
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

        var map = new Dictionary<string, Material>(StringComparer.Ordinal);
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

                map[name] = CloneStock(mat);
            }
        }

        if (map.Count == 0)
        {
            Debug.LogWarning(
                "VRMXT: stock capture empty for root '" + root.name +
                "' — will retry on next CaptureIfAbsent.");
            return;
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

        Forget(root.GetInstanceID());
    }

    public static void Forget(int rootInstanceId)
    {
        if (!ByRootId.TryGetValue(rootInstanceId, out var map))
        {
            return;
        }

        ByRootId.Remove(rootInstanceId);
        DestroyClones(map);
    }

    /// <summary>
    /// Restore snapped stock materials (shader + properties) onto live slots.
    /// Returns slots written.
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
                "VRMXT: no stock shader snapshot for root '" + root.name +
                "'. Reload Character to capture stock before override apply.");
            return 0;
        }

        var restored = 0;
        foreach (var pair in map)
        {
            var stock = pair.Value;
            if (stock == null || stock.shader == null)
            {
                Debug.LogWarning(
                    "VRMXT: stock snapshot missing for '" + pair.Key + "'.");
                continue;
            }

            // Ensure shader asset still resolves (cross-mod / uMod).
            var shader = ResolveShader(stock.shader.name);
            if (shader == null)
            {
                Debug.LogWarning(
                    "VRMXT: stock shader unresolved '" + stock.shader.name +
                    "' for '" + pair.Key + "'.");
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
                live.CopyPropertiesFromMaterial(stock);
                restored++;
            }
        }

        return restored;
    }

    private static Material CloneStock(Material source)
    {
        var clone = new Material(source)
        {
            name = source.name + " (VRMXT Stock)",
            hideFlags = HideFlags.HideAndDontSave,
        };
        return clone;
    }

    private static void DestroyClones(Dictionary<string, Material> map)
    {
        if (map == null)
        {
            return;
        }

        foreach (var pair in map)
        {
            var clone = pair.Value;
            if (clone == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(clone);
            }
            else
            {
                Object.DestroyImmediate(clone);
            }
        }

        map.Clear();
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
