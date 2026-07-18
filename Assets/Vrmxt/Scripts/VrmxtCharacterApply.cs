using System;
using UniGLTF;
using UnityEngine;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets.Character;
using Object = UnityEngine.Object;

/// <summary>
/// Post-load VRMXT applies on a Character GameObject. v1: <c>VRMXT_vfx</c> only.
/// </summary>
public static class VrmxtCharacterApply
{
    public sealed class Result : IDisposable
    {
        public VrmxtVfxInstance VfxInstance;
        public VrmxtVfxGlbTextures VfxTextures;

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

        var root = character.GameObject;
        if (root == null || glbBytes == null || glbBytes.Length == 0)
        {
            return null;
        }

        ClearExistingVfx(root);

        var resolveNode = CreateNodeResolver(root, glbBytes);
        if (resolveNode == null)
        {
            Debug.Log("VRMXT: could not resolve glTF nodes for Character " + character.Name);
            return null;
        }

        if (!VrmxtVfxRuntime.TryAttachFromGlb(
                root,
                glbBytes,
                resolveNode,
                out var instance,
                out var textures))
        {
            return null;
        }

        Debug.Log(
            $"VRMXT: attached VFX on Character '{character.Name}' " +
            $"({instance.Emitters.Count} emitter(s)).");

        return new Result
        {
            VfxInstance = instance,
            VfxTextures = textures,
        };
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

    private static Func<int, Transform> CreateNodeResolver(GameObject root, byte[] glbBytes)
    {
        var runtime = root.GetComponent<RuntimeGltfInstance>();
        if (runtime != null && runtime.Nodes != null && runtime.Nodes.Count > 0)
        {
            var nodes = runtime.Nodes;
            return index => index >= 0 && index < nodes.Count ? nodes[index] : null;
        }

        if (!GlbChunks.TryExtractJson(glbBytes, out var json) ||
            !VrmxtVfxNodeResolver.TryReadNodeNames(json, out var names))
        {
            return null;
        }

        return VrmxtVfxNodeResolver.CreateResolver(root.transform, names);
    }
}
