using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Warudo humanoid normalize zeros bone local rotations (T-pose). glTF node rest
/// frames still have non-identity orientation, so emitter local +Y (spec) points
/// world-up instead of authored direction. Restore UniVRM-like model-relative rest
/// rotation as a local correction on each particle child.
///
/// VRM 1.0 / UniVRM10 uses <c>Axes.X</c> (ReverseX) for glTF↔Unity, not ReverseZ
/// (VRM 0 / default glTF). Wrong inverter mirrors left/right.
/// </summary>
public static class VrmxtWarudoBoneAxisCorrection
{
    /// <summary>
    /// Re-apply emitter TR with glTF→Unity model-relative rest correction.
    /// No-op when JSON parse fails or an emitter node is missing.
    /// </summary>
    public static void Apply(VrmxtVfxInstance instance, string gltfJson)
    {
        if (instance?.Emitters == null || instance.Emitters.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(gltfJson))
        {
            return;
        }

        if (!TryBuildModelRelativeRotations(gltfJson, out var modelRelative))
        {
            Debug.LogWarning("VRMXT: bone axis correction skipped (glTF node rest parse failed).");
            return;
        }

        for (var i = 0; i < instance.Emitters.Count; i++)
        {
            var emitter = instance.Emitters[i];
            if (emitter?.NodeTransform == null)
            {
                continue;
            }

            if (!modelRelative.TryGetValue(emitter.Node, out var correction))
            {
                continue;
            }

            var particleTransform = FindParticleTransform(instance, emitter);
            if (particleTransform == null)
            {
                continue;
            }

            particleTransform.localPosition = correction * emitter.LocalPosition;
            particleTransform.localRotation = correction * emitter.LocalRotation;

            var emitDir = particleTransform.TransformDirection(Vector3.up);
            Debug.Log(
                "VRMXT: bone axis fix node=" + emitter.Node +
                " correctionEuler=" + correction.eulerAngles.ToString("F1") +
                " emitWorldDir=" + emitDir.ToString("F3"));
        }
    }

    private static Transform FindParticleTransform(
        VrmxtVfxInstance instance,
        VrmxtVfxResolvedEmitter emitter)
    {
        if (instance.ParticleSystems != null)
        {
            for (var i = 0; i < instance.ParticleSystems.Count; i++)
            {
                var ps = instance.ParticleSystems[i];
                if (ps == null)
                {
                    continue;
                }

                if (ps.transform.parent == emitter.NodeTransform)
                {
                    return ps.transform;
                }
            }
        }

        return emitter.NodeTransform.Find(
            VrmxtVfxParticleSystemMapper.BuildObjectName(emitter));
    }

