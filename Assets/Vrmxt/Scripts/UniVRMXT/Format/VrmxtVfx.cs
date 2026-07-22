using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniVRMXT.Format
{
    /// <summary>
    /// Parse / serialize root <c>VRMXT_sprite_particle</c>. Legacy roots
    /// (<c>VRMXT_vfx</c>, <c>VRMXT_particle</c>) are ignored (no dual-read).
    /// </summary>
    public static class VrmxtVfx
    {
        public const string ExtensionName = "VRMXT_sprite_particle";
        public const string SpecVersionValue = "1.0";

        public const float DefaultEmissionRate = 10f;
        public const int DefaultMaxParticles = 64;
        public const float DefaultLifetime = 1f;
        public const float DefaultStartSpeed = 0.1f;

        public static readonly float[] DefaultSize = { 0.05f, 0.05f };
        public static readonly float[] DefaultColor = { 1f, 1f, 1f, 1f };

        public static bool TryParse(string json, out VrmxtVfxExtension result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var root = JToken.Parse(json);
                return TryParse(root, out result);
            }
            catch (JsonReaderException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryParse(JToken root, out VrmxtVfxExtension result)
        {
            result = null;

            if (!TryGetExtensionObject(root, out var extension))
            {
                return false;
            }

            if (!TryReadSpecVersion(extension, out _))
            {
                return false;
            }

            if (!TryGetProperty(extension, "emitters", out var emittersToken) ||
                emittersToken.Type != JTokenType.Array)
            {
                return false;
            }

            var emitters = new List<VrmxtVfxEmitter>();
            foreach (var emitterToken in (JArray)emittersToken)
            {
                if (TryParseEmitter(emitterToken, out var emitter))
                {
                    emitters.Add(emitter);
                }
            }

            result = new VrmxtVfxExtension(emitters);
            return true;
        }

        /// <summary>
        /// Serialize a portable extension object to compact JSON (UTF-8).
        /// </summary>
        public static string ToJson(VrmxtVfxExtension extension)
        {
            if (extension == null)
            {
                throw new ArgumentNullException(nameof(extension));
            }

            return BuildExtensionObject(extension).ToString(Formatting.None);
        }

        /// <summary>
        /// UTF-8 JSON bytes suitable for glTF <c>extensions.VRMXT_sprite_particle</c>.
        /// </summary>
        public static byte[] ToUtf8Json(VrmxtVfxExtension extension)
        {
            var json = ToJson(extension);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        private static JObject BuildExtensionObject(VrmxtVfxExtension extension)
        {
            var emitters = new JArray();
            foreach (var emitter in extension.Emitters)
            {
                if (emitter == null)
                {
                    continue;
                }

                emitters.Add(BuildEmitterObject(emitter));
            }

            return new JObject
            {
                ["specVersion"] = SpecVersionValue,
                ["emitters"] = emitters,
            };
        }

        private static JObject BuildEmitterObject(VrmxtVfxEmitter emitter)
        {
            var obj = new JObject
            {
                ["node"] = emitter.Node,
            };

            if (!string.IsNullOrEmpty(emitter.Name))
            {
                obj["name"] = emitter.Name;
            }

            if (emitter.Texture.HasValue)
            {
                obj["texture"] = emitter.Texture.Value;
            }

            if (!IsDefaultFloatArray(emitter.Size, DefaultSize))
            {
                obj["size"] = ToJArray(emitter.Size);
            }

            if (!IsDefaultFloatArray(emitter.Color, DefaultColor))
            {
                obj["color"] = ToJArray(emitter.Color);
            }

            if (!NearlyEqual(emitter.EmissionRate, DefaultEmissionRate))
            {
                obj["emissionRate"] = emitter.EmissionRate;
            }

            if (emitter.MaxParticles != DefaultMaxParticles)
            {
                obj["maxParticles"] = emitter.MaxParticles;
            }

            if (!NearlyEqual(emitter.Lifetime, DefaultLifetime))
            {
                obj["lifetime"] = emitter.Lifetime;
            }

            if (!NearlyEqual(emitter.StartSpeed, DefaultStartSpeed))
            {
                obj["startSpeed"] = emitter.StartSpeed;
            }

            return obj;
        }

        private static JArray ToJArray(IReadOnlyList<float> values)
        {
            var array = new JArray();
            if (values == null)
            {
                return array;
            }

            for (var i = 0; i < values.Count; i++)
            {
                array.Add(values[i]);
            }

            return array;
        }

        private static bool IsDefaultFloatArray(IReadOnlyList<float> values, float[] defaults)
        {
            if (values == null || defaults == null || values.Count != defaults.Length)
            {
                return false;
            }

            for (var i = 0; i < defaults.Length; i++)
            {
                if (!NearlyEqual(values[i], defaults[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NearlyEqual(float a, float b)
        {
            return Math.Abs(a - b) <= 1e-5f;
        }

        private static bool TryGetExtensionObject(JToken root, out JObject extension)
        {
            extension = null;
            if (!(root is JObject rootObject))
            {
                return false;
            }

            if (TryGetProperty(rootObject, ExtensionName, out var direct) &&
                direct is JObject directObject)
            {
                extension = directObject;
                return true;
            }

            if (TryGetProperty(rootObject, "extensions", out var extensionsToken) &&
                extensionsToken is JObject extensions &&
                TryGetProperty(extensions, ExtensionName, out var nested) &&
                nested is JObject nestedObject)
            {
                extension = nestedObject;
                return true;
            }

            // Bare extension object (already extracted from glTF extensions map).
            // Only VRMXT_sprite_particle roots are accepted; legacy root keys are never looked up.
            if (TryGetProperty(rootObject, "specVersion", out _) &&
                TryGetProperty(rootObject, "emitters", out _))
            {
                extension = rootObject;
                return true;
            }

            return false;
        }

        private static bool TryReadSpecVersion(JObject extension, out string specVersion)
        {
            specVersion = null;
            if (!TryGetProperty(extension, "specVersion", out var versionToken) ||
                versionToken.Type != JTokenType.String)
            {
                return false;
            }

            specVersion = versionToken.Value<string>();
            return string.Equals(specVersion, SpecVersionValue, StringComparison.Ordinal);
        }

        private static bool TryParseEmitter(JToken emitterToken, out VrmxtVfxEmitter emitter)
        {
            emitter = null;

            if (!(emitterToken is JObject emitterObject))
            {
                return false;
            }

            if (!TryGetProperty(emitterObject, "node", out var nodeToken) ||
                !TryGetInt32(nodeToken, out var node) ||
                node < 0)
            {
                return false;
            }

            int? texture = null;
            if (TryGetProperty(emitterObject, "texture", out var textureToken))
            {
                if (!TryGetInt32(textureToken, out var textureIndex) || textureIndex < 0)
                {
                    return false;
                }

                texture = textureIndex;
            }

            if (!TryReadSize(emitterObject, out var size))
            {
                return false;
            }

            if (!TryReadColor(emitterObject, out var color))
            {
                return false;
            }

            if (!TryReadNonNegativeFloat(emitterObject, "emissionRate", DefaultEmissionRate, out var emissionRate))
            {
                return false;
            }

            if (!TryReadPositiveInt(emitterObject, "maxParticles", DefaultMaxParticles, out var maxParticles))
            {
                return false;
            }

            if (!TryReadNonNegativeFloat(emitterObject, "lifetime", DefaultLifetime, out var lifetime))
            {
                return false;
            }

            if (!TryReadNonNegativeFloat(emitterObject, "startSpeed", DefaultStartSpeed, out var startSpeed))
            {
                return false;
            }

            string name = null;
            if (TryGetProperty(emitterObject, "name", out var nameToken) &&
                nameToken.Type == JTokenType.String)
            {
                name = nameToken.Value<string>();
            }

            emitter = new VrmxtVfxEmitter(
                name,
                node,
                texture,
                size,
                color,
                emissionRate,
                maxParticles,
                lifetime,
                startSpeed);
            return true;
        }

        private static bool TryReadSize(JObject parent, out float[] size)
        {
            size = (float[])DefaultSize.Clone();

            if (!TryGetProperty(parent, "size", out var token))
            {
                return true;
            }

            if (token.Type != JTokenType.Array)
            {
                return false;
            }

            var items = new List<float>();
            foreach (var item in (JArray)token)
            {
                if (!TryGetDouble(item, out var number) || !IsFinite(number) || number <= 0d)
                {
                    return false;
                }

                items.Add((float)number);
            }

            if (items.Count != 2)
            {
                return false;
            }

            size = items.ToArray();
            return true;
        }

        private static bool TryReadColor(JObject parent, out float[] color)
        {
            color = (float[])DefaultColor.Clone();

            if (!TryGetProperty(parent, "color", out var token))
            {
                return true;
            }

            if (token.Type != JTokenType.Array)
            {
                return false;
            }

            var items = new List<float>();
            foreach (var item in (JArray)token)
            {
                if (!TryGetDouble(item, out var number) || !IsFinite(number))
                {
                    return false;
                }

                items.Add((float)number);
            }

            if (items.Count != 4)
            {
                return false;
            }

            // RGB >= 0; alpha in [0, 1].
            if (items[0] < 0f || items[1] < 0f || items[2] < 0f ||
                items[3] < 0f || items[3] > 1f)
            {
                return false;
            }

            color = items.ToArray();
            return true;
        }

        private static bool TryReadNonNegativeFloat(
            JObject parent,
            string propertyName,
            float defaultValue,
            out float value)
        {
            value = defaultValue;
            if (!TryGetProperty(parent, propertyName, out var token))
            {
                return true;
            }

            if (!TryGetDouble(token, out var number) || !IsFinite(number) || number < 0d)
            {
                return false;
            }

            value = (float)number;
            return true;
        }

        private static bool TryReadPositiveInt(
            JObject parent,
            string propertyName,
            int defaultValue,
            out int value)
        {
            value = defaultValue;
            if (!TryGetProperty(parent, propertyName, out var token))
            {
                return true;
            }

            if (!TryGetInt32(token, out var number) || number < 1)
            {
                return false;
            }

            value = number;
            return true;
        }

        private static bool TryGetProperty(JObject parent, string propertyName, out JToken token)
        {
            return parent.TryGetValue(propertyName, StringComparison.Ordinal, out token);
        }

        private static bool TryGetInt32(JToken token, out int value)
        {
            value = 0;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                return false;
            }

            var number = token.Value<double>();
            if (!IsFinite(number) || number != Math.Truncate(number) ||
                number < int.MinValue || number > int.MaxValue)
            {
                return false;
            }

            value = (int)number;
            return true;
        }

        private static bool TryGetDouble(JToken token, out double value)
        {
            value = 0d;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                return false;
            }

            value = token.Value<double>();
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class VrmxtVfxExtension
    {
        public VrmxtVfxExtension(IReadOnlyList<VrmxtVfxEmitter> emitters)
        {
            Emitters = emitters ?? Array.Empty<VrmxtVfxEmitter>();
        }

        public IReadOnlyList<VrmxtVfxEmitter> Emitters { get; }
    }

    public sealed class VrmxtVfxEmitter
    {
        public VrmxtVfxEmitter(
            string name,
            int node,
            int? texture,
            IReadOnlyList<float> size,
            IReadOnlyList<float> color,
            float emissionRate,
            int maxParticles,
            float lifetime,
            float startSpeed)
        {
            Name = name;
            Node = node;
            Texture = texture;
            Size = size;
            Color = color;
            EmissionRate = emissionRate;
            MaxParticles = maxParticles;
            Lifetime = lifetime;
            StartSpeed = startSpeed;
        }

        public string Name { get; }
        public int Node { get; }
        public int? Texture { get; }
        public IReadOnlyList<float> Size { get; }
        public IReadOnlyList<float> Color { get; }
        public float EmissionRate { get; }
        public int MaxParticles { get; }
        public float Lifetime { get; }
        public float StartSpeed { get; }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "sprite_particle(rate={0}, max={1})",
                EmissionRate,
                MaxParticles);
        }
    }
}
