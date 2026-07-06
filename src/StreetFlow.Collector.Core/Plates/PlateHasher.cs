using System.Security.Cryptography;
using System.Text;

namespace StreetFlow.Collector.Core.Plates;

/// <summary>
/// Irreversible SHA-256 hash of the <em>normalized</em> plate, salted per
/// campaign to defeat rainbow tables (spec §3.6). The hash is computed from the
/// displayed normalized plate, so after an operator correction (§4) it is simply
/// recomputed with the same salt. Pure function, developed TDD-first.
/// </summary>
public static class PlateHasher
{
    /// <summary>
    /// Derives the campaign salt. The exact form is an open question in the spec
    /// (§8); provisionally the salt is a fixed prefix plus the canonical lowercase
    /// GUID, which every surface can derive from the campaign config alone.
    /// </summary>
    public static string DeriveCampaignSalt(Guid campaignId) => $"streetflow:{campaignId:D}";

    /// <summary>
    /// Computes the lowercase-hex SHA-256 of <c>{salt}:{normalizedPlate}</c> (UTF-8).
    /// </summary>
    /// <param name="normalizedPlate">Plate already passed through <see cref="PlateNormalizer.Normalize"/>.</param>
    /// <param name="campaignSalt">Salt from <see cref="DeriveCampaignSalt"/>.</param>
    public static string ComputeHash(string normalizedPlate, string campaignSalt)
    {
        ArgumentNullException.ThrowIfNull(normalizedPlate);
        ArgumentException.ThrowIfNullOrEmpty(campaignSalt);

        var payload = Encoding.UTF8.GetBytes($"{campaignSalt}:{normalizedPlate}");
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }
}
