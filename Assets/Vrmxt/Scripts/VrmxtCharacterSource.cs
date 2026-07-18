using System;
using UnityEngine;

/// <summary>
/// Map Character <c>Source</c> resource URIs to PersistentDataManager relative paths.
/// Local <c>character://data/...</c> only; Workshop / other schemes unsupported in v1.
/// </summary>
public static class VrmxtCharacterSource
{
    public const string CharacterScheme = "character";

    /// <summary>
    /// Try resolve a Character Source URI to a StreamingAssets-relative path readable by
    /// <c>PersistentDataManager</c> (e.g. <c>Characters/Model.vrm</c>).
    /// </summary>
    public static bool TryGetPersistentRelativePath(string source, out string relativePath)
    {
        relativePath = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        source = source.Trim();

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return TryNormalizeBarePath(source, out relativePath);
        }

        if (!string.Equals(uri.Scheme, CharacterScheme, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"VRMXT: unsupported Character Source scheme '{uri.Scheme}' (local character:// only).");
            return false;
        }

        // character://data/Characters/Foo.vrm  →  Characters/Foo.vrm
        var path = uri.AbsolutePath ?? string.Empty;
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            path = path.Substring(1);
        }

        path = path.TrimStart('/');
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("data/".Length);
        }

        path = path.Replace('\\', '/');
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (!path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"VRMXT: Character Source is not a .vrm file ({path}).");
            return false;
        }

        relativePath = path;
        return true;
    }

    private static bool TryNormalizeBarePath(string source, out string relativePath)
    {
        relativePath = null;
        var path = source.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("data/".Length);
        }

        if (!path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        relativePath = path;
        return true;
    }
}
