using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class EntityContractShapeTests {
    [Fact]
    public void EntityContractsExposeOneAdditiveReferenceSummaryDocumentHierarchy() {
        Assert.Equal([typeof(IEntityRef)], DirectEntityContractInterfaces(typeof(IEntitySummary)));
        Assert.Equal([typeof(IEntitySummary)], DirectEntityContractInterfaces(typeof(IEntityDocument)));
        Assert.Equal([typeof(IEntityDocument)], DirectEntityContractInterfaces(typeof(IEntityCard)));
        Assert.Equal([typeof(IEntitySummary)], DirectEntityContractInterfaces(typeof(EntityThumbnail)));
        Assert.Equal([typeof(IEntityCard)], DirectEntityContractInterfaces(typeof(EntityCard)));

        AssertDeclaredProperties(
            typeof(IEntityRef),
            (nameof(IEntityRef.Id), typeof(Guid)),
            (nameof(IEntityRef.Kind), typeof(Prismedia.Domain.Entities.EntityKind)));
        AssertDeclaredProperties(
            typeof(IEntitySummary),
            (nameof(IEntitySummary.HasSourceMedia), typeof(bool)),
            (nameof(IEntitySummary.ParentEntityId), typeof(Guid?)),
            (nameof(IEntitySummary.SortOrder), typeof(int?)),
            (nameof(IEntitySummary.Title), typeof(string)));
        AssertDeclaredProperties(
            typeof(IEntityDocument),
            (nameof(IEntityDocument.Capabilities), typeof(IReadOnlyList<EntityCapability>)),
            (nameof(IEntityDocument.ChildrenByKind), typeof(IReadOnlyList<EntityGroup>)),
            (nameof(IEntityDocument.Relationships), typeof(IReadOnlyList<EntityGroup>)));
        AssertDeclaredProperties(typeof(IEntityCard));

        Assert.True(typeof(IEntitySummary).IsAssignableFrom(typeof(EntityThumbnail)));
        Assert.False(typeof(IEntityDocument).IsAssignableFrom(typeof(EntityThumbnail)));
        Assert.True(typeof(IEntityDocument).IsAssignableFrom(typeof(EntityCard)));
    }

    [Fact]
    public void EntityListAndDetailEndpointsAdvertiseConcreteWireDtos() {
        using var factory = new WebApplicationFactory<Program>();
        _ = factory.Server;
        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var endpointsByName = endpointDataSource.Endpoints
            .Select(endpoint => new {
                Name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                Endpoint = endpoint
            })
            .Where(item => item.Name is not null)
            .ToDictionary(item => item.Name!, item => item.Endpoint, StringComparer.Ordinal);
        var expectedResponses = new Dictionary<string, Type>(StringComparer.Ordinal) {
            ["ListEntities"] = typeof(EntityListResponse),
            ["GetEntity"] = typeof(EntityCard),
            ["GetMovie"] = typeof(EntityCard),
            ["GetVideoSeries"] = typeof(EntityCard),
            ["GetVideoSeason"] = typeof(EntityCard),
            ["GetVideo"] = typeof(EntityCard),
            ["GetBook"] = typeof(EntityCard),
            ["GetImage"] = typeof(EntityCard),
            ["GetGallery"] = typeof(EntityCard),
            ["GetPerson"] = typeof(EntityCard),
            ["GetTag"] = typeof(EntityCard),
            ["GetStudio"] = typeof(EntityCard),
            ["GetCollection"] = typeof(EntityCard),
            ["GetAudioTrack"] = typeof(EntityCard),
            ["GetAudioLibrary"] = typeof(EntityCard),
            ["GetMusicArtist"] = typeof(EntityCard),
            ["GetBookAuthor"] = typeof(EntityCard),
            ["GetEntityMonitorStates"] = typeof(EntityMonitorStateView[]),
        };

        foreach (var (endpointName, responseType) in expectedResponses) {
            Assert.True(endpointsByName.TryGetValue(endpointName, out var endpoint));
            var successResponse = Assert.Single(
                endpoint!.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status200OK);
            Assert.Equal(responseType, successResponse.Type);
            Assert.False(responseType.IsInterface);
            Assert.False(responseType.IsAbstract);
        }

        Assert.Equal(
            typeof(IReadOnlyList<EntityThumbnail>),
            typeof(EntityListResponse).GetProperty(nameof(EntityListResponse.Items))!.PropertyType);
    }

    [Fact]
    public async Task OpenApiKeepsCodecEnumCollectionsTyped() {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var items = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(EntityThumbnail))
            .GetProperty("properties")
            .GetProperty("acquisitionStatuses")
            .GetProperty("items");

        Assert.Equal("string", items.GetProperty("type").GetString());
        Assert.Equal(
            Enum.GetValues<Prismedia.Domain.Entities.AcquisitionStatus>()
                .Select(status => status.ToCode())
                .Order(StringComparer.Ordinal),
            items.GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OpenApiDescribesEntityThumbnailSubtitleAsNullableText() {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var types = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(EntityThumbnail))
            .GetProperty("properties")
            .GetProperty("subtitle")
            .GetProperty("type")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        Assert.Equal(["null", "string"], types);
    }

    [Fact]
    public async Task OpenApiDescribesChangelogAsMarkdownText() {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var schema = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/changelog")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("text/markdown")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
    }

    [Fact]
    public async Task KindSpecificDetailAliasesAreDeprecatedInOpenApi() {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");

        string[] aliases = [
            "/api/audio-libraries/{id}",
            "/api/audio-tracks/{id}",
            "/api/book-authors/{id}",
            "/api/books/{id}",
            "/api/collections/{id}",
            "/api/galleries/{id}",
            "/api/images/{id}",
            "/api/movies/{id}",
            "/api/music-artists/{id}",
            "/api/people/{id}",
            "/api/series/{id}",
            "/api/studios/{id}",
            "/api/tags/{id}",
            "/api/videos/{id}"
        ];
        foreach (var path in aliases) {
            var operation = paths.GetProperty(path).GetProperty("get");
            Assert.True(operation.GetProperty("deprecated").GetBoolean(), path);
            Assert.Contains("/api/entities/{id}", operation.GetProperty("description").GetString());
        }

        Assert.False(paths.GetProperty("/api/entities/{id}").GetProperty("get").TryGetProperty("deprecated", out var canonical) && canonical.GetBoolean());
    }

    private static Type[] DirectEntityContractInterfaces(Type type) {
        var interfaces = type.GetInterfaces();
        var inheritedInterfaces = interfaces
            .SelectMany(candidate => candidate.GetInterfaces())
            .ToHashSet();
        return interfaces
            .Where(candidate => !inheritedInterfaces.Contains(candidate))
            .Where(candidate => candidate == typeof(IEntityRef) || typeof(IEntityRef).IsAssignableFrom(candidate))
            .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertDeclaredProperties(
        Type type,
        params (string Name, Type Type)[] expected) {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();
        Assert.Equal(expected, properties);
    }
}
