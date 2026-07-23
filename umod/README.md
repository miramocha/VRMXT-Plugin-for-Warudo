# UMod local settings

`Assets/ExportSettings.asset` is **not** committed (see root `.gitignore`). It stores
absolute paths for your machine.

## Setup

1. Copy `ExportSettings.example.asset` to `Assets/ExportSettings.asset`
2. Replace every `REPLACE_ME` with your project / Warudo install paths
3. Or open Unity → **Warudo → Mod Settings** and fill profiles there
4. Keep **Clear Console On Build** unchecked while debugging UMod compile errors
5. Snapshot a local backup (see below)

## Local backup (UMod wipe protection)

UMod may empty or delete `ExportSettings.asset` / `.meta` when scripts change and Unity
regains focus. Keep backups under `umod/local/` (outside `Assets/`) so Unity does not
import them as a second `ExportSettings` ScriptableObject:

| File | Role |
|------|------|
| `Assets/ExportSettings.asset` | Live Mod Settings (UMod may wipe) |
| `umod/local/ExportSettings.asset.old` | Local backup (gitignored) |
| `umod/local/ExportSettings.asset.meta.old` | Optional meta backup |

```powershell
# After settings look correct
.\umod\export-settings.ps1 -Backup

# After UMod wipes profiles
.\umod\export-settings.ps1 -Restore

.\umod\export-settings.ps1 -Status
```

Or in Unity: **Tools → UMod Export Settings → Restore / Backup / Status**.

Or ask the agent to restore from `.old`.

## Plugin version bumps

When incrementing the plugin version, keep these equal: plugin `Version` attribute,
`umod/ExportSettings.example.asset` `modVersion`, live `Assets/ExportSettings.asset`,
and backup `umod/local/ExportSettings.asset.old` (so `-Restore` does not roll the
version back). Or bump live/example/`Version`, then `.\umod\export-settings.ps1 -Backup`.

## Meta file churn

UMod may delete `ExportSettings.asset.meta` when you edit scripts and return focus to
Unity. Unity recreates the meta on reimport. Safe to ignore; do not commit those files.
Restore from `umod/local/*.meta.old` via the script if the GUID matters.
