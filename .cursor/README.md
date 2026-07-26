# Cursor configuration

Shared Unity coding and agent guidance for the VRMXT Plugin for Warudo Unity project.

## Canonical shared kit (VRMXT Unity)

This repo is the **canonical source** for shared Unity Cursor rules/skills used by sibling
VRMXT Unity projects (UniVRMXT, VRMXT Unity Player).

Extended-UniVRM is an **upstream UniVRM fork** — do not add `.cursor` rules/skills there.

| Path | Role |
|------|------|
| `.cursor/shared-manifest.json` | Shared rule/skill names + consumer sibling paths |
| `scripts/sync-vrmxt-cursor-shared.ps1` | Copy (or hard-link) shared files into consumers |

```powershell
# From VRMXT Plugin for Warudo root — dry-run (default)
./scripts/sync-vrmxt-cursor-shared.ps1

# Apply copies into listed consumers
./scripts/sync-vrmxt-cursor-shared.ps1 -Apply
```

### Shared (synced to UniVRMXT + Player)

- `unity-csharp-style.mdc`
- `unity-assets-and-meta.mdc`
- `unity-runtime-safety.mdc`
- `generated-and-submodules.mdc`
- `handoff-and-git.mdc`
- `unity-tests.mdc`
- `unity-ui-toolkit.mdc`
- skill `validate-unity-meta`

### Local only (never synced)

| File | Role |
|------|------|
| `rules/unity-csharp-language.mdc` | Unity **2021.3** pin |
| `rules/warudo-plugin-repository.mdc` | Host layout, Warudo Mod Tool constraints |
| `rules/ui-labels.mdc` | User-facing VRMXT label voice (also keep aligned with Warudo Shader Plugins / Blender) |
| `agents/fresh-reviewer.md` | Warudo-specific independent reviewer |

## Project assumptions

- Unity version: `2021.3.45f2`
- First-party code: under `Assets/` (and local packages under `Packages/` if added)
- Tests are NUnit EditMode assemblies colocated with owning code
- C# and documentation use LF line endings

## Deliberately not included

- CSharpier/Prettier and editor-agent workflows not installed in this repository
- GridDungeon UITK / backlog / story Cursor kits
