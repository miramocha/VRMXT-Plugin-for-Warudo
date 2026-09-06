using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniVRMXT.Format
{
    public static class VrmxtMaterialsMtoonxt
    {
        public const string ExtensionName = "VRMXT_materials_mtoonxt";
        private const string RetiredGltfExtensionName = "VRMC_materials_mtoonxt";
        public const string SpecVersionValue = "1.0";
        public const string SiblingMtoonExtensionName = "VRMC_materials_mtoon";
        public const string BuiltinShaderName = "VRMXT/MToonXT10";
        public const string UrpShaderName = "VRMXT/Universal Render Pipeline/MToonXT10";
        public const string ZTestDefault = "lessEqual";

        public const string ZTestProp = "_M_ZTest";
        public const string OverlayDepthKeyword = "_MTOONXT_OVERLAY_DEPTH";
        public const string OutlineOverlayDepthKeyword = "_MTOONXT_OUTLINE_OVERLAY_DEPTH";

        public const string PassMtoonForward = "MToonForward";
        public const string PassUniversalForwardOverlay = "UniversalForwardOverlay";
        public const string PassForwardBase = "FORWARD_BASE";
        public const string PassForwardBaseOverlay = "FORWARD_BASE_OVERLAY";
        public const string PassForwardAdd = "FORWARD_ADD";
        public const string PassForwardAddOverlay = "FORWARD_ADD_OVERLAY";
        public const string PassMtoonOutlineMain = "MToonOutlineMain";
        public const string PassMtoonOutlineOverlay = "MToonOutlineOverlay";
        public const string PassForwardBaseOutline = "FORWARD_BASE_OUTLINE";
        public const string PassForwardBaseOutlineOverlay = "FORWARD_BASE_OUTLINE_OVERLAY";
        public const string StencilPropEnabled = "_M_StencilEnabled";
        public const string StencilPropRef = "_M_StencilRef";
        public const string StencilPropReadMask = "_M_StencilReadMask";
        public const string StencilPropWriteMask = "_M_StencilWriteMask";
        public const string StencilPropComp = "_M_StencilComp";
        public const string StencilPropPass = "_M_StencilPass";
        public const string StencilPropFail = "_M_StencilFail";
        public const string StencilPropZFail = "_M_StencilZFail";

        public const string OutlineStencilPropEnabled = "_M_OutlineStencilEnabled";
        public const string OutlineStencilPropRef = "_M_OutlineStencilRef";
        public const string OutlineStencilPropReadMask = "_M_OutlineStencilReadMask";
        public const string OutlineStencilPropWriteMask = "_M_OutlineStencilWriteMask";
        public const string OutlineStencilPropComp = "_M_OutlineStencilComp";
        public const string OutlineStencilPropPass = "_M_OutlineStencilPass";
        public const string OutlineStencilPropFail = "_M_OutlineStencilFail";
        public const string OutlineStencilPropZFail = "_M_OutlineStencilZFail";

        public static bool TryParse(string json, out VrmxtMaterialsMtoonxtExtension result)
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

        public static bool TryParse(JToken root, out VrmxtMaterialsMtoonxtExtension result)
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

            TryReadEnum(extension, "zTest", ZTestDefault, TryMapCompareFunction, out var zTest);
            TryReadOptionalBool(extension, "zWrite", out var zWrite);
            // Retired per-material stencil / outlineStencil objects are ignored.
            TryGetProperty(extension, "stencil", out _);
            TryGetProperty(extension, "outlineStencil", out _);

            result = new VrmxtMaterialsMtoonxtExtension(zTest, zWrite);
            return true;
        }

        public static string ToJson(VrmxtMaterialsMtoonxtExtension extension)
        {
            return BuildExtensionObject(extension).ToString(Formatting.None);
        }

        public static byte[] ToUtf8Json(VrmxtMaterialsMtoonxtExtension extension)
        {
            return Encoding.UTF8.GetBytes(ToJson(extension));
        }

        public static bool TryMapCompareFunction(string value, out int unityInt)
        {
            unityInt = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            switch (value)
            {
                case "never":
                    unityInt = 1;
                    return true;
                case "less":
                    unityInt = 2;
                    return true;
                case "equal":
                    unityInt = 3;
                    return true;
                case "lessEqual":
                    unityInt = 4;
                    return true;
                case "greater":
                    unityInt = 5;
                    return true;
                case "notEqual":
                    unityInt = 6;
                    return true;
                case "greaterEqual":
                    unityInt = 7;
                    return true;
                case "always":
                    unityInt = 8;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryMapStencilOp(string value, out int unityInt)
        {
            unityInt = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            switch (value)
            {
                case "keep":
                    unityInt = 0;
                    return true;
                case "zero":
                    unityInt = 1;
                    return true;
                case "replace":
                    unityInt = 2;
                    return true;
                case "incrementSaturate":
                    unityInt = 3;
                    return true;
                case "decrementSaturate":
                    unityInt = 4;
                    return true;
                case "invert":
                    unityInt = 5;
                    return true;
                case "incrementWrap":
                    unityInt = 6;
                    return true;
                case "decrementWrap":
                    unityInt = 7;
                    return true;
                default:
                    return false;
            }
        }

        private static JObject BuildExtensionObject(VrmxtMaterialsMtoonxtExtension extension)
        {
            var root = new JObject { ["specVersion"] = SpecVersionValue };

            if (
                extension != null
                && !string.IsNullOrEmpty(extension.ZTest)
                && !string.Equals(extension.ZTest, ZTestDefault, StringComparison.Ordinal)
            )
            {
                root["zTest"] = extension.ZTest;
            }

            if (extension != null && extension.ZWrite.HasValue)
            {
                root["zWrite"] = extension.ZWrite.Value;
            }

            return root;
        }

        private static bool TryGetExtensionObject(JToken root, out JObject extension)
        {
            extension = null;
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
                if (extensions != null && TryGetProperty(extensions, ExtensionName, out var nested))
                {
                    var nestedObject = nested as JObject;
                    if (nestedObject != null)
                    {
                        extension = nestedObject;
                        return true;
                    }
                }
            }

            if (HasRetiredGltfKey(rootObject))
            {
                return false;
            }

            if (TryGetProperty(rootObject, "specVersion", out _))
            {
                extension = rootObject;
                return true;
            }

            return false;
        }

        private static bool HasRetiredGltfKey(JObject rootObject)
        {
            if (
                TryGetProperty(rootObject, RetiredGltfExtensionName, out var retired)
                && retired is JObject
            )
            {
                return true;
            }

            if (
                TryGetProperty(rootObject, "extensions", out var extensionsToken)
                && extensionsToken is JObject extensions
                && TryGetProperty(extensions, RetiredGltfExtensionName, out var nested)
                && nested is JObject
            )
            {
                return true;
            }

            return false;
        }

        private static bool TryReadSpecVersion(JObject extension, out string specVersion)
        {
            specVersion = null;
            if (
                !TryGetProperty(extension, "specVersion", out var versionToken)
                || versionToken.Type != JTokenType.String
            )
            {
                return false;
            }

            specVersion = versionToken.Value<string>();
            return string.Equals(specVersion, SpecVersionValue, StringComparison.Ordinal);
        }

        private static void TryReadOptionalBool(JObject obj, string name, out bool? value)
        {
            value = null;
            if (!TryGetProperty(obj, name, out var token))
            {
                return;
            }

            if (token.Type != JTokenType.Boolean)
            {
                return;
            }

            value = token.Value<bool>();
        }

        private static bool TryReadEnum(
            JObject obj,
            string name,
            string defaultValue,
            TryMapEnum map,
            out string value
        )
        {
            value = defaultValue;
            if (!TryGetProperty(obj, name, out var token))
            {
                return true;
            }

            if (token.Type != JTokenType.String)
            {
                value = null;
                return false;
            }

            var text = token.Value<string>();
            if (!map(text, out _))
            {
                value = null;
                return false;
            }

            value = text;
            return true;
        }

        private delegate bool TryMapEnum(string value, out int unityInt);

        private static bool TryGetProperty(JObject parent, string propertyName, out JToken token)
        {
            return parent.TryGetValue(propertyName, StringComparison.Ordinal, out token);
        }
    }

    public sealed class VrmxtMaterialsMtoonxtExtension
    {
        public VrmxtMaterialsMtoonxtExtension(string zTest = null, bool? zWrite = null)
        {
            ZTest = string.IsNullOrEmpty(zTest) ? VrmxtMaterialsMtoonxt.ZTestDefault : zTest;
            ZWrite = zWrite;
        }

        public string ZTest { get; }

        public bool? ZWrite { get; }

        public int ZTestUnityInt
        {
            get
            {
                VrmxtMaterialsMtoonxt.TryMapCompareFunction(ZTest, out var value);
                return value;
            }
        }
    }
}
