using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Api.Endpoints;
using Prismedia.Application.Entities;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class EntityDeleteEndpointTests {
    private static readonly Guid EntityId = Guid.Parse("64646464-6464-6464-6464-646464646464");

    [Theory]
    [InlineData(MediaEntityDeleteFailureKind.NotFound, HttpStatusCode.NotFound, ApiProblemCodes.EntityNotFound)]
    [InlineData(MediaEntityDeleteFailureKind.NotDeletable, HttpStatusCode.UnprocessableEntity, ApiProblemCodes.EntityNotDeletable)]
    [InlineData(MediaEntityDeleteFailureKind.Conflict, HttpStatusCode.Conflict, ApiProblemCodes.EntityDeletionConflict)]
    public async Task MapsDeleteFailureKindsToMeaningfulHttpResults(
        MediaEntityDeleteFailureKind failureKind,
        HttpStatusCode expectedStatus,
        string expectedCode) {
        using var factory = CreateFactory(new MediaEntityDeleteResult(
            Deleted: false,
            Message: "Deletion was refused safely.",
            FailureKind: failureKind));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/api/entities/{EntityId}?deleteFiles=true");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, problem?.Code);
    }

    [Fact]
    public async Task BareDeleteUsesSafeLibraryOnlyDefaultAndReturnsTheManagedRejection() {
        using var factory = CreateFactory(deleteFiles => deleteFiles
            ? new MediaEntityDeleteResult(Deleted: true)
            : new MediaEntityDeleteResult(
                Deleted: false,
                Message: "Library-only Entity removal is unsupported.",
                FailureKind: MediaEntityDeleteFailureKind.NotDeletable));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/api/entities/{EntityId}");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ApiProblemCodes.EntityNotDeletable, problem?.Code);
    }

    [Fact]
    public async Task BulkDeleteRejectsLibraryOnlyRemovalBeforeProcessingEntities() {
        using var factory = CreateFactory(new MediaEntityDeleteResult(Deleted: true));
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/entities/bulk-delete",
            new EntityBulkDeleteRequest([EntityId], DeleteFiles: false));
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ApiProblemCodes.EntityNotDeletable, problem?.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory(MediaEntityDeleteResult result) =>
        CreateFactory(_ => result);

    private static WebApplicationFactory<Program> CreateFactory(Func<bool, MediaEntityDeleteResult> result) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IMediaEntityDeletionService>();
                    services.AddSingleton<IMediaEntityDeletionService>(new FakeDeletionService(result));
                });
            })
            .WithTestAuth();

    private sealed class FakeDeletionService(Func<bool, MediaEntityDeleteResult> result) : IMediaEntityDeletionService {
        public Task<MediaEntityDeleteResult> DeleteAsync(
            Guid id,
            bool deleteFiles,
            CancellationToken cancellationToken) =>
            Task.FromResult(result(deleteFiles));

        public Task<MediaEntityBulkDeleteResult> DeleteManyAsync(
            IReadOnlyList<Guid> ids,
            bool deleteFiles,
            CancellationToken cancellationToken) {
            var outcome = result(deleteFiles);
            return Task.FromResult(outcome.Deleted
                ? new MediaEntityBulkDeleteResult(ids.Distinct().Count(), outcome.FilesDeleted, [], outcome.Reverted ? 1 : 0)
                : new MediaEntityBulkDeleteResult(
                    0,
                    0,
                    [new MediaEntityBulkDeleteFailure(ids[0], outcome.Message ?? "Deletion was refused.")],
                    0));
        }
    }
}
