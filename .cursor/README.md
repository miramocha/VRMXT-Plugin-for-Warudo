# Cursor configuration

Shared Unity coding and agent guidance for the VRMXT Plugin for Warudo Unity project.

## Shared kit (from Extended-UniVRM)

Shared `unity-*.mdc` rules (except pin language), `handoff-and-git`,
`generated-and-submodules`, and skill `validate-unity-meta` are synced from sibling
[Extended-UniVRM](https://github.com/miramocha/Extended-UniVRM):

```powershell
cd ../Extended-UniVRM
./scripts/sync-vrmxt-cursor-shared.ps1 -Apply
```

Manifest: `Extended-UniVRM/.cursor/shared-manifest.json`.

## Local only (do not overwrite via sync)

| File | Role |
|------|------|
| `rules/unity-csharp-language.mdc` | Unity **2021.3** pin |
| `rules/warudo-plugin-repository.mdc` | Host layout, Warudo Mod Tool constraints |
| `rules/ui-labels.mdc` | User-facing VRMXT label voice (also keep aligned with Warudo Shader Plugins / Blender) |

## Project assumptions

- Unity version: `2021.3.45f2`
- First-party code: under `Assets/` (and local packages under `Packages/` if added)
- Tests are NUnit EditMode assemblies colocated with owning code
- C# and documentation use LF line endings

## Deliberately not synced here

- Extended-UniVRM package paths (`Packages/UniGLTF`, `Packages/VRM`, `Packages/VRM10`)
- UniVRM fork-upstream rules and `UniGLTF.TestRunner` host notes
- CSharpier/Prettier and editor-agent workflows not installed in this repository
- GridDungeon UITK / backlog / story Cursor kits
