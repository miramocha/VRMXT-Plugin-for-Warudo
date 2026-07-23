using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Vrmxt.Tests
{
    public sealed class VrmxtCharacterOwnershipTests
    {
        [Test]
        public void IsClaimedByOther_FalseWhenEmptyOrSelfOnly()
        {
            var character = Guid.NewGuid();
            var self = Guid.NewGuid();
            Assert.IsFalse(
                VrmxtCharacterOwnership.IsClaimedByOther(
                    character,
                    self,
                    Array.Empty<(Guid, Guid)>()));
            Assert.IsFalse(
                VrmxtCharacterOwnership.IsClaimedByOther(
                    character,
                    self,
                    new List<(Guid, Guid)> { (self, character) }));
        }

        [Test]
        public void IsClaimedByOther_TrueWhenAnotherAssetClaims()
        {
            var character = Guid.NewGuid();
            var self = Guid.NewGuid();
            var other = Guid.NewGuid();
            Assert.IsTrue(
                VrmxtCharacterOwnership.IsClaimedByOther(
                    character,
                    self,
                    new List<(Guid, Guid)>
                    {
                        (self, character),
                        (other, character),
                    }));
        }

        [Test]
        public void ClaimedCharacterIdsExcluding_OmitsSelfClaims()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var charA = Guid.NewGuid();
            var charB = Guid.NewGuid();
            var claimed = VrmxtCharacterOwnership.ClaimedCharacterIdsExcluding(
                a,
                new List<(Guid, Guid)>
                {
                    (a, charA),
                    (b, charB),
                });
            Assert.IsFalse(claimed.Contains(charA));
            Assert.IsTrue(claimed.Contains(charB));
        }

        [Test]
        public void ResolveFeatures_DefaultsBothOnWhenNoClaim()
        {
            VrmxtCharacterOwnership.ResolveFeatures(
                Guid.NewGuid(),
                Array.Empty<VrmxtCharacterOwnership.Claim>(),
                out var sprite,
                out var mats,
                out var owner,
                out var duplicates);
            Assert.IsTrue(sprite);
            Assert.IsTrue(mats);
            Assert.IsNull(owner);
            Assert.AreEqual(0, duplicates.Count);
        }

        [Test]
        public void ResolveFeatures_UsesOwnerToggles_ListsDuplicates()
        {
            var character = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var dupId = Guid.NewGuid();
            var claims = new List<VrmxtCharacterOwnership.Claim>
            {
                new VrmxtCharacterOwnership.Claim(ownerId, character, false, true),
                new VrmxtCharacterOwnership.Claim(dupId, character, true, false),
            };

            VrmxtCharacterOwnership.ResolveFeatures(
                character,
                claims,
                out var sprite,
                out var mats,
                out var owner,
                out var duplicates);
            Assert.IsFalse(sprite);
            Assert.IsTrue(mats);
            Assert.AreEqual(ownerId, owner);
            Assert.AreEqual(1, duplicates.Count);
            Assert.AreEqual(dupId, duplicates[0]);
        }

        [Test]
        public void AssetsThatShouldClearDuplicateClaims_KeepsFirst()
        {
            var character = Guid.NewGuid();
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var clear = VrmxtCharacterOwnership.AssetsThatShouldClearDuplicateClaims(
                new List<(Guid, Guid)>
                {
                    (first, character),
                    (second, character),
                    (third, character),
                });
            Assert.AreEqual(2, clear.Count);
            Assert.AreEqual(second, clear[0]);
            Assert.AreEqual(third, clear[1]);
            Assert.IsFalse(clear.Contains(first));
        }
    }
}
