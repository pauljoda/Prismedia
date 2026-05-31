using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class RatingTests {
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void RateAcceptsIntegerRatingsOnTheSharedZeroToFiveScale(int value) {
        var video = new Video(Guid.NewGuid(), "Test");

        video.Rate(value);

        Assert.Equal(value, video.RatingValue);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(6, 5)]
    public void RateClampsRatingsOutsideTheSharedZeroToFiveScale(int value, int normalizedValue) {
        var video = new Video(Guid.NewGuid(), "Test");

        video.Rate(value);

        Assert.Equal(normalizedValue, video.RatingValue);
    }

    [Fact]
    public void ClearRatingResetsToNull() {
        var video = new Video(Guid.NewGuid(), "Test");
        video.Rate(3);

        video.ClearRating();

        Assert.Null(video.RatingValue);
    }
}
