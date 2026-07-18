# VRMXT Plugin for Warudo

Warudo plugin that attaches VRMXT particle VFX (`VRMXT_vfx`) onto Character assets
after load.

**Install (Warudo):** [Steam Workshop — VRMXT](https://steamcommunity.com/sharedfiles/filedetails/?id=3767350210)

## Requirements

- Unity `2021.3.45f2`
- Api Compatibility Level: **.NET Framework**
- Assembly Version Validation: **off** (Warudo Mod Tool)

## Build (UMod)

1. Clone this repo and open in Unity.
2. Set up Mod Settings — see [`umod/README.md`](umod/README.md) (`ExportSettings` is
   gitignored; copy the example, then `-Backup` a local `.old` twin).
3. Active profile: **VRMXT** → export into Warudo
   `StreamingAssets/Plugins`.
4. Rebuild the mod after pulling shader/Resources changes under `Assets/Vrmxt/`.

## Layout

| Path | Role |
|------|------|
| `Assets/Vrmxt/` | First-party plugin + vendored UniVRMXT VFX subset |
| `Assets/TestPlugin/` | UMod smoke-test mod |
| `umod/` | ExportSettings template + backup/restore script |
| `Assets/Vrmxt/VENDOR.md` | Vendoring / UMod constraints |

## Version

Plugin attribute version: see `Assets/Vrmxt/Scripts/VrmxtPlugin.cs`.

## Links

| | |
|--|--|
| Steam Workshop | https://steamcommunity.com/sharedfiles/filedetails/?id=3767350210 |
| Specs (Warudo host) | https://github.com/miramocha/Extended-VRM-Specs/blob/main/implementations/warudo-vrmxt.md |
| UniVRMXT | https://github.com/miramocha/UniVRMXT |
