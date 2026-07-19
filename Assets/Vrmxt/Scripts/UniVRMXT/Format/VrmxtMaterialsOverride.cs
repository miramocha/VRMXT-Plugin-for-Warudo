using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniVRMXT.Format
{
    public static class VrmxtMaterialsOverride
    {
        public const string ExtensionName = "VRMXT_materials_override";
        public const string SpecVersionValue = "1.0";

        public const string EngineUnity = "unity";
        public const string EngineUnreal = "unreal";

        public const string UnityMaterialIdTypeShaderName = "shaderName";
        public const string UnrealMaterialIdTypeResourcePath = "resourcePath";

        public const string TargetTypeScalar = "scalar";
        public const string TargetTypeVector = "vector";
        public const string TargetTypeTexture = "texture";
        public const string TargetTypeShaderFeature = "shaderFeature";

        public static bool TryParse(string json, out VrmxtMaterialsOverrideExtension result)
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

        public static bool TryParse(JToken root, out VrmxtMaterialsOverrideExtension result)
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

            if (!TryGetProperty(extension, "overrides", out var overridesToken) ||
                overridesToken.Type != JTokenType.Array ||
                !((JArray)overridesToken).HasValues)
            {
                return false;
            }

            var overrides = new List<VrmxtMaterialEngineOverride>();
            // Selection key: engine alone, or (engine, material.variant) for unity/unreal.
            var selectionKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var overrideToken in (JArray)overridesToken)
            {
                if (!TryParseOverride(overrideToken, out var engineOverride))
                {
                    return false;
                }

                if (!selectionKeys.Add(BuildSelectionKey(engineOverride)))
                {
                    return false;
                }

                overrides.Add(engineOverride);
            }

            result = new VrmxtMaterialsOverrideExtension(overrides);
            return true;
        }

        /// <summary>
        /// First <c>unity</c> entry (any variant). Prefer
        /// <see cref="TryGetUnityOverrides"/> or pipeline selection for multi-slot files.
        /// </summary>
        public static bool TryGetUnityOverride(
            VrmxtMaterialsOverrideExtension extension,
            out UnityMaterialOverride unityOverride)
        {
            unityOverride = null;
            if (extension == null)
            {
                return false;
            }

            foreach (var entry in extension.Overrides)
            {
                if (!string.Equals(entry.Engine, EngineUnity, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Material is UnityMaterialOverride unity)
                {
                    unityOverride = unity;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// All <c>unity</c> engine overrides (one per render-pipeline slot when multi-slot).
        /// </summary>
        public static bool TryGetUnityOverrides(
            VrmxtMaterialsOverrideExtension extension,
            out IReadOnlyList<VrmxtMaterialEngineOverride> unityOverrides)
        {
            unityOverrides = Array.Empty<VrmxtMaterialEngineOverride>();
            if (extension == null)
            {
                return false;
            }

            var list = new List<VrmxtMaterialEngineOverride>();
            foreach (var entry in extension.Overrides)
            {
                if (entry == null ||
                    !string.Equals(entry.Engine, EngineUnity, StringComparison.Ordinal) ||
                    !(entry.Material is UnityMaterialOverride))
                {
                    continue;
                }

                list.Add(entry);
            }

            if (list.Count == 0)
            {
                return false;
            }

            unityOverrides = list;
            return true;
        }

        /// <summary>
        /// Selection key for uniqueness (base-spec rule 6). Unity/Unreal refine with
        /// <c>material.variant</c>; empty/omitted variant is the empty string.
        /// </summary>
        public static string BuildSelectionKey(VrmxtMaterialEngineOverride entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Engine))
            {
                return string.Empty;
            }

            if (string.Equals(entry.Engine, EngineUnity, StringComparison.Ordinal) ||
                string.Equals(entry.Engine, EngineUnreal, StringComparison.Ordinal))
            {
                return entry.Engine + "\0" + (GetMaterialVariant(entry.Material) ?? string.Empty);
            }

            return entry.Engine;
        }

        public static string GetMaterialVariant(IVrmxtMaterialDefinition material)
        {
            if (material is UnityMaterialOverride unity)
            {
                return unity.Variant;
            }

            if (material is UnrealMaterialOverride unreal)
            {
                return unreal.Variant;
            }

            return null;
        }

        /// <summary>
        /// Serialize a portable extension object to compact JSON (UTF-8).
        /// </summary>
        public static string ToJson(VrmxtMaterialsOverrideExtension extension)
        {
            if (extension == null)
            {
                throw new ArgumentNullException(nameof(extension));
            }

            return BuildExtensionObject(extension).ToString(Formatting.None);
        }

        /// <summary>
        /// UTF-8 JSON bytes suitable for glTF <c>materials[i].extensions.VRMXT_materials_override</c>.
        /// </summary>
        public static byte[] ToUtf8Json(VrmxtMaterialsOverrideExtension extension)
        {
            var json = ToJson(extension);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        private static JObject BuildExtensionObject(VrmxtMaterialsOverrideExtension extension)
        {
            var overrides = new JArray();
            foreach (var entry in extension.Overrides)
            {
                if (entry == null)
                {
                    continue;
                }

                overrides.Add(BuildOverrideObject(entry));
            }

            return new JObject
            {
                ["specVersion"] = SpecVersionValue,
                ["overrides"] = overrides,
            };
        }

        private static JObject BuildOverrideObject(VrmxtMaterialEngineOverride entry)
        {
            var obj = new JObject
            {
                ["engine"] = entry.Engine,
                ["material"] = BuildMaterialObject(entry.Material),
            };

            if (entry.Bindings.Count > 0)
            {
                var bindings = new JArray();
                foreach (var binding in entry.Bindings)
                {
                    bindings.Add(BuildBindingObject(binding));
                }

                obj["bindings"] = bindings;
            }

            if (entry.Properties.Count > 0)
            {
                var properties = new JArray();
                foreach (var property in entry.Properties)
                {
                    properties.Add(BuildPropertyObject(property));
                }

                obj["properties"] = properties;
            }

            return obj;
        }

        private static JObject BuildMaterialObject(IVrmxtMaterialDefinition material)
        {
            if (material is UnityMaterialOverride unity)
            {
                var obj = new JObject
                {
                    ["idType"] = unity.IdType,
                    ["id"] = unity.Id,
                };

                if (!string.IsNullOrEmpty(unity.Variant))
                {
                    obj["variant"] = unity.Variant;
                }

                AddProviderObject(obj, unity.Provider);
                return obj;
            }

            if (material is UnrealMaterialOverride unreal)
            {
                var obj = new JObject
                {
                    ["idType"] = unreal.IdType,
                    ["id"] = unreal.Id ?? string.Empty,
                };

                if (!string.IsNullOrEmpty(unreal.Variant))
                {
                    obj["variant"] = unreal.Variant;
                }

                AddProviderObject(obj, unreal.Provider);
                return obj;
            }

            var fallback = new JObject();
            if (material is UnknownMaterialOverride unknown && !string.IsNullOrEmpty(unknown.IdType))
            {
                fallback["idType"] = unknown.IdType;
            }

            AddProviderObject(fallback, material?.Provider);
            return fallback;
        }

        private static void AddProviderObject(JObject obj, MaterialProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            var providerObj = new JObject
            {
                ["id"] = provider.Id,
            };

            if (!string.IsNullOrEmpty(provider.Version))
            {
                providerObj["version"] = provider.Version;
            }

            obj["provider"] = providerObj;
        }

        private static JObject BuildBindingObject(VrmxtMaterialBinding binding)
        {
            return new JObject
            {
                ["source"] = binding.Source,
                ["target"] = binding.Target,
                ["targetType"] = binding.TargetType,
            };
        }

        private static JObject BuildPropertyObject(VrmxtMaterialProperty property)
        {
            var obj = new JObject
            {
                ["name"] = property.Name,
                ["type"] = property.Type,
            };

            if (string.Equals(property.Type, TargetTypeTexture, StringComparison.Ordinal))
            {
                obj["texture"] = property.TextureIndex ?? 0;
                return obj;
            }

            if (string.Equals(property.Type, TargetTypeVector, StringComparison.Ordinal))
            {
                obj["value"] = ToJArray(property.VectorValue);
                return obj;
            }

            if (string.Equals(property.Type, TargetTypeShaderFeature, StringComparison.Ordinal))
            {
                obj["value"] = property.BoolValue ?? false;
                return obj;
            }

            obj["value"] = property.ScalarValue ?? 0f;
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

        private static bool TryGetExtensionObject(JToken root, out JObject extension)
        {
            extension = null;
            // Prefer `as` over `is` pattern — Unity asmdefs + Newtonsoft can break
            // pattern matching against JObject across assembly boundaries.
            var rootObject = root as JObject;
            if (rootObject == null)
            {
                return false;
            }

            if (TryGetProperty(rootObject, ExtensionName, out var direct))
            {
                var directObject = direct as JObject;
                if (directObject != null)
                {
                    extension = directObject;
                    return true;
                }
            }

            if (TryGetProperty(rootObject, "extensions", out var extensionsToken))
            {
                var extensions = extensionsToken as JObject;
                if (extensions != null &&
                    TryGetProperty(extensions, ExtensionName, out var nested))
                {
                    var nestedObject = nested as JObject;
                    if (nestedObject != null)
                    {
                        extension = nestedObject;
                        return true;
                    }
                }
            }

            // Bare extension object (already extracted from a material extensions map).
            if (TryGetProperty(rootObject, "specVersion", out _))
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

        private static bool TryParseOverride(JToken overrideToken, out VrmxtMaterialEngineOverride engineOverride)
        {
            engineOverride = null;

            var overrideObject = overrideToken as JObject;
            if (overrideObject == null)
            {
                return false;
            }

            if (!TryGetProperty(overrideObject, "engine", out var engineToken) ||
                engineToken.Type != JTokenType.String)
            {
                return false;
            }

            var engine = engineToken.Value<string>();
            if (string.IsNullOrEmpty(engine))
            {
                return false;
            }

            if (!TryGetProperty(overrideObject, "material", out var materialToken))
            {
                return false;
            }

            var materialObject = materialToken as JObject;
            if (materialObject == null)
            {
                return false;
            }

            if (!TryParseMaterial(engine, materialObject, out var material))
            {
                return false;
            }

            var bindings = new List<VrmxtMaterialBinding>();
            if (TryGetProperty(overrideObject, "bindings", out var bindingsToken))
            {
                if (bindingsToken.Type != JTokenType.Array)
                {
                    return false;
                }

                foreach (var bindingToken in (JArray)bindingsToken)
                {
                    if (!TryParseBinding(bindingToken, out var binding))
                    {
                        return false;
                    }

                    bindings.Add(binding);
                }
            }

            var properties = new List<VrmxtMaterialProperty>();
            if (TryGetProperty(overrideObject, "properties", out var propertiesToken))
            {
                if (propertiesToken.Type != JTokenType.Array)
                {
                    return false;
                }

                foreach (var propertyToken in (JArray)propertiesToken)
                {
                    if (!TryParseProperty(propertyToken, out var property))
                    {
                        return false;
                    }

                    properties.Add(property);
                }
            }

            engineOverride = new VrmxtMaterialEngineOverride(engine, material, bindings, properties);
            return true;
        }

        private static bool TryParseMaterial(string engine, JObject materialObject, out IVrmxtMaterialDefinition material)
        {
            material = null;

            string idType = null;
            if (TryGetProperty(materialObject, "idType", out var idTypeToken))
            {
                if (idTypeToken.Type != JTokenType.String)
                {
                    return false;
                }

                idType = idTypeToken.Value<string>();
                if (string.IsNullOrEmpty(idType))
                {
                    return false;
                }
            }

            MaterialProvider provider = null;
            if (TryGetProperty(materialObject, "provider", out var providerToken))
            {
                if (!TryParseProvider(providerToken, out provider))
                {
                    return false;
                }
            }

            if (string.Equals(engine, EngineUnity, StringComparison.Ordinal))
            {
                if (!string.Equals(idType, UnityMaterialIdTypeShaderName, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!TryGetProperty(materialObject, "id", out var idToken) ||
                    idToken.Type != JTokenType.String)
                {
                    return false;
                }

                var id = idToken.Value<string>();
                if (string.IsNullOrEmpty(id))
                {
                    return false;
                }

                string variant = null;
                if (TryGetProperty(materialObject, "variant", out var variantToken) &&
                    variantToken.Type == JTokenType.String)
                {
                    variant = variantToken.Value<string>();
                }

                material = new UnityMaterialOverride(idType, id, variant, provider);
                return true;
            }

            if (string.Equals(engine, EngineUnreal, StringComparison.Ordinal))
            {
                if (!string.Equals(idType, UnrealMaterialIdTypeResourcePath, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!TryGetProperty(materialObject, "id", out var idToken) ||
                    idToken.Type != JTokenType.String)
                {
                    return false;
                }

                var id = idToken.Value<string>();
                if (string.IsNullOrEmpty(id))
                {
                    return false;
                }

                string variant = null;
                if (TryGetProperty(materialObject, "variant", out var variantToken) &&
                    variantToken.Type == JTokenType.String)
                {
                    variant = variantToken.Value<string>();
                }

                material = new UnrealMaterialOverride(idType, id, variant, provider);
                return true;
            }

            material = new UnknownMaterialOverride(idType, provider);
            return true;
        }

        private static bool TryParseProvider(JToken providerToken, out MaterialProvider provider)
        {
            provider = null;
            var providerObject = providerToken as JObject;
            if (providerObject == null)
            {
                return false;
            }

            if (!TryGetProperty(providerObject, "id", out var idToken) ||
                idToken.Type != JTokenType.String)
            {
                return false;
            }

            var id = idToken.Value<string>();
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            string version = null;
            if (TryGetProperty(providerObject, "version", out var versionToken) &&
                versionToken.Type == JTokenType.String)
            {
                version = versionToken.Value<string>();
            }

            provider = new MaterialProvider(id, version);
            return true;
        }

        private static bool TryParseBinding(JToken bindingToken, out VrmxtMaterialBinding binding)
        {
            binding = null;
            var bindingObject = bindingToken as JObject;
            if (bindingObject == null)
            {
                return false;
            }

            if (!TryGetProperty(bindingObject, "source", out var sourceToken) ||
                sourceToken.Type != JTokenType.String)
            {
                return false;
            }

            if (!TryGetProperty(bindingObject, "target", out var targetToken) ||
                targetToken.Type != JTokenType.String)
            {
                return false;
            }

            if (!TryGetProperty(bindingObject, "targetType", out var targetTypeToken) ||
                targetTypeToken.Type != JTokenType.String)
            {
                return false;
            }

            var source = sourceToken.Value<string>();
            var target = targetToken.Value<string>();
            var targetType = targetTypeToken.Value<string>();
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || string.IsNullOrEmpty(targetType))
            {
                return false;
            }

            if (!IsKnownTargetType(targetType))
            {
                return false;
            }

            binding = new VrmxtMaterialBinding(source, target, targetType);
            return true;
        }

        private static bool TryParseProperty(JToken propertyToken, out VrmxtMaterialProperty property)
        {
            property = null;
            var propertyObject = propertyToken as JObject;
            if (propertyObject == null)
            {
                return false;
            }

            if (!TryGetProperty(propertyObject, "name", out var nameToken) ||
                nameToken.Type != JTokenType.String)
            {
                return false;
            }

            var name = nameToken.Value<string>();
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!TryGetProperty(propertyObject, "type", out var typeToken) ||
                typeToken.Type != JTokenType.String)
            {
                return false;
            }

            var type = typeToken.Value<string>();
            if (!IsKnownTargetType(type))
            {
                return false;
            }

            if (string.Equals(type, TargetTypeTexture, StringComparison.Ordinal))
            {
                if (!TryGetProperty(propertyObject, "texture", out var textureToken) ||
                    !TryGetInt32(textureToken, out var textureIndex) ||
                    textureIndex < 0)
                {
                    return false;
                }

                property = new VrmxtMaterialProperty(name, type, null, null, null, textureIndex);
                return true;
            }

            if (!TryGetProperty(propertyObject, "value", out var valueToken))
            {
                return false;
            }

            if (string.Equals(type, TargetTypeVector, StringComparison.Ordinal))
            {
                if (valueToken.Type != JTokenType.Array)
                {
                    return false;
                }

                var values = new List<float>();
                foreach (var item in (JArray)valueToken)
                {
                    if (!TryGetDouble(item, out var number) || !IsFinite(number))
                    {
                        return false;
                    }

                    values.Add((float)number);
                }

                if (values.Count == 0)
                {
                    return false;
                }

                property = new VrmxtMaterialProperty(name, type, null, values, null, null);
                return true;
            }

            if (string.Equals(type, TargetTypeShaderFeature, StringComparison.Ordinal))
            {
                if (valueToken.Type != JTokenType.Boolean)
                {
                    return false;
                }

                property = new VrmxtMaterialProperty(name, type, null, null, valueToken.Value<bool>(), null);
                return true;
            }

            // Remaining known type is TargetTypeScalar.
            if (!TryGetDouble(valueToken, out var scalar) || !IsFinite(scalar))
            {
                return false;
            }

            property = new VrmxtMaterialProperty(name, type, (float)scalar, null, null, null);
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

        // Shared vocabulary: binding targetType and property type both draw from the
        // same scalar/vector/texture/shaderFeature set.
        private static bool IsKnownTargetType(string targetType)
        {
            return string.Equals(targetType, TargetTypeScalar, StringComparison.Ordinal) ||
                   string.Equals(targetType, TargetTypeVector, StringComparison.Ordinal) ||
                   string.Equals(targetType, TargetTypeTexture, StringComparison.Ordinal) ||
                   string.Equals(targetType, TargetTypeShaderFeature, StringComparison.Ordinal);
        }
    }

    public sealed class VrmxtMaterialsOverrideExtension
    {
        public VrmxtMaterialsOverrideExtension(IReadOnlyList<VrmxtMaterialEngineOverride> overrides)
        {
            Overrides = overrides ?? Array.Empty<VrmxtMaterialEngineOverride>();
        }

        public IReadOnlyList<VrmxtMaterialEngineOverride> Overrides { get; }
    }

    public sealed class VrmxtMaterialEngineOverride
    {
        public VrmxtMaterialEngineOverride(
            string engine,
            IVrmxtMaterialDefinition material,
            IReadOnlyList<VrmxtMaterialBinding> bindings,
            IReadOnlyList<VrmxtMaterialProperty> properties)
        {
            Engine = engine;
            Material = material;
            Bindings = bindings ?? Array.Empty<VrmxtMaterialBinding>();
            Properties = properties ?? Array.Empty<VrmxtMaterialProperty>();
        }

        public string Engine { get; }
        public IVrmxtMaterialDefinition Material { get; }
        public IReadOnlyList<VrmxtMaterialBinding> Bindings { get; }
        public IReadOnlyList<VrmxtMaterialProperty> Properties { get; }
    }

    public interface IVrmxtMaterialDefinition
    {
        string IdType { get; }
        MaterialProvider Provider { get; }
    }

    public sealed class UnityMaterialOverride : IVrmxtMaterialDefinition
    {
        public UnityMaterialOverride(string idType, string id, string variant, MaterialProvider provider)
        {
            IdType = idType;
            Id = id;
            ShaderName = id;
            Variant = variant;
            Provider = provider;
        }

        public string IdType { get; }
        public string Id { get; }
        public string ShaderName { get; }
        public string Variant { get; }
        public MaterialProvider Provider { get; }
    }

    public sealed class UnrealMaterialOverride : IVrmxtMaterialDefinition
    {
        public UnrealMaterialOverride(string idType, string id, string variant, MaterialProvider provider)
        {
            IdType = idType;
            Id = id;
            Variant = variant;
            Provider = provider;
        }

        public string IdType { get; }
        public string Id { get; }
        public string Variant { get; }
        public MaterialProvider Provider { get; }
    }

    public sealed class UnknownMaterialOverride : IVrmxtMaterialDefinition
    {
        public UnknownMaterialOverride(string idType, MaterialProvider provider)
        {
            IdType = idType;
            Provider = provider;
        }

        public string IdType { get; }
        public MaterialProvider Provider { get; }
    }

    public sealed class MaterialProvider
    {
        public MaterialProvider(string id, string version)
        {
            Id = id;
            Version = version;
        }

        public string Id { get; }
        public string Version { get; }
    }

    public sealed class VrmxtMaterialBinding
    {
        public VrmxtMaterialBinding(string source, string target, string targetType)
        {
            Source = source;
            Target = target;
            TargetType = targetType;
        }

        public string Source { get; }
        public string Target { get; }
        public string TargetType { get; }
    }

    public sealed class VrmxtMaterialProperty
    {
        public VrmxtMaterialProperty(
            string name,
            string type,
            float? scalarValue,
            IReadOnlyList<float> vectorValue,
            bool? boolValue,
            int? textureIndex)
        {
            Name = name;
            Type = type;
            ScalarValue = scalarValue;
            VectorValue = vectorValue;
            BoolValue = boolValue;
            TextureIndex = textureIndex;
        }

        public string Name { get; }
        public string Type { get; }
        public float? ScalarValue { get; }
        public IReadOnlyList<float> VectorValue { get; }
        public bool? BoolValue { get; }
        public int? TextureIndex { get; }
    }
}
