# Vendored UniVRMXT sources

Copied from sibling repo `D:\MiraGameDev\UniVRMXT` for Warudo plugin packaging.

Warudo plugin mods cannot ship UPM packages, DLLs, or `.asmdef` files. These files are
plain C# under the mod folder so UMod exports them with the plugin.

**Keep vendored files byte-identical to UniVRMXT** (`Runtime/Format` + `Runtime/Vfx` +
`Runtime/MaterialsOverride` subset below, plus `VrmxtInstance`, plus packaged
shaders/materials). Do not fork logic in this tree — change UniVRMXT first, then
re-copy. Namespaces match source: `UniVRMXT`, `UniVRMXT.Format`, `UniVRMXT.Vfx`,
`UniVRMXT.MaterialsOverride`. Host scripts under `Assets/Vrmxt/Scripts/` stay global
and `using` those namespaces.

UMod `referencePaths` is for other **mods**, not UnityEngine DLLs. Do not put
`UnityEngine.CoreModule.dll` there (resolves as "referenced mod" and fails).

UMod compile has `UnityEngine.dll` forwarders but not CoreModule. Reading Warudo
API members typed as UnityEngine types (`CharacterAsset.GameObject`,
`OnActiveStateChange` / `UnityEvent`) → CS0012. Host code must avoid those
members (name-find GameObject; poll active). Own `using UnityEngine` types OK.

UMod **code security** also bans `System.Reflection` (includes `GetType().Name`).
Build failures after "Compile successful!" → check
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\VRMXT Plugin for Warudo\uMod Exporter 2.0\Build.log`
and `%LOCALAPPDATA%\Unity\Editor\Editor.log`. Keep Mod Settings
**Clear Console On Build** unchecked so console keeps errors. Shared UniVRMXT
mapper already avoids that API (`GraphicsSettings.currentRenderPipeline == null` +
`Shader.Find`; packaged `VRMXT/Particles Unlit`). Materials `DetectActivePipeline`
uses `Object.ToString()` (not `GetType`); Warudo host prefers
`VrmxtCharacterApply.DetectActivePipelineForWarudo` (null → Builtin, else Urp).
Applier uses `ShaderResolveProvider` (ModHost-loaded name→Shader map) because uMod
shaders load into memory but `Shader.Find` still returns null. Sample
`TestOverrideURP` is CG/`SRPDefaultUnlit` (no URP package includes) so the BIRP-only
mod project can ship a pass that Warudo URP actually draws.

Warudo handbook ([Plugin Mod](https://docs.warudo.app/docs/scripting/plugin-mod),
[Plugins — Loading Unity Assets](https://docs.warudo.app/docs/scripting/api/plugins)):
place shaders/materials **inside the mod folder** (`Assets/Vrmxt/…`) and load at runtime
with **`ModHost.Assets.Load`**, not `Resources.Load` (Unity Resources cannot see uMod
assets). `VrmxtPlugin` binds the packaged particle mat and warms sample materials-override
shaders/mats so Applier `Shader.Find` can resolve them. Sets
`PreferPackagedParticleMaterial` so transparent ShaderLab is used instead of host BIRP
`Shader.Find` names that may lack alpha.

Editor project uses URP (`Assets/Settings/VrmxtUniversalRP` +
`com.unity.render-pipelines.universal` 12.1.15) so URP sample shaders can compile.
Keep pipeline assets **outside** `Assets/Vrmxt` so they are not exported into the mod.

## Local ExportSettings (do not commit)

`Assets/ExportSettings.asset` holds **machine-specific** `modAssetPath` /
`modExportPath`. It is **gitignored**. Copy `umod/ExportSettings.example.asset` →
`Assets/ExportSettings.asset` and replace `REPLACE_ME` paths (or use Warudo → Mod
Settings in the Editor).

UMod sometimes deletes or empties `ExportSettings` when scripts change and Unity
regains focus. Keep a local twin `Assets/ExportSettings.asset.old` (gitignored;
UMod does not touch it) via `umod/export-settings.ps1 -Backup` / `-Restore`.
See `umod/README.md`.

Warudo humanoid normalize zeros bone local rotations. Host (not UniVRMXT) applies
`VrmxtWarudoBoneAxisCorrection` after attach so emitter local +Y matches glTF
node rest (UniVRM/Blender), not Warudo's identity bone frame. Uses **ReverseX**
(VRM 1.0 / `Vrm10Importer`), not ReverseZ (VRM 0).

| Item | Value |
|------|--------|
| Source | UniVRMXT `Runtime/Format` + `Runtime/Vfx` + `Runtime/MaterialsOverride` + `Runtime/VrmxtInstance` + particle/sample shaders |
| Commit | `b5b91da` (`fix/detect-active-pipeline-no-reflection`; `(Instance)` strip + `SRPDefaultUnlit` URP sample) |
| Date | 2026-07-19 |

## Included

- Format: `VrmxtVfx.cs`, `VrmxtMaterialsOverride.cs`, `GlbChunks.cs`, `GltfImageBytes.cs`
- Vfx: Runtime, Instance, Mapper, Data, Importer, GlbTextures, NodeResolver, OwnedParticleMaterial
- MaterialsOverride: Runtime, Applier, Instance, UnityOverrideSelector, Authoring, Exporter
  (Exporter is a compile dep of Authoring `ResolveUnityVariant`; Warudo does not call export)
- Facade: `VrmxtInstance.cs`
- Shaders: `Shaders/VrmxtParticlesUnlit.shader` (`VRMXT/Particles Unlit`),
  `Shaders/VrmxtTestOverrideBuiltin.shader`, `Shaders/VrmxtTestOverrideURP.shader`
- Resources: `Resources/UniVRMXT/ParticlesUnlit.mat`,
  `VrmxtTestOverrideBuiltin.mat`, `VrmxtTestOverrideURP.mat`, `VrmxtTestTexture.png`

## Excluded (sync later if needed)

- `VrmxtVfxExporter.cs`
- `VrmxtMaterialsOverrideGenerator.cs` (Warudo has no `IMaterialDescriptorGenerator` inject)
- Editor hooks / AssetPostprocessor
- Tests

To refresh: copy the listed files from UniVRMXT at a known commit and update this table.
