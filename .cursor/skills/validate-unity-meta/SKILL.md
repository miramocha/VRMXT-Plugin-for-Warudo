---
name: validate-unity-meta
description: Validate Unity metadata under Assets and Packages
---

# Validate Unity metadata

Use after adding, moving, deleting, or editing Unity assets or `.meta` files.

Default policy: preserve existing metadata and let Unity generate metadata for new assets.
Never guess or reuse a GUID.

Run from repository root:

```powershell
python .cursor/skills/validate-unity-meta/scripts/validate_unity_meta.py
```

Scan selected paths:

```powershell
python .cursor/skills/validate-unity-meta/scripts/validate_unity_meta.py Assets
```

Use `--strict-warnings` after Unity imports new assets. Use `--no-pairing` when only GUID
format and uniqueness matter.

- `[FAIL]`: fix before handoff or commit.
- `[warn]`: usually a missing `.meta`; let Unity import, then rerun.
- `[ok]`: scanned metadata passed.

Follow `.cursor/rules/unity-assets-and-meta.mdc`.
