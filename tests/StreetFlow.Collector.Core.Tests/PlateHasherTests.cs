using StreetFlow.Collector.Core.Plates;
using Xunit;

namespace StreetFlow.Collector.Core.Tests;

public class PlateHasherTests
{
    private static readonly Guid CampaignA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CampaignB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Hash_is_deterministic()
    {
        var salt = PlateHasher.DeriveCampaignSalt(CampaignA);
        Assert.Equal(
            PlateHasher.ComputeHash("3A23456", salt),
            PlateHasher.ComputeHash("3A23456", salt));
    }

    [Fact]
    public void Hash_is_lowercase_hex_sha256()
    {
        var hash = PlateHasher.ComputeHash("3A23456", "somesalt");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Different_plates_produce_different_hashes()
    {
        var salt = PlateHasher.DeriveCampaignSalt(CampaignA);
        Assert.NotEqual(
            PlateHasher.ComputeHash("3A23456", salt),
            PlateHasher.ComputeHash("3A23457", salt));
    }

    [Fact]
    public void Same_plate_in_different_campaigns_produces_different_hashes()
    {
        // Campaign-derived salt: hashes must not be joinable across campaigns.
        Assert.NotEqual(
            PlateHasher.ComputeHash("3A23456", PlateHasher.DeriveCampaignSalt(CampaignA)),
            PlateHasher.ComputeHash("3A23456", PlateHasher.DeriveCampaignSalt(CampaignB)));
    }

    [Fact]
    public void Salt_derivation_is_stable_for_a_campaign()
    {
        Assert.Equal(
            PlateHasher.DeriveCampaignSalt(CampaignA),
            PlateHasher.DeriveCampaignSalt(CampaignA));
        Assert.NotEqual(
            PlateHasher.DeriveCampaignSalt(CampaignA),
            PlateHasher.DeriveCampaignSalt(CampaignB));
    }

    [Fact]
    public void Correction_rehash_equals_direct_hash_of_corrected_plate()
    {
        // Spec §3.6: the hash is computed from the displayed normalized plate, so
        // after an operator correction the re-computed hash must equal the hash of
        // the corrected plate as if it had been read correctly the first time.
        var salt = PlateHasher.DeriveCampaignSalt(CampaignA);

        var misread = PlateNormalizer.Normalize("3AO 1Z34");
        var corrected = PlateNormalizer.Normalize("3A0 1234");

        var rehash = PlateHasher.ComputeHash(corrected, salt);
        Assert.Equal(PlateHasher.ComputeHash(PlateNormalizer.Normalize("3A01234"), salt), rehash);
        Assert.NotEqual(PlateHasher.ComputeHash(misread, salt), rehash);
    }

    [Fact]
    public void Empty_salt_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => PlateHasher.ComputeHash("3A23456", ""));
    }
}
