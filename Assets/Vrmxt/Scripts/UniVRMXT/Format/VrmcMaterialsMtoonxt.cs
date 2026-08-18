using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniVRMXT.Format
{
    public static class VrmcMaterialsMtoonxt
    {
        public const string ExtensionName = "VRMC_materials_mtoonxt";
        public const string SpecVersionValue = "1.0";
        public const string SiblingMtoonExtensionName = "VRMC_materials_mtoon";
        public const string BuiltinShaderName = "VRMXT/MToonXT10";
        public const string UrpShaderName = "VRMXT/Universal Render Pipeline/MToonXT10";
        public const string ZTestDefault = "lessEqual";

        public const string ZTestProp = "_M_ZTest";
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

        public static bool TryParse(string json, out VrmcMaterialsMtoonxtExtension result)
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

        public static bool TryParse(JToken root, out VrmcMaterialsMtoonxtExtension result)
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

            TryParseStencilObject(extension, "stencil", out var stencil);
            TryParseStencilObject(extension, "outlineStencil", out var outlineStencil);
            TryReadEnum(extension, "zTest", ZTestDefault, TryMapCompareFunction, out var zTest);
            TryReadOptionalRenderQueue(extension, out var renderQueue);
            TryReadOptionalBool(extension, "zWrite", out var zWrite);

            result = new VrmcMaterialsMtoonxtExtension(stencil, outlineStencil, zTest, renderQueue, zWrite);
            return true;
        }

        public static string ToJson(VrmcMaterialsMtoonxtExtension extension)
        {
            return BuildExtensionObject(extension).ToString(Formatting.None);
        }

        public static byte[] ToUtf8Json(VrmcMaterialsMtoonxtExtension extension)
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

        private static JObject BuildExtensionObject(VrmcMaterialsMtoonxtExtension extension)
        {
            var root = new JObject
            {
                ["specVersion"] = SpecVersionValue,
            };

            if (extension != null && extension.Stencil != null)
            {
                root["stencil"] = BuildStencilObject(extension.Stencil);
            }

            if (extension != null && extension.OutlineStencil != null)
            {
                root["outlineStencil"] = BuildStencilObject(extension.OutlineStencil);
            }

            if (extension != null &&
                !string.IsNullOrEmpty(extension.ZTest) &&
                !string.Equals(extension.ZTest, ZTestDefault, StringComparison.Ordinal))
            {
                root["zTest"] = extension.ZTest;
            }

            if (extension != null && extension.RenderQueue.HasValue)
            {
                root["renderQueue"] = extension.RenderQueue.Value;
            }

            if (extension != null && extension.ZWrite.HasValue)
            {
                root["zWrite"] = extension.ZWrite.Value;
            }

            return root;
        }

        private static JObject BuildStencilObject(VrmcMaterialsMtoonxtStencil stencil)
        {
            return new JObject
            {
                ["enabled"] = stencil.Enabled,
                ["ref"] = stencil.Ref,
                ["readMask"] = stencil.ReadMask,
                ["writeMask"] = stencil.WriteMask,
                ["comp"] = stencil.Comp,
                ["pass"] = stencil.Pass,
                ["fail"] = stencil.Fail,
                ["zfail"] = stencil.ZFail,
            };
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

        /// <summary>
        /// Parses a stencil object. Missing or invalid → <c>false</c>, <paramref name="stencil"/> null.
        /// </summary>
        private static bool TryParseStencilObject(
            JObject extension,
            string propertyName,
            out VrmcMaterialsMtoonxtStencil stencil)
        {
            stencil = null;
            if (!TryGetProperty(extension, propertyName, out var token))
            {
                return false;
            }

            var obj = token as JObject;
            if (obj == null)
            {
                return false;
            }

            if (!TryReadBool(obj, "enabled", false, out var enabled) ||
                !TryReadByte(obj, "ref", 0, out var reference) ||
                !TryReadByte(obj, "readMask", 255, out var readMask) ||
                !TryReadByte(obj, "writeMask", 255, out var writeMask) ||
                !TryReadEnum(obj, "comp", "always", TryMapCompareFunction, out var comp) ||
                !TryReadEnum(obj, "pass", "keep", TryMapStencilOp, out var pass) ||
                !TryReadEnum(obj, "fail", "keep", TryMapStencilOp, out var fail) ||
                !TryReadEnum(obj, "zfail", "keep", TryMapStencilOp, out var zfail))
            {
                return false;
            }

            stencil = new VrmcMaterialsMtoonxtStencil(
                enabled,
                reference,
                readMask,
                writeMask,
                comp,
                pass,
                fail,
                zfail);
            return true;
        }

        private static bool TryReadBool(JObject obj, string name, bool defaultValue, out bool value)
        {
            value = defaultValue;
            if (!TryGetProperty(obj, name, out var token))
            {
                return true;
            }

            if (token.Type != JTokenType.Boolean)
            {
                value = false;
                return false;
            }

            value = token.Value<bool>();
            return true;
        }

        private static bool TryReadByte(JObject obj, string name, int defaultValue, out int value)
        {
            value = defaultValue;
            if (!TryGetProperty(obj, name, out var token))
            {
                return true;
            }

            if (!TryGetInt32(token, out var parsed) || parsed < 0 || parsed > 255)
            {
                value = 0;
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>
        /// Missing key → null (keep MToon queue). Invalid → null, does not fail parse.
        /// Inclusive Unity queue range <c>[0, 5000]</c>.
        /// </summary>
        private static void TryReadOptionalRenderQueue(JObject obj, out int? queue)
        {
            queue = null;
            if (!TryGetProperty(obj, "renderQueue", out var token))
            {
                return;
            }

            if (!TryGetInt32(token, out var parsed) || parsed < 0 || parsed > 5000)
            {
                return;
            }

            queue = parsed;
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
            out string value)
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

        private static bool TryGetInt32(JToken token, out int value)
        {
            value = 0;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                return false;
            }

            var number = token.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) ||
                number != Math.Truncate(number) ||
                number < int.MinValue || number > int.MaxValue)
            {
                return false;
            }

            value = (int)number;
            return true;
        }
    }

    public sealed class VrmcMaterialsMtoonxtExtension
    {
        public VrmcMaterialsMtoonxtExtension(
            VrmcMaterialsMtoonxtStencil stencil,
            VrmcMaterialsMtoonxtStencil outlineStencil,
            string zTest = null,
            int? renderQueue = null,
            bool? zWrite = null)
        {
            Stencil = stencil;
            OutlineStencil = outlineStencil;
            ZTest = string.IsNullOrEmpty(zTest) ? VrmcMaterialsMtoonxt.ZTestDefault : zTest;
            RenderQueue = renderQueue;
            ZWrite = zWrite;
        }

        public VrmcMaterialsMtoonxtStencil Stencil { get; }

        public VrmcMaterialsMtoonxtStencil OutlineStencil { get; }

        public string ZTest { get; }

        public int? RenderQueue { get; }

        public bool? ZWrite { get; }

        public int ZTestUnityInt
        {
            get
            {
                VrmcMaterialsMtoonxt.TryMapCompareFunction(ZTest, out var value);
                return value;
            }
        }
    }

    public sealed class VrmcMaterialsMtoonxtStencil
    {
        public VrmcMaterialsMtoonxtStencil(
            bool enabled,
            int reference,
            int readMask,
            int writeMask,
            string comp,
            string pass,
            string fail,
            string zfail)
        {
            Enabled = enabled;
            Ref = reference;
            ReadMask = readMask;
            WriteMask = writeMask;
            Comp = comp;
            Pass = pass;
            Fail = fail;
            ZFail = zfail;
        }

        public bool Enabled { get; }

        public int Ref { get; }
        public int ReadMask { get; }
        public int WriteMask { get; }
        public string Comp { get; }
        public string Pass { get; }
        public string Fail { get; }
        public string ZFail { get; }

        public int CompUnityInt
        {
            get
            {
                VrmcMaterialsMtoonxt.TryMapCompareFunction(Comp, out var value);
                return value;
            }
        }

        public int PassUnityInt
        {
            get
            {
                VrmcMaterialsMtoonxt.TryMapStencilOp(Pass, out var value);
                return value;
            }
        }

        public int FailUnityInt
        {
            get
            {
                VrmcMaterialsMtoonxt.TryMapStencilOp(Fail, out var value);
                return value;
            }
        }

        public int ZFailUnityInt
        {
            get
            {
                VrmcMaterialsMtoonxt.TryMapStencilOp(ZFail, out var value);
                return value;
            }
        }
    }
}
