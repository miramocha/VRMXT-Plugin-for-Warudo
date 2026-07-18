# Vendored UniVRMXT sources

Copied from sibling repo `D:\MiraGameDev\UniVRMXT` for Warudo plugin packaging.

Warudo plugin mods cannot ship UPM packages, DLLs, or `.asmdef` files. These files are
plain C# under the mod folder so UMod exports them with the plugin.

Namespaces are stripped to global (Warudo handbook examples and working local plugins
use global namespace; no `.asmdef` assembly boundary).

| Item | Value |
|------|--------|
| Source | UniVRMXT `Runtime/Format` + `Runtime/Vfx` |
| Commit | `39f9a87` |
| Date | 2026-07-18 |

## Included

- Format: `VrmxtVfx.cs`, `GlbChunks.cs`, `GltfImageBytes.cs`
- Vfx: Runtime, Instance, Mapper, Data, Importer, GlbTextures, NodeResolver, OwnedParticleMaterial

## Excluded (sync later if needed)

- `VrmxtVfxExporter.cs`
- Editor hooks / AssetPostprocessor
- `VrmxtMaterialsOverride.cs` (materials override feature)
- Tests

To refresh: copy the listed files from UniVRMXT at a known commit and update this table.
