using System;
using System.Collections.Generic;
using UniGLTF.SpringBoneJobs.Blittables;
using Unity.Mathematics;
using UnityEngine;
using UniVRM10;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Scenes;
using Warudo.Core.Utils;
using Warudo.Plugins.Core.Assets.Character;

/// <summary>
/// Scene-global VRM 1.0 FastSpringBone wind. Stock VRM Wind only drives VRM 0.x
/// gravity; this writes world ExternalForce via SetModelLevel each frame.
/// Player UniVRM 0.130+ BlittableModelLevel is a readonly struct (ctor + float3).
/// Mod Tool 0.129.1 still has public fields — do not emit field stores (MissingFieldException).
/// typeof remaps to the host type; Type.GetType inside UMod script domain does not.
/// Freeze rows remove matching springs from Vrm10Instance + ReconstructSpringBone
/// (kills that chain entirely, not wind-only). Snapshot restored on off / destroy.
/// </summary>
[AssetType(
    Id = "6c9f17b6-dd08-4451-9dc6-73fe3c72085e",
    Title = "VRM 1 spring bone wind",
    Category = "CATEGORY_ENVIRONMENTS",
    Singleton = true
)]
public sealed class Vrm1SpringBoneWindAsset : Asset
{
    private const float ScanIntervalSeconds = 0.5f;
    private const float DirectionEpsilonSqr = 1e-8f;

    [Markdown]
    public string Hint =
        "VRM 0 Characters still use stock VRM Wind. This Asset only hits VRM 1 "
        + "FastSpringBone. Wind is model-level. Freeze on a spring removes that chain "
        + "(all physics) until unchecked or this Asset is off.";

    [DataInput]
    [Label("Enabled")]
    public bool Enabled = true;

    [DataInput]
    [Label("Direction")]
    public Vector3 Direction = new Vector3(1f, 0f, 0f);

    [DataInput]
    [Label("Strength")]
    [FloatSlider(0f, 10f)]
    public float Strength = 2f;

    [DataInput]
    [Label("Speed")]
    [FloatSlider(0f, 5f)]
    public float Speed = 1f;

    [DataInput]
    [Label("Turbulence")]
    [FloatSlider(0f, 2f)]
    public float Turbulence = 0.2f;

    [DataInput]
    [Label("Springs")]
    [Description("Transform path of each VRM 1 spring. Freeze removes that chain (all physics).")]
    public Vrm1SpringBoneSkipRow[] Springs;

    [Trigger]
    [Label("Restore spring transforms")]
    public void RestoreSpringTransforms()
    {
        var restored = 0;
        var characters = CollectSceneVrm1Characters();
        for (var i = 0; i < characters.Count; i++)
        {
            if (TryRestoreSpringTransforms(characters[i]))
            {
                restored++;
            }
        }

        if (Enabled)
        {
            OnFreezeRowsChanged();
            _nextScanTime = 0f;
        }

        SetStatusIfChanged(
            restored == 0
                ? "No VRM 1 springs to restore."
                : "Restored spring transforms on " + restored + " Character(s).");
    }

    [Markdown]
    [Label("Status")]
    public string Status = "Idle.";

    private readonly List<CharacterAsset> _tracked = new List<CharacterAsset>();
    private readonly Dictionary<string, SkipState> _skip = new Dictionary<string, SkipState>();
    private float _nextScanTime;
    private bool _loggedSetError;
    private bool _loggedReconstructError;
    private int _skippedSpringCount;

    protected override void OnCreate()
    {
        base.OnCreate();
        if (Springs == null)
        {
            Springs = Array.Empty<Vrm1SpringBoneSkipRow>();
        }

        SetActive(true);
    }

    protected override void OnDestroy()
    {
        ClearWind();
        base.OnDestroy();
    }

