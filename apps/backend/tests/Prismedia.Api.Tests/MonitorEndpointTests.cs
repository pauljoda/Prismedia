using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

/// <summary>Locks the public result mapping for generalized recursive Entity unmonitor cleanup.</summary>
public sealed class MonitorEndpointTests {
    private static readonly Guid MonitorId = Guid.Parse("51515151-5151-5151-5151-515151515151");

    [Fact]
    public void MonitorEligibilitySeparatesDiscoveryFromMissingChildSearch() {
        var book = new MonitorEligibilityView(
            true,
            ["books-metadata"],
            DiscoversChildren: false,
            CanSearchMissingChildren: true,
            MissingChildEntityKind: EntityKind.Book);
        var album = new MonitorEligibilityView(
            true,
            ["music-metadata"],
            DiscoversChildren: false,
            CanSearchMissingChildren: false,
            MissingChildEntityKind: null);
        var grouping = new MonitorEligibilityView(
            true,
            ["books-metadata"],
            DiscoversChildren: true,
            CanSearchMissingChildren: true,
            MissingChildEntityKind: EntityKind.Book);

        Assert.False(book.DiscoversChildren);
        Assert.True(book.CanSearchMissingChildren);
        Assert.Equal(EntityKind.Book, book.MissingChildEntityKind);
        Assert.False(album.DiscoversChildren);
        Assert.False(album.CanSearchMissingChildren);
        Assert.Null(album.MissingChildEntityKind);
        Assert.True(grouping.DiscoversChildren);
        Assert.True(grouping.CanSearchMissingChildren);
        Assert.Equal(book.CanMonitor, grouping.CanMonitor);
    }

    [Fact]
    public async Task BatchMonitorStateRejectsAnEmptyEntitySelection() {
        using var factory = CreateFactory(StopScenario.Completed);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/monitors/states",
            new EntityMonitorStateRequest([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StopMonitorReturnsTypedRetentionOutcomeAfterRecursiveCleanupCompletes() {
        using var factory = CreateFactory(StopScenario.Completed);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/api/monitors/{MonitorId}");
        var outcome = await response.Content.ReadFromJsonAsync<MonitorStopResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(outcome?.EntityPruned);
    }

    [Fact]
    public async Task StopMonitorReportsWhenItsFilelessRootWasPruned() {
        using var factory = CreateFactory(StopScenario.CompletedPruned);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/api/monitors/{MonitorId}");
        var outcome = await response.Content.ReadFromJsonAsync<MonitorStopResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(outcome?.EntityPruned);
    }

    [Fact]
    public async Task StopMonitorReturnsNotFoundWhenTheMonitorDoesNotExist() {
        using var factory = CreateFactory(StopScenario.Missing);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/api/monitors/{MonitorId}");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiProblemCodes.NotFound, problem?.Code);
    }

    [Theory]
    [InlineData(StopScenario.PreflightConflict)]
    [InlineData(StopScenario.ClientConflict)]
    public async Task StopMonitorReturnsConflictWhenSafetyOrClientTeardownRefusesCleanup(StopScenario scenario) {
        using var factory = CreateFactory(scenario);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.DeleteAsync($"/api/monitors/{MonitorId}");
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ApiProblemCodes.AcquisitionInvalid, problem?.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory(StopScenario scenario) {
        var acquisitionId = Guid.Parse("61616161-6161-6161-6161-616161616161");
        var scope = new EntityUnmonitorScope(
            MonitorId,
            Guid.Parse("71717171-7171-7171-7171-717171717171"),
            [Guid.Parse("71717171-7171-7171-7171-717171717171")],
            scenario is StopScenario.Completed or StopScenario.CompletedPruned ? [] : [acquisitionId],
            [MonitorId],
            RootSuppression: null);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.RemoveAll<IEntityUnmonitorPersistence>();
                    services.RemoveAll<IAcquisitionRequestService>();
                    services.RemoveAll<IWantedSuppressionStore>();
                    services.AddSingleton<IEntityUnmonitorPersistence>(
                        new FakeUnmonitorPersistence(
                            scenario == StopScenario.Missing ? null : scope,
                            rootEntityPruned: scenario == StopScenario.CompletedPruned));
                    services.AddSingleton<IAcquisitionRequestService>(new FakeAcquisitions(scenario));
                    services.AddSingleton<IWantedSuppressionStore>(new NoopSuppressions());
                });
            })
            .WithTestAuth();
    }

    public enum StopScenario {
        Completed,
        CompletedPruned,
        Missing,
        PreflightConflict,
        ClientConflict
    }

    private sealed class FakeUnmonitorPersistence(
        EntityUnmonitorScope? scope,
        bool rootEntityPruned) : IEntityUnmonitorPersistence {
        public Task<EntityUnmonitorScope?> ResolveAsync(Guid monitorId, CancellationToken cancellationToken) =>
            Task.FromResult(scope);

        public Task<EntityUnmonitorScope?> ResolveForEntityAsync(
            Guid entityId,
            CancellationToken cancellationToken) => Task.FromResult(scope);

        public Task<bool> ClaimAsync(
            EntityUnmonitorScope resolved,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<bool>>? revalidateRemovalEligibility = null) =>
            Task.FromResult(true);

        public Task<bool> CompleteAsync(EntityUnmonitorScope resolved, CancellationToken cancellationToken) =>
            Task.FromResult(rootEntityPruned);
    }

    private sealed class FakeAcquisitions(StopScenario scenario) : IAcquisitionRequestService {
        public Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(scenario == StopScenario.PreflightConflict
                ? new AcquisitionRemovalEligibility(false, "An import is still being applied.")
                : new AcquisitionRemovalEligibility(true));

        public Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken) =>
            scenario == StopScenario.ClientConflict
                ? Task.FromException<bool>(new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The download client could not confirm data removal."))
                : Task.FromResult(true);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) =>
            DeleteForUnmonitorAsync(id, cancellationToken);

        public Task<AcquisitionSummary> CreateAndSearchAsync(
            AcquisitionCreateRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(
            Guid id,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ClaimTeardownAsync(Guid id, AcquisitionTeardownIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> CompleteTeardownAsync(Guid id, AcquisitionTeardownIntent intent, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoopSuppressions : IWantedSuppressionStore {
        public Task SuppressAsync(
            IReadOnlyList<ExternalIdentity> identities,
            EntityKind kind,
            string title,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<ExternalIdentity>>(new HashSet<ExternalIdentity>());

        public Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
