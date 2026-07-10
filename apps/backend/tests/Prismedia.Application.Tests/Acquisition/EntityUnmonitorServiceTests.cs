using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

/// <summary>Locks the all-or-nothing application ordering for recursive Entity unmonitor cleanup.</summary>
public sealed class EntityUnmonitorServiceTests {
    [Fact]
    public async Task MissingMonitorReturnsNotFoundWithoutMutatingAnything() {
        var persistence = new RecordingPersistence { Scope = null };
        var acquisitions = new RecordingAcquisitions();
        var service = new EntityUnmonitorService(persistence, acquisitions);

        var result = await service.StopAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Found);
        Assert.False(result.Stopped);
        Assert.Empty(persistence.Events);
        Assert.Empty(acquisitions.Events);
    }

    [Fact]
    public async Task PreflightsTheEntireScopeBeforePausingOrDeletingAnything() {
        var first = Guid.NewGuid();
        var importing = Guid.NewGuid();
        var persistence = new RecordingPersistence { Scope = Scope(first, importing) };
        var acquisitions = new RecordingAcquisitions();
        acquisitions.Eligibility[importing] = new AcquisitionRemovalEligibility(
            false,
            "The import checkpoint must finish first.");
        var service = new EntityUnmonitorService(persistence, acquisitions);

        var result = await service.StopAsync(persistence.Scope.MonitorId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.Stopped);
        Assert.Equal("The import checkpoint must finish first.", result.Message);
        Assert.Equal([$"preflight:{first}", $"preflight:{importing}"], acquisitions.Events);
        Assert.Empty(persistence.Events);
    }

    [Fact]
    public async Task EligibilityThatChangesUnderTheLifecycleClaimBlocksBeforeSuppressionOrTeardown() {
        var acquisitionId = Guid.NewGuid();
        var events = new List<string>();
        var scope = Scope(acquisitionId) with {
            RootSuppression = new UnmonitorSuppressionTarget(
                Guid.NewGuid(),
                EntityKind.Book,
                "Wanted book",
                [new ExternalIdentity("openlibrary", "OL1W")])
        };
        var persistence = new RecordingPersistence(events) { Scope = scope };
        var acquisitions = new RecordingAcquisitions(events);
        acquisitions.RecheckEligibility[acquisitionId] = new AcquisitionRemovalEligibility(
            false,
            "The import started while unmonitoring was being prepared.");
        var service = new EntityUnmonitorService(persistence, acquisitions);

        var result = await service.StopAsync(scope.MonitorId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.Stopped);
        Assert.Equal("The import started while unmonitoring was being prepared.", result.Message);
        Assert.Equal([$"preflight:{acquisitionId}", $"preflight:{acquisitionId}"], events);
        Assert.DoesNotContain("claim", events);
        Assert.DoesNotContain(events, value => value.StartsWith("suppress:", StringComparison.Ordinal));
        Assert.DoesNotContain(events, value => value.StartsWith("claim-acquisition:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopsTheWholeScopeThenRemovesDownloadsSuppressesPhantomsAndFinalizes() {
        var pending = Guid.NewGuid();
        var imported = Guid.NewGuid();
        var scope = Scope(pending, imported) with {
            RootSuppression = new UnmonitorSuppressionTarget(
                Guid.NewGuid(),
                EntityKind.Book,
                "Wanted book",
                [new ExternalIdentity("openlibrary", "OL1W")])
        };
        var events = new List<string>();
        var persistence = new RecordingPersistence(events) { Scope = scope, RootEntityPruned = true };
        var acquisitions = new RecordingAcquisitions(events);
        var service = new EntityUnmonitorService(persistence, acquisitions);

        var result = await service.StopAsync(scope.MonitorId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.Stopped);
        Assert.True(result.RootEntityPruned);
        Assert.Equal([
            $"preflight:{pending}",
            $"preflight:{imported}",
            $"preflight:{pending}",
            $"preflight:{imported}",
            "claim",
            "suppress:openlibrary:OL1W",
            $"claim-acquisition:{pending}",
            $"claim-acquisition:{imported}",
            $"delete:{pending}",
            $"delete:{imported}",
            "complete"
        ], events);
    }

    [Fact]
    public async Task LifecycleRaceLeavesTheScopeClaimedAndNeverFinalizesPartialEntityCleanup() {
        var acquisitionId = Guid.NewGuid();
        var events = new List<string>();
        var scope = Scope(acquisitionId) with {
            RootSuppression = new UnmonitorSuppressionTarget(
                Guid.NewGuid(),
                EntityKind.Book,
                "Wanted book",
                [new ExternalIdentity("openlibrary", "OL1W")])
        };
        var persistence = new RecordingPersistence(events) { Scope = scope };
        var acquisitions = new RecordingAcquisitions(events) { DeleteFailureId = acquisitionId };
        var service = new EntityUnmonitorService(persistence, acquisitions);

        var result = await service.StopAsync(persistence.Scope.MonitorId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.Stopped);
        Assert.Contains("changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([
            $"preflight:{acquisitionId}",
            $"preflight:{acquisitionId}",
            "claim",
            "suppress:openlibrary:OL1W",
            $"claim-acquisition:{acquisitionId}",
            $"delete:{acquisitionId}"
        ], events);
        Assert.DoesNotContain("complete", events);
    }

    [Fact]
    public async Task AcquisitionClaimRaceKeepsTheAtomicSuppressionAndStopsBeforeRemoteTeardown() {
        var first = Guid.NewGuid();
        var raced = Guid.NewGuid();
        var events = new List<string>();
        var scope = Scope(first, raced) with {
            RootSuppression = new UnmonitorSuppressionTarget(
                Guid.NewGuid(),
                EntityKind.Book,
                "Wanted book",
                [new ExternalIdentity("openlibrary", "OL1W")])
        };
        var persistence = new RecordingPersistence(events) { Scope = scope };
        var acquisitions = new RecordingAcquisitions(events) { ClaimFailureId = raced };
        var service = new EntityUnmonitorService(persistence, acquisitions);

        var result = await service.StopAsync(scope.MonitorId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.Stopped);
        Assert.Contains("changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([
            $"preflight:{first}",
            $"preflight:{raced}",
            $"preflight:{first}",
            $"preflight:{raced}",
            "claim",
            "suppress:openlibrary:OL1W",
            $"claim-acquisition:{first}",
            $"claim-acquisition:{raced}"
        ], events);
    }

    [Fact]
    public async Task GiveUpEntityUsesEntityResolutionAndTheSameStrictTeardownPipeline() {
        var entityId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var scope = Scope(acquisitionId) with { RootEntityId = entityId, SyntheticMonitorAnchor = true };
        var events = new List<string>();
        var persistence = new RecordingPersistence(events) { Scope = scope };
        var service = new EntityUnmonitorService(persistence, new RecordingAcquisitions(events));

        var result = await service.GiveUpEntityAsync(entityId, CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.Stopped);
        Assert.Equal(entityId, persistence.ResolvedEntityId);
        Assert.Equal([
            $"preflight:{acquisitionId}",
            $"preflight:{acquisitionId}",
            "claim",
            $"claim-acquisition:{acquisitionId}",
            $"delete:{acquisitionId}",
            "complete"
        ], events);
    }

    private static EntityUnmonitorScope Scope(params Guid[] acquisitionIds) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [Guid.NewGuid()],
            acquisitionIds,
            [Guid.NewGuid()],
            null);

    private sealed class RecordingPersistence(List<string>? sharedEvents = null) : IEntityUnmonitorPersistence {
        public EntityUnmonitorScope? Scope { get; init; }
        public Guid? ResolvedEntityId { get; private set; }
        public bool RootEntityPruned { get; init; }
        public List<string> Events { get; } = sharedEvents ?? [];

        public Task<EntityUnmonitorScope?> ResolveAsync(Guid monitorId, CancellationToken cancellationToken) =>
            Task.FromResult(Scope);

        public Task<EntityUnmonitorScope?> ResolveForEntityAsync(
            Guid entityId,
            CancellationToken cancellationToken) {
            ResolvedEntityId = entityId;
            return Task.FromResult(Scope);
        }

        public async Task<bool> ClaimAsync(
            EntityUnmonitorScope scope,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<bool>>? revalidateRemovalEligibility = null) {
            if (revalidateRemovalEligibility is not null
                && !await revalidateRemovalEligibility(cancellationToken)) {
                return false;
            }

            Events.Add("claim");
            if (scope.RootSuppression is { } target) {
                Events.AddRange(target.ExternalIdentities.Select(identity =>
                    $"suppress:{identity.Namespace}:{identity.Value}"));
            }
            return true;
        }

        public Task<bool> CompleteAsync(EntityUnmonitorScope scope, CancellationToken cancellationToken) {
            Events.Add("complete");
            return Task.FromResult(RootEntityPruned);
        }
    }

    private sealed class RecordingAcquisitions(List<string>? sharedEvents = null) : IAcquisitionRequestService {
        public Dictionary<Guid, AcquisitionRemovalEligibility> Eligibility { get; } = [];
        public Dictionary<Guid, AcquisitionRemovalEligibility> RecheckEligibility { get; } = [];
        public List<string> Events { get; } = sharedEvents ?? [];
        public Guid? DeleteFailureId { get; init; }
        public Guid? ClaimFailureId { get; init; }
        private readonly Dictionary<Guid, int> eligibilityChecks = [];

        public Task<AcquisitionRemovalEligibility> GetRemovalEligibilityAsync(
            Guid id,
            CancellationToken cancellationToken) {
            Events.Add($"preflight:{id}");
            eligibilityChecks[id] = eligibilityChecks.GetValueOrDefault(id) + 1;
            if (eligibilityChecks[id] > 1 && RecheckEligibility.TryGetValue(id, out var recheck)) {
                return Task.FromResult(recheck);
            }
            return Task.FromResult(Eligibility.GetValueOrDefault(id) ?? new AcquisitionRemovalEligibility(true));
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) {
            return DeleteForUnmonitorAsync(id, cancellationToken);
        }

        public Task<bool> DeleteForUnmonitorAsync(Guid id, CancellationToken cancellationToken) {
            Events.Add($"delete:{id}");
            if (id == DeleteFailureId) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition changed while cleanup was running.");
            }
            return Task.FromResult(true);
        }

        public Task<bool> ClaimTeardownAsync(
            Guid id,
            AcquisitionTeardownIntent intent,
            CancellationToken cancellationToken) {
            Events.Add($"claim-acquisition:{id}");
            if (id == ClaimFailureId) {
                throw new AcquisitionConfigurationException(
                    ApiProblemCodes.AcquisitionInvalid,
                    "The acquisition changed while cleanup was being claimed.");
            }

            Assert.Equal(AcquisitionTeardownIntent.Remove, intent);
            return Task.FromResult(true);
        }

        public Task<bool> CompleteTeardownAsync(
            Guid id,
            AcquisitionTeardownIntent intent,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionReacquireEligibility> GetReacquireEligibilityAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConfirmTransferRemovedAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
