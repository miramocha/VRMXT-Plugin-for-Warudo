using System;
using System.Collections.Generic;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Attach parsed <c>VRMXT_vfx</c> data to a loaded avatar without referencing UniVRM types.
    /// Call after stock <c>Vrm10.LoadGltfDataAsync</c> (or equivalent) with glTF JSON and
    /// <c>RuntimeGltfInstance.Nodes</c>. Prefer <see cref="TryAttachFromGlb"/> when particle
    /// textures are not imported by UniVRM (extension-only <c>textures[]</c>).
    /// </summary>
    public static class VrmxtVfxRuntime
    {
        /// <summary>
        /// Parse + resolve nodes onto <see cref="VrmxtVfxInstance"/>. Does not build
        /// <see cref="ParticleSystem"/> components; call
        /// <see cref="VrmxtVfxInstance.BuildParticleSystems"/> or the texture overload.
        /// Missing / invalid extension → false (no-op, no component added).
        /// </summary>
        public static bool TryAttach(
            GameObject root,
            string gltfJson,
            IReadOnlyList<Transform> nodes,
            out VrmxtVfxInstance instance)
        {
            instance = null;
            if (root == null)
            {
                return false;
            }

            if (!VrmxtVfxImporter.TryImport(gltfJson, nodes, out var resolved))
            {
                return false;
            }

            instance = EnsureInstance(root);
            if (instance == null)
            {
                return false;
            }

            instance.SetEmitters(resolved);
            return true;
        }

        public static bool TryAttach(
            GameObject root,
            string gltfJson,
            Func<int, Transform> resolveNode,
            out VrmxtVfxInstance instance)
        {
            instance = null;
            if (root == null)
            {
                return false;
            }

            if (!VrmxtVfxImporter.TryImport(gltfJson, resolveNode, out var resolved))
            {
                return false;
            }

            instance = EnsureInstance(root);
            if (instance == null)
            {
                return false;
            }

            instance.SetEmitters(resolved);
            return true;
        }

        /// <summary>
        /// Attach resolved emitters and map them to <see cref="ParticleSystem"/> children.
        /// <paramref name="resolveTexture"/> may be null (all emitters use solid tint fallback).
        /// </summary>
        public static bool TryAttach(
            GameObject root,
            string gltfJson,
            IReadOnlyList<Transform> nodes,
            Func<int, Texture> resolveTexture,
            out VrmxtVfxInstance instance)
        {
            if (!TryAttach(root, gltfJson, nodes, out instance))
            {
                return false;
            }

            instance.BuildParticleSystems(resolveTexture);
            return true;
        }

        public static bool TryAttach(
            GameObject root,
            string gltfJson,
            Func<int, Transform> resolveNode,
            Func<int, Texture> resolveTexture,
            out VrmxtVfxInstance instance)
        {
            if (!TryAttach(root, gltfJson, resolveNode, out instance))
            {
                return false;
            }

            instance.BuildParticleSystems(resolveTexture);
            return true;
        }

        /// <summary>
        /// Re-read GLB bytes: parse <c>VRMXT_vfx</c>, decode extension textures, build particles.
        /// Caller owns <paramref name="textures"/> until disposed (or
        /// <see cref="VrmxtVfxGlbTextures.ReleaseOwnership"/> after saving into an asset).
        /// </summary>
        public static bool TryAttachFromGlb(
            GameObject root,
            byte[] glbBytes,
            Func<int, Transform> resolveNode,
            out VrmxtVfxInstance instance,
            out VrmxtVfxGlbTextures textures)
        {
            instance = null;
            textures = null;
            if (root == null || glbBytes == null || resolveNode == null)
            {
                return false;
            }

            if (!VrmxtVfxGlbTextures.TryCreate(glbBytes, out textures))
            {
                return false;
            }

            if (!TryAttach(root, textures.Json, resolveNode, textures.AsResolver(), out instance))
            {
                textures.Dispose();
                textures = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Same as <see cref="TryAttachFromGlb(GameObject,byte[],Func{int,Transform},out VrmxtVfxInstance,out VrmxtVfxGlbTextures)"/>
        /// with node list resolution (runtime <c>RuntimeGltfInstance.Nodes</c>).
        /// </summary>
        public static bool TryAttachFromGlb(
            GameObject root,
            byte[] glbBytes,
            IReadOnlyList<Transform> nodes,
            out VrmxtVfxInstance instance,
            out VrmxtVfxGlbTextures textures)
        {
            textures = null;
            instance = null;
            if (nodes == null)
            {
                return false;
            }

            return TryAttachFromGlb(
                root,
                glbBytes,
                index => index >= 0 && index < nodes.Count ? nodes[index] : null,
                out instance,
                out textures);
        }

        private static VrmxtVfxInstance EnsureInstance(GameObject root)
        {
            var instance = root.GetComponent<VrmxtVfxInstance>();
            if (instance != null)
            {
                return instance;
            }

            // ScriptedImporter main assets reject AddComponent during AssetPostprocessor
            // (returns null). Callers must Instantiate / use a companion prefab first.
            return root.AddComponent<VrmxtVfxInstance>();
        }
    }
}
