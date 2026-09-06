using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniVRMXT.Mtoonxt
{
    [System.Serializable]
    public sealed class VrmxtMtoonxtAuxiliaryDraw
    {
        public Renderer Renderer;
        public Material Material;
        public int Submesh;
        public bool AfterOpaque;
        public int BodyPass = -1;
        public int OutlinePass = -1;
    }

    /// <summary>
    /// Retained Built-in-pipeline passes for stencil modes that cannot be
    /// represented by one material pass (M02/M07/M08 and coverage masks).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VrmxtMaterialsMtoonxtAuxiliaryRenderer : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private List<VrmxtMtoonxtAuxiliaryDraw> draws =
            new List<VrmxtMtoonxtAuxiliaryDraw>();
        [SerializeField, HideInInspector]
        private List<Material> ownedMaterials = new List<Material>();
        [SerializeField, HideInInspector]
        private List<NativeBinding> nativeBindings = new List<NativeBinding>();
        [SerializeField, HideInInspector]
        private int resourceOwnerId;
        private readonly Dictionary<Camera, CommandBuffer> cameras =
            new Dictionary<Camera, CommandBuffer>();

        public int DrawCount => draws.Count;
        public int NativeRendererCount => nativeBindings.Count;

        public void Configure(
            IEnumerable<VrmxtMtoonxtAuxiliaryDraw> values,
            IEnumerable<Material> materials
        )
        {
            RemoveAllCameraBuffers();
            RestoreNativeMaterials();
            DestroyOwnedMaterials();
            resourceOwnerId = GetInstanceID();
            draws.Clear();
            if (values != null)
            {
                draws.AddRange(values);
            }

            if (materials != null)
            {
                ownedMaterials.AddRange(materials);
            }

            enabled = draws.Count > 0;
            if (isActiveAndEnabled)
            {
                InstallNativeMaterials();
            }
        }

        private void OnEnable()
        {
            if (draws.Count == 0)
            {
                var store = GetComponent<VrmxtMaterialsMtoonxtInstance>();
                if (store != null && store.Stencils.Count > 0)
                {
                    VrmxtMaterialsMtoonxtApplier.ReapplyStencils(gameObject, store);
                }
            }

            InstallNativeMaterials();
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPreCull += OnCameraPreCull;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCull;
            RemoveAllCameraBuffers();
            RestoreNativeMaterials();
        }

        private void OnDestroy()
        {
            OnDisable();
            DestroyOwnedMaterials();
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (
                camera == null
                || draws.Count == 0
                || GraphicsSettings.currentRenderPipeline != null
                || cameras.ContainsKey(camera)
                || !draws.Exists(draw => draw != null && !draw.AfterOpaque)
            )
            {
                return;
            }

            var before = new CommandBuffer { name = "UniVRMXT Stencil Prepass" };
            for (var i = 0; i < draws.Count; i++)
            {
                var draw = draws[i];
                // Lit subjects must use native material submissions: DrawRenderer does
                // not initialize per-object lights, shadows, or probes. Only colorless
                // coverage stays in command buffers, including mixed M08 plans.
                if (draw == null || draw.AfterOpaque || draw.Renderer == null || draw.Material == null)
                {
                    continue;
                }

                var buffer = before;
                if (draw.BodyPass >= 0)
                {
                    buffer.DrawRenderer(
                        draw.Renderer,
                        draw.Material,
                        draw.Submesh,
                        draw.BodyPass
                    );
                }

                if (draw.OutlinePass >= 0)
                {
                    buffer.DrawRenderer(
                        draw.Renderer,
                        draw.Material,
                        draw.Submesh,
                        draw.OutlinePass
                    );
                }
            }

            camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, before);
            cameras[camera] = before;
        }

        private void InstallNativeMaterials()
        {
            if (nativeBindings.Count > 0 || GraphicsSettings.currentRenderPipeline != null)
            {
                return;
            }

            var grouped = new Dictionary<Renderer, List<VrmxtMtoonxtAuxiliaryDraw>>();
            foreach (var draw in draws)
            {
                if (draw == null || !draw.AfterOpaque || draw.Renderer == null || draw.Material == null)
                {
                    continue;
                }
                if (!grouped.TryGetValue(draw.Renderer, out var list))
                {
                    list = new List<VrmxtMtoonxtAuxiliaryDraw>();
                    grouped.Add(draw.Renderer, list);
                }
                list.Add(draw);
            }

            try
            {
                foreach (var entry in grouped)
                {
                    var binding = NativeBinding.Install(entry.Key, entry.Value);
                    if (binding != null)
                    {
                        nativeBindings.Add(binding);
                    }
                }
            }
            catch
            {
                RestoreNativeMaterials();
                throw;
            }
        }

        // Restore before slot discovery, otherwise reapply would gather our own layers.
        public void RestoreNativeMaterials()
        {
            RestoreNativeMaterials(true);
        }

        private void RestoreNativeMaterials(bool destroyMeshes)
        {
            foreach (var binding in nativeBindings)
            {
                binding.Restore(destroyMeshes && resourceOwnerId == GetInstanceID());
            }
            nativeBindings.Clear();
        }

        /// <summary>Only call on UniVRM's export copy, never on the live root.</summary>
        public void PrepareExportCopy()
        {
            // Instantiate copies serialized resource references. Ownership guards
            // protect the live root, but release layers rebuilt by the copy's OnEnable.
            RestoreNativeMaterials();
            draws.Clear();
            DestroyOwnedMaterials();
            enabled = false;
        }

        [System.Serializable]
        private sealed class NativeBinding
        {
            public Renderer Renderer;
            public Mesh SourceMesh;
            public Mesh GeneratedMesh;
            public Material[] SourceMaterials;
            public Material[] InstalledMaterials;

            private static Mesh GetMesh(Renderer renderer)
            {
                if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
                var filter = renderer.GetComponent<MeshFilter>();
                return filter == null ? null : filter.sharedMesh;
            }

            private static void SetMesh(Renderer renderer, Mesh mesh)
            {
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var bounds = skin.localBounds;
                    skin.sharedMesh = mesh;
                    skin.localBounds = bounds;
                }
                else
                {
                    renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
                }
            }

            public static NativeBinding Install(Renderer renderer, List<VrmxtMtoonxtAuxiliaryDraw> layers)
            {
                var mesh = GetMesh(renderer);
                var materials = renderer.sharedMaterials;
                if (mesh == null || mesh.subMeshCount == 0 || materials.Length < mesh.subMeshCount)
                {
                    // Empty/incomplete renderers have no visible subject to layer.
                    return null;
                }
                foreach (var layer in layers)
                {
                    if (layer.Submesh < 0 || layer.Submesh >= mesh.subMeshCount) return null;
                }
                var result = new NativeBinding
                {
                    Renderer = renderer,
                    SourceMesh = mesh,
                    SourceMaterials = materials,
                    InstalledMaterials = new Material[materials.Length + layers.Count],
                };
                System.Array.Copy(materials, result.InstalledMaterials, materials.Length);
                var needsMesh = layers.Exists(layer => layer.Submesh != mesh.subMeshCount - 1);
                try
                {
                    if (needsMesh)
                    {
                        // Appending materials alone repeats only the LAST submesh.
                        // Retain vertex/skin/blend-shape data and copy only the index
                        // ranges needed for explicitly targeted additional material slots.
                        result.GeneratedMesh = Instantiate(mesh);
                        result.GeneratedMesh.name = mesh.name + " (VRMXT native layers)";
                        result.GeneratedMesh.hideFlags = HideFlags.HideAndDontSave;
                        result.GeneratedMesh.subMeshCount = result.InstalledMaterials.Length;
                        for (var i = mesh.subMeshCount; i < materials.Length; i++)
                        {
                            CopySubmesh(mesh, mesh.subMeshCount - 1, result.GeneratedMesh, i);
                        }
                    }
                    for (var i = 0; i < layers.Count; i++)
                    {
                        var layer = layers[i];
                        result.InstalledMaterials[materials.Length + i] = layer.Material;
                        if (needsMesh)
                        {
                            CopySubmesh(mesh, layer.Submesh, result.GeneratedMesh, materials.Length + i);
                        }
                    }
                    if (needsMesh) SetMesh(renderer, result.GeneratedMesh);
                    renderer.sharedMaterials = result.InstalledMaterials;
                    return result;
                }
                catch
                {
                    result.Restore(true);
                    throw;
                }
            }

            private static void CopySubmesh(Mesh source, int from, Mesh target, int to)
            {
                target.SetIndices(source.GetIndices(from, false), source.GetTopology(from), to,
                    calculateBounds: false, baseVertex: (int)source.GetBaseVertex(from));
            }

            public void Restore(bool destroyMesh)
            {
                if (Renderer != null)
                {
                    var current = Renderer.sharedMaterials;
                    // Preserve edits to base slots while removing only the layer suffix
                    // this binding owns. Also handles unsaved layer references on load.
                    var matches = current.Length == InstalledMaterials.Length;
                    for (var i = SourceMaterials.Length; matches && i < current.Length; i++)
                    {
                        matches = current[i] == InstalledMaterials[i];
                    }
                    if (matches)
                    {
                        var restored = new Material[SourceMaterials.Length];
                        System.Array.Copy(current, restored, restored.Length);
                        Renderer.sharedMaterials = restored;
                    }
                    if (GetMesh(Renderer) == GeneratedMesh && SourceMesh != null)
                    {
                        SetMesh(Renderer, SourceMesh);
                    }
                }
                if (destroyMesh && GeneratedMesh != null)
                {
                    if (Application.isPlaying) Destroy(GeneratedMesh);
                    else DestroyImmediate(GeneratedMesh);
                }
            }
        }

        private void RemoveAllCameraBuffers()
        {
            foreach (var pair in cameras)
            {
                var camera = pair.Key;
                if (camera != null)
                {
                    camera.RemoveCommandBuffer(
                        CameraEvent.BeforeForwardOpaque,
                        pair.Value
                    );
                }

                pair.Value.Release();
            }

            cameras.Clear();
        }

        private void DestroyOwnedMaterials()
        {
            for (var i = 0; i < ownedMaterials.Count; i++)
            {
                var material = ownedMaterials[i];
                if (material != null && resourceOwnerId == GetInstanceID())
                {
                    if (Application.isPlaying)
                    {
                        Destroy(material);
                    }
                    else
                    {
                        DestroyImmediate(material);
                    }
                }
            }

            ownedMaterials.Clear();
        }

    }
}
