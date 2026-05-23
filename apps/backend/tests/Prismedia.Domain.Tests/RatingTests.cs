using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Tests;

public sealed class RatingTests {
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void ConstructorAcceptsIntegerRatingsOnTheSharedZeroToFiveScale(int value) {
        var rating = new CapabilityRating(value);

        Assert.Equal(value, rating.Value);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(6, 5)]
    public void ConstructorClampsRatingsOutsideTheSharedZeroToFiveScale(int value, int normalizedValue) {
        var rating = new CapabilityRating(value);

        Assert.Equal(normalizedValue, rating.Value);
    }

    [Fact]
    public void RateClampsReplacementRatings() {
        var rating = new CapabilityRating(3);

        rating.Rate(9);

        Assert.Equal(5, rating.Value);
    }

    [Fact]
    public void ScaleNormalizeClampsOntoTheSharedScale() {
        Assert.Equal(CapabilityRating.Scale.MinValue, CapabilityRating.Scale.Normalize(-4));
        Assert.Equal(CapabilityRating.Scale.MaxValue, CapabilityRating.Scale.Normalize(42));
    }
}
