using System.Text.Json;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Tests;

public sealed class CodecJsonConverterFactoryTests {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        Converters = { new CodecJsonConverterFactory() }
    };

    [Fact]
    public void EveryEntityKindRoundTripsWithItsExistingWireCode() {
        foreach (var code in CodecRegistry.Get<EntityKind>().Codes) {
            var json = JsonSerializer.Serialize(code);
            var kind = JsonSerializer.Deserialize<EntityKind>(json, Options);

            Assert.Equal(json, JsonSerializer.Serialize(kind, Options));
        }
    }

    [Fact]
    public void IdentifyQueueContractsSerializeTypedEnumsAsCanonicalCodes() {
        var queue = new IdentifyQueueItem(
            Guid.NewGuid(), Guid.NewGuid(), EntityKind.Video, "Video", false,
            IdentifyQueueState.Proposal, "tmdb", IdentifyAction.LookupId,
            null, [], null, null, false,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var progress = new IdentifyApplyProgress(
            Guid.NewGuid(), Guid.NewGuid(), IdentifyApplyState.Succeeded,
            1, 1, EntityKind.Video, "Video", [], null, DateTimeOffset.UnixEpoch);

        using var queueJson = JsonDocument.Parse(JsonSerializer.Serialize(queue, Options));
        using var progressJson = JsonDocument.Parse(JsonSerializer.Serialize(progress, Options));

        Assert.Equal(IdentifyQueueState.Proposal.ToCode(), queueJson.RootElement.GetProperty("state").GetString());
        Assert.Equal(IdentifyAction.LookupId.ToCode(), queueJson.RootElement.GetProperty("action").GetString());
        Assert.Equal(IdentifyApplyState.Succeeded.ToCode(), progressJson.RootElement.GetProperty("state").GetString());
    }
}
