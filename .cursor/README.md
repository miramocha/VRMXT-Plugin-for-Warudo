# Cursor configuration

Shared Unity coding and agent guidance for the VRMXT Plugin for Warudo Unity project.

Generic rules (`unity-*.mdc`), handoff/generated rules, and the `validate-unity-meta`
skill are copied from Extended-UniVRM so collaborators get the same Cursor project
guidance on clone. Repo-specific layout lives in `rules/warudo-plugin-repository.mdc`.

## Project assumptions

- Unity version: `2021.3.45f2`
- First-party code: under `Assets/` (and local packages under `Packages/` if added)
- Tests are NUnit EditMode assemblies colocated with owning code
- C# and documentation use LF line endings

## Deliberately not copied

- Extended-UniVRM package paths (`Packages/UniGLTF`, `Packages/VRM`, `Packages/VRM10`)
- UniVRM-specific test runner entry points and package consumer assumptions
- CSharpier/Prettier and editor-agent workflows not installed in this repository
