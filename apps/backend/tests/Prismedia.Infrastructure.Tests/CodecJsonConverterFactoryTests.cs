using System.Text.Json;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Tests;

public sealed class CodecJsonConverterFactoryTests {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        Converters = { new CodecJsonConverterFactory() }
    };

    [Fact]
    public void EveryProposalKindRoundTripsWithItsExistingWireCode() {
        foreach (var code in CodecRegistry.Get<ProposalKind>().Codes) {
            var json = JsonSerializer.Serialize(code);
            var kind = JsonSerializer.Deserialize<ProposalKind>(json, Options);

            Assert.Equal(json, JsonSerializer.Serialize(kind, Options));
        }
    }
}
