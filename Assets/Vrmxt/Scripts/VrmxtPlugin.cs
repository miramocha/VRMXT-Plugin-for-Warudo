using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Core.Scenes;
using Warudo.Core.Serializations;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets.Character;

/// <summary>
/// VRMXT host plugin. v1 auto-attaches <c>VRMXT_vfx</c> onto Character GameObjects after load.
/// </summary>
[PluginType(
    Id = "mira.vrmxt",
    Name = "VRMXT",
    Description = "VRMXT extensions for Warudo Characters (v1: particle VFX)",
    Version = "1.0.1",
    Author = "Mira",
    SupportUrl = "https://github.com/miramocha/UniVRMXT"
)]
public sealed class VrmxtPlugin : Plugin
{
    private readonly Dictionary<Guid, BoundCharacter> _bound = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        if (Context.OpenedScene != null)
        {
            BindAllCharacters(Context.OpenedScene);
        }
    }

    public override void OnSceneLoaded(Scene scene, SerializedScene serializedScene)
    {
        base.OnSceneLoaded(scene, serializedScene);
        UnbindAll();
        BindAllCharacters(scene);
    }

    public override void OnSceneUnloaded(Scene scene)
    {
        UnbindAll();
        base.OnSceneUnloaded(scene);
    }

    protected override void OnDestroy()
    {
        UnbindAll();
        base.OnDestroy();
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        ReconcileCharacters();
        PollActiveStateChanges();
    }

    /// <summary>
    /// Poll active; avoid <c>OnActiveStateChange</c> (UMod CS0012 on UnityEvent/CoreModule).
    /// </summary>
    private void PollActiveStateChanges()
    {
        foreach (var pair in _bound)
        {
            var bound = pair.Value;
            var character = bound.Character;
            if (character == null)
            {
                continue;
            }

            var active = character.IsNonNullAndActive();
            if (active == bound.WasActive)
            {
                continue;
            }

            bound.WasActive = active;
            OnCharacterChanged(pair.Key);
        }
    }

    private void BindAllCharacters(Scene scene)
    {
        if (scene == null)
        {
            return;
        }

        foreach (var character in scene.GetAssets<CharacterAsset>())
        {
            BindCharacter(character);
        }
    }

    private void ReconcileCharacters()
    {
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            if (_bound.Count > 0)
            {
                UnbindAll();
            }

            return;
        }

        var live = scene.GetAssets<CharacterAsset>();
        var liveIds = new HashSet<Guid>();
        for (var i = 0; i < live.Count; i++)
        {
            var character = live[i];
            if (character == null)
            {
                continue;
            }

            liveIds.Add(character.Id);
            if (!_bound.ContainsKey(character.Id))
            {
                BindCharacter(character);
            }
        }

        if (_bound.Count == liveIds.Count)
        {
            return;
        }

        var stale = new List<Guid>();
        foreach (var id in _bound.Keys)
        {
            if (!liveIds.Contains(id))
            {
                stale.Add(id);
            }
        }

        for (var i = 0; i < stale.Count; i++)
        {
            UnbindCharacter(stale[i]);
        }
    }

    private void BindCharacter(CharacterAsset character)
    {
        if (character == null || _bound.ContainsKey(character.Id))
        {
            return;
        }

        var bound = new BoundCharacter(character);
        bound.SourceWatchHandle = Watch(character, "Source", () => OnCharacterChanged(character.Id));
        bound.WasActive = character.IsNonNullAndActive();
        _bound[character.Id] = bound;

        OnCharacterChanged(character.Id);
    }

    private void UnbindCharacter(Guid characterId)
    {
        if (!_bound.TryGetValue(characterId, out var bound))
        {
            return;
        }

        _bound.Remove(characterId);

        if (bound.Character != null && bound.SourceWatchHandle != Guid.Empty)
        {
            Unwatch(bound.Character, bound.SourceWatchHandle);
        }

        bound.DisposeApply();
    }

    private void UnbindAll()
    {
        var ids = new List<Guid>(_bound.Keys);
        for (var i = 0; i < ids.Count; i++)
        {
            UnbindCharacter(ids[i]);
        }
    }

    private void OnCharacterChanged(Guid characterId)
    {
        if (!_bound.TryGetValue(characterId, out var bound))
        {
            return;
        }

        bound.ApplyGeneration++;
        var generation = bound.ApplyGeneration;
        bound.DisposeApply();

        var character = bound.Character;
        if (character == null || !character.IsNonNullAndActive())
        {
            return;
        }

        ApplyAsync(characterId, character, generation).Forget();
    }

    private async UniTaskVoid ApplyAsync(Guid characterId, CharacterAsset character, int generation)
    {
        try
        {
            if (!VrmxtCharacterSource.TryGetPersistentRelativePath(character.Source, out var relativePath))
            {
                Debug.Log(
                    "VRMXT: skip Character '" + character.Name +
                    "' — Source not a local character:// .vrm (Source='" + character.Source + "').");
                return;
            }

            if (!Context.PersistentDataManager.HasFile(relativePath))
            {
                Debug.Log($"VRMXT: Character file not found at '{relativePath}'.");
                return;
            }

            Debug.Log("VRMXT: loading '" + relativePath + "' for Character '" + character.Name + "'.");

            var bytes = await Context.PersistentDataManager.ReadFileBytesAsync(relativePath);
            if (!_bound.TryGetValue(characterId, out var bound) ||
                bound.Character != character ||
                bound.ApplyGeneration != generation)
            {
                return;
            }

            if (!character.IsNonNullAndActive())
            {
                return;
            }

            // Character mesh may appear a few frames after active; retry root find briefly.
            VrmxtCharacterApply.Result applyResult = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (!_bound.TryGetValue(characterId, out bound) ||
                    bound.Character != character ||
                    bound.ApplyGeneration != generation)
                {
                    return;
                }

                if (!character.IsNonNullAndActive())
                {
                    return;
                }

                applyResult = VrmxtCharacterApply.Apply(character, bytes);
                if (applyResult != null)
                {
                    break;
                }

                if (VrmxtCharacterApply.TryFindCharacterRoot(character) != null)
                {
                    // Root exists but attach failed — do not spin.
                    break;
                }

                await UniTask.Delay(50);
            }

            if (!_bound.TryGetValue(characterId, out bound) ||
                bound.Character != character ||
                bound.ApplyGeneration != generation)
            {
                applyResult?.Dispose();
                return;
            }

            bound.DisposeApply();
            bound.ApplyResult = applyResult;
        }
        catch (Exception e)
        {
            Log.UserError("VRMXT: failed to apply extensions on Character " + character.Name, e);
        }
    }

    private sealed class BoundCharacter
    {
        public readonly CharacterAsset Character;
        public Guid SourceWatchHandle;
        public bool WasActive;
        public VrmxtCharacterApply.Result ApplyResult;
        public int ApplyGeneration;

        public BoundCharacter(CharacterAsset character)
        {
            Character = character;
        }

        public void DisposeApply()
        {
            if (ApplyResult == null)
            {
                return;
            }

            ApplyResult.Dispose();
            ApplyResult = null;
        }
    }
}