    private static bool TryBuildModelRelativeRotations(
        string gltfJson,
        out Dictionary<int, Quaternion> modelRelative)
    {
        modelRelative = null;
        try
        {
            var root = JToken.Parse(gltfJson);
            if (root is not JObject rootObject ||
                !rootObject.TryGetValue("nodes", StringComparison.Ordinal, out var nodesToken) ||
                nodesToken is not JArray nodesArray ||
                nodesArray.Count == 0)
            {
                return false;
            }

            var count = nodesArray.Count;
            var locals = new Matrix4x4[count];
            var parents = new int[count];
            for (var i = 0; i < count; i++)
            {
                parents[i] = -1;
                locals[i] = ReadLocalMatrix(nodesArray[i]);
            }

            for (var i = 0; i < count; i++)
            {
                if (nodesArray[i] is not JObject nodeObject)
                {
                    continue;
                }

                if (!nodeObject.TryGetValue("children", StringComparison.Ordinal, out var childrenToken) ||
                    childrenToken is not JArray children)
                {
                    continue;
                }

                for (var c = 0; c < children.Count; c++)
                {
                    if (!TryGetInt(children[c], out var childIndex) ||
                        childIndex < 0 ||
                        childIndex >= count)
                    {
                        continue;
                    }

                    parents[childIndex] = i;
                }
            }

            var worlds = new Matrix4x4[count];
            var computed = new bool[count];
            for (var i = 0; i < count; i++)
            {
                ComputeWorld(i, locals, parents, worlds, computed);
            }

            modelRelative = new Dictionary<int, Quaternion>(count);
            for (var i = 0; i < count; i++)
            {
                var rootIndex = i;
                while (parents[rootIndex] >= 0)
                {
                    rootIndex = parents[rootIndex];
                }

                // Vrm10Importer: InvertAxis = Axes.X (ReverseX), matching vrm.dev VRM-1.
                var nodeUnity = ReverseXRotation(worlds[i].rotation);
                var rootUnity = ReverseXRotation(worlds[rootIndex].rotation);
                modelRelative[i] = Quaternion.Inverse(rootUnity) * nodeUnity;
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("VRMXT: bone axis correction parse error: " + e.Message);
            return false;
        }
    }

    private static void ComputeWorld(
        int index,
        Matrix4x4[] locals,
        int[] parents,
        Matrix4x4[] worlds,
        bool[] computed)
    {
        if (computed[index])
        {
            return;
        }

        var parent = parents[index];
        if (parent < 0)
        {
            worlds[index] = locals[index];
        }
        else
        {
            ComputeWorld(parent, locals, parents, worlds, computed);
            worlds[index] = worlds[parent] * locals[index];
        }

        computed[index] = true;
    }

    private static Matrix4x4 ReadLocalMatrix(JToken nodeToken)
    {
        if (nodeToken is not JObject node)
        {
            return Matrix4x4.identity;
        }

        if (node.TryGetValue("matrix", StringComparison.Ordinal, out var matrixToken) &&
            matrixToken is JArray matrixArray &&
            matrixArray.Count >= 16)
        {
            return MatrixFromGltfColumnMajor(matrixArray);
        }

        var t = Vector3.zero;
        var r = Quaternion.identity;
        var s = Vector3.one;

        if (node.TryGetValue("translation", StringComparison.Ordinal, out var tToken) &&
            tToken is JArray tArray &&
            tArray.Count >= 3)
        {
            t = new Vector3(ReadFloat(tArray[0]), ReadFloat(tArray[1]), ReadFloat(tArray[2]));
        }

        if (node.TryGetValue("rotation", StringComparison.Ordinal, out var rToken) &&
            rToken is JArray rArray &&
            rArray.Count >= 4)
        {
            r = new Quaternion(
                ReadFloat(rArray[0]),
                ReadFloat(rArray[1]),
                ReadFloat(rArray[2]),
                ReadFloat(rArray[3]));
        }

        if (node.TryGetValue("scale", StringComparison.Ordinal, out var sToken) &&
            sToken is JArray sArray &&
            sArray.Count >= 3)
        {
            s = new Vector3(ReadFloat(sArray[0]), ReadFloat(sArray[1]), ReadFloat(sArray[2]));
        }

        return Matrix4x4.TRS(t, r, s);
    }

    private static Matrix4x4 MatrixFromGltfColumnMajor(JArray values)
    {
        var m = new Matrix4x4();
        m.m00 = ReadFloat(values[0]);
        m.m10 = ReadFloat(values[1]);
        m.m20 = ReadFloat(values[2]);
        m.m30 = ReadFloat(values[3]);
        m.m01 = ReadFloat(values[4]);
        m.m11 = ReadFloat(values[5]);
        m.m21 = ReadFloat(values[6]);
        m.m31 = ReadFloat(values[7]);
        m.m02 = ReadFloat(values[8]);
        m.m12 = ReadFloat(values[9]);
        m.m22 = ReadFloat(values[10]);
        m.m32 = ReadFloat(values[11]);
        m.m03 = ReadFloat(values[12]);
        m.m13 = ReadFloat(values[13]);
        m.m23 = ReadFloat(values[14]);
        m.m33 = ReadFloat(values[15]);
        return m;
    }

    /// <summary>
    /// UniGLTF <c>ReverseX</c> on quaternions (VRM 1.0 glTF↔Unity).
    /// </summary>
    private static Quaternion ReverseXRotation(Quaternion q)
    {
        q.ToAngleAxis(out var angle, out var axis);
        if (float.IsNaN(axis.x) || float.IsNaN(axis.y) || float.IsNaN(axis.z))
        {
            return Quaternion.identity;
        }

        if (axis.sqrMagnitude < 1e-8f)
        {
            return Quaternion.identity;
        }

        return Quaternion.AngleAxis(-angle, new Vector3(-axis.x, axis.y, axis.z));
    }

    private static float ReadFloat(JToken token)
    {
        return token != null ? token.Value<float>() : 0f;
    }

    private static bool TryGetInt(JToken token, out int value)
    {
        value = 0;
        if (token == null)
        {
            return false;
        }

        if (token.Type == JTokenType.Integer)
        {
            value = token.Value<int>();
            return true;
        }

        if (token.Type == JTokenType.Float)
        {
            value = (int)token.Value<float>();
            return true;
        }

        return int.TryParse(token.ToString(), out value);
    }
}
