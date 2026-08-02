using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;

/// <summary>
/// One Character material row for <see cref="VrmxtManagerAsset"/>:
/// material autocomplete + shader autocomplete + YAML template Transfer (values only).
/// Top-level type — nested StructuredData under UMod hits duplicate type-ID registration.
/// <see cref="StructuredData{T}.Parent"/> is often null under UMod for top-level rows;
/// use <see cref="BindManager"/> / scene lookup instead.
/// </summary>
public sealed class VrmxtMaterialShaderRow
    : StructuredData<VrmxtManagerAsset>,
        ICollapsibleStructuredData
{
    private VrmxtManagerAsset _manager;

    [DataInput]
    [Label("Material")]
    [AutoComplete(nameof(AutoCompleteMaterial), forceSelection: true)]
    public string MaterialName = string.Empty;

    [DataInput]
    [Label("Shader")]
    [Description("Set and Apply shader before Transfer. Transfer does not change Shader.")]
    [AutoComplete(nameof(AutoCompleteShader), forceSelection: false)]
    public string ShaderName = string.Empty;

    [DataInput]
    [Label("Material template")]
    [Description(
        "StreamingAssets path to a Unity .mat YAML "
            + "(e.g. VRMXT/MaterialTemplates/MyLook.mat). Transfer copies floats, colors, "
            + "and keywords only — not shader."
    )]
    [AutoComplete(nameof(AutoCompleteTemplate), forceSelection: false)]
    public string TemplateAssetPath = string.Empty;

    [DataInput]
    [Label("Texture handling")]
    [Description(
        "Keep packed: leave override texture rows. Clear if set: drop texture slots that "
            + "already have a map on the live material. Clear all: drop every texture slot."
    )]
    [AutoComplete(nameof(AutoCompleteTextureHandling), forceSelection: true)]
    public string TextureHandling = VrmxtMaterialsTemplateTransfer.TextureHandlingKeepPacked;

    [Markdown]
    [Label("Template textures")]
    [HiddenIf(nameof(HideTemplateTextures))]
    public string TemplateTextureSlots = "Template textures: (none)";

    [DataInput]
    [Hidden]
    public int GltfMaterialIndex = -1;

    public void BindManager(VrmxtManagerAsset manager)
    {
        _manager = manager;
    }

    [Trigger]
    [Label("Transfer from template")]
    [Description(
        "Parse this row's .mat YAML and merge values into VRMXT override JSON, then Apply. "
            + "Shader is unchanged. Texture handling controls packed maps. Set Shader first."
    )]
    [DisabledIf(nameof(DisableTransferFromTemplate))]
    public void TransferFromTemplate()
    {
        var manager = ResolveManager();
        if (manager == null)
        {
            Debug.LogWarning(
                "VRMXT: Transfer from template — no VRMXT Manager found for this row."
            );
            return;
        }

        manager.TransferSingleTemplate(this);
    }

    private VrmxtManagerAsset ResolveManager()
    {
        if (Parent != null)
        {
            return Parent;
        }

        if (_manager != null)
        {
            return _manager;
        }

        return FindOwningManager();
    }

    private VrmxtManagerAsset FindOwningManager()
    {
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            return null;
        }

        var assets = scene.GetAssets<VrmxtManagerAsset>();
        if (assets == null)
        {
            return null;
        }

        for (var i = 0; i < assets.Count; i++)
        {
            var manager = assets[i];
            if (manager == null || manager.Materials == null)
            {
                continue;
            }

            for (var m = 0; m < manager.Materials.Length; m++)
            {
                if (ReferenceEquals(manager.Materials[m], this))
                {
                    return manager;
                }
            }
        }

        return null;
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        Watch(nameof(TemplateAssetPath), RefreshTemplateTextureSlots);
        RefreshTemplateTextureSlots();
    }

    protected override void OnAssignedParent()
    {
        base.OnAssignedParent();
        if (Parent != null)
        {
            _manager = Parent;
        }

        RefreshTemplateTextureSlots();
    }

    protected bool DisableTransferFromTemplate() =>
        string.IsNullOrWhiteSpace(TemplateAssetPath)
        || string.IsNullOrWhiteSpace(MaterialName);

    protected bool HideTemplateTextures() => string.IsNullOrWhiteSpace(TemplateAssetPath);

    public void RefreshTemplateTextureSlots()
    {
        if (string.IsNullOrWhiteSpace(TemplateAssetPath))
        {
            TemplateTextureSlots = "Template textures: (none)";
            BroadcastDataInput(nameof(TemplateTextureSlots));
            return;
        }

        TemplateTextureSlots = VrmxtMaterialsTemplateTransfer.FormatTemplateTextureSlotsLabel(
            TemplateAssetPath
        );
        BroadcastDataInput(nameof(TemplateTextureSlots));
    }

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

                entries.Add(new AutoCompleteEntry { label = name, value = name });
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
                entries.Add(new AutoCompleteEntry { label = MaterialName, value = MaterialName });
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
            entries.Add(new AutoCompleteEntry { label = name, value = name });
        }

        return UniTask.FromResult(AutoCompleteList.Single(entries));
    }

    public UniTask<AutoCompleteList> AutoCompleteTemplate()
    {
        var paths = VrmxtMaterialsTemplateTransfer.ListTemplatePaths();
        if (
            !string.IsNullOrEmpty(TemplateAssetPath)
            && !paths.Contains(TemplateAssetPath)
        )
        {
            paths.Add(TemplateAssetPath);
            paths.Sort(StringComparer.Ordinal);
        }

        var entries = new List<AutoCompleteEntry>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var slash = path.LastIndexOf('/');
            var label = slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
            entries.Add(new AutoCompleteEntry { label = label, value = path });
        }

        return UniTask.FromResult(AutoCompleteList.Single(entries));
    }

    public UniTask<AutoCompleteList> AutoCompleteTextureHandling()
    {
        var modes = VrmxtMaterialsTemplateTransfer.TextureHandlingOptions;
        var entries = new List<AutoCompleteEntry>(modes.Length);
        for (var i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            entries.Add(new AutoCompleteEntry { label = mode, value = mode });
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
