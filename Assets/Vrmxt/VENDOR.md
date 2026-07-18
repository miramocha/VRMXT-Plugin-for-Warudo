# Vendored UniVRMXT sources

Copied from sibling repo `D:\MiraGameDev\UniVRMXT` for Warudo plugin packaging.

Warudo plugin mods cannot ship UPM packages, DLLs, or `.asmdef` files. These files are
plain C# under the mod folder so UMod exports them with the plugin.

**Keep vendored files byte-identical to UniVRMXT** (`Runtime/Format` + `Runtime/Vfx`
subset below, plus packaged particle shader/material). Do not fork logic in this tree —
change UniVRMXT first, then re-copy. Namespaces match source: `UniVRMXT.Format`,
`UniVRMXT.Vfx`. Host scripts under `Assets/Vrmxt/Scripts/` stay global and `using`
those namespaces.

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
`Shader.Find`; packaged `UniVRMXT/Particles Unlit` via Resources).

Warudo humanoid normalize zeros bone local rotations. Host (not UniVRMXT) applies
`VrmxtWarudoBoneAxisCorrection` after attach so emitter local +Y matches glTF
node rest (UniVRM/Blender), not Warudo's identity bone frame. Uses **ReverseX**
(VRM 1.0 / `Vrm10Importer`), not ReverseZ (VRM 0).

| Item | Value |
|------|--------|
| Source | UniVRMXT `Runtime/Format` + `Runtime/Vfx` + particle shader/Resources |
| Commit | *(uncommitted UniVRMXT working tree — pin after UniVRMXT commit)* |
| Date | 2026-07-18 |

## Included

- Format: `VrmxtVfx.cs`, `GlbChunks.cs`, `GltfImageBytes.cs`
- Vfx: Runtime, Instance, Mapper, Data, Importer, GlbTextures, NodeResolver, OwnedParticleMaterial
- Shaders: `Shaders/UniVRMXTParticlesUnlit.shader` (`UniVRMXT/Particles Unlit`)
- Resources: `Resources/UniVRMXT/ParticlesUnlit.mat` (keeps shader in mod/player builds)

## Excluded (sync later if needed)

- `VrmxtVfxExporter.cs`
- Editor hooks / AssetPostprocessor
- `VrmxtMaterialsOverride.cs` (materials override feature)
- Tests

To refresh: copy the listed files from UniVRMXT at a known commit and update this table.
