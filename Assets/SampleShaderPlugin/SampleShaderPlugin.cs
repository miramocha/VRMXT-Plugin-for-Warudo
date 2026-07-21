using System;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

/// <summary>
/// Example shader-only Warudo mod: warm ShaderLab via <c>ModHost.Assets.Load</c>.
/// Pattern for packs such as Liltoon. VRMXT <c>ShaderResolveProvider</c> scans
/// loaded Shader assets when <c>Shader.Find</c> is null (uMod ModHost shaders).
/// </summary>
[PluginType(
    Id = "mira.vrmxt.sampleshader",
    Name = "VRMXT Sample Shader",
    Description = "Example shader-only plugin (ModHost.Assets.Load). Not Liltoon.",
    Version = "1.0.0",
    Author = "Mira",
    SupportUrl = "https://github.com/miramocha/VRMXT-Plugin-for-Warudo"
)]
public sealed class SampleShaderPlugin : Plugin
{
    /// <summary>
    /// Mod-folder path (Warudo handbook: load via <see cref="Plugin.ModHost"/>, not
    /// <c>Resources.Load</c>).
    /// </summary>
    public const string SampleShaderAssetPath =
        "Assets/SampleShaderPlugin/Shaders/SampleExternalOverride.shader";

    private Shader _sampleShader;

    protected override void OnCreate()
    {
        base.OnCreate();
        WarmSampleShader();
    }

    protected override void OnDestroy()
    {
        _sampleShader = null;
        base.OnDestroy();
    }

    private void WarmSampleShader()
    {
        _sampleShader = null;

        try
        {
            _sampleShader = ModHost.Assets.Load<Shader>(SampleShaderAssetPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "VRMXT Sample Shader: ModHost.Assets.Load failed at '" +
                SampleShaderAssetPath + "': " + e.Message);
            return;
        }

        if (_sampleShader == null)
        {
            Debug.LogWarning(
                "VRMXT Sample Shader: ModHost.Assets.Load null at '" +
                SampleShaderAssetPath + "'.");
            return;
        }

        Debug.Log(
            "VRMXT Sample Shader: loaded '" + _sampleShader.name +
            "' from '" + SampleShaderAssetPath + "'.");
    }
}