    public void OnFreezeRowsChanged()
    {
        foreach (var state in _skip.Values)
        {
            state.FilterKey = null;
        }

        _nextScanTime = 0f;
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        try
        {
            if (!Enabled)
            {
                ClearWind();
                SetStatusIfChanged("Off.");
                return;
            }

            ScanIfDue();

            var force = ComputeGustForce();
            var applied = 0;
            for (var i = _tracked.Count - 1; i >= 0; i--)
            {
                var character = _tracked[i];
                if (character == null
                    || !character.IsNonNullAndActive()
                    || character.Vrm10Instance == null)
                {
                    ForgetCharacter(character);
                    _tracked.RemoveAt(i);
                    continue;
                }

                if (TrySetForce(character, force))
                {
                    applied++;
                }
            }

            if (_tracked.Count == 0)
            {
                SetStatusIfChanged("No VRM 1 Characters.");
            }
            else if (applied == 0)
            {
                SetStatusIfChanged(
                    "Found "
                    + _tracked.Count
                    + " VRM 1 Character(s); SetModelLevel failed.");
            }
            else if (_skippedSpringCount > 0)
            {
                SetStatusIfChanged(
                    "Wind on "
                    + applied
                    + " VRM 1 Character(s), "
                    + _skippedSpringCount
                    + " spring(s) frozen.");
            }
            else
            {
                SetStatusIfChanged("Wind on " + applied + " VRM 1 Character(s).");
            }
        }
        catch (Exception e)
        {
            SetStatusIfChanged("Error: " + e.Message);
        }
    }

    private void ScanIfDue()
    {
        if (Time.unscaledTime < _nextScanTime)
        {
            return;
        }

        _nextScanTime = Time.unscaledTime + ScanIntervalSeconds;
        RefreshTracked();
    }

    private static List<CharacterAsset> CollectSceneVrm1Characters()
    {
        var next = new List<CharacterAsset>();
        var scene = Context.OpenedScene;
        if (scene == null)
        {
            return next;
        }

        var characters = scene.GetAssets<CharacterAsset>();
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            if (character == null || !character.IsNonNullAndActive())
            {
                continue;
            }

            if (character.Vrm10Instance == null)
            {
                continue;
            }

            next.Add(character);
        }

