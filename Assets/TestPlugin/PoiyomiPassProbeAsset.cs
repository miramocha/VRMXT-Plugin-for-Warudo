using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Scenes;

/// <summary>
/// Debug asset: dump ShaderLab pass names + which SVC PassTypes accept
/// variants for materials in the live scene (Poiyomi / filter).
/// Live copy also in Warudo Playground/PoiyomiProbe. Here for UMod compile check.
/// Does not prove Frame Debugger draw order — only compile/warm eligibility.
/// </summary>
[AssetType(
    Id = "096462be-4b45-406c-bdc9-d3bb275c98e2",
    Title = "Poiyomi Pass Probe",
    Category = "CATEGORY_DEBUG",
    Singleton = true
)]
public class PoiyomiPassProbeAsset : Asset
{
    private static readonly PassType[] ProbePassTypes =
    {
        PassType.Normal,
        PassType.Vertex,
        PassType.VertexLM,
        PassType.VertexLMRGBM,
        PassType.ForwardBase,
        PassType.ForwardAdd,
        PassType.LightPrePassBase,
        PassType.LightPrePassFinal,
        PassType.ShadowCaster,
        PassType.Deferred,
        PassType.Meta,
        PassType.MotionVectors,
        PassType.ScriptableRenderPipeline,
        PassType.ScriptableRenderPipelineDefaultUnlit,
    };

    [Markdown]
    public string Status = "Add to scene → click **Probe Scene Materials**. Check Player.log / console.";

    [DataInput]
    [Label("Shader name contains")]
    [Description("Case-insensitive substring. Empty = all shaders with materials.")]
    public string ShaderNameFilter = "poiyomi";

    [DataInput]
    [Label("Max materials to detail")]
    [IntegerSlider(1, 64)]
    public int MaxMaterialsDetail = 12;

    [DataInput]
    [Label("Include disabled GameObjects")]
    public bool IncludeInactive = true;

    protected override void OnCreate()
    {
        base.OnCreate();
        SetActive(true);
    }

    [Trigger]
    [Label("Probe Scene Materials")]
    public void ProbeSceneMaterials()
    {
        try
        {
            var report = BuildReport();
            Debug.Log(report);
            var shortStatus = TruncateForUi(report, 3500);
            SetDataInput(nameof(Status), "```\n" + shortStatus + "\n```", broadcast: true);
            Context.Service.PromptMessage(
                "Poiyomi Pass Probe",
                "Logged pass probe to console / Player.log.");
        }
        catch (Exception e)
        {
            Debug.LogError("PoiyomiPassProbe: " + e);
            SetDataInput(nameof(Status), "Probe failed: " + e.Message, broadcast: true);
            Context.Service.PromptMessage("Poiyomi Pass Probe", "Failed: " + e.Message);
        }
    }

