using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UniVRMXT.Format;

namespace Vrmxt.Tests
{
    public sealed class VrmxtPatchExportTests
    {
        [Test]
        public void NormalizeFileSuffix_AddsDotAndStripsVrm()
        {
            Assert.AreEqual(".vrmxt", VrmxtPatchExport.NormalizeFileSuffix(null));
            Assert.AreEqual(".vrmxt", VrmxtPatchExport.NormalizeFileSuffix("vrmxt"));
            Assert.AreEqual(".vrmxt", VrmxtPatchExport.NormalizeFileSuffix(".vrmxt.vrm"));
            Assert.IsNull(VrmxtPatchExport.NormalizeFileSuffix("a/b"));
        }

        [Test]
        public void TryBuildOutputPath_InsertsSuffixBeforeExtension()
        {
            var paths = VrmxtPatchExport.TryBuildOutputPath("Characters/Foo.vrm", ".vrmxt");
            Assert.IsTrue(paths.Success);
            Assert.AreEqual("Characters/Foo.vrm", paths.SourceRelativePath);
            Assert.AreEqual("Characters/Foo.vrmxt.vrm", paths.OutputRelativePath);
        }

        [Test]
        public void TryBuildOutputPath_RejectsInvalidSuffix()
        {
            Assert.AreEqual(".vrmxt", VrmxtPatchExport.NormalizeFileSuffix("   "));
            Assert.IsNull(VrmxtPatchExport.NormalizeFileSuffix(".vrm"));
            var bad = VrmxtPatchExport.TryBuildOutputPath("Characters/Foo.vrm", ".vrm");
            Assert.IsFalse(bad.Success);
        }

        [Test]
        public void TryRewriteJson_InjectsOneMaterialExtension()
        {
            var json =
                "{\"asset\":{\"version\":\"2.0\"}," +
                "\"materials\":[{\"name\":\"Hair\"},{\"name\":\"Body\"}]}";
            var entries = new List<VrmxtPatchExport.MaterialEntry>
            {
                new VrmxtPatchExport.MaterialEntry(
                    "Hair",
                    MinimalOverrideJson("lilToon"),
                    0),
            };

            var result = VrmxtPatchExport.TryRewriteJson(json, entries);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.WrittenCount);
            var root = JObject.Parse(result.Json);
            Assert.AreEqual(
                "lilToon",
                root["materials"][0]["extensions"]["VRMXT_materials_override"]
                    ["overrides"][0]["material"]["id"]!.ToString());
            Assert.IsNull(root["materials"][1]["extensions"]);
            AssertContainsExtensionsUsed(root, "VRMXT_materials_override");
        }

        [Test]
        public void TryRewriteJson_ReplacesExistingOverride_PreservesExtras()
        {
            var json =
                "{\"asset\":{\"version\":\"2.0\"}," +
                "\"extensions\":{\"VRMXT_sprite_particle\":{\"specVersion\":\"1.0\"}}," +
                "\"materials\":[{" +
                "\"name\":\"Hair\"," +
                "\"extras\":{\"note\":\"keep\"}," +
                "\"extensions\":{" +
                "\"VRMC_materials_mtoon\":{\"specVersion\":\"1.0\"}," +
                "\"VRMXT_materials_override\":" + MinimalOverrideJson("OldShader") +
                "}}]}";

            var entries = new List<VrmxtPatchExport.MaterialEntry>
            {
                new VrmxtPatchExport.MaterialEntry(
                    "Hair",
                    MinimalOverrideJson("NewShader"),
                    0),
            };

            var result = VrmxtPatchExport.TryRewriteJson(json, entries);
            Assert.IsTrue(result.Success);
            var root = JObject.Parse(result.Json);
            Assert.AreEqual("1.0", root["extensions"]["VRMXT_sprite_particle"]["specVersion"]!.ToString());
            Assert.AreEqual("keep", root["materials"][0]["extras"]["note"]!.ToString());
            Assert.IsNotNull(root["materials"][0]["extensions"]["VRMC_materials_mtoon"]);
            Assert.AreEqual(
                "NewShader",
                root["materials"][0]["extensions"]["VRMXT_materials_override"]
                    ["overrides"][0]["material"]["id"]!.ToString());
        }

