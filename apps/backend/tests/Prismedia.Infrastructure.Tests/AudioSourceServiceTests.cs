using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Audio;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class AudioSourceServiceTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-audio-source-{Guid.NewGuid():N}");

    public AudioSourceServiceTests() {
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData("aac", true)]
    [InlineData("mp3", true)]
    [InlineData("alac", false)]
    [InlineData("ape", false)]
    public async Task MarksBrowserPlayableAudioCodecs(string codec, bool directPlayable) {
        await using var db = CreateContext();
        var trackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var filePath = Path.Combine(_tempDir, "track.m4a");
        await File.WriteAllTextAsync(filePath, "audio-bytes");
        SeedAudioSource(db, trackId, filePath, codec);
        await db.SaveChangesAsync();

        var service = new AudioSourceService(db);
        var source = await service.GetSourceAsync(trackId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal(directPlayable, source.DirectPlayable);
        Assert.Equal(codec, source.Codec);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"audio-source-{Guid.NewGuid():N}")
            .Options);

    private static void SeedAudioSource(
        PrismediaDbContext db,
        Guid trackId,
        string filePath,
        string codec) {
        db.Entities.Add(new EntityRow {
            Id = trackId,
            KindCode = EntityKindRegistry.AudioTrack.Code,
            Title = "Track",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.AudioTrackDetails.Add(new AudioTrackDetailRow { EntityId = trackId });
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = trackId,
            Role = EntityFileRole.Source,
            Path = filePath,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.EntityTechnical.Add(new EntityTechnicalRow {
            EntityId = trackId,
            Codec = codec,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
