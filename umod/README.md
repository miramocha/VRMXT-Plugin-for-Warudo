# UMod local settings

`Assets/ExportSettings.asset` is **not** committed (see root `.gitignore`). It stores
absolute paths for your machine.

## Setup

1. Copy `ExportSettings.example.asset` to `Assets/ExportSettings.asset`
2. Replace every `REPLACE_ME` with your project / Warudo install paths
3. Or open Unity → **Warudo → Mod Settings** and fill profiles there
4. Keep **Clear Console On Build** unchecked while debugging UMod compile errors
5. Snapshot a local backup (see below)

## Local `.old` backup (UMod wipe protection)

UMod may empty or delete `ExportSettings.asset` / `.meta` when scripts change and Unity
regains focus. Keep a gitignored twin that UMod does not touch:

| File | Role |
|------|------|
| `Assets/ExportSettings.asset` | Live Mod Settings (UMod may wipe) |
| `Assets/ExportSettings.asset.old` | Local backup (gitignored) |
| `Assets/ExportSettings.asset.meta.old` | Optional meta backup |

```powershell
# After settings look correct
.\umod\export-settings.ps1 -Backup

# After UMod wipes profiles
.\umod\export-settings.ps1 -Restore

.\umod\export-settings.ps1 -Status
```

Or ask the agent to restore from `.old`.

## Meta file churn

UMod may delete `ExportSettings.asset.meta` when you edit scripts and return focus to
Unity. Unity recreates the meta on reimport. Safe to ignore; do not commit those files.
Restore from `.meta.old` via the script if the GUID matters.
