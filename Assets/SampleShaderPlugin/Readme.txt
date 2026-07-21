# VRMXT Sample Shader (UMod)

Shader-only example mod. Warms packaged ShaderLab with ModHost.Assets.Load on
plugin enable. Copy this folder pattern for packs such as Liltoon.

# Requirements

	uMod version: 2.9.0 or newer
	Unity version: 2021.3.45f2

# Content

	Scene mods: No
	Prefab mods: No
	Script mods: Yes
	Shaders: Yes (under Shaders/)

# Load path

	Assets/SampleShaderPlugin/Shaders/SampleExternalOverride.shader
	ShaderLab name: VRMXT/Samples/ExternalShaderPlugin

Place all export content inside this mod folder. Paths passed to
ModHost.Assets.Load must match the local asset path under the mod folder.

# Export

Use the VRMXTSampleShader profile in Mod Settings (see umod/ExportSettings.example.asset).
Export into Warudo StreamingAssets/Plugins alongside the main VRMXT mod.

Note: loading alone does not register shaders with VRMXT materials-override
resolve (Shader.Find stays null for uMod shaders). Shared register is a follow-up.
