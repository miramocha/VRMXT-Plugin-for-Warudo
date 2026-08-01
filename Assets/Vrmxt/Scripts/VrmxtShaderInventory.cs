using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared shader name inventory for debug dump and materials-export autocomplete.
/// Matches the relevant-shader filter used by <c>VrmxtPlugin</c> dump logs.
/// </summary>
public static class VrmxtShaderInventory
{
    /// <summary>
    /// Optional extra names (e.g. ModHost warm cache). Cleared when plugin destroys.
    /// </summary>
    public static Func<IEnumerable<string>> ExtraNamesProvider { get; set; }

    /// <summary>
    /// Poiyomi alts deferred from Manager autocomplete / claimed inventory.
    /// Lil Fur ships with <c>mira.shaders.poiyomi.birp</c>; Two Pass does not.
    /// </summary>
    private static bool IsDeferredPoiyomiAltShader(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // World / Two Pass (not main Toon / Lil Fur).
        if (name.IndexOf("Poiyomi Toon World", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Poiyomi Pro World", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // Poiyomi only — lilToon uses TwoPass (no space) and must stay.
        if (name.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0 &&
            name.IndexOf("Two Pass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Names suitable for override shader dropdown: loaded relevant shaders + extras.
    /// Sorted ordinal. Never null.
    /// </summary>
    public static List<string> CollectRelevantShaderNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        var extras = ExtraNamesProvider;
        if (extras != null)
        {
            try
            {
                foreach (var name in extras())
                {
                    if (!string.IsNullOrEmpty(name) &&
                        IsRelevantShaderName(name) &&
                        !IsDeferredPoiyomiAltShader(name))
                    {
                        names.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("VRMXT: shader inventory extras failed: " + e.Message);
            }
        }

        Shader[] loaded;
        try
        {
            loaded = Resources.FindObjectsOfTypeAll<Shader>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: FindObjectsOfTypeAll<Shader> failed: " + e.Message);
            loaded = null;
        }

        if (loaded != null)
        {
            for (var i = 0; i < loaded.Length; i++)
            {
                var shader = loaded[i];
                if (shader == null || string.IsNullOrEmpty(shader.name))
                {
                    continue;
                }

                if (IsRelevantShaderName(shader.name) &&
                    !IsDeferredPoiyomiAltShader(shader.name))
                {
                    names.Add(shader.name);
                }
            }
        }

        return new List<string>(names);
    }

    /// <summary>
    /// Same filter as plugin dump: lil / Poiyomi / VRMXT / MToon / Sample External.
    /// </summary>
    public static bool IsRelevantShaderName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.IndexOf("lil", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("VRMXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("MToon", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Sample External", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
