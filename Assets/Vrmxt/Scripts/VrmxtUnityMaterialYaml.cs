using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UniVRMXT.Format;

/// <summary>
/// Parse Unity <c>.mat</c> YAML text into VRMXT property rows (no Material load).
/// Skips textures and shader GUID; Transfer applies values only.
/// </summary>
public static class VrmxtUnityMaterialYaml
{
    private static readonly Regex ListFloatOrInt = new Regex(
        @"^\s*-\s+([^\s:]+)\s*:\s*([-+0-9.eE]+)\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex ListColor = new Regex(
        @"^\s*-\s+([^\s:]+)\s*:\s*\{r:\s*([-+0-9.eE]+),\s*g:\s*([-+0-9.eE]+),\s*b:\s*([-+0-9.eE]+),\s*a:\s*([-+0-9.eE]+)\}\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex ListKeyword = new Regex(
        @"^\s*-\s+([A-Za-z0-9_]+)\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex TexEnvName = new Regex(
        @"^\s*-\s+([^\s:]+)\s*:\s*$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Extract scalar / vector / shaderFeature properties from Unity material YAML.
    /// Does not resolve <c>m_Shader</c> GUID. Skips <c>m_TexEnvs</c>.
    /// </summary>
    public static bool TryParseProperties(
        string yaml,
        out List<VrmxtMaterialProperty> properties,
        out List<string> textureSlotNames,
        out string error
    )
    {
        properties = new List<VrmxtMaterialProperty>();
        textureSlotNames = new List<string>();
        error = null;

        if (string.IsNullOrWhiteSpace(yaml))
        {
            error = "Material YAML is empty.";
            return false;
        }

        if (yaml.IndexOf("Material:", StringComparison.Ordinal) < 0)
        {
            error = "Not a Unity Material YAML file.";
            return false;
        }

        ParseFloatSection(yaml, "m_Floats:", properties);
        ParseFloatSection(yaml, "m_Ints:", properties);
        ParseColorSection(yaml, properties);
        ParseKeywordSection(yaml, "m_ValidKeywords:", true, properties);
        ParseKeywordSection(yaml, "m_InvalidKeywords:", false, properties);
        ParseTexEnvNames(yaml, textureSlotNames);
        return true;
    }

    private static void ParseFloatSection(
        string yaml,
        string sectionHeader,
        List<VrmxtMaterialProperty> properties
    )
    {
        foreach (var line in EnumerateSectionLines(yaml, sectionHeader))
        {
            var match = ListFloatOrInt.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (
                !float.TryParse(
                    match.Groups[2].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value
                )
            )
            {
                continue;
            }

            properties.Add(
                new VrmxtMaterialProperty(
                    match.Groups[1].Value,
                    VrmxtMaterialsOverride.TargetTypeScalar,
                    value,
                    null,
                    null,
                    null
                )
            );
        }
    }

    private static void ParseColorSection(string yaml, List<VrmxtMaterialProperty> properties)
    {
        foreach (var line in EnumerateSectionLines(yaml, "m_Colors:"))
        {
            var match = ListColor.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (
                !TryParseFloat(match.Groups[2].Value, out var r)
                || !TryParseFloat(match.Groups[3].Value, out var g)
                || !TryParseFloat(match.Groups[4].Value, out var b)
                || !TryParseFloat(match.Groups[5].Value, out var a)
            )
            {
                continue;
            }

            properties.Add(
                new VrmxtMaterialProperty(
                    match.Groups[1].Value,
                    VrmxtMaterialsOverride.TargetTypeVector,
                    null,
                    new[] { r, g, b, a },
                    null,
                    null
                )
            );
        }
    }

    private static void ParseKeywordSection(
        string yaml,
        string sectionHeader,
        bool enabled,
        List<VrmxtMaterialProperty> properties
    )
    {
        foreach (var line in EnumerateSectionLines(yaml, sectionHeader))
        {
            var match = ListKeyword.Match(line);
            if (!match.Success)
            {
                continue;
            }

            properties.Add(
                new VrmxtMaterialProperty(
                    match.Groups[1].Value,
                    VrmxtMaterialsOverride.TargetTypeShaderFeature,
                    null,
                    null,
                    enabled,
                    null
                )
            );
        }
    }

    private static void ParseTexEnvNames(string yaml, List<string> textureSlotNames)
    {
        foreach (var line in EnumerateSectionLines(yaml, "m_TexEnvs:"))
        {
            var match = TexEnvName.Match(line);
            if (!match.Success)
            {
                continue;
            }

            textureSlotNames.Add(match.Groups[1].Value);
        }
    }

    private static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    /// <summary>
    /// Lines under a Unity YAML block header until the next sibling <c>m_*</c> key.
    /// </summary>
    private static IEnumerable<string> EnumerateSectionLines(string yaml, string sectionHeader)
    {
        var lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inSection = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!inSection)
            {
                if (line.TrimStart().StartsWith(sectionHeader, StringComparison.Ordinal))
                {
                    inSection = true;
                }

                continue;
            }

            var trimmed = line.TrimStart();
            if (
                trimmed.StartsWith("m_", StringComparison.Ordinal)
                && trimmed.EndsWith(":", StringComparison.Ordinal)
                && !trimmed.StartsWith("m_Texture", StringComparison.Ordinal)
                && !trimmed.StartsWith("m_Scale", StringComparison.Ordinal)
                && !trimmed.StartsWith("m_Offset", StringComparison.Ordinal)
            )
            {
                // Next top-level Material field (2-space indent typical).
                if (line.Length - trimmed.Length <= 2)
                {
                    yield break;
                }
            }

            yield return line;
        }
    }
}
