using System;
using System.Collections.Generic;

/// <summary>
/// Pure helpers for unique VRMXT Manager ownership of a Character and
/// per-character feature flag resolution.
/// </summary>
public static class VrmxtCharacterOwnership
{
    public readonly struct Claim
    {
        public Claim(
            Guid assetId,
            Guid characterId,
            bool enableSpriteParticle,
            bool enableMaterialsOverride)
        {
            AssetId = assetId;
            CharacterId = characterId;
            EnableSpriteParticle = enableSpriteParticle;
            EnableMaterialsOverride = enableMaterialsOverride;
        }

        public Guid AssetId { get; }
        public Guid CharacterId { get; }
        public bool EnableSpriteParticle { get; }
        public bool EnableMaterialsOverride { get; }
    }

    /// <summary>
    /// True when <paramref name="characterId"/> is already claimed by an asset other
    /// than <paramref name="claimantAssetId"/>.
    /// </summary>
    public static bool IsClaimedByOther(
        Guid characterId,
        Guid claimantAssetId,
        IReadOnlyList<(Guid AssetId, Guid CharacterId)> claims)
    {
        if (claims == null || claims.Count == 0 || characterId == Guid.Empty)
        {
            return false;
        }

        for (var i = 0; i < claims.Count; i++)
        {
            var claim = claims[i];
            if (claim.CharacterId != characterId)
            {
                continue;
            }

            if (claim.AssetId != claimantAssetId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Character ids claimed by any asset except <paramref name="excludeAssetId"/>.
    /// </summary>
    public static HashSet<Guid> ClaimedCharacterIdsExcluding(
        Guid excludeAssetId,
        IReadOnlyList<(Guid AssetId, Guid CharacterId)> claims)
    {
        var result = new HashSet<Guid>();
        if (claims == null)
        {
            return result;
        }

        for (var i = 0; i < claims.Count; i++)
        {
            var claim = claims[i];
            if (claim.AssetId == excludeAssetId || claim.CharacterId == Guid.Empty)
            {
                continue;
            }

            result.Add(claim.CharacterId);
        }

        return result;
    }

    /// <summary>
    /// Resolve feature flags for a Character. No Manager claim → both on.
    /// Multiple claims → first in list wins; duplicates listed in
    /// <paramref name="duplicateAssetIds"/>.
    /// </summary>
    public static void ResolveFeatures(
        Guid characterId,
        IReadOnlyList<Claim> claims,
        out bool applySpriteParticle,
        out bool applyMaterialsOverride,
        out Guid? ownerAssetId,
        out List<Guid> duplicateAssetIds)
    {
        applySpriteParticle = true;
        applyMaterialsOverride = true;
        ownerAssetId = null;
        duplicateAssetIds = new List<Guid>();

        if (claims == null || claims.Count == 0 || characterId == Guid.Empty)
        {
            return;
        }

        for (var i = 0; i < claims.Count; i++)
        {
            var claim = claims[i];
            if (claim.CharacterId != characterId)
            {
                continue;
            }

            if (ownerAssetId == null)
            {
                ownerAssetId = claim.AssetId;
                applySpriteParticle = claim.EnableSpriteParticle;
                applyMaterialsOverride = claim.EnableMaterialsOverride;
                continue;
            }

            duplicateAssetIds.Add(claim.AssetId);
        }
    }

    /// <summary>
    /// Soft reconcile: for each Character claimed more than once, keep the first
    /// asset and return later asset ids that should clear their Character field.
    /// </summary>
    public static List<Guid> AssetsThatShouldClearDuplicateClaims(
        IReadOnlyList<(Guid AssetId, Guid CharacterId)> claims)
    {
        var clear = new List<Guid>();
        if (claims == null || claims.Count == 0)
        {
            return clear;
        }

        var firstOwner = new Dictionary<Guid, Guid>();
        for (var i = 0; i < claims.Count; i++)
        {
            var claim = claims[i];
            if (claim.CharacterId == Guid.Empty)
            {
                continue;
            }

            if (!firstOwner.ContainsKey(claim.CharacterId))
            {
                firstOwner[claim.CharacterId] = claim.AssetId;
                continue;
            }

            clear.Add(claim.AssetId);
        }

        return clear;
    }
}
