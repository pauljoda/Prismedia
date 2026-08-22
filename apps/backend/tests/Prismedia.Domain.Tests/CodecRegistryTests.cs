using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class CodecRegistryTests {
    [Fact]
    public void RegistryDiscoversCodecsByEnumType() {
        var bookTypes = CodecRegistry.Get<BookType>();
        var jobStatuses = CodecRegistry.Get<JobRunStatus>();

        Assert.Equal("book", bookTypes.Encode(BookType.Book));
        Assert.Equal(BookType.Novel, bookTypes.Decode(" Novel "));
        Assert.True(bookTypes.TryDecode("novel", out var bookType));
        Assert.Equal(BookType.Novel, bookType);
        Assert.Equal("queued", jobStatuses.Encode(JobRunStatus.Queued));
    }
}
