using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

/// <summary>Locks the starter-rule endpoint's profile-kind filter and its rejection of unknown kinds.</summary>
public sealed class AcquisitionRulePresetEndpointTests {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task PresetsCanBeListedForOneProfileKind() {
        using var factory = new WebApplicationFactory<Program>().WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();

        var book = await ListAsync(client, $"?kind={EntityKind.Book.ToCode()}");
        var movie = await ListAsync(client, $"?kind={EntityKind.Movie.ToCode()}");
        var all = await ListAsync(client, string.Empty);

        Assert.Contains(book, preset => preset.Name == "M4B audiobook");
        Assert.DoesNotContain(book, preset => preset.Name == "H.265 / HEVC");
        Assert.All(book, preset => Assert.Contains(EntityKind.Book, preset.ProfileKinds));
        Assert.Contains(movie, preset => preset.Name == "H.265 / HEVC");
        Assert.DoesNotContain(movie, preset => preset.Name == "M4B audiobook");
        Assert.True(all.Count > book.Count);
    }

    [Fact]
    public async Task AnUnknownKindIsRejected() {
        using var factory = new WebApplicationFactory<Program>().WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/acquisitions/rule-presets?kind=not-a-kind");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<IReadOnlyList<AcquisitionRulePresetView>> ListAsync(HttpClient client, string query) {
        using var response = await client.GetAsync($"/api/acquisitions/rule-presets{query}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<IReadOnlyList<AcquisitionRulePresetView>>(
            await response.Content.ReadAsStringAsync(), CodecJson)!;
    }
}