    private string BuildReport()
    {
        var sb = new StringBuilder(8192);
        sb.AppendLine("=== Poiyomi Pass Probe ===");
        sb.AppendLine("time=" + DateTime.Now.ToString("o"));
        AppendGraphics(sb);
        AppendKnownPoiyomiShaders(sb);

        var filter = (ShaderNameFilter ?? string.Empty).Trim();
        var renderers = IncludeInactive
            ? UnityEngine.Object.FindObjectsOfType<Renderer>(true)
            : UnityEngine.Object.FindObjectsOfType<Renderer>(false);

        var byShader = new Dictionary<Shader, List<MaterialHit>>(16);
        var matSeen = new HashSet<int>();
        var inventory = new List<string>(64);
        var nullMatSlots = 0;
        var totalSlots = 0;

        for (var r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            Material[] mats = null;
            try
            {
                mats = renderer.sharedMaterials;
            }
            catch
            {
                // ignore
            }

            // Prefer sharedMaterials only — renderer.materials instantiates copies and can
            // permanently replace shared slots with leaked instances.
            if (mats == null || mats.Length == 0)
            {
                continue;
            }

            var path = GetPath(renderer.transform);
            for (var m = 0; m < mats.Length; m++)
            {
                totalSlots++;
                var mat = mats[m];
                if (mat == null)
                {
                    nullMatSlots++;
                    inventory.Add(
                        "renderer='" + path + "' slot=" + m + " mat=<null>");
                    continue;
                }

                var shader = mat.shader;
                var shaderName = shader != null ? (shader.name ?? string.Empty) : "<null shader>";
                inventory.Add(
                    "renderer='" + path + "' slot=" + m
                    + " mat='" + mat.name + "' shader='" + shaderName + "'");

                if (shader == null)
                {
                    continue;
                }

                if (filter.Length > 0
                    && shaderName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var id = mat.GetInstanceID();
                if (!matSeen.Add(id))
                {
                    continue;
                }

                if (!byShader.TryGetValue(shader, out var list))
                {
                    list = new List<MaterialHit>(8);
                    byShader[shader] = list;
                }

                list.Add(new MaterialHit
                {
                    Material = mat,
                    RendererPath = path,
                    Slot = m,
                });
            }
        }

        sb.AppendLine(
            "filter='" + filter + "' renderers=" + renderers.Length
            + " matSlots=" + totalSlots
            + " nullSlots=" + nullMatSlots
            + " uniqueMats=" + matSeen.Count
            + " uniqueShaders=" + byShader.Count);
        sb.AppendLine();

        if (byShader.Count == 0)
        {
            sb.AppendLine("No matching materials for filter.");
            sb.AppendLine("--- scene material inventory (all slots) ---");
            var invLimit = Math.Min(inventory.Count, 64);
            for (var i = 0; i < invLimit; i++)
            {
                sb.AppendLine(inventory[i]);
            }

            if (inventory.Count > invLimit)
            {
                sb.AppendLine("... +" + (inventory.Count - invLimit) + " more");
            }

            if (inventory.Count == 0)
            {
                sb.AppendLine("(no renderer material slots found)");
            }

            sb.AppendLine("Tip: clear Shader name filter, or load character with Poiyomi mats.");
            sb.AppendLine("=== end probe ===");
            return sb.ToString();
        }

        var detailed = 0;
        foreach (var kv in byShader)
        {
            var shader = kv.Key;
            var hits = kv.Value;
            sb.AppendLine("--- shader: " + shader.name + " (mats=" + hits.Count + ") ---");

            AppendSvcPassTypes(sb, shader, hits[0].Material);

            if (detailed >= MaxMaterialsDetail)
            {
                sb.AppendLine("(skip per-mat detail; raise MaxMaterialsDetail)");
                sb.AppendLine();
                continue;
            }

            var take = Math.Min(hits.Count, Math.Max(1, MaxMaterialsDetail - detailed));
            for (var i = 0; i < take; i++)
            {
                AppendMaterialPasses(sb, hits[i]);
                detailed++;
            }

            sb.AppendLine();
        }

        sb.AppendLine("=== end probe ===");
        return sb.ToString();
    }

    private static void AppendKnownPoiyomiShaders(StringBuilder sb)
    {
        // Shader loaded via UMod may exist even before avatar assigns mats.
        string[] candidates =
        {
            ".poiyomi/Poiyomi Toon",
            ".poiyomi/Poiyomi Toon Early Outline",
            ".poiyomi/Poiyomi Toon Grab Pass",
            "Hidden/Poiyomi/Poiyomi Toon",
        };

        sb.AppendLine("Shader.Find (known Poiyomi names):");
        for (var i = 0; i < candidates.Length; i++)
        {
            var name = candidates[i];
            var found = Shader.Find(name);
            sb.AppendLine(
                "  '" + name + "' -> " + (found != null ? "FOUND" : "null"));
        }
    }

    private static void AppendGraphics(StringBuilder sb)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine(
            "renderPipeline=" + (rp != null ? rp.GetType().Name : "Built-in (null SRP)"));
        sb.AppendLine(
            "quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]
            + " shadows=" + QualitySettings.shadows
            + " pixelLightCount=" + QualitySettings.pixelLightCount
            + " realtimeReflectionProbes=" + QualitySettings.realtimeReflectionProbes);

        // QualitySettings.renderingPath removed in newer Unity; use Camera APIs.
        var cams = UnityEngine.Object.FindObjectsOfType<Camera>(true);
        sb.AppendLine("cameras=" + cams.Length);
        var camLimit = Math.Min(cams.Length, 8);
        for (var i = 0; i < camLimit; i++)
        {
            var cam = cams[i];
            if (cam == null)
            {
                continue;
            }

            sb.AppendLine(
                "  cam='" + GetPath(cam.transform)
                + "' enabled=" + cam.enabled
                + " renderingPath=" + cam.renderingPath
                + " actual=" + cam.actualRenderingPath);
        }

        sb.AppendLine(
            "lights(scene)=" + UnityEngine.Object.FindObjectsOfType<Light>(true).Length);
    }

