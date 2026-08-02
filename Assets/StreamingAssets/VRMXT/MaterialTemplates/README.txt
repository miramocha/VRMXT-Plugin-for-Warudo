Drop Unity .mat files here (YAML text from the Unity material asset).

Warudo Transfer reads them via PersistentDataManager and applies floats, ints, colors,
and keywords only — not the shader GUID, not textures.

In Warudo Manager: set Shader → Apply shader overrides → pick Material template → Transfer.

Paths look like: VRMXT/MaterialTemplates/MyLook.mat
