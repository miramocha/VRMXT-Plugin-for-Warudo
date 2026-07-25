using System;
using System.Collections.Generic;
using System.Text;
using UniVRMXT.Format;
using UnityEngine;

namespace UniVRMXT.MaterialsOverride
{
    /// <summary>
    /// Console dump: extension JSON vs live renderer materials vs optional Override
    /// Material assets. Use after import / Apply to compare authoring vs applied state.
    /// </summary>
    public static class VrmxtMaterialsOverrideDebug
    {
        public static void Dump(VrmxtMaterialsOverrideInstance store)
        {
            if (store == null)
            {
                Debug.LogWarning("VRMXT materials debug: store is null.");
                return;
            }

            Dump(store.gameObject, store);
        }

        public static void Dump(GameObject root, VrmxtMaterialsOverrideInstance store)
        {
            if (root == null)
            {
                Debug.LogWarning("VRMXT materials debug: root is null.");
                return;
            }

            store ??= root.GetComponent<VrmxtMaterialsOverrideInstance>();
            var pipeline = VrmxtMaterialsOverrideApplier.DetectActivePipeline();
            var sb = new StringBuilder(4096);
            sb.Append("VRMXT materials debug [")
                .Append(root.name)
                .Append("] pipeline=")
                .Append(pipeline)
                .Append(" rememberedTextures=")
                .Append(store?.ImportedTextures != null ? store.ImportedTextures.Count : 0)
                .Append('\n');

            if (store?.Pairs == null || store.Pairs.Count == 0)
            {
                sb.AppendLine("(no store pairs)");
                Debug.Log(sb.ToString());
                return;
            }

            for (var i = 0; i < store.Pairs.Count; i++)
            {
                var pair = store.Pairs[i];
                if (pair == null || string.IsNullOrEmpty(pair.MaterialName))
                {
                    continue;
                }

                sb.Append("--- pair[")
                    .Append(i)
                    .Append("] '")
                    .Append(pair.MaterialName)
                    .Append("' ---\n");

                AppendJsonSummary(sb, pair.ExtensionJson, pipeline);
                AppendMaterialSummary(sb, "source", pair.SourceMaterial);
                AppendMaterialSummary(sb, "overrideMat", pair.OverrideMaterial);
                AppendLiveSummaries(sb, root, pair.MaterialName);
                AppendRememberedTextures(sb, store, pair.ExtensionJson);
            }

            Debug.Log(sb.ToString());
        }