    private static void AppendSvcPassTypes(StringBuilder sb, Shader shader, Material sample)
    {
        sb.AppendLine("SVC PassType accept (empty keywords):");
        var svc = new ShaderVariantCollection();
        var ok = new List<string>(8);
        var fail = new List<string>(8);

        for (var i = 0; i < ProbePassTypes.Length; i++)
        {
            var pt = ProbePassTypes[i];
            if (TryAddVariant(svc, shader, pt, Array.Empty<string>()))
            {
                ok.Add(pt.ToString());
            }
            else
            {
                fail.Add(pt.ToString());
            }
        }

        sb.AppendLine("  OK: " + string.Join(", ", ok));
        sb.AppendLine("  FAIL: " + string.Join(", ", fail));

        // Also try with material's enabled keywords (closer to warm path).
        string[] kws;
        try
        {
            kws = sample.shaderKeywords ?? Array.Empty<string>();
        }
        catch
        {
            kws = Array.Empty<string>();
        }

        sb.AppendLine("material keywords (" + kws.Length + "): " + string.Join(" ", kws));
        sb.AppendLine("SVC PassType accept (material keywords):");
        ok.Clear();
        fail.Clear();
        var svc2 = new ShaderVariantCollection();
        for (var i = 0; i < ProbePassTypes.Length; i++)
        {
            var pt = ProbePassTypes[i];
            if (TryAddVariant(svc2, shader, pt, kws))
            {
                ok.Add(pt.ToString());
            }
            else
            {
                fail.Add(pt.ToString());
            }
        }

        sb.AppendLine("  OK: " + string.Join(", ", ok));
        sb.AppendLine("  FAIL: " + string.Join(", ", fail));

        UnityEngine.Object.Destroy(svc);
        UnityEngine.Object.Destroy(svc2);
    }

    private static void AppendMaterialPasses(StringBuilder sb, MaterialHit hit)
    {
        var mat = hit.Material;
        sb.AppendLine(
            "mat='" + mat.name + "' renderer='" + hit.RendererPath
            + "' slot=" + hit.Slot
            + " queue=" + mat.renderQueue
            + " passCount=" + mat.passCount);

        string[] kws;
        try
        {
            kws = mat.shaderKeywords ?? Array.Empty<string>();
        }
        catch
        {
            kws = Array.Empty<string>();
        }

        sb.AppendLine("  keywords (" + kws.Length + "): " + string.Join(" ", kws));
        AppendTextureStSample(sb, mat);

        for (var p = 0; p < mat.passCount; p++)
        {
            string passName;
            try
            {
                passName = mat.GetPassName(p);
            }
            catch (Exception e)
            {
                passName = "<GetPassName fail: " + e.Message + ">";
            }

            var enabled = true;
            try
            {
                if (!string.IsNullOrEmpty(passName))
                {
                    enabled = mat.GetShaderPassEnabled(passName);
                }
            }
            catch
            {
                // Some pass names may not map.
            }

            var setPass = false;
            try
            {
                setPass = mat.SetPass(p);
            }
            catch
            {
                setPass = false;
            }

            sb.AppendLine(
                "  pass[" + p + "] name='" + passName
                + "' enabled=" + enabled
                + " SetPass=" + setPass);
        }
    }

    private static readonly string[] TextureStProbeSlots =
    {
        "_MainTex",
        "_EmissionMap",
        "_EmissionMap1",
        "_EmissionMask",
        "_EmissionMask1",
        "_DissolveDetailNoise",
        "_DissolveNoiseTexture",
        "_GlitterTexture",
        "_Matcap",
    };

    private static void AppendTextureStSample(StringBuilder sb, Material mat)
    {
        if (mat == null)
        {
            return;
        }

        var any = false;
        for (var i = 0; i < TextureStProbeSlots.Length; i++)
        {
            var slot = TextureStProbeSlots[i];
            if (!mat.HasProperty(slot))
            {
                continue;
            }

            var tex = mat.GetTexture(slot);
            if (tex == null)
            {
                continue;
            }

            var scale = mat.GetTextureScale(slot);
            var offset = mat.GetTextureOffset(slot);
            if (!any)
            {
                sb.AppendLine("  texture ST (non-null slots):");
                any = true;
            }

            sb.AppendLine(
                "    " + slot
                + " tex='" + tex.name + "'"
                + " scale=(" + scale.x + "," + scale.y + ")"
                + " offset=(" + offset.x + "," + offset.y + ")"
                + " wrap=(" + tex.wrapModeU + "," + tex.wrapModeV + ")");
        }

        if (!any)
        {
            sb.AppendLine("  texture ST: (no probed slots with textures)");
        }
    }

    private static bool TryAddVariant(
        ShaderVariantCollection collection,
        Shader shader,
        PassType passType,
        string[] keywords
    )
    {
        try
        {
            var variant = new ShaderVariantCollection.ShaderVariant(
                shader,
                passType,
                keywords ?? Array.Empty<string>());
            collection.Add(variant);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
        {
            return "?";
        }

        var parts = new List<string>(8);
        var cur = t;
        while (cur != null)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string TruncateForUi(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }

        return text.Substring(0, max) + "\n... truncated; full text in Player.log";
    }

    private struct MaterialHit
    {
        public Material Material;
        public string RendererPath;
        public int Slot;
    }
}