        [Test]
        public void TryRewriteJson_DedupesExtensionsUsed()
        {
            var json =
                "{\"asset\":{\"version\":\"2.0\"}," +
                "\"extensionsUsed\":[\"VRMXT_materials_override\",\"KHR_texture_transform\"]," +
                "\"materials\":[{\"name\":\"Hair\"}]}";
            var entries = new List<VrmxtPatchExport.MaterialEntry>
            {
                new VrmxtPatchExport.MaterialEntry("Hair", MinimalOverrideJson("X"), 0),
            };

            var result = VrmxtPatchExport.TryRewriteJson(json, entries);
            Assert.IsTrue(result.Success);
            var used = (JArray)JObject.Parse(result.Json)["extensionsUsed"];
            var count = 0;
            for (var i = 0; i < used.Count; i++)
            {
                if (used[i]!.ToString() == "VRMXT_materials_override")
                {
                    count++;
                }
            }

            Assert.AreEqual(1, count);
            Assert.AreEqual(2, used.Count);
        }

        [Test]
        public void TryRewriteJson_NameFallback_UniqueOnly()
        {
            var json =
                "{\"asset\":{\"version\":\"2.0\"}," +
                "\"materials\":[{\"name\":\"Hair\"},{\"name\":\"Body\"}]}";
            var entries = new List<VrmxtPatchExport.MaterialEntry>
            {
                new VrmxtPatchExport.MaterialEntry("Hair", MinimalOverrideJson("A"), -1),
            };

            var result = VrmxtPatchExport.TryRewriteJson(json, entries);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.WrittenCount);
            Assert.AreEqual(0, result.Skipped.Count);
        }

        [Test]
        public void TryRewriteJson_AmbiguousName_Skips()
        {
            var json =
                "{\"asset\":{\"version\":\"2.0\"}," +
                "\"materials\":[{\"name\":\"Hair\"},{\"name\":\"Hair\"}]}";
            var entries = new List<VrmxtPatchExport.MaterialEntry>
            {
                new VrmxtPatchExport.MaterialEntry("Hair", MinimalOverrideJson("A"), -1),
            };

            var result = VrmxtPatchExport.TryRewriteJson(json, entries);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.WrittenCount);
            Assert.AreEqual(1, result.Skipped.Count);
            Assert.IsNull(JObject.Parse(result.Json)["materials"][0]["extensions"]);
        }

        [Test]
        public void TryRebuildGlb_PreservesBinHash()
        {
            var bin = Encoding.UTF8.GetBytes("BINPAYLOAD!!");
            Assert.IsTrue(GlbChunks.TryRebuild(
                "{\"asset\":{\"version\":\"2.0\"},\"materials\":[{\"name\":\"Hair\"}]}",
                bin,
                out var sourceGlb));

            var entries = new List<VrmxtPatchExport.MaterialEntry>
            {
                new VrmxtPatchExport.MaterialEntry("Hair", MinimalOverrideJson("lilToon"), 0),
            };

            Assert.IsTrue(VrmxtPatchExport.TryRebuildGlb(
                sourceGlb,
                entries,
                out var outputGlb,
                out var rewrite));
            Assert.IsTrue(rewrite.Success);
            Assert.AreEqual(1, rewrite.WrittenCount);

            Assert.IsTrue(GlbChunks.TryExtract(sourceGlb, out _, out var sourceBin));
            Assert.IsTrue(GlbChunks.TryExtract(outputGlb, out _, out var outputBin));
            Assert.AreEqual(Sha256(sourceBin), Sha256(outputBin));
        }

        private static string MinimalOverrideJson(string shaderName)
        {
            return "{" +
                   "\"specVersion\":\"1.0\"," +
                   "\"overrides\":[{" +
                   "\"engine\":\"unity\"," +
                   "\"material\":{" +
                   "\"idType\":\"shaderName\"," +
                   "\"id\":\"" + shaderName + "\"," +
                   "\"variant\":\"builtin\"" +
                   "}}]}";
        }

        private static void AssertContainsExtensionsUsed(JObject root, string name)
        {
            var used = root["extensionsUsed"] as JArray;
            Assert.IsNotNull(used);
            var found = false;
            for (var i = 0; i < used.Count; i++)
            {
                if (used[i]!.ToString() == name)
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found);
        }

        private static string Sha256(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
