using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

/// <summary>
/// Minimal Hello World clone for UMod smoke test (isolate vs VRMXT CS0012).
/// </summary>
[PluginType(
    Id = "mira.testplugin.helloworld",
    Name = "Test Hello World",
    Description = "Smoke-test plugin (Hello World clone). Not VRMXT.",
    Version = "1.0.0",
    Author = "Mira",
    SupportUrl = "https://docs.warudo.app",
    AssetTypes = new[] { typeof(TestCookieClickerAsset) },
    NodeTypes = new[] { typeof(TestHelloWorldNode) }
)]
public class TestHelloWorldPlugin : Plugin
{
    protected override void OnCreate()
    {
        base.OnCreate();
        Debug.Log("TestHelloWorldPlugin enabled (UMod smoke test).");
    }
}
