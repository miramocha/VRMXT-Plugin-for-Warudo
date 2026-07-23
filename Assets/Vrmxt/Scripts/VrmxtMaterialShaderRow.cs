using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Data;

/// <summary>
/// One Character material row for <see cref="VrmxtManagerAsset"/>:
/// material autocomplete + shader autocomplete.
/// Top-level type — nested StructuredData under UMod hits duplicate type-ID registration.
/// </summary>
public sealed class VrmxtMaterialShaderRow : StructuredData, ICollapsibleStructuredData
{
    [DataInput]
    [Label("Material")]
    [AutoComplete(nameof(AutoCompleteMaterial), forceSelection: true)]
    public string MaterialName = string.Empty;

    [DataInput]
    [Label("Shader")]
    [AutoComplete(nameof(AutoCompleteShader), forceSelection: false)]
    public string ShaderName = string.Empty;

    /// <summary>Not shown; carried for export match when known.</summary>
    [DataInput]
    [Hidden]
    public int GltfMaterialIndex = -1;

    public UniTask<AutoCompleteList> AutoCompleteMaterial()
    {
        var names = VrmxtMaterialsShaderAuthoring.LastMaterialNames;
        var entries = new List<AutoCompleteEntry>();
        if (names != null)
        {
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                entries.Add(new AutoCompleteEntry
                {
                    label = name,
                    value = name,
                });
            }
        }

        if (!string.IsNullOrEmpty(MaterialName))
        {
            var hasCurrent = false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].value, MaterialName, StringComparison.Ordinal))
                {
                    hasCurrent = true;
                    break;
                }
            }

            if (!hasCurrent)
            {
                entries.Add(new AutoCompleteEntry
                {
                    label = MaterialName,
                    value = MaterialName,
                });
            }
        }

        return UniTask.FromResult(AutoCompleteList.Single(entries));
    }

    public UniTask<AutoCompleteList> AutoCompleteShader()
    {
        var names = VrmxtShaderInventory.CollectRelevantShaderNames();
        if (!string.IsNullOrEmpty(ShaderName))
        {
            var hasCurrent = false;
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], ShaderName, StringComparison.Ordinal))
                {
                    hasCurrent = true;
                    break;
                }
            }

            if (!hasCurrent)
            {
                names.Add(ShaderName);
                names.Sort(StringComparer.Ordinal);
            }
        }

        var entries = new List<AutoCompleteEntry>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            entries.Add(new AutoCompleteEntry
            {
                label = name,
                value = name,
            });
        }

        return UniTask.FromResult(AutoCompleteList.Single(entries));
    }

    public string GetHeader()
    {
        if (string.IsNullOrEmpty(MaterialName))
        {
            return "(material)";
        }

        if (string.IsNullOrEmpty(ShaderName))
        {
            return MaterialName;
        }

        return MaterialName + " · " + ShaderName;
    }
}
