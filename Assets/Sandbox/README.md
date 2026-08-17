# Sandbox (local experiments)

**Local only — not in git.** Drop throwaway scenes, scripts, prefabs, and materials here.

## Use for

- Shader / material spikes
- One-off Play Mode prototypes
- Scenes that should not ship or run in CI

## Do not

- Reference `Assets/Sandbox/**` from production `Assets/` paths (scripts, shaders, shipped scenes, asmdefs)
- Commit sandbox assets (gitignore blocks them except this README)

## Gitignore

| Path | In git? |
|------|---------|
| `Assets/Sandbox/*` | No |
| `Assets/Sandbox/README.md` | **Yes** |

After clone: folder exists from this README. Unity creates `Assets/Sandbox.meta` on first import if missing.
