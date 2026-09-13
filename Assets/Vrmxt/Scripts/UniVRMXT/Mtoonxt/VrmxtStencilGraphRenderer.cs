using System;
using System.Collections.Generic;
using UniVRMXT.MaterialsOverride;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniVRMXT.Mtoonxt
{
    [Serializable]
    public sealed class VrmxtStencilReaderCoverage
    {
        public Material Reader;
        public bool RespectReaderDepth;
        public List<Material> Writers = new List<Material>();
    }

    /// <summary>
    /// Baseline overlapping graphs cannot fit in one stencil reference per material.
    /// Build each reader's union separately; only coverage uses manual draws. Native
    /// renderer submissions retain lighting, shadows, skinning and material alpha.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class VrmxtStencilGraphRenderer : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private List<VrmxtStencilReaderCoverage> readers = new List<VrmxtStencilReaderCoverage>();
        private readonly Dictionary<Camera, CameraResources> cameras = new Dictionary<Camera, CameraResources>();
        private readonly Dictionary<Material, Material[]> coverageMaterials = new Dictionary<Material, Material[]>();
        private readonly List<Slot> slots = new List<Slot>();
        private MaterialPropertyBlock propertyBlock;
        private MaterialPropertyBlock block => propertyBlock ?? (propertyBlock = new MaterialPropertyBlock());
        public int ReaderCount => readers.Count;
        public int CameraCount => cameras.Count;

        public void Configure(IEnumerable<VrmxtStencilReaderCoverage> values)
        {
            Release();
            readers.Clear();
            if (values != null) readers.AddRange(values);
            RebuildSlots();
            enabled = readers.Count > 0;
        }

        private void OnEnable()
        {
            RebuildSlots();
            Camera.onPreCull -= BeforeCamera;
            Camera.onPreCull += BeforeCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= BeforeCamera;
            Release();
        }

        private void OnDestroy() => OnDisable();

        public void PrepareExportCopy()
        {
            Configure(null);
        }

        private void RebuildSlots()
        {
            slots.Clear();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                for (var r = 0; r < readers.Count; r++)
                    if (materials[i] != null && materials[i] == readers[r].Reader)
                        slots.Add(new Slot { Renderer = renderer, Submesh = i, ReaderIndex = r });
            }
        }

        private void BeforeCamera(Camera camera)
        {
            if (camera == null || readers.Count == 0 || GraphicsSettings.currentRenderPipeline != null
                || camera.stereoEnabled
                || (camera.scene.IsValid() && camera.scene != gameObject.scene)) return;

            // Camera-owned targets prevent Scene/Game/preview cameras sharing stale masks.
            var dead = new List<Camera>();
            foreach (var entry in cameras) if (entry.Key == null) dead.Add(entry.Key);
            foreach (var key in dead) { cameras[key].Dispose(key); cameras.Remove(key); }
            var width = Math.Max(1, camera.pixelWidth);
            var height = Math.Max(1, camera.pixelHeight);
            if (!cameras.TryGetValue(camera, out var resources)
                || resources.Width != width || resources.Height != height)
            {
                resources?.Dispose(camera);
                resources = new CameraResources(width, height, readers);
                cameras[camera] = resources;
                camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, resources.Commands);
            }
            var vp = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            var commands = resources.Commands;
            commands.Clear();
            var visible = new List<Renderer>(GetComponentsInChildren<Renderer>());
            if (camera.scene.IsValid())
                foreach (var sceneRoot in camera.scene.GetRootGameObjects())
                foreach (var renderer in sceneRoot.GetComponentsInChildren<Renderer>())
                    if (!visible.Contains(renderer)) visible.Add(renderer);
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (!visible.Contains(renderer)) visible.Add(renderer);
            // Copy live expression/UV/alpha properties once per camera, not per reader.
            var updated = new HashSet<Material>();
            foreach (var renderer in visible)
            foreach (var material in renderer.sharedMaterials)
                if (material != null && updated.Add(material)) UpdateCoverageMaterial(material, vp);

            for (var r = 0; r < readers.Count; r++)
            {
                if (Array.IndexOf(resources.Targets, resources.Targets[r]) != r) continue;
                var rule = readers[r];
                commands.SetRenderTarget(resources.Targets[r]);
                commands.SetViewport(new Rect(0, 0, width, height));
                commands.ClearRenderTarget(true, true, Color.black);
                // Each writer has its own allowed readers. Reconstruct depth without
                // those readers, then union its coverage without clearing prior color.
                // Otherwise C would block A's mask for B in A->B, A->C, B->C.
                foreach (var currentWriter in rule.Writers)
                {
                    commands.ClearRenderTarget(true, false, Color.black);
                    var excluded = new HashSet<Material>(rule.Writers) { rule.Reader };
                    foreach (var other in readers)
                        if (other.Writers.Contains(currentWriter)) excluded.Add(other.Reader);
                    // Cyclic writer components share one ordinary depth stage.
                    if (rule.RespectReaderDepth)
                        excluded.Remove(rule.Reader);
                    foreach (var renderer in visible)
                    {
                        if (!Visible(renderer, camera)) continue;
                        var own = renderer.transform.IsChildOf(transform);
                        var materials = renderer.sharedMaterials;
                        for (var s = 0; s < materials.Length; s++)
                        {
                            var material = materials[s];
                            if (material == null || (own && excluded.Contains(material))
                                || IsTransparent(material)) continue;
                            Draw(commands, renderer, material, s, false);
                        }
                    }
                    foreach (var renderer in visible)
                    {
                        if (!Visible(renderer, camera) || !renderer.transform.IsChildOf(transform)) continue;
                        var materials = renderer.sharedMaterials;
                        for (var s = 0; s < materials.Length; s++)
                            if (materials[s] == currentWriter) Draw(commands, renderer, materials[s], s, true);
                    }
                }
            }
            commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            commands.SetViewport(camera.pixelRect);
            foreach (var slot in slots)
            {
                if (slot.Renderer == null) continue;
                slot.Renderer.GetPropertyBlock(block, slot.Submesh);
                block.SetFloat("_VRMXTGraphEnabled", 1);
                block.SetTexture("_VRMXTGraphMask", resources.Targets[slot.ReaderIndex]);
                block.SetMatrix("_VRMXTGraphVP", vp);
                slot.Renderer.SetPropertyBlock(block, slot.Submesh);
            }
        }

        private static bool Visible(Renderer renderer, Camera camera)
        {
            return renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy
                && (!camera.scene.IsValid() || renderer.gameObject.scene == camera.scene)
                && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
        }

        private static bool IsTransparent(Material material)
        {
            return material.IsKeywordEnabled("_ALPHABLEND_ON")
                || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")
                || material.renderQueue > 2500;
        }

        private void UpdateCoverageMaterial(Material source, Matrix4x4 vp)
        {
            if (!coverageMaterials.TryGetValue(source, out var copies))
            {
                var shader = VrmxtMaterialsOverrideApplier.ResolveShader("VRMXT/MToonXT10");
                if (shader == null) return;
                copies = new[] { new Material(shader), new Material(shader) };
                foreach (var copy in copies) copy.hideFlags = HideFlags.HideAndDontSave;
                coverageMaterials.Add(source, copies);
            }
            for (var i = 0; i < copies.Length; i++)
            {
                var copy = copies[i];
                copy.CopyPropertiesFromMaterial(source);
                copy.DisableKeyword("_MTOONXT_OVERLAY_DEPTH");
                copy.DisableKeyword("_MTOONXT_OUTLINE_OVERLAY_DEPTH");
                copy.SetMatrix("_VRMXTGraphVP", vp);
                copy.SetFloat("_VRMXTGraphValue", i);
                copy.SetFloat("_VRMXTGraphColorMask", i == 0 ? 0 : 15);
                copy.SetFloat("_VRMXTGraphEnabled", 0);
            }
        }

        private void Draw(CommandBuffer commands, Renderer renderer, Material source, int submesh, bool writer)
        {
            if (source == null || !coverageMaterials.TryGetValue(source, out var copies)) return;
            var material = copies[writer ? 1 : 0];
            var pass = material.FindPass("VRMXT_GRAPH_COVERAGE");
            if (pass >= 0) commands.DrawRenderer(renderer, material, submesh, pass);
        }

        private void Release()
        {
            foreach (var slot in slots)
            {
                if (slot.Renderer == null) continue;
                slot.Renderer.GetPropertyBlock(block, slot.Submesh);
                block.SetFloat("_VRMXTGraphEnabled", 0);
                block.SetTexture("_VRMXTGraphMask", Texture2D.blackTexture);
                slot.Renderer.SetPropertyBlock(block, slot.Submesh);
            }
            foreach (var entry in cameras) entry.Value.Dispose(entry.Key);
            cameras.Clear();
            foreach (var copies in coverageMaterials.Values)
                foreach (var material in copies) DestroyResource(material);
            coverageMaterials.Clear();
        }

        private static void DestroyResource(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private sealed class Slot
        {
            public Renderer Renderer;
            public int Submesh;
            public int ReaderIndex;
        }

        private sealed class CameraResources
        {
            public readonly int Width, Height;
            public readonly RenderTexture[] Targets;
            public readonly CommandBuffer Commands = new CommandBuffer { name = "UniVRMXT reader coverage unions" };
            public CameraResources(int width, int height, List<VrmxtStencilReaderCoverage> readers)
            {
                Width = width; Height = height;
                Targets = new RenderTexture[readers.Count];
                for (var i = 0; i < readers.Count; i++)
                {
                    for (var j = 0; !readers[i].RespectReaderDepth && j < i; j++)
                        if (new HashSet<Material>(readers[j].Writers).SetEquals(readers[i].Writers))
                        {
                            if (readers[j].RespectReaderDepth) continue;
                            Targets[i] = Targets[j]; break;
                        }
                    if (Targets[i] != null) continue;
                    Targets[i] = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    {
                        name = "UniVRMXT reader coverage " + i,
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        antiAliasing = 1,
                    };
                    Targets[i].Create();
                }
            }
            public void Dispose(Camera camera)
            {
                if (camera != null) camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, Commands);
                Commands.Release();
                foreach (var target in new HashSet<RenderTexture>(Targets)) { target.Release(); DestroyResource(target); }
            }
        }
    }
}
