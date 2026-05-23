using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class CodecRegistryTests {
    [Fact]
    public void RegistryDiscoversCodecsByEnumType() {
        var bookTypes = CodecRegistry.Get<BookType>();
        var jobStatuses = CodecRegistry.Get<JobRunStatus>();

        Assert.Equal("comic", bookTypes.Encode(BookType.Comic));
        Assert.Equal(BookType.Manga, bookTypes.Decode(" Manga "));
        Assert.True(bookTypes.TryDecode("novel", out var bookType));
        Assert.Equal(BookType.Novel, bookType);
        Assert.Equal("queued", jobStatuses.Encode(JobRunStatus.Queued));
    }
}