        private static void AppendJsonSummary(
            StringBuilder sb,
            string extensionJson,
            RenderPipelineVariant pipeline
        )
        {
            if (string.IsNullOrWhiteSpace(extensionJson))
            {
                sb.AppendLine("  json: (empty)");
                return;
            }

            if (!VrmxtMaterialsOverride.TryParse(extensionJson, out var extension))
            {
                sb.AppendLine("  json: (parse fail)");
                return;
            }

            if (
                !UnityOverrideSelector.TrySelectUnityEngineOverride(
                    extension,
                    pipeline,
                    out var engineOverride
                )
            )
            {
                sb.AppendLine("  json: (no unity slot for active pipeline)");
                return;
            }

            var unity = engineOverride.Material as UnityMaterialOverride;
            var props = engineOverride.Properties;
            var texNames = new List<string>();
            var propCount = props != null ? props.Count : 0;
            var texCount = 0;
            var hasMainTex = false;
            Color? jsonColor = null;

            if (props != null)
            {
                for (var i = 0; i < props.Count; i++)
                {
                    var p = props[i];
                    if (p == null)
                    {
                        continue;
                    }

                    if (
                        string.Equals(
                            p.Type,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        texCount++;
                        texNames.Add(
                            (p.Name ?? "?")
                                + "@"
                                + (
                                    p.TextureIndex.HasValue
                                        ? p.TextureIndex.Value.ToString()
                                        : "?"
                                )
                        );
                        if (IsMainAlbedoName(p.Name))
                        {
                            hasMainTex = true;
                        }
                    }
                    else if (
                        string.Equals(p.Name, "_Color", StringComparison.Ordinal)
                        && p.VectorValue != null
                        && p.VectorValue.Count >= 3
                    )
                    {
                        jsonColor = new Color(
                            p.VectorValue[0],
                            p.VectorValue[1],
                            p.VectorValue[2],
                            p.VectorValue.Count >= 4 ? p.VectorValue[3] : 1f
                        );
                    }
                }
            }

            var bindings =
                engineOverride.Bindings != null ? engineOverride.Bindings.Count : 0;
            sb.Append("  json: shader='")
                .Append(unity != null ? unity.ShaderName : "(null)")
                .Append("' variant=")
                .Append(unity != null ? unity.Variant ?? "" : "?")
                .Append(" props=")
                .Append(propCount)
                .Append(" texProps=")
                .Append(texCount)
                .Append(" bindings=")
                .Append(bindings)
                .Append(" hasMainTex=")
                .Append(hasMainTex)
                .Append(" _Color=")
                .Append(FormatColor(jsonColor))
                .Append(" tex=[")
                .Append(string.Join(",", texNames))
                .Append("]\n");

            if (texCount > 0 && !hasMainTex)
            {
                sb.AppendLine(
                    "  RISK: JSON texture ownership without _MainTex/_BaseMap "
                        + "(ClearUnlisted wipes stock albedo)."
                );
            }

            if (jsonColor.HasValue && IsNearBlack(jsonColor.Value))
            {
                sb.AppendLine("  RISK: JSON _Color near-black.");
            }
        }

        private static void AppendLiveSummaries(
            StringBuilder sb,
            GameObject root,
            string materialName
        )
        {
            var found = false;
            foreach (
                var material in VrmxtMaterialsOverrideRuntime.FindMaterialsForStoreKey(
                    root,
                    materialName
                )
            )
            {
                if (material == null)
                {
                    continue;
                }

                found = true;
                AppendMaterialSummary(sb, "live", material);
            }

            if (!found)
            {
                sb.AppendLine("  live: (none — name mismatch?)");
            }
        }

        private static void AppendMaterialSummary(StringBuilder sb, string label, Material material)
        {
            if (material == null)
            {
                sb.Append("  ").Append(label).AppendLine(": (null)");
                return;
            }

            var shaderName = material.shader != null ? material.shader.name : "(null shader)";
            ResolveMainTex(material, out var mainName, out var mainTex);
            Color? color = null;
            if (material.HasProperty("_Color"))
            {
                color = material.GetColor("_Color");
            }
            else if (material.HasProperty("_BaseColor"))
            {
                color = material.GetColor("_BaseColor");
            }

            var risks = new List<string>();
            if (mainName != null && mainTex == null)
            {
                risks.Add("mainTex-null");
            }

            if (color.HasValue && IsNearBlack(color.Value))
            {
                risks.Add("color-near-black");
            }

            var keywords =
                material.shaderKeywords != null && material.shaderKeywords.Length > 0
                    ? string.Join(" ", material.shaderKeywords)
                    : "(none)";
            if (keywords.Length > 120)
            {
                keywords = keywords.Substring(0, 117) + "...";
            }

            sb.Append("  ")
                .Append(label)
                .Append(": '")
                .Append(material.name)
                .Append("' id=")
                .Append(material.GetInstanceID())
                .Append(" shader='")
                .Append(shaderName)
                .Append("' ")
                .Append(mainName ?? "(no main slot)")
                .Append("=")
                .Append(mainTex != null ? "'" + mainTex.name + "'" : "null")
                .Append(" _Color=")
                .Append(FormatColor(color))
                .Append(" keywords=[")
                .Append(keywords)
                .Append(']');
            if (risks.Count > 0)
            {
                sb.Append(" RISKS=[").Append(string.Join(", ", risks)).Append(']');
            }

            sb.Append('\n');
            AppendPoiyomiEffectSummary(sb, label, material);
        }

        /// <summary>
        /// Live glitter / scrolling-emission knobs — catches SetColor-on-vector corruption
        /// (<c>_EmissiveScroll_Direction</c> y=-10) and missing <c>_SUNDISK_SIMPLE</c>.
        /// </summary>
        private static void AppendPoiyomiEffectSummary(
            StringBuilder sb,
            string label,
            Material material
        )
        {
            if (material == null)
            {
                return;
            }

            var parts = new List<string>();
            if (material.HasProperty("_GlitterEnable"))
            {
                parts.Add("_GlitterEnable=" + material.GetFloat("_GlitterEnable"));
            }

            var sundisk = material.IsKeywordEnabled("_SUNDISK_SIMPLE");
            parts.Add("_SUNDISK_SIMPLE=" + (sundisk ? "on" : "OFF"));

            if (material.HasProperty("_ScrollingEmission"))
            {
                parts.Add("_ScrollingEmission=" + material.GetFloat("_ScrollingEmission"));
            }

            if (material.HasProperty("_EmissiveScroll_Direction"))
            {
                var d = material.GetVector("_EmissiveScroll_Direction");
                parts.Add(
                    "_EmissiveScroll_Direction=("
                        + d.x.ToString("0.###")
                        + ","
                        + d.y.ToString("0.###")
                        + ","
                        + d.z.ToString("0.###")
                        + ","
                        + d.w.ToString("0.###")
                        + ")"
                );
            }

            if (material.HasProperty("_EmissiveScroll_Velocity"))
            {
                parts.Add(
                    "_EmissiveScroll_Velocity=" + material.GetFloat("_EmissiveScroll_Velocity")
                );
            }

            if (material.HasProperty("_EmissionStrength"))
            {
                parts.Add("_EmissionStrength=" + material.GetFloat("_EmissionStrength"));
            }

            if (material.HasProperty("_Mode"))
            {
                parts.Add("_Mode=" + material.GetFloat("_Mode"));
            }

            if (material.HasProperty("_SrcBlend") && material.HasProperty("_DstBlend"))
            {
                parts.Add(
                    "blend="
                        + material.GetFloat("_SrcBlend")
                        + "/"
                        + material.GetFloat("_DstBlend")
                );
            }

            parts.Add("queue=" + material.renderQueue);
            parts.Add(
                "RenderType="
                    + material.GetTag("RenderType", false, "(none)")
            );

            if (parts.Count == 0)
            {
                return;
            }

            sb.Append("  ")
                .Append(label)
                .Append(" effects: ")
                .Append(string.Join(" ", parts))
                .Append('\n');
        }

        private static void AppendRememberedTextures(
            StringBuilder sb,
            VrmxtMaterialsOverrideInstance store,
            string extensionJson
        )
        {
            if (store == null || string.IsNullOrWhiteSpace(extensionJson))
            {
                return;
            }

            if (
                !VrmxtMaterialsOverride.TryParse(extensionJson, out var extension)
                || !VrmxtMaterialsOverride.TryGetUnityOverrides(extension, out var slots)
            )
            {
                return;
            }

            var indices = new SortedSet<int>();
            for (var s = 0; s < slots.Count; s++)
            {
                var props = slots[s]?.Properties;
                if (props == null)
                {
                    continue;
                }

                for (var i = 0; i < props.Count; i++)
                {
                    var p = props[i];
                    if (
                        p != null
                        && string.Equals(
                            p.Type,
                            VrmxtMaterialsOverride.TargetTypeTexture,
                            StringComparison.Ordinal
                        )
                        && p.TextureIndex.HasValue
                        && p.TextureIndex.Value >= 0
                    )
                    {
                        indices.Add(p.TextureIndex.Value);
                    }
                }
            }

            if (indices.Count == 0)
            {
                sb.AppendLine("  remembered: (no texture indices in JSON)");
                return;
            }

            sb.Append("  remembered:");
            foreach (var index in indices)
            {
                var ok = store.TryGetImportedTexture(index, out var texture) && texture != null;
                sb.Append(" [")
                    .Append(index)
                    .Append("]=")
                    .Append(ok ? "'" + texture.name + "'" : "UNRESOLVED");
            }

            sb.Append('\n');
        }

        private static void ResolveMainTex(Material material, out string name, out Texture texture)
        {
            name = null;
            texture = null;
            if (material.HasProperty("_MainTex"))
            {
                name = "_MainTex";
                texture = material.GetTexture("_MainTex");
                return;
            }

            if (material.HasProperty("_BaseMap"))
            {
                name = "_BaseMap";
                texture = material.GetTexture("_BaseMap");
                return;
            }

            if (material.HasProperty("_BaseColorMap"))
            {
                name = "_BaseColorMap";
                texture = material.GetTexture("_BaseColorMap");
            }
        }

        private static bool IsMainAlbedoName(string name)
        {
            return string.Equals(name, "_MainTex", StringComparison.Ordinal)
                || string.Equals(name, "_BaseMap", StringComparison.Ordinal)
                || string.Equals(name, "_BaseColorMap", StringComparison.Ordinal);
        }

        private static bool IsNearBlack(Color c)
        {
            return c.r <= 0.02f && c.g <= 0.02f && c.b <= 0.02f;
        }

        private static string FormatColor(Color? c)
        {
            if (!c.HasValue)
            {
                return "(none)";
            }

            var v = c.Value;
            return "("
                + v.r.ToString("0.###")
                + ","
                + v.g.ToString("0.###")
                + ","
                + v.b.ToString("0.###")
                + ","
                + v.a.ToString("0.###")
                + ")";
        }
    }
}
