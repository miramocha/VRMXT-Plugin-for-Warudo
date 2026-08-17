using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Scenes;

/// <summary>
/// One VRM 1 spring chain. Header is the leaf bone name; Freeze removes the
/// chain (all physics) until cleared. Top-level type — nested StructuredData breaks UMod.
/// </summary>
public sealed class Vrm1SpringBoneSkipRow
    : StructuredData<Vrm1SpringBoneWindAsset>,
        ICollapsibleStructuredData
{
    [DataInput]
    [Hidden]
    public string CharacterId = string.Empty;

    [DataInput]
    [Hidden]
    public string CharacterName = string.Empty;

    [DataInput]
    [Hidden]
    public string TransformPath = string.Empty;

    [Markdown]
    [Label("Transform")]
    [HiddenIf(nameof(HideFullPath))]
    public string PathDisplay = string.Empty;

    [DataInput]
    [Label("Freeze")]
    public bool Freeze;

    public string GetHeader()
    {
        var path = string.IsNullOrEmpty(TransformPath) ? "(unnamed)" : TransformPath;
        var slash = path.LastIndexOf('/');
        var leaf = slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        if (!string.IsNullOrEmpty(CharacterName))
        {
            leaf = CharacterName + "/" + leaf;
        }

        return Freeze ? leaf + " (frozen)" : leaf;
    }

    protected bool HideFullPath() =>
        CollapsedSelf || string.IsNullOrEmpty(TransformPath);

    public void RefreshPathDisplay()
    {
        if (string.IsNullOrEmpty(TransformPath))
        {
            return;
        }

        var boxed = "```\n" + TransformPath + "\n```";
        if (PathDisplay == boxed)
        {
            return;
        }

        SetDataInput(nameof(PathDisplay), boxed, broadcast: true);
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        RefreshPathDisplay();
        Watch(nameof(Freeze), NotifyParent);
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        if (string.IsNullOrEmpty(PathDisplay) && !string.IsNullOrEmpty(TransformPath))
        {
            RefreshPathDisplay();
        }
    }

    private void NotifyParent()
    {
        var parent = Parent ?? FindOwningAsset();
        parent?.OnFreezeRowsChanged();
    }

    private static Vrm1SpringBoneWindAsset FindOwningAsset()
    {
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            return null;
        }

        var assets = scene.GetAssets<Vrm1SpringBoneWindAsset>();
        if (assets == null || assets.Count == 0)
        {
            return null;
        }

        return assets[0];
    }
}