        return next;
    }

    private void RefreshTracked()
    {
        var next = CollectSceneVrm1Characters();

        for (var i = 0; i < _tracked.Count; i++)
        {
            var old = _tracked[i];
            if (!next.Contains(old))
            {
                ForgetCharacter(old);
            }
        }

        _tracked.Clear();
        _tracked.AddRange(next);

        EnsureSnapshots();
        SyncSpringRows();

        _skippedSpringCount = 0;
        for (var i = 0; i < _tracked.Count; i++)
        {
            _skippedSpringCount += ApplySpringSkip(_tracked[i]);
        }
    }

    private void EnsureSnapshots()
    {
        for (var i = 0; i < _tracked.Count; i++)
        {
            var character = _tracked[i];
            var vrm = character.Vrm10Instance;
            if (vrm == null || vrm.SpringBone == null || vrm.SpringBone.Springs == null)
            {
                continue;
            }

            var id = character.Id.ToString();
            if (!_skip.TryGetValue(id, out var state) || state.Vrm != vrm)
            {
                _skip[id] = new SkipState
                {
                    Vrm = vrm,
                    Snapshot = CopySprings(vrm.SpringBone.Springs),
                    FilterKey = null
                };
            }
        }
    }

    private void SyncSpringRows()
    {
        var wanted = new List<RowKey>();
        for (var i = 0; i < _tracked.Count; i++)
        {
            var character = _tracked[i];
            var id = character.Id.ToString();
            if (!_skip.TryGetValue(id, out var state) || state.Snapshot == null)
            {
                continue;
            }

            for (var s = 0; s < state.Snapshot.Count; s++)
            {
                var spring = state.Snapshot[s];
                if (spring == null)
                {
                    continue;
                }

                wanted.Add(
                    new RowKey
                    {
                        CharacterId = id,
                        CharacterName = character.Name ?? string.Empty,
                        TransformPath = BuildSpringPath(state.Vrm, spring)
                    });
            }
        }

        var existing = new Dictionary<string, Vrm1SpringBoneSkipRow>();
        if (Springs != null)
        {
            for (var i = 0; i < Springs.Length; i++)
            {
                var row = Springs[i];
                if (row == null || string.IsNullOrEmpty(row.CharacterId))
                {
                    continue;
                }

                existing[RowDictKey(row.CharacterId, row.TransformPath)] = row;
            }
        }

        var next = new Vrm1SpringBoneSkipRow[wanted.Count];
        var changed = Springs == null || Springs.Length != wanted.Count;
        for (var i = 0; i < wanted.Count; i++)
        {
            var key = RowDictKey(wanted[i].CharacterId, wanted[i].TransformPath);
            if (existing.TryGetValue(key, out var row))
            {
                next[i] = row;
                if (row.CharacterName != wanted[i].CharacterName)
                {
                    row.CharacterName = wanted[i].CharacterName;
                    row.BroadcastDataInput(nameof(Vrm1SpringBoneSkipRow.CharacterName));
                }
            }
            else
            {
                var captured = wanted[i];
                next[i] = StructuredData.Create<Vrm1SpringBoneSkipRow>(
                    created =>
                    {
                        created.CharacterId = captured.CharacterId;
                        created.CharacterName = captured.CharacterName;
                        created.TransformPath = captured.TransformPath;
                        created.Freeze = false;
                    });
                changed = true;
            }

            next[i].RefreshPathDisplay();

            if (Springs == null || i >= Springs.Length || !ReferenceEquals(Springs[i], next[i]))
            {
                changed = true;
            }
        }

        if (changed)
        {
            SetDataInput(nameof(Springs), next, broadcast: true);
        }
    }

    private void ClearWind()
    {
        for (var i = 0; i < _tracked.Count; i++)
        {
            ForgetCharacter(_tracked[i]);
        }

        _tracked.Clear();
        _skip.Clear();
        _skippedSpringCount = 0;
        _nextScanTime = 0f;
    }

    private void ForgetCharacter(CharacterAsset character)
    {
        TrySetForce(character, Vector3.zero);
        RestoreSprings(character);
    }

    private int ApplySpringSkip(CharacterAsset character)
    {
        if (character == null)
        {
            return 0;
        }

        var vrm = character.Vrm10Instance;
        var id = character.Id.ToString();
        if (vrm == null
            || vrm.SpringBone == null
            || vrm.SpringBone.Springs == null
            || !_skip.TryGetValue(id, out var state)
            || state.Snapshot == null)
        {
            return 0;
        }

        var key = BuildSkipKey(id);
        var desired = BuildDesiredSprings(state.Vrm, state.Snapshot, id);
        if (state.FilterKey == key && SameSpringRefs(vrm.SpringBone.Springs, desired))
        {
            return state.Snapshot.Count - desired.Count;
        }

        if (!SameSpringRefs(vrm.SpringBone.Springs, desired))
        {
            var springs = vrm.SpringBone.Springs;
            springs.Clear();
            for (var i = 0; i < desired.Count; i++)
            {
                springs.Add(desired[i]);
            }

            try
            {
                if (!vrm.Runtime.SpringBone.ReconstructSpringBone() && !_loggedReconstructError)
                {
                    _loggedReconstructError = true;
                    Debug.LogWarning(
                        "VRM 1 spring bone wind: ReconstructSpringBone returned false.");
                }
            }
            catch (Exception e)
            {
                if (!_loggedReconstructError)
                {
                    _loggedReconstructError = true;
                    Debug.LogWarning(
                        "VRM 1 spring bone wind: ReconstructSpringBone failed: " + e);
                }

                return 0;
            }
        }

        state.FilterKey = key;
        return CountSkippedInSnapshot(state.Vrm, state.Snapshot, id);
    }

    private bool TryRestoreSpringTransforms(CharacterAsset character)
    {
        if (character == null)
        {
            return false;
        }

        var vrm = character.Vrm10Instance;
        if (vrm == null)
        {
            return false;
        }

        var id = character.Id.ToString();
        if (_skip.TryGetValue(id, out var state) && state.Vrm == vrm && state.Snapshot != null)
        {
            try
            {
                WriteSpringList(vrm, state.Snapshot);
                state.FilterKey = null;
            }
            catch (Exception e)
            {
                if (!_loggedReconstructError)
                {
                    _loggedReconstructError = true;
                    Debug.LogWarning(
                        "VRM 1 spring bone wind: restore springs failed: " + e);
                }

                return false;
            }
        }

        try
        {
            vrm.Runtime.SpringBone.RestoreInitialTransform();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "VRM 1 spring bone wind: RestoreInitialTransform failed: " + e);
            return false;
        }
    }

    private void RestoreSprings(CharacterAsset character)
    {
        if (character == null)
        {
            return;
        }

        var id = character.Id.ToString();
        if (!_skip.TryGetValue(id, out var state))
        {
            return;
        }

        try
        {
            var vrm = character.Vrm10Instance;
            if (vrm != null && vrm == state.Vrm && state.Snapshot != null)
            {
                WriteSpringList(vrm, state.Snapshot);
            }
        }
        catch (Exception e)
        {
            if (!_loggedReconstructError)
            {
                _loggedReconstructError = true;
                Debug.LogWarning(
                    "VRM 1 spring bone wind: restore springs failed: " + e);
            }
        }

        _skip.Remove(id);
    }

    private static void WriteSpringList(
        Vrm10Instance vrm,
        List<Vrm10InstanceSpringBone.Spring> snapshot)
    {
        var springs = vrm.SpringBone.Springs;
        if (SameSpringRefs(springs, snapshot))
        {
            return;
        }

        springs.Clear();
        for (var i = 0; i < snapshot.Count; i++)
        {
            springs.Add(snapshot[i]);
        }

        vrm.Runtime.SpringBone.ReconstructSpringBone();
    }

    private Vector3 ComputeGustForce()
    {
        if (Direction.sqrMagnitude < DirectionEpsilonSqr || Strength <= 0f)
        {
            return Vector3.zero;
        }

        var dir = Direction.normalized;
        if (Turbulence > 0f)
        {
            var t = Time.time * Speed;
            var n = new Vector3(
                Mathf.PerlinNoise(t, 0.13f) * 2f - 1f,
                Mathf.PerlinNoise(0.37f, t) * 2f - 1f,
                Mathf.PerlinNoise(t, 0.71f) * 2f - 1f);
            var mixed = dir + n * Turbulence;
            if (mixed.sqrMagnitude < DirectionEpsilonSqr)
            {
                return Vector3.zero;
            }

            dir = mixed.normalized;
        }

        return dir * Strength;
    }

    private bool TrySetForce(CharacterAsset character, Vector3 force)
    {
        if (character == null)
        {
            return false;
        }

        var vrm = character.Vrm10Instance;
        if (vrm == null)
        {
            return false;
        }

        try
        {
            var level = CreateModelLevel(force);
            vrm.Runtime.SpringBone.SetModelLevel(vrm.transform, level);
            return true;
        }
        catch (Exception e)
        {
            if (!_loggedSetError)
            {
                _loggedSetError = true;
                Debug.LogWarning("VRM 1 spring bone wind: SetModelLevel failed: " + e);
            }

            return false;
        }
    }

    private static BlittableModelLevel CreateModelLevel(Vector3 force)
    {
        var f3 = new float3(force.x, force.y, force.z);
        try
        {
            return (BlittableModelLevel)Activator.CreateInstance(
                typeof(BlittableModelLevel),
                f3,
                false,
                false);
        }
        catch (MissingMethodException)
        {
            return (BlittableModelLevel)Activator.CreateInstance(
                typeof(BlittableModelLevel),
                f3,
                false,
                false,
                false);
        }
    }

    private List<Vrm10InstanceSpringBone.Spring> BuildDesiredSprings(
        Vrm10Instance vrm,
        List<Vrm10InstanceSpringBone.Spring> snapshot,
        string characterId)
    {
        var desired = new List<Vrm10InstanceSpringBone.Spring>(snapshot.Count);
        for (var i = 0; i < snapshot.Count; i++)
        {
            var spring = snapshot[i];
            if (spring == null)
            {
                continue;
            }

            if (!IsFrozen(characterId, BuildSpringPath(vrm, spring)))
            {
                desired.Add(spring);
            }
        }

        return desired;
    }

    private int CountSkippedInSnapshot(
        Vrm10Instance vrm,
        List<Vrm10InstanceSpringBone.Spring> snapshot,
        string characterId)
    {
        var skipped = 0;
        for (var i = 0; i < snapshot.Count; i++)
        {
            var spring = snapshot[i];
            if (spring != null && IsFrozen(characterId, BuildSpringPath(vrm, spring)))
            {
                skipped++;
            }
        }

        return skipped;
    }

    private bool IsFrozen(string characterId, string transformPath)
    {
        if (Springs == null)
        {
            return false;
        }

        for (var i = 0; i < Springs.Length; i++)
        {
            var row = Springs[i];
            if (row == null || !row.Freeze)
            {
                continue;
            }

            if (row.CharacterId == characterId && row.TransformPath == transformPath)
            {
                return true;
            }
        }

        return false;
    }

    private string BuildSkipKey(string characterId)
    {
        if (Springs == null || Springs.Length == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        for (var i = 0; i < Springs.Length; i++)
        {
            var row = Springs[i];
            if (row == null || !row.Freeze || row.CharacterId != characterId)
            {
                continue;
            }

            parts.Add(row.TransformPath ?? string.Empty);
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join("\n", parts.ToArray());
    }

    private static string BuildSpringPath(
        Vrm10Instance vrm,
        Vrm10InstanceSpringBone.Spring spring)
    {
        Transform leaf = null;
        var joints = spring.Joints;
        if (joints != null)
        {
            for (var i = 0; i < joints.Count; i++)
            {
                var joint = joints[i];
                if (joint == null)
                {
                    continue;
                }

                leaf = joint.transform;
                break;
            }
        }

        if (leaf == null)
        {
            leaf = spring.Center;
        }

        var path = BuildRelativePath(vrm != null ? vrm.transform : null, leaf);
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        return string.IsNullOrEmpty(spring.Name) ? "(unnamed)" : spring.Name;
    }

    private static string BuildRelativePath(Transform root, Transform leaf)
    {
        if (leaf == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var current = leaf;
        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        if (parts.Count == 0)
        {
            return leaf.name;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static List<Vrm10InstanceSpringBone.Spring> CopySprings(
        List<Vrm10InstanceSpringBone.Spring> source)
    {
        var copy = new List<Vrm10InstanceSpringBone.Spring>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    private static bool SameSpringRefs(
        List<Vrm10InstanceSpringBone.Spring> a,
        List<Vrm10InstanceSpringBone.Spring> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string RowDictKey(string characterId, string transformPath)
    {
        return characterId + "\n" + transformPath;
    }

    private void SetStatusIfChanged(string status)
    {
        if (Status == status)
        {
            return;
        }

        SetDataInput(nameof(Status), status, broadcast: true);
    }

    private struct RowKey
    {
        public string CharacterId;
        public string CharacterName;
        public string TransformPath;
    }

    private sealed class SkipState
    {
        public Vrm10Instance Vrm;
        public List<Vrm10InstanceSpringBone.Spring> Snapshot;
        public string FilterKey;
    }
}
