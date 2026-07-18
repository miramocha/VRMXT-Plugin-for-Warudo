# UMod local settings

`Assets/ExportSettings.asset` is **not** committed (see root `.gitignore`). It stores
absolute paths for your machine.

## Setup

1. Copy `ExportSettings.example.asset` to `Assets/ExportSettings.asset`
2. Replace every `REPLACE_ME` with your project / Warudo install paths
3. Or open Unity → **Warudo → Mod Settings** and fill profiles there
4. Keep **Clear Console On Build** unchecked while debugging UMod compile errors

## Meta file churn

UMod may delete `ExportSettings.asset.meta` when you edit scripts and return focus to
Unity. Unity recreates the meta on reimport. Safe to ignore; do not commit those files.
