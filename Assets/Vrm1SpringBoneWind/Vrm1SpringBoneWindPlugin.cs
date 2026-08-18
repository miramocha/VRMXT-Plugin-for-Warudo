using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

/// <summary>
/// VRM 1.0 FastSpringBone wind only. Stock VRM Wind covers VRM 0.x.
/// </summary>
[PluginType(
    Id = "mira.vrm1springbonewind",
    Name = "VRM 1 spring bone wind",
    Description = "VRM 1.0 FastSpringBone wind. Stock VRM Wind is VRM 0 only.",
    Version = "0.1.0",
    Author = "Mira",
    SupportUrl = "https://github.com/miramocha/VRMXT-Plugin-for-Warudo",
    AssetTypes = new[] { typeof(Vrm1SpringBoneWindAsset) }
)]
public sealed class Vrm1SpringBoneWindPlugin : Plugin
{
    protected override void OnCreate()
    {
        base.OnCreate();
        Debug.Log("VRM 1 spring bone wind plugin enabled.");
    }
}
